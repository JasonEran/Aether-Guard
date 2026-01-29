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

struct AgentCapabilities {
    std::string kernelVersion;
    std::string criuVersion;
    bool criuAvailable = false;
    bool ebpfAvailable = false;
    bool supportsSnapshot = false;
    bool supportsNetTopology = false;
    bool supportsChaos = false;
};

struct AgentConfig {
    bool enableSnapshot = false;
    bool enableEbpf = false;
    bool enableNetTopology = false;
    bool enableChaos = false;
    std::string nodeMode;
};

struct TlsSettings {
    bool enabled = false;
    std::string certPath;
    std::string keyPath;
    std::string caPath;
    bool verifyPeer = true;
    bool verifyHost = false;
};

class NetworkClient {
public:
    explicit NetworkClient(std::string baseUrl, TlsSettings tlsSettings = {}, std::string apiKey = {});

    bool Register(
        const std::string& hostname,
        const std::string& os,
        const AgentCapabilities& capabilities,
        std::string& outToken,
        std::string& outAgentId,
        AgentConfig* outConfig = nullptr);
    bool SendHeartbeat(
        const std::string& token,
        const std::string& agentId,
        const std::string& state,
        const std::string& tier,
        std::vector<AgentCommand>& outCommands);
    bool PollCommands(const std::string& agentId, std::vector<CommandPayload>& outCommands);
    bool SendFeedback(const std::string& agentId, const CommandFeedback& feedback);
    bool UploadSnapshot(const std::string& url, const std::string& filePath);
    bool DownloadSnapshot(const std::string& url, const std::string& outputPath);

private:
    std::string baseUrl_;
    TlsSettings tlsSettings_;
    std::string apiKey_;
};
