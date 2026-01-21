#pragma once

#include "SysMonitor.hpp"

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
    bool SendHeartbeat(const std::string& token, const TelemetryData& data, std::vector<AgentCommand>& outCommands);
    bool SendTelemetry(const TelemetryData& data);

private:
    std::string baseUrl_;
};
