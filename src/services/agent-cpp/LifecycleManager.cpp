#include "LifecycleManager.hpp"

#include "NetworkClient.hpp"

#include <chrono>
#include <ctime>
#include <exception>
#include <filesystem>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <utility>

LifecycleManager::LifecycleManager(NetworkClient& client, std::string orchestratorBaseUrl)
    : client_(client),
      orchestratorBaseUrl_(std::move(orchestratorBaseUrl)) {}

bool LifecycleManager::PreFlightCheck() const {
    constexpr long long minDiskBytes = 1LL * 1024 * 1024 * 1024;
    const long long availableDiskBytes = 2LL * 1024 * 1024 * 1024;
    const std::string criuVersion = "3.15";

    const bool diskOk = availableDiskBytes > minDiskBytes;
    const bool criuOk = !criuVersion.empty();
    return diskOk && criuOk;
}

std::string LifecycleManager::Checkpoint(const std::string& workloadId) {
    if (workloadId.empty()) {
        return {};
    }

    const std::string safeWorkloadId = SanitizeWorkloadId(workloadId);
    const std::string timestamp = FormatTimestamp();

    std::filesystem::path baseDir;
    std::filesystem::path imagesDir;
    std::filesystem::path archivePath;

    try {
        baseDir = std::filesystem::temp_directory_path() / "aether-guard" / "snapshots" / safeWorkloadId;
        std::filesystem::create_directories(baseDir);
        imagesDir = baseDir / timestamp;
        std::filesystem::create_directories(imagesDir);
        archivePath = baseDir / (timestamp + ".tar.gz");
    } catch (const std::filesystem::filesystem_error& ex) {
        std::cerr << "[Agent] Snapshot path error: " << ex.what() << std::endl;
        return {};
    }

    const int pid = ParsePid(workloadId);
    if (!criu_.Dump(pid, imagesDir.string())) {
        std::cerr << "[Agent] CRIU dump failed for workload " << workloadId << std::endl;
        return {};
    }

    if (!archive_.Compress(imagesDir.string(), archivePath.string())) {
        std::cerr << "[Agent] Snapshot compression failed for workload " << workloadId << std::endl;
        return {};
    }

    const std::string uploadUrl = BuildUrl(orchestratorBaseUrl_, "/upload/" + safeWorkloadId);
    if (!client_.UploadSnapshot(uploadUrl, archivePath.string())) {
        std::cerr << "[Agent] Snapshot upload failed for workload " << workloadId << std::endl;
        return {};
    }

    return archivePath.string();
}

bool LifecycleManager::Transfer(const std::string& snapshotPath, const std::string& targetIp) {
    return !snapshotPath.empty() && !targetIp.empty();
}

bool LifecycleManager::Restore(const std::string& snapshotPath) {
    if (snapshotPath.empty()) {
        return false;
    }

    const std::string workloadId = ExtractWorkloadId(snapshotPath);
    if (workloadId.empty()) {
        return false;
    }

    std::filesystem::path downloadDir;
    std::filesystem::path archivePath;
    std::filesystem::path outputDir;

    try {
        downloadDir = std::filesystem::temp_directory_path() / "aether-guard" / "downloads" / workloadId;
        std::filesystem::create_directories(downloadDir);
        archivePath = downloadDir / "latest.tar.gz";
        outputDir = downloadDir / "images";
        std::filesystem::create_directories(outputDir);
    } catch (const std::filesystem::filesystem_error& ex) {
        std::cerr << "[Agent] Restore path error: " << ex.what() << std::endl;
        return false;
    }

    const std::string downloadUrl = BuildUrl(orchestratorBaseUrl_, "/download/" + workloadId);
    if (!client_.DownloadSnapshot(downloadUrl, archivePath.string())) {
        std::cerr << "[Agent] Snapshot download failed for workload " << workloadId << std::endl;
        return false;
    }

    if (!archive_.Decompress(archivePath.string(), outputDir.string())) {
        std::cerr << "[Agent] Snapshot decompression failed for workload " << workloadId << std::endl;
        return false;
    }

    return criu_.Restore(outputDir.string());
}

bool LifecycleManager::Thaw(const std::string& workloadId) {
    return !workloadId.empty();
}

std::string LifecycleManager::BuildUrl(const std::string& baseUrl, const std::string& path) {
    if (baseUrl.empty()) {
        return path;
    }

    if (!path.empty() && path.front() == '/' && baseUrl.back() == '/') {
        return baseUrl.substr(0, baseUrl.size() - 1) + path;
    }

    if (!path.empty() && path.front() != '/' && baseUrl.back() != '/') {
        return baseUrl + "/" + path;
    }

    return baseUrl + path;
}

std::string LifecycleManager::FormatTimestamp() {
    const auto now = std::chrono::system_clock::now();
    const auto nowTime = std::chrono::system_clock::to_time_t(now);
    std::tm utcTime = {};
#ifdef _WIN32
    gmtime_s(&utcTime, &nowTime);
#else
    gmtime_r(&nowTime, &utcTime);
#endif

    std::ostringstream output;
    output << std::put_time(&utcTime, "%Y%m%d%H%M%S");
    return output.str();
}

std::string LifecycleManager::SanitizeWorkloadId(const std::string& workloadId) {
    std::string safe = workloadId;
    for (auto& ch : safe) {
        if (ch == '/' || ch == '\\') {
            ch = '_';
        }
    }
    return safe;
}

std::string LifecycleManager::ExtractWorkloadId(const std::string& snapshotPath) {
    std::filesystem::path path(snapshotPath);
    if (!path.parent_path().empty()) {
        const std::string name = path.parent_path().filename().string();
        if (!name.empty()) {
            return name;
        }
    }
    return snapshotPath;
}

int LifecycleManager::ParsePid(const std::string& workloadId) {
    try {
        size_t index = 0;
        const int pid = std::stoi(workloadId, &index);
        if (index == workloadId.size() && pid > 0) {
            return pid;
        }
    } catch (const std::exception&) {
    }

    return 1;
}
