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

bool NetworkClient::SendHeartbeat(
    const std::string& token,
    const std::string& agentId,
    const std::string& state,
    const std::string& tier,
    std::vector<AgentCommand>& outCommands) {
    outCommands.clear();

    nlohmann::json payload = {
        {"agentId", agentId},
        {"state", state},
        {"tier", tier}
    };
    if (!token.empty()) {
        payload["token"] = token;
    }

    cpr::Header headers{{"Content-Type", "application/json"}};
    if (!token.empty()) {
        headers["Authorization"] = "Bearer " + token;
    }

    cpr::Response response = cpr::Post(
        cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/heartbeat")},
        cpr::Body{payload.dump()},
        headers);

    if (response.error.code != cpr::ErrorCode::OK) {
        std::cerr << "[Agent] heartbeat failed: " << response.error.message << std::endl;
        return false;
    }

    if (response.status_code >= 400) {
        std::cerr << "[Agent] heartbeat failed with HTTP " << response.status_code << std::endl;
        return false;
    }

    auto json = nlohmann::json::parse(response.text, nullptr, false);
    if (!json.is_discarded() && json.contains("commands") && json["commands"].is_array()) {
        for (const auto& item : json["commands"]) {
            AgentCommand command;
            command.id = item.value("id", 0);
            command.type = item.value("type", "");
            if (command.id > 0 && !command.type.empty()) {
                outCommands.push_back(std::move(command));
            }
        }
    }

    return true;
}

bool NetworkClient::PollCommands(const std::string& agentId, std::vector<CommandPayload>& outCommands) {
    outCommands.clear();

    if (agentId.empty()) {
        return false;
    }

    cpr::Response response = cpr::Get(
        cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/poll")},
        cpr::Parameters{{"agentId", agentId}});

    if (response.error.code != cpr::ErrorCode::OK) {
        std::cerr << "[Agent] poll failed: " << response.error.message << std::endl;
        return false;
    }

    if (response.status_code >= 400) {
        std::cerr << "[Agent] poll failed with HTTP " << response.status_code << std::endl;
        return false;
    }

    auto json = nlohmann::json::parse(response.text, nullptr, false);
    if (json.is_discarded() || !json.contains("commands") || !json["commands"].is_array()) {
        return true;
    }

    for (const auto& item : json["commands"]) {
        CommandPayload command;
        command.commandId = item.value("commandId", "");
        command.workloadId = item.value("workloadId", "");
        command.action = item.value("action", "");
        command.nonce = item.value("nonce", "");
        command.signature = item.value("signature", "");
        command.expiresAt = item.value("expiresAt", "");

        if (item.contains("parameters")) {
            if (item["parameters"].is_string()) {
                command.parameters = item.value("parameters", "");
            } else {
                command.parameters = item["parameters"].dump();
            }
        }

        if (!command.commandId.empty()) {
            outCommands.push_back(std::move(command));
        }
    }

    return true;
}

bool NetworkClient::SendFeedback(const std::string& agentId, const CommandFeedback& feedback) {
    nlohmann::json payload = {
        {"agentId", agentId},
        {"commandId", feedback.commandId},
        {"status", feedback.status},
        {"result", feedback.result},
        {"error", feedback.error}
    };

    cpr::Response response = cpr::Post(
        cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/feedback")},
        cpr::Body{payload.dump()},
        cpr::Header{{"Content-Type", "application/json"}});

    if (response.error.code != cpr::ErrorCode::OK) {
        std::cerr << "[Agent] feedback failed: " << response.error.message << std::endl;
        return false;
    }

    if (response.status_code >= 400) {
        std::cerr << "[Agent] feedback failed with HTTP " << response.status_code << std::endl;
        return false;
    }

    return true;
}
