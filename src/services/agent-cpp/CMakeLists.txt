cmake_minimum_required(VERSION 3.10)
project(AetherAgent)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(CURL REQUIRED)
include_directories(${CURL_INCLUDE_DIRS})

add_executable(AetherAgent
    main.cpp
    SysMonitor.cpp
    NetworkClient.cpp
)

target_link_libraries(AetherAgent ${CURL_LIBRARIES})
