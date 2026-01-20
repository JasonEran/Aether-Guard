#pragma once

#include "SysMonitor.hpp"

#include <string>

class NetworkClient {
public:
    explicit NetworkClient(std::string baseUrl);

    bool Register(const std::string& hostname, std::string& outToken);
    bool SendHeartbeat(const std::string& token, const TelemetryData& data);

private:
    std::string baseUrl_;
};
