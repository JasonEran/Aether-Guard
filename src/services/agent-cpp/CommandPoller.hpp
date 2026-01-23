#pragma once

#include "CommandDispatcher.hpp"
#include "NetworkClient.hpp"

#include <atomic>
#include <string>
#include <thread>

class CommandPoller {
public:
    CommandPoller(NetworkClient& client, CommandDispatcher& dispatcher, std::string agentId);
    ~CommandPoller();

    void Start();
    void Stop();

private:
    void Run();
    bool IsExpired(const std::string& expiresAt) const;

    NetworkClient& client_;
    CommandDispatcher& dispatcher_;
    std::string agentId_;
    std::atomic<bool> running_{false};
    std::thread worker_;
};
