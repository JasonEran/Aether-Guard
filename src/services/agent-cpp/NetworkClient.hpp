#pragma once

#include "SysMonitor.hpp"

#include <string>

class NetworkClient {
public:
    explicit NetworkClient(std::string baseUrl);

    bool sendTelemetry(const TelemetryData& data);

private:
    std::string baseUrl_;
};
