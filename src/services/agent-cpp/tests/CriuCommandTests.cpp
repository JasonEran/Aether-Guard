#include "ArchiveManager.hpp"
#include "CriuManager.hpp"

#include <iostream>
#include <string>

namespace {
int RunTests() {
    int failures = 0;

    std::string captured;
    CriuManager criu([&captured](const std::string& command) {
        captured = command;
        return 1;
    });

    if (criu.Dump(123, "/tmp/images") != false) {
        std::cerr << "Expected Dump to fail when runner returns non-zero." << std::endl;
        failures++;
    }

    const std::string expectedDump =
        "criu dump --tree 123 --images-dir \"/tmp/images\" --shell-job --tcp-established";
    if (captured != expectedDump) {
        std::cerr << "Dump command mismatch." << std::endl;
        failures++;
    }

    captured.clear();
    if (criu.Restore("/tmp/images") != false) {
        std::cerr << "Expected Restore to fail when runner returns non-zero." << std::endl;
        failures++;
    }

    const std::string expectedRestore =
        "criu restore --images-dir \"/tmp/images\" --shell-job --tcp-established";
    if (captured != expectedRestore) {
        std::cerr << "Restore command mismatch." << std::endl;
        failures++;
    }

    ArchiveManager archive([&captured](const std::string& command) {
        captured = command;
        return 0;
    });

    if (!archive.Compress("/tmp/images", "/tmp/snapshot.tar.gz")) {
        std::cerr << "Expected Compress to succeed when runner returns zero." << std::endl;
        failures++;
    }

    const std::string expectedCompress =
        "tar -czf \"/tmp/snapshot.tar.gz\" -C \"/tmp/images\" .";
    if (captured != expectedCompress) {
        std::cerr << "Compress command mismatch." << std::endl;
        failures++;
    }

    if (!archive.Decompress("/tmp/snapshot.tar.gz", "/tmp/output")) {
        std::cerr << "Expected Decompress to succeed when runner returns zero." << std::endl;
        failures++;
    }

    const std::string expectedDecompress =
        "tar -xzf \"/tmp/snapshot.tar.gz\" -C \"/tmp/output\"";
    if (captured != expectedDecompress) {
        std::cerr << "Decompress command mismatch." << std::endl;
        failures++;
    }

    return failures;
}
} // namespace

int main() {
    const int failures = RunTests();
    if (failures > 0) {
        std::cerr << "Tests failed: " << failures << std::endl;
        return 1;
    }

    std::cout << "All tests passed." << std::endl;
    return 0;
}
