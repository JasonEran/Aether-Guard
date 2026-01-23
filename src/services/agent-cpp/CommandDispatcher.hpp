#pragma once

#include "LifecycleManager.hpp"
#include "NetworkClient.hpp"

#include <mutex>
#include <set>
#include <string>

class CommandDispatcher {
public:
    CommandDispatcher(NetworkClient& client, LifecycleManager& lifecycle, std::string agentId);

    void Dispatch(const CommandPayload& command);
    void ReportExpired(const CommandPayload& command);

private:
    void ReportResult(const CommandPayload& command, const std::string& status, const std::string& result, const std::string& error);
    bool IsDuplicateNonce(const std::string& nonce);
    static std::string ToUpper(std::string value);

    NetworkClient& client_;
    LifecycleManager& lifecycle_;
    std::string agentId_;
    std::set<std::string> processedNonces_;
    std::mutex mutex_;
};
