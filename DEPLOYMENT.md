# Ando.Server Deployment Guide

This guide covers deploying Ando.Server (the CI/CD server component) on TrueNAS SCALE with persistent storage and automatic restarts. This guide assumes you have an existing SQL Server instance for the database.

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
- Email notifications for build failures (Resend, Azure, or SMTP)
- OAuth authentication via GitHub App

### Components

| Service | Image | Port | Purpose |
|---------|-------|------|---------|
| `ando-server` | Built from source | 8080 | Main application server |

**External Dependencies:**
- SQL Server (existing instance) - Database for EF Core and Hangfire

---

## Prerequisites

- **TrueNAS SCALE** with Docker/Apps support
- **Storage pool** with at least 50GB free space
- **Public IP or domain** for GitHub webhooks
- **GitHub account** with admin access to target repositories
- **SQL Server instance** (2019 or later) with:
  - A database created for Ando (e.g., `AndoServer`)
  - A SQL login with `db_owner` role on that database
  - Network connectivity from the TrueNAS host

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
                    │                       └──────────────┬──────────────┘│
                    │                                      │               │
                    │  Datasets:                           │               │
                    │  ├── /mnt/pool/ando/artifacts        │               │
                    │  ├── /mnt/pool/ando/repos            │               │
                    │  └── /mnt/pool/ando/config           │               │
                    └──────────────────────────────────────┼───────────────┘
                                                           │
                                                           ▼
                                            ┌─────────────────────────────┐
                                            │   Existing SQL Server       │
                                            │   (External)                │
                                            └─────────────────────────────┘
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
| `ando/artifacts` | `/mnt/pool/ando/artifacts` | Build artifacts |
| `ando/repos` | `/mnt/pool/ando/repos` | Cloned git repositories |
| `ando/config` | `/mnt/pool/ando/config` | Configuration files |

### Via TrueNAS Web Shell

```bash
# Create datasets (replace 'tank' with your pool name)
zfs create tank/ando
zfs create tank/ando/artifacts
zfs create tank/ando/repos
zfs create tank/ando/config

# Set permissions for application data (568 is the TrueNAS apps user)
chown -R 568:568 /mnt/tank/ando/artifacts
chown -R 568:568 /mnt/tank/ando/repos
chown -R 568:568 /mnt/tank/ando/config
```

### Finding Your Mount Path

TrueNAS SCALE mounts ZFS datasets at `/mnt/<pool-name>/<dataset-path>`. To find your pool's mount path:

```bash
# List all pools and their mount points
zfs list -o name,mountpoint | grep -v "^NAME"

# Example output:
# tank                    /mnt/tank
# tank/ando               /mnt/tank/ando
# tank/ando/artifacts     /mnt/tank/ando/artifacts
```

**Important:** Update all paths in `docker-compose.yml` to match your pool name. If your pool is named `storage` instead of `tank`, your paths would be:

| Example Pool Name | Mount Path |
|-------------------|------------|
| `tank` | `/mnt/tank/ando` |
| `storage` | `/mnt/storage/ando` |
| `data` | `/mnt/data/ando` |

The `docker-compose.yml` in this guide uses `/mnt/tank/ando` - replace `tank` with your actual pool name throughout.

### Configuring Volumes in TrueNAS Apps

If deploying via the TrueNAS Apps UI instead of docker-compose, configure the storage mounts in the app editor:

![TrueNAS Apps Volume Configuration](images/truenas-volumes.png)

Configure the following host path volumes:

| Container Path | Host Path | Description |
|----------------|-----------|-------------|
| `/data/artifacts` | `/mnt/tank/ando/artifacts` | Build artifacts storage |
| `/data/repos` | `/mnt/tank/ando/repos` | Cloned repositories |
| `/app/config` | `/mnt/tank/ando/config` | Configuration directory |

