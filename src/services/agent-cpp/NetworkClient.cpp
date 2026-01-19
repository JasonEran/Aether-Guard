#include "NetworkClient.hpp"

#include <curl/curl.h>

#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <utility>

namespace {
size_t DiscardResponse(char* ptr, size_t size, size_t nmemb, void* userdata) {
    (void)ptr;
    (void)userdata;
    return size * nmemb;
}

bool EnsureCurlInitialized() {
    static bool initialized = []() {
        return curl_global_init(CURL_GLOBAL_DEFAULT) == CURLE_OK;
    }();
    return initialized;
}
} // namespace

NetworkClient::NetworkClient(std::string baseUrl)
    : baseUrl_(std::move(baseUrl)) {}

bool NetworkClient::sendTelemetry(const TelemetryData& data) {
    if (!EnsureCurlInitialized()) {
        std::cerr << "[Agent] curl global init failed" << std::endl;
        return false;
    }

    CURL* curl = curl_easy_init();
    if (!curl) {
        std::cerr << "[Agent] curl init failed" << std::endl;
        return false;
    }

    std::string url = baseUrl_;
    if (!url.empty() && url.back() == '/') {
        url.pop_back();
    }
    url += "/api/v1/Ingestion";

    std::ostringstream payload;
    payload << std::fixed << std::setprecision(1);
    payload << "{\"agentId\":\"" << data.agentId << "\","
            << "\"timestamp\":" << data.timestamp << ","
            << "\"cpuUsage\":" << (data.cpuUsage * 100.0) << ","
            << "\"memoryUsage\":" << data.memoryUsage << ","
            << "\"diskIoUsage\":0,"
            << "\"metadata\":\"\"}";

    std::string payloadStr = payload.str();

    curl_slist* headers = nullptr;
    headers = curl_slist_append(headers, "Content-Type: application/json");

    curl_easy_setopt(curl, CURLOPT_URL, url.c_str());
    curl_easy_setopt(curl, CURLOPT_POST, 1L);
    curl_easy_setopt(curl, CURLOPT_POSTFIELDS, payloadStr.c_str());
    curl_easy_setopt(curl, CURLOPT_POSTFIELDSIZE, static_cast<long>(payloadStr.size()));
    curl_easy_setopt(curl, CURLOPT_HTTPHEADER, headers);
    curl_easy_setopt(curl, CURLOPT_WRITEFUNCTION, DiscardResponse);

    CURLcode result = curl_easy_perform(curl);
    long httpCode = 0;
    if (result == CURLE_OK) {
        curl_easy_getinfo(curl, CURLINFO_RESPONSE_CODE, &httpCode);
    }

    curl_slist_free_all(headers);
    curl_easy_cleanup(curl);

    if (result != CURLE_OK) {
        std::cerr << "[Agent] curl request failed: " << curl_easy_strerror(result) << std::endl;
        return false;
    }

    if (httpCode >= 400) {
        std::cerr << "[Agent] server returned HTTP " << httpCode << std::endl;
        return false;
    }

    return true;
}
