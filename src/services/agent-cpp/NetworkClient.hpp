#pragma once

#include <string>
#include <vector>

struct AgentCommand {
    int id = 0;
    std::string type;
};

struct CommandPayload {
    std::string commandId;
    std::string workloadId;
    std::string action;
    std::string parameters;
    std::string nonce;
    std::string signature;
    std::string expiresAt;
};

struct CommandFeedback {
    std::string commandId;
    std::string status;
    std::string result;
    std::string error;
};

class NetworkClient {
public:
    explicit NetworkClient(std::string baseUrl);

    bool Register(const std::string& hostname, std::string& outToken, std::string& outAgentId);
    bool SendHeartbeat(
        const std::string& token,
        const std::string& agentId,
        const std::string& state,
        const std::string& tier,
        std::vector<AgentCommand>& outCommands);
    bool PollCommands(const std::string& agentId, std::vector<CommandPayload>& outCommands);
    bool SendFeedback(const std::string& agentId, const CommandFeedback& feedback);

private:
    std::string baseUrl_;
};
