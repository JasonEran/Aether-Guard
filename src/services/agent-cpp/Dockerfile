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

RUN cmake . && make

FROM ubuntu:22.04 AS runtime

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libstdc++6 \
        libgcc-s1 \
        libcurl4 \
        libssl3 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=builder /src/AetherAgent ./AetherAgent

CMD ["./AetherAgent"]
