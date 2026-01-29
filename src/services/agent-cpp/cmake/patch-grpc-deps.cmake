if(NOT DEFINED GRPC_ROOT)
    message(FATAL_ERROR "GRPC_ROOT is not set.")
endif()

set(_care_path "${GRPC_ROOT}/third_party/cares/cares/CMakeLists.txt")
if(EXISTS "${_care_path}")
    file(READ "${_care_path}" _care_contents)
    string(REPLACE
        "CMAKE_MINIMUM_REQUIRED (VERSION 3.1.0)"
        "CMAKE_MINIMUM_REQUIRED (VERSION 3.5.0)"
        _care_contents
        "${_care_contents}")
    string(REPLACE
        "cmake_minimum_required(VERSION 3.1)"
        "cmake_minimum_required(VERSION 3.5)"
        _care_contents
        "${_care_contents}")
    string(REPLACE
        "cmake_minimum_required(VERSION 3.1.0)"
        "cmake_minimum_required(VERSION 3.5.0)"
        _care_contents
        "${_care_contents}")
    file(WRITE "${_care_path}" "${_care_contents}")
endif()
