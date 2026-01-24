#include "CriuManager.hpp"

#include <iostream>
#include <string>

namespace {
int Fail(const std::string& message) {
    std::cerr << message << std::endl;
    return 1;
}
} // namespace

int main() {
    const std::string dumpCommand = CriuManager::BuildDumpCommand(4321, "/tmp/snapshots");
    if (dumpCommand != "criu dump --tree 4321 --images-dir \"/tmp/snapshots\" --shell-job --tcp-established") {
        return Fail("Unexpected dump command: " + dumpCommand);
    }

    const std::string restoreCommand = CriuManager::BuildRestoreCommand("/tmp/snapshots");
    if (restoreCommand != "criu restore --images-dir \"/tmp/snapshots\" --shell-job --tcp-established") {
        return Fail("Unexpected restore command: " + restoreCommand);
    }

    bool invoked = false;
    std::string captured;
    CriuManager manager([&](const std::string& cmd) {
        invoked = true;
        captured = cmd;
        return 0;
    });

    invoked = false;
    if (manager.Dump(0, "/tmp/skip")) {
        return Fail("Dump should fail for invalid PID.");
    }
    if (invoked) {
        return Fail("Runner invoked for invalid dump input.");
    }

    if (!manager.Dump(12, "/tmp/out")) {
        return Fail("Dump should succeed with runner.");
    }
    if (!invoked || captured != CriuManager::BuildDumpCommand(12, "/tmp/out")) {
        return Fail("Runner received unexpected dump command.");
    }

    invoked = false;
    if (!manager.Restore("/tmp/in")) {
        return Fail("Restore should succeed with runner.");
    }
    if (!invoked || captured != CriuManager::BuildRestoreCommand("/tmp/in")) {
        return Fail("Runner received unexpected restore command.");
    }

    invoked = false;
    if (manager.Restore("")) {
        return Fail("Restore should fail for empty input.");
    }
    if (invoked) {
        return Fail("Runner invoked for invalid restore input.");
    }

    return 0;
}
