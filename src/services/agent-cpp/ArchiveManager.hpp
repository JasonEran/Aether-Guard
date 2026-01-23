#pragma once

#include <functional>
#include <string>

class ArchiveManager {
public:
    using CommandRunner = std::function<int(const std::string&)>;

    explicit ArchiveManager(CommandRunner runner = CommandRunner());

    bool Compress(const std::string& dir, const std::string& tarPath) const;
    bool Decompress(const std::string& tarPath, const std::string& outputDir) const;

    static std::string BuildCompressCommand(const std::string& dir, const std::string& tarPath);
    static std::string BuildDecompressCommand(const std::string& tarPath, const std::string& outputDir);

private:
    int Run(const std::string& command) const;

    CommandRunner runner_;
};
