#include "InferenceEngine.hpp"

#include <iostream>
#include <string>

namespace {
int Fail(const std::string& message) {
    std::cerr << message << std::endl;
    return 1;
}
} // namespace

int main() {
    SemanticHeartbeatFeatures semantic;
    semantic.present = true;
    semantic.sVNegative = 0.8;
    semantic.sVNeutral = 0.1;
    semantic.sVPositive = 0.1;
    semantic.pV = 0.9;
    semantic.bS = 0.1;
    semantic.fallbackUsed = false;

    {
        InferenceRuntimeConfig config;
        config.enabled = false;
        InferenceEngine engine(config);
        if (!engine.Initialize()) {
            return Fail("Initialization should succeed when feature gate is disabled.");
        }
        const auto decision = engine.Evaluate(semantic);
        if (decision.enabled) {
            return Fail("Decision should be marked disabled when feature gate is off.");
        }
        if (!decision.fallbackApplied) {
            return Fail("Fallback should be applied when feature gate is disabled.");
        }
    }

    {
        InferenceRuntimeConfig config;
        config.enabled = true;
        config.forceV22Fallback = true;
        InferenceEngine engine(config);
        if (!engine.Initialize()) {
            return Fail("Initialization should succeed when rollback fallback is forced.");
        }
        const auto decision = engine.Evaluate(semantic);
        if (!decision.fallbackApplied) {
            return Fail("Rollback should force fallback mode.");
        }
        if (decision.reason.find("rollback_forced_v22_fallback") == std::string::npos) {
            return Fail("Expected rollback reason in fallback decision.");
        }
    }

    {
        InferenceRuntimeConfig config;
        config.enabled = true;
        config.failOpen = true;
        config.decisionThreshold = 0.7;
        config.modelPath = "missing-model.onnx";
        InferenceEngine engine(config);
        if (!engine.Initialize()) {
            return Fail("Initialization should fail-open when model file is missing.");
        }
        const auto decision = engine.Evaluate(semantic);
        if (!decision.fallbackApplied) {
            return Fail("Missing model should use fallback inference.");
        }
        if (!decision.shouldPreempt) {
            return Fail("High-risk semantic vector should trigger preempt recommendation.");
        }
    }

    return 0;
}
