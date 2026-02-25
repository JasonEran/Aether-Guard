#include "InferenceEngine.hpp"

#include <algorithm>
#include <cmath>
#include <filesystem>
#include <sstream>
#include <utility>
#include <vector>

#if defined(AETHER_ENABLE_ONNX_RUNTIME)
#include <onnxruntime/core/session/onnxruntime_cxx_api.h>
#endif

namespace {
constexpr double kDefaultFallbackNeutral = 0.5;
constexpr size_t kFallbackWindow = 24;
constexpr size_t kFallbackChannels = 4;
} // namespace

struct InferenceEngine::OnnxSessionHandle {
#if defined(AETHER_ENABLE_ONNX_RUNTIME)
    Ort::Env env;
    Ort::SessionOptions options;
    std::unique_ptr<Ort::Session> session;
    std::string inputName;
    std::string outputName;
    size_t windowSize = kFallbackWindow;
    size_t channels = kFallbackChannels;

    OnnxSessionHandle()
        : env(ORT_LOGGING_LEVEL_WARNING, "aether-agent-onnx") {}
#endif
};

InferenceEngine::InferenceEngine(InferenceRuntimeConfig config)
    : config_(std::move(config)),
      initStatus_("not_initialized"),
      onnxSession_(std::make_unique<OnnxSessionHandle>()) {}

InferenceEngine::~InferenceEngine() = default;

bool InferenceEngine::Initialize()
{
    if (!config_.enabled) {
        initStatus_ = "feature_gate_disabled";
        return true;
    }

    if (config_.forceV22Fallback) {
        initStatus_ = "rollback_forced_v22_fallback";
        return true;
    }

    if (config_.modelPath.empty()) {
        initStatus_ = "onnx_model_path_missing";
        return config_.failOpen;
    }

    if (!std::filesystem::exists(config_.modelPath)) {
        initStatus_ = "onnx_model_not_found";
        return config_.failOpen;
    }

#if defined(AETHER_ENABLE_ONNX_RUNTIME)
    try {
        onnxSession_->options.SetIntraOpNumThreads(1);
        onnxSession_->options.SetGraphOptimizationLevel(GraphOptimizationLevel::ORT_ENABLE_ALL);
        onnxSession_->session = std::make_unique<Ort::Session>(
            onnxSession_->env,
            config_.modelPath.c_str(),
            onnxSession_->options);

        Ort::AllocatorWithDefaultOptions allocator;
        auto inputName = onnxSession_->session->GetInputNameAllocated(0, allocator);
        auto outputName = onnxSession_->session->GetOutputNameAllocated(0, allocator);
        onnxSession_->inputName = inputName.get();
        onnxSession_->outputName = outputName.get();

        auto inputType = onnxSession_->session->GetInputTypeInfo(0);
        auto tensorInfo = inputType.GetTensorTypeAndShapeInfo();
        auto shape = tensorInfo.GetShape();
        if (shape.size() == 3) {
            if (shape[1] > 0) {
                onnxSession_->windowSize = static_cast<size_t>(shape[1]);
            }
            if (shape[2] > 0) {
                onnxSession_->channels = static_cast<size_t>(shape[2]);
            }
        }

        initStatus_ = "onnx_runtime_ready";
        return true;
    } catch (const std::exception& ex) {
        initStatus_ = std::string("onnx_runtime_init_failed:") + ex.what();
        return config_.failOpen;
    }
#else
    initStatus_ = "compiled_without_onnx_runtime";
    return config_.failOpen;
#endif
}

