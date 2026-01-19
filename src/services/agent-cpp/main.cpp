#include "SysMonitor.hpp"

#include <chrono>
#include <iomanip>
#include <iostream>
#include <thread>

int main() {
    std::cout << "Aether Agent Starting..." << std::endl;

    SysMonitor monitor;

    while (true) {
        TelemetryData data = monitor.collect();
        double cpuPercent = data.cpuUsage * 100.0;

        std::cout << std::fixed << std::setprecision(1)
                  << "[Agent] CPU: " << cpuPercent << "% | Mem: "
                  << data.memoryUsage << "%" << std::endl;

        std::this_thread::sleep_for(std::chrono::seconds(1));
    }
}
