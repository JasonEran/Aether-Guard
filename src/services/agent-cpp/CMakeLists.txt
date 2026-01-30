cmake_minimum_required(VERSION 3.14)
project(AetherAgent)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)

include(FetchContent)

option(AETHER_ENABLE_GRPC "Build gRPC stubs for agent protos." ON)
option(AETHER_USE_LOCAL_PROTOBUF "Use third_party/protobuf when available." ON)
option(AETHER_GRPC_USE_ARCHIVE "Use gRPC release archive (no submodules)." OFF)
option(AETHER_ENABLE_OTEL "Enable OpenTelemetry spans for the agent." OFF)

# CMake < 3.24 doesn't understand DOWNLOAD_EXTRACT_TIMESTAMP, which breaks ExternalProject.
set(_AETHER_FC_EXTRACT_TIMESTAMP)
if(CMAKE_VERSION VERSION_GREATER_EQUAL 3.24)
    list(APPEND _AETHER_FC_EXTRACT_TIMESTAMP DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
endif()

if(WIN32)
    set(CPR_USE_SYSTEM_CURL OFF CACHE BOOL "" FORCE)
    set(CURL_ZLIB OFF CACHE STRING "" FORCE)
else()
    set(CPR_USE_SYSTEM_CURL ON CACHE BOOL "" FORCE)
endif()
set(CPR_BUILD_TESTS OFF CACHE BOOL "" FORCE)

FetchContent_Declare(
    nlohmann_json
    URL https://github.com/nlohmann/json/archive/refs/tags/v3.11.2.zip
    URL_HASH SHA256=95651d7d1fcf2e5c3163c3d37df6d6b3e9e5027299e6bd050d157322ceda9ac9
    ${_AETHER_FC_EXTRACT_TIMESTAMP}
    PATCH_COMMAND ${CMAKE_COMMAND}
        -DNLOHMANN_JSON_CMAKE_FILE=<SOURCE_DIR>/CMakeLists.txt
        -P ${CMAKE_CURRENT_SOURCE_DIR}/cmake/patch-nlohmann-json.cmake
)

FetchContent_Declare(
    cpr
    URL https://github.com/libcpr/cpr/archive/refs/tags/1.10.4.zip
    URL_HASH SHA256=faf4f8340eafcb5834202c8dc43d8f0bcbd193fb810b05dc4502d4c67ea4d11d
    ${_AETHER_FC_EXTRACT_TIMESTAMP}
)

if(AETHER_ENABLE_OTEL)
    set(WITH_OTLP_HTTP ON CACHE BOOL "" FORCE)
    set(WITH_OTLP_GRPC OFF CACHE BOOL "" FORCE)
    set(WITH_EXAMPLES OFF CACHE BOOL "" FORCE)
    set(WITH_TESTS OFF CACHE BOOL "" FORCE)
    set(WITH_BENCHMARK OFF CACHE BOOL "" FORCE)
    set(WITH_ABSEIL OFF CACHE BOOL "" FORCE)

    FetchContent_Declare(
        opentelemetry_cpp
        GIT_REPOSITORY https://github.com/open-telemetry/opentelemetry-cpp.git
        GIT_TAG v1.16.1
        GIT_SHALLOW TRUE
    )
endif()

if(AETHER_ENABLE_GRPC)
    set(gRPC_BUILD_TESTS OFF CACHE BOOL "" FORCE)
    set(gRPC_INSTALL OFF CACHE BOOL "" FORCE)
    set(gRPC_BUILD_GRPC_CPP_PLUGIN ON CACHE BOOL "" FORCE)
    set(protobuf_INSTALL OFF CACHE BOOL "" FORCE)
    set(gRPC_ABSL_PROVIDER "module" CACHE STRING "" FORCE)
    set(gRPC_CARES_PROVIDER "module" CACHE STRING "" FORCE)
    set(gRPC_PROTOBUF_PROVIDER "module" CACHE STRING "" FORCE)
    set(gRPC_SSL_PROVIDER "module" CACHE STRING "" FORCE)
    set(gRPC_ZLIB_PROVIDER "module" CACHE STRING "" FORCE)

    if(AETHER_GRPC_USE_ARCHIVE)
        FetchContent_Declare(
            grpc
            URL https://github.com/grpc/grpc/archive/refs/tags/v1.63.2.zip
            URL_HASH SHA256=c2fccc3c1acff569f23c2721dcb058ef2e562784757c76ae965e980a7688657d
            ${_AETHER_FC_EXTRACT_TIMESTAMP}
            PATCH_COMMAND ${CMAKE_COMMAND}
                -DGRPC_ROOT=<SOURCE_DIR>
                -P ${CMAKE_CURRENT_SOURCE_DIR}/cmake/patch-grpc-deps.cmake
        )
    else()
        FetchContent_Declare(
            grpc
            GIT_REPOSITORY https://github.com/grpc/grpc.git
            GIT_TAG v1.63.2
            GIT_SHALLOW TRUE
            GIT_SUBMODULES_RECURSE TRUE
            GIT_PROGRESS TRUE
            PATCH_COMMAND ${CMAKE_COMMAND}
                -DGRPC_ROOT=<SOURCE_DIR>
                -P ${CMAKE_CURRENT_SOURCE_DIR}/cmake/patch-grpc-deps.cmake
        )
    endif()
endif()

set(protobuf_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)

if(AETHER_ENABLE_GRPC)
    if(AETHER_ENABLE_OTEL)
        FetchContent_MakeAvailable(nlohmann_json cpr grpc opentelemetry_cpp)
    else()
        FetchContent_MakeAvailable(nlohmann_json cpr grpc)
    endif()
else()
    set(_LOCAL_PROTOBUF_DIR ${CMAKE_CURRENT_SOURCE_DIR}/../../../third_party/protobuf)
    if(AETHER_USE_LOCAL_PROTOBUF AND EXISTS ${_LOCAL_PROTOBUF_DIR}/CMakeLists.txt)
        FetchContent_Declare(
            protobuf
            SOURCE_DIR ${_LOCAL_PROTOBUF_DIR}
        )
    else()
        FetchContent_Declare(
            protobuf
            GIT_REPOSITORY https://github.com/protocolbuffers/protobuf.git
            GIT_TAG v25.3
            GIT_SHALLOW TRUE
        )
    endif()
    if(AETHER_ENABLE_OTEL)
        FetchContent_MakeAvailable(nlohmann_json cpr protobuf opentelemetry_cpp)
    else()
        FetchContent_MakeAvailable(nlohmann_json cpr protobuf)
    endif()
endif()

if(AETHER_ENABLE_GRPC)
    if(TARGET grpc_cpp_plugin AND NOT TARGET gRPC::grpc_cpp_plugin)
        add_executable(gRPC::grpc_cpp_plugin ALIAS grpc_cpp_plugin)
    endif()

    if(TARGET grpc++ AND NOT TARGET gRPC::grpc++)
        add_library(gRPC::grpc++ ALIAS grpc++)
    endif()
endif()

set(AETHER_PROTO_DIR ${CMAKE_CURRENT_SOURCE_DIR}/../../shared/protos)
set(GENERATED_PROTO_DIR ${CMAKE_CURRENT_BINARY_DIR}/generated)
file(MAKE_DIRECTORY ${GENERATED_PROTO_DIR})

set(AETHER_PROTO_FILES_NO_GRPC
    ${AETHER_PROTO_DIR}/common.proto
    ${AETHER_PROTO_DIR}/google/api/annotations.proto
    ${AETHER_PROTO_DIR}/google/api/http.proto
)

set(PROTO_IMPORT_DIRS
    ${AETHER_PROTO_DIR}
)

if(AETHER_ENABLE_GRPC)
    list(APPEND PROTO_IMPORT_DIRS ${grpc_SOURCE_DIR}/third_party/protobuf/src)
else()
    list(APPEND PROTO_IMPORT_DIRS ${protobuf_SOURCE_DIR}/src)
endif()

if(AETHER_ENABLE_GRPC)
    set(AETHER_PROTO_FILES_WITH_GRPC
        ${AETHER_PROTO_DIR}/agent_service.proto
    )
else()
    list(APPEND AETHER_PROTO_FILES_NO_GRPC ${AETHER_PROTO_DIR}/agent_service.proto)
endif()

foreach(PROTO_DIR IN LISTS PROTO_IMPORT_DIRS)
    list(APPEND PROTO_IMPORT_FLAGS "-I" ${PROTO_DIR})
endforeach()

set(PROTO_SRCS)
set(PROTO_HDRS)

if(AETHER_ENABLE_GRPC)
    foreach(PROTO_FILE IN LISTS AETHER_PROTO_FILES_WITH_GRPC)
        file(RELATIVE_PATH PROTO_REL_PATH ${AETHER_PROTO_DIR} ${PROTO_FILE})
        get_filename_component(PROTO_REL_DIR ${PROTO_REL_PATH} DIRECTORY)
        get_filename_component(PROTO_NAME ${PROTO_REL_PATH} NAME_WE)
        if(PROTO_REL_DIR STREQUAL ".")
            set(PROTO_OUT_DIR ${GENERATED_PROTO_DIR})
        else()
            set(PROTO_OUT_DIR ${GENERATED_PROTO_DIR}/${PROTO_REL_DIR})
        endif()
        file(MAKE_DIRECTORY ${PROTO_OUT_DIR})

        set(PROTO_SRC ${PROTO_OUT_DIR}/${PROTO_NAME}.pb.cc)
        set(PROTO_HDR ${PROTO_OUT_DIR}/${PROTO_NAME}.pb.h)
        set(GRPC_SRC ${PROTO_OUT_DIR}/${PROTO_NAME}.grpc.pb.cc)
        set(GRPC_HDR ${PROTO_OUT_DIR}/${PROTO_NAME}.grpc.pb.h)

        list(APPEND PROTO_SRCS ${PROTO_SRC} ${GRPC_SRC})
        list(APPEND PROTO_HDRS ${PROTO_HDR} ${GRPC_HDR})

        add_custom_command(
            OUTPUT ${PROTO_SRC} ${PROTO_HDR} ${GRPC_SRC} ${GRPC_HDR}
            COMMAND $<TARGET_FILE:protobuf::protoc>
            ARGS --grpc_out ${GENERATED_PROTO_DIR}
                 --cpp_out ${GENERATED_PROTO_DIR}
                 --plugin=protoc-gen-grpc=$<TARGET_FILE:gRPC::grpc_cpp_plugin>
                 ${PROTO_IMPORT_FLAGS}
                 ${PROTO_FILE}
            DEPENDS ${PROTO_FILE} protobuf::protoc gRPC::grpc_cpp_plugin
        )
    endforeach()
endif()

foreach(PROTO_FILE IN LISTS AETHER_PROTO_FILES_NO_GRPC)
    file(RELATIVE_PATH PROTO_REL_PATH ${AETHER_PROTO_DIR} ${PROTO_FILE})
    get_filename_component(PROTO_REL_DIR ${PROTO_REL_PATH} DIRECTORY)
    get_filename_component(PROTO_NAME ${PROTO_REL_PATH} NAME_WE)
    if(PROTO_REL_DIR STREQUAL ".")
        set(PROTO_OUT_DIR ${GENERATED_PROTO_DIR})
    else()
        set(PROTO_OUT_DIR ${GENERATED_PROTO_DIR}/${PROTO_REL_DIR})
    endif()
    file(MAKE_DIRECTORY ${PROTO_OUT_DIR})

    set(PROTO_SRC ${PROTO_OUT_DIR}/${PROTO_NAME}.pb.cc)
    set(PROTO_HDR ${PROTO_OUT_DIR}/${PROTO_NAME}.pb.h)

    list(APPEND PROTO_SRCS ${PROTO_SRC})
    list(APPEND PROTO_HDRS ${PROTO_HDR})

    add_custom_command(
        OUTPUT ${PROTO_SRC} ${PROTO_HDR}
        COMMAND $<TARGET_FILE:protobuf::protoc>
        ARGS --cpp_out ${GENERATED_PROTO_DIR}
             ${PROTO_IMPORT_FLAGS}
             ${PROTO_FILE}
        DEPENDS ${PROTO_FILE} protobuf::protoc
    )
endforeach()

add_library(aether_proto ${PROTO_SRCS} ${PROTO_HDRS})
target_include_directories(aether_proto
    PUBLIC
    ${GENERATED_PROTO_DIR}
    ${AETHER_PROTO_DIR}
)
target_link_libraries(aether_proto
    PUBLIC
    protobuf::libprotobuf
)
if(AETHER_ENABLE_GRPC)
    target_link_libraries(aether_proto
        PUBLIC
        gRPC::grpc++
    )
endif()

add_executable(AetherAgent
    ArchiveManager.cpp
    CriuManager.cpp
    CommandDispatcher.cpp
    CommandPoller.cpp
    main.cpp
    LifecycleManager.cpp
    NetworkClient.cpp
    Tracing.cpp
)

target_link_libraries(AetherAgent
    cpr::cpr
    nlohmann_json::nlohmann_json
    aether_proto
)

target_compile_definitions(AetherAgent
    PRIVATE
    $<$<BOOL:${AETHER_ENABLE_OTEL}>:AETHER_ENABLE_OTEL=1>
)

if(AETHER_ENABLE_OTEL)
    set(_AETHER_OTEL_TARGETS
        opentelemetry::trace
        opentelemetry::sdk
        opentelemetry::resources
        opentelemetry::otlp_http_exporter
        opentelemetry_trace
        opentelemetry_sdk
        opentelemetry_resources
        opentelemetry_exporter_otlp_http
    )
    set(_AETHER_OTEL_LIBS "")
    foreach(_target IN LISTS _AETHER_OTEL_TARGETS)
        if(TARGET ${_target})
            list(APPEND _AETHER_OTEL_LIBS ${_target})
        endif()
    endforeach()
    if(_AETHER_OTEL_LIBS)
        target_link_libraries(AetherAgent ${_AETHER_OTEL_LIBS})
    else()
        message(FATAL_ERROR "AETHER_ENABLE_OTEL is ON but OpenTelemetry targets were not found.")
    endif()
endif()

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
