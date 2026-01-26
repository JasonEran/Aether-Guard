#include "CommandPoller.hpp"
#include "LifecycleManager.hpp"
#include "NetworkClient.hpp"

#include <chrono>
#include <cstdlib>
#include <filesystem>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

#ifdef _WIN32
#include <windows.h>
#else
#include <sys/utsname.h>
#include <unistd.h>
#endif

namespace {
std::string DetectOsName() {
#ifdef _WIN32
    return "Windows";
#elif __APPLE__
    return "macOS";
#else
    return "Linux";
#endif
}

std::string DetectKernelVersion() {
#ifdef _WIN32
    return "windows";
#else
    struct utsname info;
    if (uname(&info) == 0) {
        return info.release;
    }
    return {};
#endif
}

bool DetectEbpfAvailable() {
#ifdef _WIN32
    return false;
#else
    return std::filesystem::exists("/sys/fs/bpf");
#endif
}
} // namespace

int main() {
    std::cout << "Aether Agent Starting..." << std::endl;

    const std::string coreUrl = "http://core-service:8080";
    NetworkClient client(coreUrl);
    LifecycleManager lifecycle(client, coreUrl);

    std::string hostname = "unknown-host";
    char hostnameBuffer[256] = {};
#ifdef _WIN32
    DWORD hostnameSize = static_cast<DWORD>(sizeof(hostnameBuffer));
    if (GetComputerNameA(hostnameBuffer, &hostnameSize) != 0) {
        hostname = hostnameBuffer;
    }
#else
    if (gethostname(hostnameBuffer, sizeof(hostnameBuffer)) == 0) {
        hostname = hostnameBuffer;
    }
#endif

    const std::string osName = DetectOsName();
    AgentCapabilities capabilities;
    capabilities.kernelVersion = DetectKernelVersion();
    capabilities.criuAvailable = lifecycle.IsCriuAvailable();
    capabilities.ebpfAvailable = DetectEbpfAvailable();
    capabilities.supportsSnapshot = capabilities.criuAvailable;
    capabilities.supportsNetTopology = false;
    capabilities.supportsChaos = false;

    std::string token;
    std::string agentId;
    AgentConfig agentConfig;
    while (token.empty() || agentId.empty()) {
        if (client.Register(hostname, osName, capabilities, token, agentId, &agentConfig)) {
            std::cout << "[Agent] Registered with Core. Token acquired." << std::endl;
            if (!agentConfig.nodeMode.empty()) {
                std::cout << "[Agent] Config: snapshot=" << (agentConfig.enableSnapshot ? "on" : "off")
                          << ", ebpf=" << (agentConfig.enableEbpf ? "on" : "off")
                          << ", node_mode=" << agentConfig.nodeMode << std::endl;
            }
            break;
        }

        std::cerr << "[Agent] Waiting for Core..." << std::endl;
        std::this_thread::sleep_for(std::chrono::seconds(5));
    }

    const bool preflightOk = lifecycle.PreFlightCheck();
    if (!preflightOk) {
        std::cerr << "[Agent] Pre-flight check failed. Proceeding with heartbeat in IDLE state." << std::endl;
    }

    const std::string tier = "T2";
    const std::string state = "IDLE";
    CommandDispatcher dispatcher(client, lifecycle, agentId);
    CommandPoller poller(client, dispatcher, agentId);
    poller.Start();

    while (true) {
        std::vector<AgentCommand> commands;
        bool heartbeatSent = client.SendHeartbeat(token, agentId, state, tier, commands);

        if (heartbeatSent) {
            std::cout << "[Agent] Heartbeat sent." << std::endl;
        } else {
            std::cerr << "[Agent] Failed to send heartbeat" << std::endl;
        }

        if (!commands.empty()) {
            std::cout << "[Agent] Received " << commands.size() << " pending command(s)." << std::endl;
        }

        std::this_thread::sleep_for(std::chrono::seconds(5));
    }
}
