#pragma once

#include "ArchiveManager.hpp"
#include "CriuManager.hpp"

#include <string>

class NetworkClient;

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
    LifecycleManager(NetworkClient& client, std::string orchestratorBaseUrl);

    bool PreFlightCheck() const;
    std::string Checkpoint(const std::string& workloadId);
    bool Transfer(const std::string& snapshotPath, const std::string& targetIp);
    bool Restore(const std::string& snapshotPath);
    bool Thaw(const std::string& workloadId);

private:
    static std::string BuildUrl(const std::string& baseUrl, const std::string& path);
    static std::string FormatTimestamp();
    static std::string SanitizeWorkloadId(const std::string& workloadId);
    static std::string ExtractWorkloadId(const std::string& snapshotPath);
    static int ParsePid(const std::string& workloadId);

    NetworkClient& client_;
    CriuManager criu_;
    ArchiveManager archive_;
    std::string orchestratorBaseUrl_;
    bool criu_available_ = false;
};
