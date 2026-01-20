# =============================================================================
# Dockerfile.builder for Ando Build Containers
#
# Creates a Docker image that can be used to run Ando builds. This image
# contains the .NET SDK and the Ando CLI tool pre-installed.
#
# Build: docker build -t ando-builder -f src/Ando.Server/Dockerfile.builder .
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Build Ando CLI
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files for better layer caching
COPY src/Ando/Ando.csproj src/Ando/

# Restore dependencies
RUN dotnet restore src/Ando/Ando.csproj

# Copy all source code
COPY src/Ando/ src/Ando/

# Build and publish Ando CLI
WORKDIR /src/src/Ando
RUN dotnet publish -c Release -o /ando --self-contained false

# -----------------------------------------------------------------------------
# Stage 2: Builder Runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine

# Install git (needed for some build operations)
RUN apk add --no-cache git

# Copy Ando CLI
COPY --from=build /ando /ando

# Create a wrapper script for easier invocation
RUN echo '#!/bin/sh' > /usr/local/bin/ando && \
    echo 'exec dotnet /ando/ando.dll "$@"' >> /usr/local/bin/ando && \
    chmod +x /usr/local/bin/ando

# Set working directory
WORKDIR /workspace

# No entrypoint - the server will run commands via docker exec
# The container is started with -d and uses sleep to stay alive
CMD ["sleep", "infinity"]
