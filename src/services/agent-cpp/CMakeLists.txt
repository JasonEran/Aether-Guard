cmake_minimum_required(VERSION 3.14)
project(AetherAgent)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)

include(FetchContent)

option(AETHER_ENABLE_GRPC "Build gRPC stubs for agent protos." ON)

if(WIN32)
    set(CPR_USE_SYSTEM_CURL OFF CACHE BOOL "" FORCE)
    set(CURL_ZLIB OFF CACHE STRING "" FORCE)
else()
    set(CPR_USE_SYSTEM_CURL ON CACHE BOOL "" FORCE)
endif()
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

    FetchContent_Declare(
        grpc
        GIT_REPOSITORY https://github.com/grpc/grpc.git
        GIT_TAG v1.63.2
    )
endif()

set(protobuf_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)

if(AETHER_ENABLE_GRPC)
    FetchContent_MakeAvailable(nlohmann_json cpr grpc)
else()
    FetchContent_Declare(
        protobuf
        GIT_REPOSITORY https://github.com/protocolbuffers/protobuf.git
        GIT_TAG v25.3
    )
    FetchContent_MakeAvailable(nlohmann_json cpr protobuf)
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
)

target_link_libraries(AetherAgent
    cpr::cpr
    nlohmann_json::nlohmann_json
    aether_proto
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