InferenceDecision InferenceEngine::Evaluate(const SemanticHeartbeatFeatures& semanticFeatures) const
{
    if (!config_.enabled) {
        return EvaluateFallback(semanticFeatures, "feature_gate_disabled");
    }

    if (config_.forceV22Fallback) {
        return EvaluateFallback(semanticFeatures, "rollback_forced_v22_fallback");
    }

#if defined(AETHER_ENABLE_ONNX_RUNTIME)
    if (onnxSession_ && onnxSession_->session) {
        try {
            const auto window = onnxSession_->windowSize;
            const auto channels = onnxSession_->channels;
            std::vector<float> input(window * channels, 0.0f);

            for (size_t t = 0; t < window; ++t) {
                for (size_t c = 0; c < channels; ++c) {
                    double value = kDefaultFallbackNeutral;
                    switch (c % 4) {
                    case 0:
                        value = Clamp01(semanticFeatures.pV);
                        break;
                    case 1:
                        value = Clamp01(semanticFeatures.sVNegative);
                        break;
                    case 2:
                        value = Clamp01(semanticFeatures.sVPositive);
                        break;
                    case 3:
                        value = Clamp01(semanticFeatures.bS);
                        break;
                    default:
                        break;
                    }
                    input[t * channels + c] = static_cast<float>(value);
                }
            }

            const std::vector<int64_t> inputShape{
                1,
                static_cast<int64_t>(window),
                static_cast<int64_t>(channels)};
            auto memoryInfo = Ort::MemoryInfo::CreateCpu(OrtArenaAllocator, OrtMemTypeDefault);
            Ort::Value inputTensor = Ort::Value::CreateTensor<float>(
                memoryInfo,
                input.data(),
                input.size(),
                inputShape.data(),
                inputShape.size());

            const char* inputNames[] = {onnxSession_->inputName.c_str()};
            const char* outputNames[] = {onnxSession_->outputName.c_str()};
            auto outputs = onnxSession_->session->Run(
                Ort::RunOptions{nullptr},
                inputNames,
                &inputTensor,
                1,
                outputNames,
                1);

            if (outputs.empty() || !outputs[0].IsTensor()) {
                return EvaluateFallback(semanticFeatures, "onnx_output_invalid");
            }

            auto* logits = outputs[0].GetTensorMutableData<float>();
            const double probability = Sigmoid(static_cast<double>(logits[0]));
            InferenceDecision decision;
            decision.enabled = true;
            decision.usedOnnxRuntime = true;
            decision.fallbackApplied = false;
            decision.probability = Clamp01(probability);
            decision.shouldPreempt = decision.probability >= config_.decisionThreshold;
            decision.reason = "onnx_runtime";
            return decision;
        } catch (const std::exception& ex) {
            return EvaluateFallback(
                semanticFeatures,
                std::string("onnx_runtime_error:") + ex.what());
        }
    }
#endif

    return EvaluateFallback(semanticFeatures, initStatus_);
}

const std::string& InferenceEngine::InitializationStatus() const
{
    return initStatus_;
}

double InferenceEngine::Clamp01(double value)
{
    if (value < 0.0) {
        return 0.0;
    }
    if (value > 1.0) {
        return 1.0;
    }
    return value;
}

double InferenceEngine::Sigmoid(double value)
{
    return 1.0 / (1.0 + std::exp(-value));
}

InferenceDecision InferenceEngine::EvaluateFallback(
    const SemanticHeartbeatFeatures& semanticFeatures,
    const std::string& reason) const
{
    const double volatility = Clamp01(semanticFeatures.pV);
    const double negative = Clamp01(semanticFeatures.sVNegative);
    const double supplyStress = Clamp01(1.0 - semanticFeatures.bS);
    double probability = 0.60 * volatility + 0.25 * negative + 0.15 * supplyStress;
    if (semanticFeatures.fallbackUsed) {
        probability = std::max(probability, 0.5);
    }

    InferenceDecision decision;
    decision.enabled = config_.enabled && !config_.forceV22Fallback;
    decision.usedOnnxRuntime = false;
    decision.fallbackApplied = true;
    decision.probability = Clamp01(probability);
    decision.shouldPreempt = decision.probability >= config_.decisionThreshold;
    decision.reason = "fallback:" + reason;
    return decision;
}
