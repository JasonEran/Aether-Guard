#include "CommandPoller.hpp"
#include "LifecycleManager.hpp"
#include "NetworkClient.hpp"

#include <chrono>
#include <cstdlib>
#include <iostream>
#include <string>
#include <thread>
#include <vector>

#ifdef _WIN32
#include <windows.h>
#else
#include <unistd.h>
#endif

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

    std::string token;
    std::string agentId;
    while (token.empty() || agentId.empty()) {
        if (client.Register(hostname, token, agentId)) {
            std::cout << "[Agent] Registered with Core. Token acquired." << std::endl;
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
