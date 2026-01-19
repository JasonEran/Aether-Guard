#include "SysMonitor.hpp"

#include <ctime>
#include <fstream>
#include <sstream>
#include <string>

namespace {
bool ReadCpuTimes(unsigned long long& idle, unsigned long long& total) {
    std::ifstream statFile("/proc/stat");
    if (!statFile.is_open()) {
        return false;
    }

    std::string line;
    if (!std::getline(statFile, line)) {
        return false;
    }

    std::istringstream iss(line);
    std::string label;
    iss >> label;
    if (label != "cpu") {
        return false;
    }

    unsigned long long user = 0;
    unsigned long long nice = 0;
    unsigned long long system = 0;
    unsigned long long idleVal = 0;
    unsigned long long iowait = 0;
    unsigned long long irq = 0;
    unsigned long long softirq = 0;
    unsigned long long steal = 0;
    unsigned long long guest = 0;
    unsigned long long guestNice = 0;

    iss >> user >> nice >> system >> idleVal >> iowait >> irq >> softirq >> steal >> guest >> guestNice;

    idle = idleVal + iowait;
    total = user + nice + system + idleVal + iowait + irq + softirq + steal + guest + guestNice;
    return true;
}

bool ReadMemInfo(unsigned long long& totalKb, unsigned long long& availableKb) {
    std::ifstream memFile("/proc/meminfo");
    if (!memFile.is_open()) {
        return false;
    }

    std::string line;
    while (std::getline(memFile, line)) {
        std::istringstream iss(line);
        std::string key;
        unsigned long long value = 0;
        std::string unit;
        if (!(iss >> key >> value)) {
            continue;
        }

        if (key == "MemTotal:") {
            totalKb = value;
        } else if (key == "MemAvailable:") {
            availableKb = value;
        }

        if (totalKb > 0 && availableKb > 0) {
            return true;
        }
    }

    return totalKb > 0 && availableKb > 0;
}
} // namespace

SysMonitor::SysMonitor()
    : agentId_("agent-001"),
      prevIdle_(0),
      prevTotal_(0) {}

TelemetryData SysMonitor::collect() {
    TelemetryData data;
    data.agentId = agentId_;
    data.timestamp = static_cast<long>(std::time(nullptr));
    data.cpuUsage = 0.0;
    data.memoryUsage = 0.0;

    unsigned long long idle = 0;
    unsigned long long total = 0;
    if (ReadCpuTimes(idle, total) && total > 0) {
        if (prevTotal_ > 0) {
            unsigned long long idleDelta = idle - prevIdle_;
            unsigned long long totalDelta = total - prevTotal_;
            if (totalDelta > 0) {
                data.cpuUsage = static_cast<double>(idleDelta) / static_cast<double>(totalDelta);
            }
        }

        prevIdle_ = idle;
        prevTotal_ = total;
    }

    unsigned long long totalKb = 0;
    unsigned long long availableKb = 0;
    if (ReadMemInfo(totalKb, availableKb) && totalKb > 0) {
        unsigned long long usedKb = totalKb > availableKb ? (totalKb - availableKb) : 0;
        data.memoryUsage = (static_cast<double>(usedKb) / static_cast<double>(totalKb)) * 100.0;
    }

    return data;
}
