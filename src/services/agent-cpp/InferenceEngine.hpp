#pragma once

#include "SemanticFeatures.hpp"

#include <memory>
#include <string>

struct InferenceRuntimeConfig {
    bool enabled = false;
    bool forceV22Fallback = false;
    bool failOpen = true;
    double decisionThreshold = 0.65;
    std::string modelPath;
};

struct InferenceDecision {
    bool enabled = false;
    bool usedOnnxRuntime = false;
    bool fallbackApplied = true;
    bool shouldPreempt = false;
    double probability = 0.0;
    std::string reason;
};

class InferenceEngine {
public:
    explicit InferenceEngine(InferenceRuntimeConfig config);
    ~InferenceEngine();

    bool Initialize();
    InferenceDecision Evaluate(const SemanticHeartbeatFeatures& semanticFeatures) const;
    const std::string& InitializationStatus() const;

private:
    InferenceRuntimeConfig config_;
    mutable std::string initStatus_;

    struct OnnxSessionHandle;
    std::unique_ptr<OnnxSessionHandle> onnxSession_;

    static double Clamp01(double value);
    static double Sigmoid(double value);
    InferenceDecision EvaluateFallback(
        const SemanticHeartbeatFeatures& semanticFeatures,
        const std::string& reason) const;
};
