cmake_minimum_required(VERSION 3.14)
project(AetherAgent)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(FetchContent)

set(CPR_USE_SYSTEM_CURL ON)
set(CPR_BUILD_TESTS OFF CACHE BOOL "" FORCE)

FetchContent_Declare(
    nlohmann_json
    GIT_REPOSITORY https://github.com/nlohmann/json.git
    GIT_TAG v3.11.2
)

FetchContent_Declare(
    cpr
    GIT_REPOSITORY https://github.com/libcpr/cpr.git
    GIT_TAG 1.10.4
)

FetchContent_MakeAvailable(nlohmann_json cpr)

add_executable(AetherAgent
    ArchiveManager.cpp
    CriuManager.cpp
    CommandDispatcher.cpp
    CommandPoller.cpp
    main.cpp
    LifecycleManager.cpp
    NetworkClient.cpp
)

target_link_libraries(AetherAgent
    cpr::cpr
    nlohmann_json::nlohmann_json
)

enable_testing()

add_executable(AetherAgentTests
    ArchiveManager.cpp
    CriuManager.cpp
    tests/CriuCommandTests.cpp
)

target_include_directories(AetherAgentTests
    PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
)

add_test(NAME AetherAgentTests COMMAND AetherAgentTests)
