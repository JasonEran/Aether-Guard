#pragma once

#include <string>

enum class AgentState {
    IDLE,
    PREPARING,
    CHECKPOINTING,
    TRANSFERRING,
    RESTORING,
    FAILED
};

class LifecycleManager {
public:
    bool PreFlightCheck() const;
    std::string Checkpoint(const std::string& workloadId) const;
    bool Transfer(const std::string& snapshotPath, const std::string& targetIp) const;
    bool Restore(const std::string& snapshotPath) const;
    bool Thaw(const std::string& workloadId) const;
};
