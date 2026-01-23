#include "CommandPoller.hpp"

#include <chrono>
#include <ctime>
#include <iomanip>
#include <iostream>
#include <sstream>

namespace {
std::time_t Timegm(std::tm* tm) {
#ifdef _WIN32
    return _mkgmtime(tm);
#else
    return timegm(tm);
#endif
}

std::string StripFractionalSeconds(std::string value) {
    const auto dotPos = value.find('.');
    if (dotPos == std::string::npos) {
        return value;
    }
    const auto zPos = value.find('Z', dotPos);
    if (zPos == std::string::npos) {
        return value.substr(0, dotPos);
    }
    value.erase(dotPos, zPos - dotPos);
    return value;
}

std::string StripTimezoneOffset(std::string value) {
    const auto tzPos = value.find_first_of("+-", 19);
    if (tzPos != std::string::npos) {
        return value.substr(0, tzPos);
    }
    return value;
}
} // namespace

CommandPoller::CommandPoller(NetworkClient& client, CommandDispatcher& dispatcher, std::string agentId)
    : client_(client),
      dispatcher_(dispatcher),
      agentId_(std::move(agentId)) {}

CommandPoller::~CommandPoller() {
    Stop();
}

void CommandPoller::Start() {
    if (running_.exchange(true)) {
        return;
    }

    worker_ = std::thread(&CommandPoller::Run, this);
}

void CommandPoller::Stop() {
    if (!running_.exchange(false)) {
        return;
    }

    if (worker_.joinable()) {
        worker_.join();
    }
}

void CommandPoller::Run() {
    while (running_) {
        std::vector<CommandPayload> commands;
        if (client_.PollCommands(agentId_, commands)) {
            for (const auto& command : commands) {
                if (IsExpired(command.expiresAt)) {
                    std::cerr << "[Agent] Command expired: " << command.commandId << std::endl;
                    dispatcher_.ReportExpired(command);
                    continue;
                }
                dispatcher_.Dispatch(command);
            }
        }

        std::this_thread::sleep_for(std::chrono::seconds(2));
    }
}

bool CommandPoller::IsExpired(const std::string& expiresAt) const {
    if (expiresAt.empty()) {
        return true;
    }

    std::string normalized = StripFractionalSeconds(expiresAt);
    normalized = StripTimezoneOffset(normalized);
    if (!normalized.empty() && normalized.back() == 'Z') {
        normalized.pop_back();
    }

    std::tm tm = {};
    std::istringstream stream(normalized);
    stream >> std::get_time(&tm, "%Y-%m-%dT%H:%M:%S");
    if (stream.fail()) {
        return true;
    }

    const std::time_t expiresUtc = Timegm(&tm);
    const std::time_t nowUtc = std::time(nullptr);
    return expiresUtc <= nowUtc;
}
