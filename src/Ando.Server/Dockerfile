# =============================================================================
# Dockerfile for Ando.Server
#
# Multi-stage build that creates a production-ready image for the CI server.
# Includes Docker CLI for Docker-in-Docker build execution.
#
# Build: docker build -t ando-server -f src/Ando.Server/Dockerfile .
# Run: docker run -p 5000:8080 -v /var/run/docker.sock:/var/run/docker.sock ando-server
# =============================================================================

# -----------------------------------------------------------------------------
# Stage 1: Build
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first for better layer caching
COPY src/Ando/Ando.csproj src/Ando/
COPY src/Ando.Server/Ando.Server.csproj src/Ando.Server/

# Restore dependencies
RUN dotnet restore src/Ando.Server/Ando.Server.csproj

# Copy all source code
COPY src/ src/

# Build and publish
WORKDIR /src/src/Ando.Server
RUN dotnet publish -c Release -o /app

# -----------------------------------------------------------------------------
# Stage 2: Runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0

# Install Docker CLI for Docker-in-Docker capability.
# The server needs to spawn Docker containers for builds.
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gnupg && \
    install -m 0755 -d /etc/apt/keyrings && \
    curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc && \
    chmod a+r /etc/apt/keyrings/docker.asc && \
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
        tee /etc/apt/sources.list.d/docker.list > /dev/null && \
    apt-get update && \
    apt-get install -y --no-install-recommends docker-ce-cli && \
    rm -rf /var/lib/apt/lists/*

# Install git for cloning repositories
RUN apt-get update && \
    apt-get install -y --no-install-recommends git && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

# Copy published application
COPY --from=build /app .

# Create directories for data volumes
RUN mkdir -p /data/artifacts /data/repos

# Expose HTTP port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Run the application
ENTRYPOINT ["dotnet", "Ando.Server.dll"]
