#include "CriuManager.hpp"

#include <cstdlib>
#include <sstream>
#include <utility>

CriuManager::CriuManager(CommandRunner runner)
    : runner_(std::move(runner)) {}

bool CriuManager::Dump(int pid, const std::string& outputDir) const {
    if (pid <= 0 || outputDir.empty()) {
        return false;
    }

    const std::string command = BuildDumpCommand(pid, outputDir);
    if (command.empty()) {
        return false;
    }

    return Run(command) == 0;
}

bool CriuManager::Restore(const std::string& inputDir) const {
    if (inputDir.empty()) {
        return false;
    }

    const std::string command = BuildRestoreCommand(inputDir);
    if (command.empty()) {
        return false;
    }

    return Run(command) == 0;
}

std::string CriuManager::BuildDumpCommand(int pid, const std::string& outputDir) {
    if (pid <= 0 || outputDir.empty()) {
        return {};
    }

    std::ostringstream command;
    command << "criu dump --tree " << pid
            << " --images-dir \"" << outputDir << "\""
            << " --shell-job --tcp-established";
    return command.str();
}

std::string CriuManager::BuildRestoreCommand(const std::string& inputDir) {
    if (inputDir.empty()) {
        return {};
    }

    std::ostringstream command;
    command << "criu restore --images-dir \"" << inputDir << "\""
            << " --shell-job --tcp-established";
    return command.str();
}

int CriuManager::Run(const std::string& command) const {
    if (runner_) {
        return runner_(command);
    }

    return std::system(command.c_str());
}
