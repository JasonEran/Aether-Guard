#include "NetworkClient.hpp"

#include <cpr/cpr.h>
#include <cpr/ssl_options.h>
#include <nlohmann/json.hpp>

#include <chrono>
#include <filesystem>
#include <iomanip>
#include <random>
#include <sstream>
#include <system_error>
#include <fstream>
#include <iostream>
#include <string>
#include <thread>
#include <utility>

namespace {
constexpr int kMaxRetries = 3;
constexpr auto kConnectTimeout = std::chrono::seconds(3);
constexpr auto kTelemetryTimeout = std::chrono::seconds(5);
constexpr int kLowSpeedBytesPerSecond = 1000;
constexpr int kLowSpeedTimeoutSeconds = 5;

std::string BuildUrl(const std::string& baseUrl, const std::string& path) {
    if (baseUrl.empty()) {
        return path;
    }

    if (baseUrl.back() == '/') {
        return baseUrl.substr(0, baseUrl.size() - 1) + path;
    }

    return baseUrl + path;
}

bool IsSuccessStatus(const cpr::Response& response) {
    return response.status_code == 200 || response.status_code == 201;
}

int BackoffSeconds(int attempt) {
    return 1 << attempt;
}

void LogRetry(int attempt) {
    const int waitSeconds = BackoffSeconds(attempt);
    std::cerr << "[Network] Request failed (Attempt " << (attempt + 1) << "/" << kMaxRetries
              << "). Retrying in " << waitSeconds << "s..." << std::endl;
}

cpr::SslOptions BuildSslOptions(const TlsSettings& settings) {
    return cpr::Ssl(
        cpr::ssl::CaInfo{settings.caPath},
        cpr::ssl::CertFile{settings.certPath},
        cpr::ssl::KeyFile{settings.keyPath},
        cpr::ssl::VerifyPeer{settings.verifyPeer},
        cpr::ssl::VerifyHost{settings.verifyHost});
}

std::string RandomHex(size_t bytes) {
    static thread_local std::mt19937_64 rng{std::random_device{}()};
    std::uniform_int_distribution<int> dist(0, 255);

    std::ostringstream out;
    out << std::hex << std::nouppercase;
    for (size_t i = 0; i < bytes; ++i) {
        out << std::setw(2) << std::setfill('0') << dist(rng);
    }
    return out.str();
}

std::string BuildTraceParent() {
    return "00-" + RandomHex(16) + "-" + RandomHex(8) + "-01";
}

cpr::Header AddTraceParentHeader(cpr::Header headers) {
    headers["traceparent"] = BuildTraceParent();
    return headers;
}
} // namespace

NetworkClient::NetworkClient(std::string baseUrl, TlsSettings tlsSettings)
    : baseUrl_(std::move(baseUrl)), tlsSettings_(std::move(tlsSettings)) {}

