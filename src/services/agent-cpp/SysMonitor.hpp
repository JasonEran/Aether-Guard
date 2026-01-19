#pragma once

#include <string>
#include <vector>
#include <ctime>

struct TelemetryData {
    std::string agentId;
    long timestamp;
    double cpuUsage;
    double memoryUsage;
};

class SysMonitor {
public:
    SysMonitor();
    TelemetryData collect();

private:
    std::string agentId_;
    unsigned long long prevIdle_;
    unsigned long long prevTotal_;
};
