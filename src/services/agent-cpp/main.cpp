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
#include <algorithm>
#include <cctype>

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

std::string GetEnvOrDefault(const char* name, const std::string& defaultValue) {
    const char* value = std::getenv(name);
    return value ? std::string(value) : defaultValue;
}

bool GetEnvBool(const char* name, bool defaultValue) {
    const char* value = std::getenv(name);
    if (!value) {
        return defaultValue;
    }

    std::string normalized(value);
    std::transform(normalized.begin(), normalized.end(), normalized.begin(), [](unsigned char ch) {
        return static_cast<char>(std::tolower(ch));
    });

    if (normalized == "1" || normalized == "true" || normalized == "yes") {
        return true;
    }
    if (normalized == "0" || normalized == "false" || normalized == "no") {
        return false;
    }

    return defaultValue;
}

bool WaitForTlsFiles(const TlsSettings& settings, int timeoutSeconds) {
    if (settings.certPath.empty() || settings.keyPath.empty() || settings.caPath.empty()) {
        return false;
    }

    for (int attempt = 0; attempt < timeoutSeconds; ++attempt) {
        if (std::filesystem::exists(settings.certPath)
            && std::filesystem::exists(settings.keyPath)
            && std::filesystem::exists(settings.caPath)) {
            return true;
        }

        std::this_thread::sleep_for(std::chrono::seconds(1));
    }

    return false;
}
} // namespace

int main() {
    std::cout << "Aether Agent Starting..." << std::endl;

    const std::string coreUrl = GetEnvOrDefault("AG_CORE_URL", "http://core-service:8080");
    TlsSettings tlsSettings;
    tlsSettings.enabled = GetEnvBool("AG_MTLS_ENABLED", false);
    if (tlsSettings.enabled) {
        tlsSettings.certPath = GetEnvOrDefault("AG_MTLS_CERT_PATH", "");
        tlsSettings.keyPath = GetEnvOrDefault("AG_MTLS_KEY_PATH", "");
        tlsSettings.caPath = GetEnvOrDefault("AG_MTLS_CA_PATH", "");
        tlsSettings.verifyPeer = GetEnvBool("AG_MTLS_VERIFY_PEER", true);
        tlsSettings.verifyHost = GetEnvBool("AG_MTLS_VERIFY_HOST", false);

        if (coreUrl.rfind("https://", 0) != 0) {
            std::cerr << "[Agent] AG_MTLS_ENABLED requires an https core URL." << std::endl;
            return 1;
        }

        if (!WaitForTlsFiles(tlsSettings, 30)) {
            std::cerr << "[Agent] mTLS enabled but certificate files are missing." << std::endl;
            return 1;
        }
    }

    NetworkClient client(coreUrl, tlsSettings);
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
