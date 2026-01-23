#pragma once

#include <string>
#include <vector>

struct AgentCommand {
    int id = 0;
    std::string type;
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

private:
    std::string baseUrl_;
};
