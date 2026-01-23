#include "CommandDispatcher.hpp"

#include <cctype>
#include <iostream>
#include <nlohmann/json.hpp>

namespace {
nlohmann::json ParseParameters(const std::string& parameters) {
    if (parameters.empty()) {
        return nlohmann::json::object();
    }

    auto parsed = nlohmann::json::parse(parameters, nullptr, false);
    if (parsed.is_discarded()) {
        return nlohmann::json::object();
    }

    return parsed;
}
} // namespace

CommandDispatcher::CommandDispatcher(NetworkClient& client, LifecycleManager& lifecycle, std::string agentId)
    : client_(client),
      lifecycle_(lifecycle),
      agentId_(std::move(agentId)) {}

void CommandDispatcher::Dispatch(const CommandPayload& command) {
    if (command.nonce.empty()) {
        ReportResult(command, "FAILED", "Invalid nonce", "Missing nonce");
        return;
    }

    if (IsDuplicateNonce(command.nonce)) {
        ReportResult(command, "COMPLETED", "Duplicate", "Duplicate nonce");
        return;
    }

    const std::string action = ToUpper(command.action);
    const nlohmann::json parameters = ParseParameters(command.parameters);

    const std::string workloadId = command.workloadId.empty() ? command.commandId : command.workloadId;

    if (action == "MIGRATE") {
        if (!lifecycle_.PreFlightCheck()) {
            lifecycle_.Thaw(workloadId);
            ReportResult(command, "FAILED", "Pre-flight check failed", "Pre-flight check failed");
            return;
        }

        const std::string snapshotPath = lifecycle_.Checkpoint(workloadId);
        if (snapshotPath.empty()) {
            lifecycle_.Thaw(workloadId);
            ReportResult(command, "FAILED", "Checkpoint failed", "Snapshot capture failed");
            return;
        }
        const std::string targetIp = parameters.value("targetIp", "");
        const bool transferred = lifecycle_.Transfer(snapshotPath, targetIp);
        const bool restored = lifecycle_.Restore(snapshotPath);

        if (transferred && restored) {
            ReportResult(command, "COMPLETED", "Migrated", "");
        } else {
            lifecycle_.Thaw(workloadId);
            ReportResult(command, "FAILED", "Migration failed", "Transfer/restore failed");
        }
        return;
    }

    if (action == "FREEZE") {
        if (!lifecycle_.PreFlightCheck()) {
            lifecycle_.Thaw(workloadId);
            ReportResult(command, "FAILED", "Pre-flight check failed", "Pre-flight check failed");
            return;
        }

        const std::string snapshotPath = lifecycle_.Checkpoint(workloadId);
        if (snapshotPath.empty()) {
            ReportResult(command, "FAILED", "Freeze failed", "Snapshot capture failed");
            return;
        }
        ReportResult(command, "COMPLETED", "Frozen", "");
        return;
    }

    if (action == "CHECKPOINT") {
        if (!lifecycle_.PreFlightCheck()) {
            lifecycle_.Thaw(workloadId);
            ReportResult(command, "FAILED", "Pre-flight check failed", "Pre-flight check failed");
            return;
        }

        const std::string snapshotPath = lifecycle_.Checkpoint(workloadId);
        if (snapshotPath.empty()) {
            ReportResult(command, "FAILED", "Checkpoint failed", "Snapshot capture failed");
            return;
        }
        ReportResult(command, "COMPLETED", "Checkpointed", "");
        return;
    }

    if (action == "RESTORE") {
        std::string snapshotPath = parameters.value("snapshotUrl", "");
        if (snapshotPath.empty()) {
            snapshotPath = parameters.value("downloadUrl", "");
        }
        if (snapshotPath.empty()) {
            snapshotPath = parameters.value("snapshotPath", "");
        }
        if (snapshotPath.empty()) {
            ReportResult(command, "FAILED", "Restore failed", "snapshotPath missing");
            return;
        }

        const bool restored = lifecycle_.Restore(snapshotPath);
        if (restored) {
            ReportResult(command, "COMPLETED", "Restored", "");
        } else {
            ReportResult(command, "FAILED", "Restore failed", "Restore returned false");
        }
        return;
    }

    ReportResult(command, "FAILED", "Unsupported action", "Unsupported action");
}

void CommandDispatcher::ReportExpired(const CommandPayload& command) {
    ReportResult(command, "FAILED", "Expired", "Command expired");
}

void CommandDispatcher::ReportResult(
    const CommandPayload& command,
    const std::string& status,
    const std::string& result,
    const std::string& error) {
    CommandFeedback feedback;
    feedback.commandId = command.commandId;
    feedback.status = status;
    feedback.result = result;
    feedback.error = error;

    if (!client_.SendFeedback(agentId_, feedback)) {
        std::cerr << "[Agent] Failed to send feedback for command " << command.commandId << std::endl;
    }
}

bool CommandDispatcher::IsDuplicateNonce(const std::string& nonce) {
    std::lock_guard<std::mutex> lock(mutex_);
    auto [it, inserted] = processedNonces_.insert(nonce);
    return !inserted;
}

std::string CommandDispatcher::ToUpper(std::string value) {
    for (auto& ch : value) {
        ch = static_cast<char>(std::toupper(static_cast<unsigned char>(ch)));
    }
    return value;
}
