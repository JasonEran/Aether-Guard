#pragma once

#include <functional>
#include <string>

class CriuManager {
public:
    using CommandRunner = std::function<int(const std::string&)>;

    explicit CriuManager(CommandRunner runner = CommandRunner());

    bool Dump(int pid, const std::string& outputDir) const;
    bool Restore(const std::string& inputDir) const;

    static std::string BuildDumpCommand(int pid, const std::string& outputDir);
    static std::string BuildRestoreCommand(const std::string& inputDir);

private:
    int Run(const std::string& command) const;

    CommandRunner runner_;
};
