#include "NetworkClient.hpp"
#include "SysMonitor.hpp"

#include <chrono>
#include <iomanip>
#include <iostream>
#include <thread>

int main() {
    std::cout << "Aether Agent Starting..." << std::endl;

    SysMonitor monitor;
    NetworkClient client("http://core-service:8080");

    while (true) {
        TelemetryData data = monitor.collect();
        bool sent = client.sendTelemetry(data);
        double cpuPercent = data.cpuUsage * 100.0;

        if (sent) {
            std::cout << std::fixed << std::setprecision(1)
                      << "[Agent] Sent telemetry: CPU: " << cpuPercent << "% | Mem: "
                      << data.memoryUsage << "%" << std::endl;
        } else {
            std::cerr << "[Agent] Failed to send telemetry" << std::endl;
        }

        std::this_thread::sleep_for(std::chrono::seconds(1));
    }
}
