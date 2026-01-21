#include "NetworkClient.hpp"
#include "SysMonitor.hpp"

#include <chrono>
#include <iomanip>
#include <iostream>
#include <thread>
#include <unistd.h>

int main() {
    std::cout << "Aether Agent Starting..." << std::endl;

    SysMonitor monitor;
    NetworkClient client("http://core-service:8080");

    std::string hostname = "unknown-host";
    char hostnameBuffer[256] = {};
    if (gethostname(hostnameBuffer, sizeof(hostnameBuffer)) == 0) {
        hostname = hostnameBuffer;
    }

    std::string token;
    std::string agentId;
    while (token.empty() || agentId.empty()) {
        if (client.Register(hostname, token, agentId)) {
            monitor.SetAgentId(agentId);
            std::cout << "[Agent] Registered with Core. Token acquired." << std::endl;
            break;
        }

        std::cerr << "[Agent] Waiting for Core..." << std::endl;
        std::this_thread::sleep_for(std::chrono::seconds(5));
    }

    while (true) {
        TelemetryData data = monitor.collect();
        bool heartbeatSent = client.SendHeartbeat(token, data);
        bool telemetrySent = client.SendTelemetry(data);
        double cpuPercent = data.cpuUsage * 100.0;

        if (heartbeatSent) {
            std::cout << "[Agent] Heartbeat sent." << std::endl;
        } else {
            std::cerr << "[Agent] Failed to send heartbeat" << std::endl;
        }

        if (telemetrySent) {
            std::cout << std::fixed << std::setprecision(1)
                      << "[Agent] Telemetry sent: CPU: " << cpuPercent << "% | Mem: "
                      << data.memoryUsage << "%" << std::endl;
        } else {
            std::cerr << "[Agent] Failed to send telemetry" << std::endl;
        }

        std::this_thread::sleep_for(std::chrono::seconds(5));
    }
}
