#include "LifecycleManager.hpp"

#include <chrono>
#include <sstream>

bool LifecycleManager::PreFlightCheck() const {
    constexpr long long minDiskBytes = 1LL * 1024 * 1024 * 1024;
    const long long availableDiskBytes = 2LL * 1024 * 1024 * 1024;
    const std::string criuVersion = "3.15";

    const bool diskOk = availableDiskBytes > minDiskBytes;
    const bool criuOk = !criuVersion.empty();
    return diskOk && criuOk;
}

std::string LifecycleManager::Checkpoint(const std::string& workloadId) const {
    const auto now = std::chrono::system_clock::now().time_since_epoch();
    const auto seconds = std::chrono::duration_cast<std::chrono::seconds>(now).count();
    std::ostringstream snapshotPath;
    snapshotPath << "/var/lib/aether-guard/snapshots/" << workloadId << "_" << seconds << ".tar";
    return snapshotPath.str();
}

bool LifecycleManager::Transfer(const std::string& snapshotPath, const std::string& targetIp) const {
    return !snapshotPath.empty() && !targetIp.empty();
}

bool LifecycleManager::Restore(const std::string& snapshotPath) const {
    return !snapshotPath.empty();
}

bool LifecycleManager::Thaw(const std::string& workloadId) const {
    return !workloadId.empty();
}
