#include "NetworkClient.hpp"

#include <cpr/cpr.h>
#include <nlohmann/json.hpp>

#include <iostream>
#include <string>
#include <utility>

namespace {
std::string BuildUrl(const std::string& baseUrl, const std::string& path) {
    if (baseUrl.empty()) {
        return path;
    }

    if (baseUrl.back() == '/') {
        return baseUrl.substr(0, baseUrl.size() - 1) + path;
    }

    return baseUrl + path;
}
} // namespace

NetworkClient::NetworkClient(std::string baseUrl)
    : baseUrl_(std::move(baseUrl)) {}

bool NetworkClient::Register(const std::string& hostname, std::string& outToken, std::string& outAgentId) {
    nlohmann::json payload = {
        {"hostname", hostname},
        {"os", "Linux"}
    };

    cpr::Response response = cpr::Post(
        cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/register")},
        cpr::Body{payload.dump()},
        cpr::Header{{"Content-Type", "application/json"}});

    if (response.error.code != cpr::ErrorCode::OK) {
        std::cerr << "[Agent] register failed: " << response.error.message << std::endl;
        return false;
    }

    if (response.status_code != 200) {
        std::cerr << "[Agent] register failed with HTTP " << response.status_code << std::endl;
        return false;
    }

    auto json = nlohmann::json::parse(response.text, nullptr, false);
    if (json.is_discarded() || !json.contains("token") || !json.contains("agentId")) {
        std::cerr << "[Agent] register response missing token or agentId" << std::endl;
        return false;
    }

    outToken = json.value("token", "");
    outAgentId = json.value("agentId", "");
    return !outToken.empty() && !outAgentId.empty();
}

bool NetworkClient::SendHeartbeat(const std::string& token, const TelemetryData& data) {
    nlohmann::json payload = {
        {"token", token},
        {"cpuUsage", data.cpuUsage * 100.0},
        {"memoryUsage", data.memoryUsage},
        {"timestamp", data.timestamp}
    };

    cpr::Response response = cpr::Post(
        cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/heartbeat")},
        cpr::Body{payload.dump()},
        cpr::Header{
            {"Content-Type", "application/json"},
            {"Authorization", "Bearer " + token}
        });

    if (response.error.code != cpr::ErrorCode::OK) {
        std::cerr << "[Agent] heartbeat failed: " << response.error.message << std::endl;
        return false;
    }

    if (response.status_code >= 400) {
        std::cerr << "[Agent] heartbeat failed with HTTP " << response.status_code << std::endl;
        return false;
    }

    return true;
}

bool NetworkClient::SendTelemetry(const TelemetryData& data) {
    nlohmann::json payload = {
        {"agentId", data.agentId},
        {"cpuUsage", data.cpuUsage * 100.0},
        {"memoryUsage", data.memoryUsage},
        {"timestamp", data.timestamp},
        {"diskIoUsage", 0.0},
        {"metadata", ""}
    };

    cpr::Response response = cpr::Post(
        cpr::Url{BuildUrl(baseUrl_, "/api/v1/ingestion")},
        cpr::Body{payload.dump()},
        cpr::Header{{"Content-Type", "application/json"}});

    if (response.error.code != cpr::ErrorCode::OK) {
        std::cerr << "[Agent] telemetry failed: " << response.error.message << std::endl;
        return false;
    }

    if (response.status_code >= 400) {
        std::cerr << "[Agent] telemetry failed with HTTP " << response.status_code << std::endl;
        return false;
    }

    return true;
}
