#pragma once

#include <string>

struct SemanticHeartbeatFeatures {
    bool present = false;
    std::string schemaVersion;
    double sVNegative = 0.33;
    double sVNeutral = 0.34;
    double sVPositive = 0.33;
    double pV = 0.50;
    double bS = 0.50;
    std::string source;
    long long generatedAtUnix = 0;
    bool fallbackUsed = true;
};
