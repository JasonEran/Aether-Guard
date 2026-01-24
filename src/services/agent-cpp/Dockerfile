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
COPY CMakeLists.txt .
COPY *.cpp .
COPY *.hpp .
COPY tests ./tests

RUN cmake . && make

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
COPY --from=builder /src/AetherAgent ./AetherAgent
COPY --from=builder /src/_deps/cpr-build/cpr/libcpr.so* /usr/lib/
RUN ldconfig

CMD ["./AetherAgent"]
