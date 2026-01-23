#include "LifecycleManager.hpp"
#include "NetworkClient.hpp"

#include <chrono>
#include <cstdlib>
#include <iostream>
#include <thread>
#include <unistd.h>
#include <vector>

int main() {
    std::cout << "Aether Agent Starting..." << std::endl;

    NetworkClient client("http://core-service:8080");
    LifecycleManager lifecycle;

    std::string hostname = "unknown-host";
    char hostnameBuffer[256] = {};
    if (gethostname(hostnameBuffer, sizeof(hostnameBuffer)) == 0) {
        hostname = hostnameBuffer;
    }

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