bool NetworkClient::Register(
    const std::string& hostname,
    const std::string& os,
    const AgentCapabilities& capabilities,
    std::string& outToken,
    std::string& outAgentId,
    AgentConfig* outConfig) {
    nlohmann::json payload = {
        {"hostname", hostname},
        {"os", os},
        {"capabilities", {
            {"kernelVersion", capabilities.kernelVersion},
            {"criuVersion", capabilities.criuVersion},
            {"criuAvailable", capabilities.criuAvailable},
            {"ebpfAvailable", capabilities.ebpfAvailable},
            {"supportsSnapshot", capabilities.supportsSnapshot},
            {"supportsNetTopology", capabilities.supportsNetTopology},
            {"supportsChaos", capabilities.supportsChaos}
        }}
    };

    cpr::Response response = tlsSettings_.enabled
        ? cpr::Post(
            cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/register")},
            cpr::Body{payload.dump()},
            AddTraceParentHeader(cpr::Header{{"Content-Type", "application/json"}}),
            BuildSslOptions(tlsSettings_))
        : cpr::Post(
            cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/register")},
            cpr::Body{payload.dump()},
            AddTraceParentHeader(cpr::Header{{"Content-Type", "application/json"}}));

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
    if (outConfig != nullptr && json.contains("config") && json["config"].is_object()) {
        const auto& config = json["config"];
        outConfig->enableSnapshot = config.value("enableSnapshot", false);
        outConfig->enableEbpf = config.value("enableEbpf", false);
        outConfig->enableNetTopology = config.value("enableNetTopology", false);
        outConfig->enableChaos = config.value("enableChaos", false);
        outConfig->nodeMode = config.value("nodeMode", "");
    }
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
    headers = AddTraceParentHeader(std::move(headers));

    for (int attempt = 0; attempt < kMaxRetries; ++attempt) {
        cpr::Response response = tlsSettings_.enabled
            ? cpr::Post(
                cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/heartbeat")},
                cpr::Body{payload.dump()},
                headers,
                cpr::ConnectTimeout{kConnectTimeout},
                cpr::Timeout{kTelemetryTimeout},
                BuildSslOptions(tlsSettings_))
            : cpr::Post(
                cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/heartbeat")},
                cpr::Body{payload.dump()},
                headers,
                cpr::ConnectTimeout{kConnectTimeout},
                cpr::Timeout{kTelemetryTimeout});

        const bool requestOk = response.error.code == cpr::ErrorCode::OK;
        const bool statusOk = IsSuccessStatus(response);
        if (requestOk && statusOk) {
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

        if (attempt + 1 < kMaxRetries) {
            LogRetry(attempt);
            std::this_thread::sleep_for(std::chrono::seconds(BackoffSeconds(attempt)));
            continue;
        }

        if (!requestOk) {
            std::cerr << "[Agent] heartbeat failed: " << response.error.message << std::endl;
        } else {
            std::cerr << "[Agent] heartbeat failed with HTTP " << response.status_code << std::endl;
        }
        return false;
    }

    return false;
}

bool NetworkClient::PollCommands(const std::string& agentId, std::vector<CommandPayload>& outCommands) {
    outCommands.clear();

    if (agentId.empty()) {
        return false;
    }

    cpr::Response response = tlsSettings_.enabled
        ? cpr::Get(
            cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/poll")},
            cpr::Parameters{{"agentId", agentId}},
            AddTraceParentHeader(cpr::Header{}),
            BuildSslOptions(tlsSettings_))
        : cpr::Get(
            cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/poll")},
            cpr::Parameters{{"agentId", agentId}},
            AddTraceParentHeader(cpr::Header{}));

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

    cpr::Response response = tlsSettings_.enabled
        ? cpr::Post(
            cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/feedback")},
            cpr::Body{payload.dump()},
            AddTraceParentHeader(cpr::Header{{"Content-Type", "application/json"}}),
            BuildSslOptions(tlsSettings_))
        : cpr::Post(
            cpr::Url{BuildUrl(baseUrl_, "/api/v1/agent/feedback")},
            cpr::Body{payload.dump()},
            AddTraceParentHeader(cpr::Header{{"Content-Type", "application/json"}}));

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

bool NetworkClient::UploadSnapshot(const std::string& url, const std::string& filePath) {
    if (url.empty() || filePath.empty()) {
        return false;
    }

    if (!std::filesystem::exists(filePath)) {
        std::cerr << "[Agent] upload failed: file not found " << filePath << std::endl;
        return false;
    }

    for (int attempt = 0; attempt < kMaxRetries; ++attempt) {
        cpr::Response response = tlsSettings_.enabled
            ? cpr::Post(
                cpr::Url{url},
                cpr::Multipart{{"file", cpr::File{filePath}}},
                AddTraceParentHeader(cpr::Header{}),
                cpr::ConnectTimeout{kConnectTimeout},
                cpr::LowSpeed{kLowSpeedBytesPerSecond, kLowSpeedTimeoutSeconds},
                BuildSslOptions(tlsSettings_))
            : cpr::Post(
                cpr::Url{url},
                cpr::Multipart{{"file", cpr::File{filePath}}},
                AddTraceParentHeader(cpr::Header{}),
                cpr::ConnectTimeout{kConnectTimeout},
                cpr::LowSpeed{kLowSpeedBytesPerSecond, kLowSpeedTimeoutSeconds});

        const bool requestOk = response.error.code == cpr::ErrorCode::OK;
        const bool statusOk = IsSuccessStatus(response);
        if (requestOk && statusOk) {
            return true;
        }

        if (attempt + 1 < kMaxRetries) {
            LogRetry(attempt);
            std::this_thread::sleep_for(std::chrono::seconds(BackoffSeconds(attempt)));
            continue;
        }

        if (!requestOk) {
            std::cerr << "[Agent] upload failed: " << response.error.message << std::endl;
        } else {
            std::cerr << "[Agent] upload failed with HTTP " << response.status_code << std::endl;
        }
        return false;
    }

    return false;
}

bool NetworkClient::DownloadSnapshot(const std::string& url, const std::string& outputPath) {
    if (url.empty() || outputPath.empty()) {
        return false;
    }

    std::filesystem::path output = outputPath;
    if (!output.parent_path().empty()) {
        std::error_code error;
        std::filesystem::create_directories(output.parent_path(), error);
        if (error) {
            std::cerr << "[Agent] download failed: " << error.message() << std::endl;
            return false;
        }
    }

    cpr::Response response = tlsSettings_.enabled
        ? cpr::Get(cpr::Url{url}, AddTraceParentHeader(cpr::Header{}), BuildSslOptions(tlsSettings_))
        : cpr::Get(cpr::Url{url}, AddTraceParentHeader(cpr::Header{}));

    if (response.error.code != cpr::ErrorCode::OK) {
        std::cerr << "[Agent] download failed: " << response.error.message << std::endl;
        return false;
    }

    if (response.status_code >= 400) {
        std::cerr << "[Agent] download failed with HTTP " << response.status_code << std::endl;
        return false;
    }

    std::ofstream outputFile(outputPath, std::ios::binary);
    if (!outputFile) {
        std::cerr << "[Agent] download failed: unable to write " << outputPath << std::endl;
        return false;
    }

    outputFile.write(response.text.data(), static_cast<std::streamsize>(response.text.size()));
    return outputFile.good();
}
