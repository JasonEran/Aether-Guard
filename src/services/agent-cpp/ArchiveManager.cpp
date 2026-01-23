#include "ArchiveManager.hpp"

#include <cstdlib>
#include <sstream>
#include <utility>

ArchiveManager::ArchiveManager(CommandRunner runner)
    : runner_(std::move(runner)) {}

bool ArchiveManager::Compress(const std::string& dir, const std::string& tarPath) const {
    if (dir.empty() || tarPath.empty()) {
        return false;
    }

    const std::string command = BuildCompressCommand(dir, tarPath);
    if (command.empty()) {
        return false;
    }

    return Run(command) == 0;
}

bool ArchiveManager::Decompress(const std::string& tarPath, const std::string& outputDir) const {
    if (tarPath.empty() || outputDir.empty()) {
        return false;
    }

    const std::string command = BuildDecompressCommand(tarPath, outputDir);
    if (command.empty()) {
        return false;
    }

    return Run(command) == 0;
}

std::string ArchiveManager::BuildCompressCommand(const std::string& dir, const std::string& tarPath) {
    if (dir.empty() || tarPath.empty()) {
        return {};
    }

    std::ostringstream command;
    command << "tar -czf \"" << tarPath << "\" -C \"" << dir << "\" .";
    return command.str();
}

std::string ArchiveManager::BuildDecompressCommand(const std::string& tarPath, const std::string& outputDir) {
    if (tarPath.empty() || outputDir.empty()) {
        return {};
    }

    std::ostringstream command;
    command << "tar -xzf \"" << tarPath << "\" -C \"" << outputDir << "\"";
    return command.str();
}

int ArchiveManager::Run(const std::string& command) const {
    if (runner_) {
        return runner_(command);
    }

    return std::system(command.c_str());
}
