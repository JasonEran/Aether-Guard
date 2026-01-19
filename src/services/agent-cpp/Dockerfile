# Multi-stage build for the C++ agent
FROM alpine:latest AS builder

RUN apk add --no-cache build-base cmake

WORKDIR /src
COPY CMakeLists.txt .
COPY *.cpp .
COPY *.hpp .

RUN cmake . && make

FROM alpine:latest AS runtime

RUN apk add --no-cache libstdc++ libgcc

WORKDIR /app
COPY --from=builder /src/AetherAgent ./AetherAgent

CMD ["./AetherAgent"]
