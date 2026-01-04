# Ando.Server Deployment Guide

This guide covers deploying Ando.Server (the CI/CD server component) on TrueNAS SCALE with persistent storage, automatic restarts, and all required dependencies.

## Table of Contents

- [Overview](#overview)
- [Prerequisites](#prerequisites)
- [Architecture](#architecture)
- [Step 1: Create Datasets](#step-1-create-datasets)
- [Step 2: Create GitHub App](#step-2-create-github-app)
- [Step 3: Generate Secrets](#step-3-generate-secrets)
- [Step 4: Create Configuration Files](#step-4-create-configuration-files)
- [Step 5: Deploy with Docker Compose](#step-5-deploy-with-docker-compose)
- [Step 6: Configure Reverse Proxy](#step-6-configure-reverse-proxy)
- [Step 7: Verify Deployment](#step-7-verify-deployment)
- [Configuration Reference](#configuration-reference)
- [Maintenance](#maintenance)
- [Troubleshooting](#troubleshooting)

---

## Overview

Ando.Server provides:
- GitHub webhook integration for automatic builds on push/PR events
- Real-time build log streaming via SignalR
- Background job execution via Hangfire
- Build artifact management with retention policies
- Email notifications for build failures (via Resend)
- OAuth authentication via GitHub App

### Components

| Service | Image | Port | Purpose |
|---------|-------|------|---------|
| `ando-server` | Built from source | 8080 | Main application server |
| `sqlserver` | `mcr.microsoft.com/mssql/server:2022-latest` | 1433 | Database (EF Core + Hangfire) |

---

## Prerequisites

- **TrueNAS SCALE** with Docker/Apps support
- **Storage pool** with at least 50GB free space
- **Public IP or domain** for GitHub webhooks
- **GitHub account** with admin access to target repositories

---

## Architecture

```
                    ┌─────────────────────────────────────────────────────┐
                    │                    TrueNAS SCALE                     │
                    │                                                       │
   GitHub           │  ┌─────────────┐      ┌─────────────────────────────┐│
   Webhooks ───────────▶│   Reverse   │──────▶│      ando-server          ││
   (HTTPS)          │  │   Proxy     │      │  ┌─────────────────────────┐││
                    │  │ (Traefik/   │      │  │  ASP.NET Core (8080)    │││
   Users ──────────────▶│  Nginx)     │      │  │  - Web UI               │││
   (HTTPS)          │  └─────────────┘      │  │  - Webhooks             │││
                    │        │              │  │  - SignalR              │││
                    │        │              │  │  - Hangfire             │││
                    │        ▼              │  └───────────┬─────────────┘││
                    │  ┌─────────────┐      │              │              ││
                    │  │   Port      │      │              ▼              ││
                    │  │   5000      │      │  ┌─────────────────────────┐││
                    │  └─────────────┘      │  │  Docker Socket          │││
                    │                       │  │  (Build Containers)     │││
                    │                       │  └─────────────────────────┘││
                    │                       └─────────────────────────────┘│
                    │                                     │                │
                    │                                     ▼                │
                    │                       ┌─────────────────────────────┐│
                    │                       │      sqlserver              ││
                    │                       │  SQL Server 2022 (1433)     ││
                    │                       └─────────────────────────────┘│
                    │                                                       │
                    │  Datasets:                                            │
                    │  ├── /mnt/pool/ando/sqldata     (database files)     │
                    │  ├── /mnt/pool/ando/artifacts   (build artifacts)    │
                    │  ├── /mnt/pool/ando/repos       (git repositories)   │
                    │  └── /mnt/pool/ando/config      (config files)       │
                    └─────────────────────────────────────────────────────┘
```

---

## Step 1: Create Datasets

Create persistent datasets on your TrueNAS pool for data that must survive container restarts.

### Via TrueNAS Web UI

1. Navigate to **Storage** → **Pools**
2. Click the three-dot menu on your pool → **Add Dataset**
3. Create the following datasets:

| Dataset Name | Path | Purpose |
|--------------|------|---------|
| `ando` | `/mnt/pool/ando` | Parent dataset |
| `ando/sqldata` | `/mnt/pool/ando/sqldata` | SQL Server data files |
| `ando/artifacts` | `/mnt/pool/ando/artifacts` | Build artifacts |
| `ando/repos` | `/mnt/pool/ando/repos` | Cloned git repositories |
| `ando/config` | `/mnt/pool/ando/config` | Configuration files |

### Via CLI

```bash
# SSH into TrueNAS
ssh root@truenas

# Create datasets (replace 'tank' with your pool name)
zfs create tank/ando
zfs create tank/ando/sqldata
zfs create tank/ando/artifacts
zfs create tank/ando/repos
zfs create tank/ando/config

# Set permissions for SQL Server (requires specific UID)
chown -R 10001:10001 /mnt/tank/ando/sqldata

# Set permissions for application data
chown -R 1000:1000 /mnt/tank/ando/artifacts
chown -R 1000:1000 /mnt/tank/ando/repos
chown -R 1000:1000 /mnt/tank/ando/config
```

---

## Step 2: Create GitHub App

Ando.Server uses a GitHub App for authentication and webhook integration.

### 2.1 Register the GitHub App

1. Go to **GitHub** → **Settings** → **Developer settings** → **GitHub Apps**
2. Click **New GitHub App**
3. Fill in the form:

| Field | Value |
|-------|-------|
| **GitHub App name** | `Ando CI` (or your preferred name) |
| **Homepage URL** | `https://your-domain.com` |
| **Callback URL** | `https://your-domain.com/auth/github/callback` |
| **Setup URL** | (leave blank) |
| **Webhook URL** | `https://your-domain.com/webhooks/github` |
| **Webhook secret** | Generate a secure random string (save this!) |

### 2.2 Set Permissions

Under **Permissions & events**, set:

**Repository permissions:**
| Permission | Access |
|------------|--------|
| Contents | Read-only |
| Metadata | Read-only |
| Pull requests | Read & write |
| Commit statuses | Read & write |
| Webhooks | Read & write |

**Account permissions:**
| Permission | Access |
|------------|--------|
| Email addresses | Read-only |

**Subscribe to events:**
- [x] Push
- [x] Pull request
- [x] Check run
- [x] Check suite

### 2.3 Generate Private Key

1. After creating the app, scroll to **Private keys**
2. Click **Generate a private key**
3. Save the downloaded `.pem` file as `github-app.pem`
4. Copy it to your config directory:

```bash
cp ~/Downloads/*.private-key.pem /mnt/tank/ando/config/github-app.pem
chmod 600 /mnt/tank/ando/config/github-app.pem
```

### 2.4 Note Your App Credentials

From the GitHub App page, note:
- **App ID** (numeric, e.g., `123456`)
- **Client ID** (e.g., `Iv1.abc123def456`)
- **Client secret** (generate one if not already created)
- **Webhook secret** (the one you set during creation)

---

## Step 3: Generate Secrets

### 3.1 Generate Encryption Key

The encryption key is used to encrypt secrets stored in the database (e.g., repository access tokens).

```bash
# Generate a 32-byte base64-encoded key
openssl rand -base64 32
# Example output: K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=
```

**Save this key securely** - you'll need it for the configuration.

### 3.2 Generate SQL Server Password

```bash
# Generate a strong password (must meet SQL Server complexity requirements)
openssl rand -base64 24 | tr -d '/+=' | head -c 24
# Example output: Xt3mK9pL2nR8vQ5wY7zA
```

The password must contain:
- At least 8 characters
- Uppercase and lowercase letters
- Numbers
- Special characters (add one manually, e.g., `!`)

Example: `Xt3mK9pL2nR8vQ5wY7zA!`

### 3.3 Generate Webhook Secret

```bash
# Generate webhook secret
openssl rand -hex 32
# Example output: a1b2c3d4e5f6...
```

---

## Step 4: Create Configuration Files

### 4.1 Create Environment File

Create `/mnt/tank/ando/config/.env`:

```bash
# =============================================================================
# Ando.Server Environment Configuration
# =============================================================================

# -----------------------------------------------------------------------------
# Environment
# -----------------------------------------------------------------------------
ASPNETCORE_ENVIRONMENT=Production

# -----------------------------------------------------------------------------
# Database
# -----------------------------------------------------------------------------
# SQL Server SA password (must be complex: uppercase, lowercase, number, special char)
SA_PASSWORD=YourSecurePassword123!

# Full connection string (uses SA_PASSWORD above)
# Note: 'sqlserver' is the Docker service name
CONNECTION_STRING=Server=sqlserver;Database=AndoServer;User Id=sa;Password=YourSecurePassword123!;TrustServerCertificate=true;Encrypt=true

# -----------------------------------------------------------------------------
# GitHub App Configuration
# Get these from: https://github.com/settings/apps/YOUR-APP-NAME
# -----------------------------------------------------------------------------
GITHUB_APP_ID=123456
GITHUB_APP_NAME=ando-ci
GITHUB_CLIENT_ID=Iv1.abc123def456
GITHUB_CLIENT_SECRET=your_client_secret_here
GITHUB_WEBHOOK_SECRET=your_webhook_secret_here

# -----------------------------------------------------------------------------
# Encryption
# Generate with: openssl rand -base64 32
# Used to encrypt secrets stored in the database
# -----------------------------------------------------------------------------
ENCRYPTION_KEY=K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=

# -----------------------------------------------------------------------------
# Email Notifications (Optional)
# Sign up at: https://resend.com
# Leave empty to disable email notifications
# -----------------------------------------------------------------------------
RESEND_API_KEY=
RESEND_FROM_ADDRESS=builds@your-domain.com
RESEND_FROM_NAME=Ando CI

# -----------------------------------------------------------------------------
# Build Configuration
# -----------------------------------------------------------------------------
# Default timeout for builds (in minutes)
BUILD_TIMEOUT_MINUTES=15
# Maximum allowed timeout
BUILD_MAX_TIMEOUT_MINUTES=60
# Number of concurrent build workers
BUILD_WORKER_COUNT=2
# Default Docker image for builds
BUILD_DOCKER_IMAGE=mcr.microsoft.com/dotnet/sdk:9.0-alpine

# -----------------------------------------------------------------------------
# Storage Configuration
# -----------------------------------------------------------------------------
# Days to keep build artifacts before cleanup
ARTIFACT_RETENTION_DAYS=30
# Days to keep build logs before cleanup
LOG_RETENTION_DAYS=90
```

**Set secure permissions:**

```bash
chmod 600 /mnt/tank/ando/config/.env
```

### 4.2 Create Docker Compose File

Create `/mnt/tank/ando/config/docker-compose.yml`:

```yaml
# =============================================================================
# Ando.Server Production Deployment for TrueNAS
#
# This configuration provides:
# - Persistent storage mapped to TrueNAS datasets
# - Automatic restart on failure and host reboot
# - Health checks for all services
# - Proper security isolation
#
# Usage:
#   cd /mnt/tank/ando/config
#   docker compose up -d
#   docker compose logs -f ando-server
# =============================================================================

services:
  # ---------------------------------------------------------------------------
  # Ando CI Server
  # ---------------------------------------------------------------------------
  ando-server:
    image: ghcr.io/yourname/ando-server:latest  # Or build locally (see below)
    # Uncomment to build from source instead of using pre-built image:
    # build:
    #   context: /path/to/ando
    #   dockerfile: src/Ando.Server/Dockerfile
    container_name: ando-server
    hostname: ando-server

    # Expose on port 5000 (configure reverse proxy to handle HTTPS)
    ports:
      - "5000:8080"

    # Environment variables from .env file
    environment:
      - ASPNETCORE_ENVIRONMENT=${ASPNETCORE_ENVIRONMENT:-Production}
      - ASPNETCORE_URLS=http://+:8080

      # Database connection
      - ConnectionStrings__DefaultConnection=${CONNECTION_STRING}

      # GitHub App configuration
      - GitHub__AppId=${GITHUB_APP_ID}
      - GitHub__AppName=${GITHUB_APP_NAME}
      - GitHub__ClientId=${GITHUB_CLIENT_ID}
      - GitHub__ClientSecret=${GITHUB_CLIENT_SECRET}
      - GitHub__WebhookSecret=${GITHUB_WEBHOOK_SECRET}
      - GitHub__PrivateKeyPath=/app/github-app.pem

      # Encryption
      - Encryption__Key=${ENCRYPTION_KEY}

      # Email (optional)
      - Resend__ApiKey=${RESEND_API_KEY:-}
      - Resend__FromAddress=${RESEND_FROM_ADDRESS:-builds@localhost}
      - Resend__FromName=${RESEND_FROM_NAME:-Ando CI}

      # Storage paths (inside container)
      - Storage__ArtifactsPath=/data/artifacts
      - Storage__ArtifactRetentionDays=${ARTIFACT_RETENTION_DAYS:-30}
      - Storage__BuildLogRetentionDays=${LOG_RETENTION_DAYS:-90}

      # Build configuration
      - Build__DefaultTimeoutMinutes=${BUILD_TIMEOUT_MINUTES:-15}
      - Build__MaxTimeoutMinutes=${BUILD_MAX_TIMEOUT_MINUTES:-60}
      - Build__DefaultDockerImage=${BUILD_DOCKER_IMAGE:-mcr.microsoft.com/dotnet/sdk:9.0-alpine}
      - Build__WorkerCount=${BUILD_WORKER_COUNT:-2}
      - Build__ReposPath=/data/repos

    # Persistent volume mounts
    volumes:
      # Docker socket for Docker-in-Docker builds
      - /var/run/docker.sock:/var/run/docker.sock:rw

      # Persistent storage - mapped to TrueNAS datasets
      - /mnt/tank/ando/artifacts:/data/artifacts:rw
      - /mnt/tank/ando/repos:/data/repos:rw

      # GitHub App private key (read-only)
      - /mnt/tank/ando/config/github-app.pem:/app/github-app.pem:ro

    # Wait for SQL Server to be healthy before starting
    depends_on:
      sqlserver:
        condition: service_healthy

    # Restart policy - survives host reboots
    restart: unless-stopped

    # Health check
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost:8080/health"]
      interval: 30s
      timeout: 10s
      retries: 3
      start_period: 30s

    # Resource limits (adjust based on your TrueNAS resources)
    deploy:
      resources:
        limits:
          memory: 2G
        reservations:
          memory: 512M

    # Logging configuration
    logging:
      driver: "json-file"
      options:
        max-size: "100m"
        max-file: "5"

    # Security options
    security_opt:
      - no-new-privileges:true

  # ---------------------------------------------------------------------------
  # SQL Server Database
  # ---------------------------------------------------------------------------
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: ando-sqlserver
    hostname: sqlserver

    # Expose SQL Server port (optional - remove if only internal access needed)
    ports:
      - "1433:1433"

    environment:
      - ACCEPT_EULA=Y
      - MSSQL_SA_PASSWORD=${SA_PASSWORD}
      - MSSQL_PID=Developer  # Use 'Express' for production if not licensed
      - MSSQL_AGENT_ENABLED=false
      - MSSQL_COLLATION=SQL_Latin1_General_CP1_CI_AS

    # Persistent storage for database files
    volumes:
      - /mnt/tank/ando/sqldata:/var/opt/mssql:rw

    # Restart policy - survives host reboots
    restart: unless-stopped

    # Health check - ensures SQL Server is ready to accept connections
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "${SA_PASSWORD}" -C -Q "SELECT 1" || exit 1
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 60s  # SQL Server needs time to initialize

    # Resource limits
    deploy:
      resources:
        limits:
          memory: 4G  # SQL Server needs significant memory
        reservations:
          memory: 2G

    # Logging configuration
    logging:
      driver: "json-file"
      options:
        max-size: "100m"
        max-file: "5"

# =============================================================================
# Networks
# =============================================================================
networks:
  default:
    name: ando-network
    driver: bridge
```

---

## Step 5: Deploy with Docker Compose

### 5.1 Pull/Build Images

```bash
cd /mnt/tank/ando/config

# If using pre-built image:
docker compose pull

# If building from source:
docker compose build --no-cache
```

### 5.2 Start Services

```bash
# Start all services in detached mode
docker compose up -d

# Check status
docker compose ps

# View logs
docker compose logs -f
```

### 5.3 Verify Database Initialization

The first startup will:
1. Create the `AndoServer` database
2. Run EF Core migrations
3. Set up Hangfire tables

Check the logs:

```bash
docker compose logs ando-server | grep -i "database\|migration"
```

---

## Step 6: Configure Reverse Proxy

For production, you need HTTPS. Configure your reverse proxy to:
1. Terminate TLS/SSL
2. Forward requests to `http://localhost:5000`
3. Support WebSocket connections (for SignalR)

### Traefik Example (TrueNAS Apps)

If using Traefik (common in TrueNAS SCALE):

```yaml
# Add labels to ando-server service in docker-compose.yml
labels:
  - "traefik.enable=true"
  - "traefik.http.routers.ando.rule=Host(`ci.your-domain.com`)"
  - "traefik.http.routers.ando.entrypoints=websecure"
  - "traefik.http.routers.ando.tls.certresolver=letsencrypt"
  - "traefik.http.services.ando.loadbalancer.server.port=8080"
```

### Nginx Example

```nginx
upstream ando-server {
    server localhost:5000;
}

server {
    listen 443 ssl http2;
    server_name ci.your-domain.com;

    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;

    # Proxy settings
    location / {
        proxy_pass http://ando-server;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # WebSocket support (SignalR)
        proxy_read_timeout 86400;
        proxy_send_timeout 86400;
    }

    # Health check endpoint
    location /health {
        proxy_pass http://ando-server/health;
        access_log off;
    }
}

# Redirect HTTP to HTTPS
server {
    listen 80;
    server_name ci.your-domain.com;
    return 301 https://$server_name$request_uri;
}
```

---

## Step 7: Verify Deployment

### 7.1 Check Health Endpoint

```bash
curl http://localhost:5000/health
# Expected: {"status":"healthy","timestamp":"2026-01-04T12:00:00Z"}
```

### 7.2 Check All Services

```bash
# Check container status
docker compose ps

# Expected output:
# NAME             STATUS                   PORTS
# ando-server      Up X minutes (healthy)   0.0.0.0:5000->8080/tcp
# ando-sqlserver   Up X minutes (healthy)   0.0.0.0:1433->1433/tcp
```

### 7.3 Test GitHub OAuth

1. Navigate to `https://ci.your-domain.com`
2. Click **Login with GitHub**
3. Authorize the GitHub App
4. Verify you're redirected to the dashboard

### 7.4 Test Webhook Delivery

1. Install the GitHub App on a repository
2. Make a commit or open a PR
3. Check the webhook delivery in GitHub App settings
4. Verify build appears in Ando dashboard

---

## Configuration Reference

### Environment Variables

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | No | `Production` | Runtime environment |
| `CONNECTION_STRING` | Yes | - | SQL Server connection string |
| `GITHUB_APP_ID` | Yes | - | GitHub App ID (numeric) |
| `GITHUB_APP_NAME` | Yes | - | GitHub App slug name |
| `GITHUB_CLIENT_ID` | Yes | - | OAuth Client ID |
| `GITHUB_CLIENT_SECRET` | Yes | - | OAuth Client Secret |
| `GITHUB_WEBHOOK_SECRET` | Yes | - | Webhook HMAC secret |
| `ENCRYPTION_KEY` | Yes | - | Base64-encoded 32-byte key |
| `RESEND_API_KEY` | No | - | Resend API key for emails |
| `RESEND_FROM_ADDRESS` | No | `builds@localhost` | Email sender address |
| `RESEND_FROM_NAME` | No | `Ando CI` | Email sender name |
| `ARTIFACT_RETENTION_DAYS` | No | `30` | Days to keep artifacts |
| `LOG_RETENTION_DAYS` | No | `90` | Days to keep build logs |
| `BUILD_TIMEOUT_MINUTES` | No | `15` | Default build timeout |
| `BUILD_MAX_TIMEOUT_MINUTES` | No | `60` | Maximum build timeout |
| `BUILD_WORKER_COUNT` | No | `2` | Concurrent build workers |
| `BUILD_DOCKER_IMAGE` | No | `mcr.microsoft.com/dotnet/sdk:9.0-alpine` | Default build image |

### Volume Mounts

| Container Path | Host Path | Purpose |
|----------------|-----------|---------|
| `/data/artifacts` | `/mnt/tank/ando/artifacts` | Build artifacts |
| `/data/repos` | `/mnt/tank/ando/repos` | Cloned repositories |
| `/var/opt/mssql` | `/mnt/tank/ando/sqldata` | SQL Server data |
| `/app/github-app.pem` | `/mnt/tank/ando/config/github-app.pem` | GitHub App private key |
| `/var/run/docker.sock` | `/var/run/docker.sock` | Docker socket (DinD) |

### Ports

| Port | Service | Protocol | Notes |
|------|---------|----------|-------|
| 5000 | ando-server | HTTP | Main application |
| 1433 | sqlserver | TCP | Database (can be internal-only) |

---

## Maintenance

### Viewing Logs

```bash
cd /mnt/tank/ando/config

# All services
docker compose logs -f

# Specific service
docker compose logs -f ando-server
docker compose logs -f sqlserver

# Last 100 lines
docker compose logs --tail=100 ando-server
```

### Updating

```bash
cd /mnt/tank/ando/config

# Pull latest images
docker compose pull

# Recreate containers with new images
docker compose up -d

# Or if building from source
docker compose build --no-cache
docker compose up -d
```

### Backup

#### Database Backup

```bash
# Create backup directory
mkdir -p /mnt/tank/ando/backups

# Backup SQL Server database
docker exec ando-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "YourPassword" -C \
  -Q "BACKUP DATABASE [AndoServer] TO DISK = N'/var/opt/mssql/backup/AndoServer.bak' WITH FORMAT"

# Copy backup to host
docker cp ando-sqlserver:/var/opt/mssql/backup/AndoServer.bak \
  /mnt/tank/ando/backups/AndoServer_$(date +%Y%m%d).bak
```

#### Full Backup Script

Create `/mnt/tank/ando/config/backup.sh`:

```bash
#!/bin/bash
set -e

BACKUP_DIR="/mnt/tank/ando/backups"
DATE=$(date +%Y%m%d_%H%M%S)

echo "Starting backup at $(date)"

# Create backup directory
mkdir -p "$BACKUP_DIR"

# Backup database
echo "Backing up database..."
docker exec ando-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "${SA_PASSWORD}" -C \
  -Q "BACKUP DATABASE [AndoServer] TO DISK = N'/var/opt/mssql/backup/AndoServer.bak' WITH FORMAT"
docker cp ando-sqlserver:/var/opt/mssql/backup/AndoServer.bak \
  "$BACKUP_DIR/AndoServer_$DATE.bak"

# Backup config files
echo "Backing up config..."
tar -czf "$BACKUP_DIR/config_$DATE.tar.gz" \
  -C /mnt/tank/ando/config .env docker-compose.yml github-app.pem

# Cleanup old backups (keep 7 days)
find "$BACKUP_DIR" -type f -mtime +7 -delete

echo "Backup completed at $(date)"
```

```bash
chmod +x /mnt/tank/ando/config/backup.sh
```

Add to cron for daily backups:

```bash
# Edit crontab
crontab -e

# Add daily backup at 2 AM
0 2 * * * /mnt/tank/ando/config/backup.sh >> /mnt/tank/ando/backups/backup.log 2>&1
```

### Cleanup

```bash
# Remove unused Docker resources
docker system prune -f

# Remove old build containers (created by Ando)
docker container prune -f --filter "label=ando.build=true"

# Remove old build images
docker image prune -f --filter "label=ando.build=true"
```

---

## Troubleshooting

### Container Won't Start

```bash
# Check logs
docker compose logs ando-server

# Common issues:
# - Database connection failed: Check SA_PASSWORD matches
# - GitHub key not found: Verify github-app.pem exists and has correct permissions
# - Port already in use: Change port mapping in docker-compose.yml
```

### Database Connection Issues

```bash
# Test SQL Server connection
docker exec -it ando-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "YourPassword" -C -Q "SELECT 1"

# Check if database exists
docker exec -it ando-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "YourPassword" -C \
  -Q "SELECT name FROM sys.databases"
```

### GitHub OAuth Not Working

1. Verify callback URL in GitHub App settings matches your domain
2. Check `GITHUB_CLIENT_ID` and `GITHUB_CLIENT_SECRET` are correct
3. Ensure HTTPS is working (OAuth requires HTTPS in production)

### Webhooks Not Received

1. Check webhook URL is publicly accessible
2. Verify `GITHUB_WEBHOOK_SECRET` matches what's configured in GitHub
3. Check recent deliveries in GitHub App settings → Advanced → Recent Deliveries

### Builds Not Running

```bash
# Check Hangfire status
docker compose logs ando-server | grep -i hangfire

# Check Docker socket permissions
docker exec ando-server docker ps

# If permission denied, ensure docker.sock is mounted correctly
```

### Permission Issues

```bash
# Fix artifact directory permissions
chown -R 1000:1000 /mnt/tank/ando/artifacts
chown -R 1000:1000 /mnt/tank/ando/repos

# Fix SQL Server directory permissions
chown -R 10001:10001 /mnt/tank/ando/sqldata
```

### Reset Everything

```bash
cd /mnt/tank/ando/config

# Stop and remove containers
docker compose down

# Remove volumes (WARNING: destroys all data)
docker compose down -v

# Remove data directories
rm -rf /mnt/tank/ando/sqldata/*
rm -rf /mnt/tank/ando/artifacts/*
rm -rf /mnt/tank/ando/repos/*

# Start fresh
docker compose up -d
```

---

## Security Considerations

1. **Secrets Management**: Never commit `.env` or `github-app.pem` to version control
2. **HTTPS**: Always use HTTPS in production (configure reverse proxy)
3. **Database**: Consider restricting SQL Server port to internal network only
4. **Docker Socket**: The server has access to the Docker socket - ensure only trusted users have access
5. **Encryption Key**: Keep the encryption key secure - losing it means losing access to encrypted secrets
6. **Firewall**: Only expose necessary ports (5000, or just 443 via reverse proxy)

---

## Quick Reference

```bash
# Start services
cd /mnt/tank/ando/config && docker compose up -d

# Stop services
docker compose down

# View logs
docker compose logs -f

# Restart a service
docker compose restart ando-server

# Check health
curl http://localhost:5000/health

# Enter server container
docker exec -it ando-server bash

# Enter database container
docker exec -it ando-sqlserver bash
```
