if(NOT DEFINED NLOHMANN_JSON_CMAKE_FILE)
    message(FATAL_ERROR "NLOHMANN_JSON_CMAKE_FILE is not set.")
endif()

if(NOT EXISTS "${NLOHMANN_JSON_CMAKE_FILE}")
    message(FATAL_ERROR "Nlohmann JSON CMakeLists.txt not found at ${NLOHMANN_JSON_CMAKE_FILE}.")
endif()

file(READ "${NLOHMANN_JSON_CMAKE_FILE}" _nlohmann_cmake_contents)

string(REPLACE
    "cmake_minimum_required(VERSION 3.1)"
    "cmake_minimum_required(VERSION 3.5)"
    _nlohmann_cmake_contents
    "${_nlohmann_cmake_contents}")

string(REPLACE
    "cmake_minimum_required(VERSION 3.1...3.14)"
    "cmake_minimum_required(VERSION 3.5...3.14)"
    _nlohmann_cmake_contents
    "${_nlohmann_cmake_contents}")

file(WRITE "${NLOHMANN_JSON_CMAKE_FILE}" "${_nlohmann_cmake_contents}")