**Note:** TrueNAS Apps only supports directory mounts, not individual files. Place `github-app.pem` in the config directory and set `GitHub__PrivateKeyPath=/app/config/github-app.pem` in your environment variables.

**Docker Socket Access:** Ando requires access to the Docker socket for running builds. TrueNAS SCALE uses Kubernetes (k3s) for its Apps system, so the Docker socket may not be available. Use docker-compose deployment instead (see [Step 5](#step-5-deploy-with-docker-compose)).

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

### Generate Encryption Key

The encryption key is used to encrypt secrets stored in the database (e.g., repository access tokens).

```bash
# Generate a 32-byte base64-encoded key
openssl rand -base64 32
# Example output: K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=
```

**Save this key securely** - you'll need it for the configuration.

### Generate Webhook Secret

```bash
# Generate webhook secret
openssl rand -hex 32
# Example output: a1b2c3d4e5f6...
```

## Environment Variables

The server validates configuration on startup and shows an error page if anything is missing. See the `.env` file below for all required variables.

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
# Database (External SQL Server)
# -----------------------------------------------------------------------------
# Connection string to your existing SQL Server instance
# Replace with your server hostname, database name, and credentials
#
# Format: Server=<hostname>;Database=<dbname>;User Id=<user>;Password=<password>;TrustServerCertificate=true;Encrypt=true
#
# Examples:
#   - Named instance: Server=sqlserver.local\MSSQLSERVER;Database=AndoServer;...
#   - Default instance: Server=192.168.1.100;Database=AndoServer;...
#   - Azure SQL: Server=yourserver.database.windows.net;Database=AndoServer;...
#
ConnectionStrings__DefaultConnection=Server=your-sql-server.local;Database=AndoServer;User Id=ando_user;Password=YourPassword;TrustServerCertificate=true;Encrypt=true

# -----------------------------------------------------------------------------
# GitHub App Configuration
# Get these from: https://github.com/settings/apps/YOUR-APP-NAME
# -----------------------------------------------------------------------------
GitHub__AppId=123456
GitHub__AppName=ando-ci
GitHub__ClientId=Iv1.abc123def456
GitHub__ClientSecret=your_client_secret_here
GitHub__WebhookSecret=your_webhook_secret_here
GitHub__PrivateKeyPath=/app/github-app.pem

# -----------------------------------------------------------------------------
# Encryption
# Generate with: openssl rand -base64 32
# Used to encrypt secrets stored in the database
# -----------------------------------------------------------------------------
Encryption__Key=K7gNU3sdo+OL0wNhqoVWhr3g6s1xYv72ol/pe/Unols=

# -----------------------------------------------------------------------------
# Email Notifications (Optional)
# Choose a provider: Resend, Azure, or Smtp
# Leave provider-specific fields empty to disable email notifications
# -----------------------------------------------------------------------------
Email__Provider=Resend
Email__FromAddress=builds@your-domain.com
Email__FromName=Ando CI

# Resend provider (https://resend.com)
Email__Resend__ApiKey=

# Azure Email Communication Service
Email__Azure__ConnectionString=

# SMTP provider (direct SMTP connection)
Email__Smtp__Host=
Email__Smtp__Port=587
Email__Smtp__Username=
Email__Smtp__Password=
Email__Smtp__UseSsl=true

# -----------------------------------------------------------------------------
# Build Configuration
# -----------------------------------------------------------------------------
Build__DefaultTimeoutMinutes=15
Build__MaxTimeoutMinutes=60
Build__WorkerCount=2
Build__DefaultDockerImage=mcr.microsoft.com/dotnet/sdk:9.0-alpine
Build__ReposPath=/data/repos

# -----------------------------------------------------------------------------
# Storage Configuration
# -----------------------------------------------------------------------------
Storage__ArtifactsPath=/data/artifacts
Storage__ArtifactRetentionDays=30
Storage__BuildLogRetentionDays=90
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
# - Health checks for the application
# - Proper security isolation
#
# Prerequisites:
# - Existing SQL Server instance with database created
# - .env file configured with ConnectionStrings__DefaultConnection
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

    # Load environment variables from .env file
    # Uses standard .NET configuration syntax (e.g., GitHub__AppId)
    env_file:
      - .env

    # Additional environment variables (overrides .env if needed)
    environment:
      - ASPNETCORE_URLS=http://+:8080

    # Persistent volume mounts
    volumes:
      # Docker socket for Docker-in-Docker builds
      - /var/run/docker.sock:/var/run/docker.sock:rw

      # Persistent storage - mapped to TrueNAS datasets
      - /mnt/tank/ando/artifacts:/data/artifacts:rw
      - /mnt/tank/ando/repos:/data/repos:rw

      # GitHub App private key (read-only)
      - /mnt/tank/ando/config/github-app.pem:/app/github-app.pem:ro

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
1. Run EF Core migrations on your existing database
2. Set up Hangfire tables

Check the logs:

```bash
docker compose logs ando-server | grep -i "database\|migration"
```

**Note:** Ensure your SQL Server user has sufficient permissions to create tables and run migrations.

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

### 7.2 Check Container Status

```bash
# Check container status
docker compose ps

# Expected output:
# NAME             STATUS                   PORTS
# ando-server      Up X minutes (healthy)   0.0.0.0:5000->8080/tcp
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

Environment variables use .NET's standard configuration syntax with `__` (double underscore) as the hierarchy separator.

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | No | `Production` | Runtime environment |
| `ConnectionStrings__DefaultConnection` | Yes | - | SQL Server connection string |
| `GitHub__AppId` | Yes | - | GitHub App ID (numeric) |
| `GitHub__AppName` | Yes | - | GitHub App slug name |
| `GitHub__ClientId` | Yes | - | OAuth Client ID |
| `GitHub__ClientSecret` | Yes | - | OAuth Client Secret |
| `GitHub__WebhookSecret` | Yes | - | Webhook HMAC secret |
| `GitHub__PrivateKeyPath` | Yes | - | Path to GitHub App private key |
| `Encryption__Key` | Yes | - | Base64-encoded 32-byte key |
| `Email__Provider` | No | `Resend` | Email provider: `Resend`, `Azure`, or `Smtp` |
| `Email__FromAddress` | No | - | Email sender address |
| `Email__FromName` | No | `Ando CI` | Email sender name |
| `Email__Resend__ApiKey` | No | - | Resend API key (if using Resend) |
| `Email__Azure__ConnectionString` | No | - | Azure Communication Services connection string |
| `Email__Smtp__Host` | No | - | SMTP server hostname |
| `Email__Smtp__Port` | No | `587` | SMTP server port |
| `Email__Smtp__Username` | No | - | SMTP authentication username |
| `Email__Smtp__Password` | No | - | SMTP authentication password |
| `Email__Smtp__UseSsl` | No | `true` | Use SSL/TLS for SMTP |
| `Storage__ArtifactsPath` | No | `/data/artifacts` | Path to store build artifacts |
| `Storage__ArtifactRetentionDays` | No | `30` | Days to keep artifacts |
| `Storage__BuildLogRetentionDays` | No | `90` | Days to keep build logs |
| `Build__DefaultTimeoutMinutes` | No | `15` | Default build timeout |
| `Build__MaxTimeoutMinutes` | No | `60` | Maximum build timeout |
| `Build__WorkerCount` | No | `2` | Concurrent build workers |
| `Build__DefaultDockerImage` | No | `mcr.microsoft.com/dotnet/sdk:9.0-alpine` | Default build image |
| `Build__ReposPath` | No | `/data/repos` | Path for cloned repositories |

### Connection String Format

The `ConnectionStrings__DefaultConnection` environment variable should follow this format:

```
Server=<hostname>;Database=<database>;User Id=<username>;Password=<password>;TrustServerCertificate=true;Encrypt=true
```

| Component | Description | Example |
|-----------|-------------|---------|
| `Server` | SQL Server hostname (and instance name if applicable) | `sqlserver.local`, `192.168.1.100\MSSQLSERVER` |
| `Database` | Database name | `AndoServer` |
| `User Id` | SQL login username | `ando_user` |
| `Password` | SQL login password | `YourSecurePassword` |
| `TrustServerCertificate` | Set to `true` for self-signed certs | `true` |
| `Encrypt` | Enable encryption | `true` |

### Volume Mounts

| Container Path | Host Path | Purpose |
|----------------|-----------|---------|
| `/data/artifacts` | `/mnt/tank/ando/artifacts` | Build artifacts |
| `/data/repos` | `/mnt/tank/ando/repos` | Cloned repositories |
| `/app/github-app.pem` | `/mnt/tank/ando/config/github-app.pem` | GitHub App private key |
| `/var/run/docker.sock` | `/var/run/docker.sock` | Docker socket (DinD) |

### Ports

| Port | Service | Protocol | Notes |
|------|---------|----------|-------|
| 5000 | ando-server | HTTP | Main application |

---

## Maintenance

### Viewing Logs

```bash
cd /mnt/tank/ando/config

# View logs
docker compose logs -f

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

**Note:** Database backups should be handled by your existing SQL Server backup procedures. The steps below cover backing up Ando-specific configuration and data.

#### Configuration Backup Script

Create `/mnt/tank/ando/config/backup.sh`:

```bash
#!/bin/bash
set -e

BACKUP_DIR="/mnt/tank/ando/backups"
DATE=$(date +%Y%m%d_%H%M%S)

echo "Starting backup at $(date)"

# Create backup directory
mkdir -p "$BACKUP_DIR"

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
# - Database connection failed: Verify ConnectionStrings__DefaultConnection is correct and SQL Server is reachable
# - GitHub key not found: Verify github-app.pem exists and has correct permissions
# - Port already in use: Change port mapping in docker-compose.yml
```

### Database Connection Issues

1. Verify your SQL Server is reachable from the TrueNAS host:
   ```bash
   # Test connectivity (replace with your SQL Server hostname and port)
   nc -zv your-sql-server.local 1433
   ```

2. Check that the database exists and the user has proper permissions
3. Verify the connection string format in your `.env` file
4. Check SQL Server logs for authentication failures

### GitHub OAuth Not Working

1. Verify callback URL in GitHub App settings matches your domain
2. Check `GitHub__ClientId` and `GitHub__ClientSecret` are correct
3. Ensure HTTPS is working (OAuth requires HTTPS in production)

### Webhooks Not Received

1. Check webhook URL is publicly accessible
2. Verify `GitHub__WebhookSecret` matches what's configured in GitHub
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
# Fix artifact directory permissions (568 is the TrueNAS apps user)
chown -R 568:568 /mnt/tank/ando/artifacts
chown -R 568:568 /mnt/tank/ando/repos
```

### Reset Everything

```bash
cd /mnt/tank/ando/config

# Stop and remove containers
docker compose down

# Remove volumes (WARNING: destroys local data)
docker compose down -v

# Remove data directories
rm -rf /mnt/tank/ando/artifacts/*
rm -rf /mnt/tank/ando/repos/*

# Start fresh
docker compose up -d
```

**Note:** This does not affect your external SQL Server database. To reset the database, use your SQL Server management tools.

---

## Security Considerations

1. **Secrets Management**: Never commit `.env` or `github-app.pem` to version control
2. **HTTPS**: Always use HTTPS in production (configure reverse proxy)
3. **Database Credentials**: Use a dedicated SQL login with minimal required permissions (db_owner on the Ando database only)
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

# Restart
docker compose restart ando-server

# Check health
curl http://localhost:5000/health

# Enter container
docker exec -it ando-server bash
```
