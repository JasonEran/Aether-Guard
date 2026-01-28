# Multi-stage build for the C++ agent
FROM ubuntu:22.04 AS builder

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        git \
        libcurl4-openssl-dev \
        libssl-dev \
        cmake \
        g++ \
        build-essential \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY src/services/agent-cpp/CMakeLists.txt /src/services/agent-cpp/
COPY src/services/agent-cpp/*.cpp /src/services/agent-cpp/
COPY src/services/agent-cpp/*.hpp /src/services/agent-cpp/
COPY src/services/agent-cpp/tests /src/services/agent-cpp/tests
COPY src/shared/protos /src/shared/protos

WORKDIR /src/services/agent-cpp
RUN cmake -S . -B build -DCMAKE_BUILD_TYPE=Release -DAETHER_ENABLE_GRPC=OFF \
    && cmake --build build --parallel 2

FROM ubuntu:22.04 AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        criu \
        iproute2 \
        libstdc++6 \
        libgcc-s1 \
        libcurl4 \
        libssl3 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=builder /src/services/agent-cpp/build/AetherAgent ./AetherAgent
COPY --from=builder /src/services/agent-cpp/build/_deps/cpr-build/cpr/libcpr.so* /usr/lib/
RUN ldconfig

CMD ["./AetherAgent"]
