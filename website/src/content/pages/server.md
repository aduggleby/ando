---
title: ANDO CI Server
description: Self-hosted CI/CD server for ANDO build scripts with GitHub integration.
toc: true
---

## Overview

The ANDO CI Server runs your `build.csando` scripts automatically when you push to GitHub. It provides a web interface for monitoring builds, viewing logs, and managing projects.

### Features

- **GitHub App Integration**: Receives webhooks on push events and reports build status back to GitHub.
- **Rootless Docker**: Runs under rootless Docker for enhanced security. Build containers are isolated.
- **Automatic HTTPS**: Caddy reverse proxy automatically obtains and renews Let's Encrypt certificates.
- **Automated Backups**: Daily backups with 7-day daily + 12-month monthly retention.

## Installation

### Linux/Ubuntu Server

Run the installer from your local machine:

```bash
curl -fsSL https://andobuild.com/server-install.sh | bash -s user@your-server-ip
```

#### Install Options

```bash
# Build image locally instead of pulling from ghcr.io
./server-install.sh --build-local user@your-server-ip

# Use an existing SQL Server instead of deploying a container
./server-install.sh --external-sql user@your-server-ip
```

### Create SQL Database and User (External SQL)

If you install with `--external-sql`, create a dedicated database and login first.

Connect to your SQL Server using any SQL client (Azure Data Studio, SSMS, `sqlcmd`, etc.):

```bash
# Example using sqlcmd
sqlcmd -S YOUR_SERVER_IP,1433 -U sa -P 'YOUR_SA_PASSWORD' -C
```

Run these SQL commands to create the database and dedicated login for ANDO:

```sql
-- Create database
CREATE DATABASE AndoServer;
GO

-- Create login with a strong password
CREATE LOGIN ando WITH PASSWORD = 'YourSecurePassword123!';
GO

-- Create user and grant permissions
USE AndoServer;
GO

CREATE USER ando FOR LOGIN ando;
GO

ALTER ROLE db_owner ADD MEMBER ando;
GO
```

Type `exit` to quit `sqlcmd`.

Then set your server connection string in `/opt/ando/config/.env`:

```bash
ConnectionStrings__DefaultConnection=Server=YOUR_SERVER_IP,1433;Database=AndoServer;User Id=ando;Password=YourSecurePassword123!;TrustServerCertificate=true;Encrypt=true
```

### TrueNAS SCALE

For TrueNAS SCALE installations, see the dedicated guide: **[TrueNAS Installation](/server-truenas)**

This guide covers installing ANDO as a custom app using the TrueNAS Apps interface.

## GitHub App Configuration

Create a GitHub App with these settings:

| Setting | Value |
|---------|-------|
| Homepage URL | `https://your-domain.com` |
| Callback URL | `https://your-domain.com/auth/github/callback` |
| Webhook URL | `https://your-domain.com/webhooks/github` |

### Required Permissions

| Permission | Access |
|------------|--------|
| Contents | Read |
| Metadata | Read |
| Commit statuses | Read and write |

### Events

Subscribe to: Push, Pull request

## Server Configuration

Configure the server's public URL in your `.env` file. This is required for generating links in emails (verification, password reset).

```bash
Server__BaseUrl=https://ci.yourdomain.com
```

The URL must include the scheme (`https://`) and should not have a trailing slash.

### Docker-in-Docker Builds

When a build requires DIND mode (e.g., `Docker.Build`, `Docker.Push`), the server automatically installs the Docker CLI inside the build container if it is not already present. This supports Alpine and Debian/Ubuntu-based images. You do not need to call `Docker.Install()` in your build script when running on the CI server — though it is harmless to include.

### Git Identity in Build Containers

The CI server automatically configures a git committer identity (`user.name` and `user.email`) inside build containers so that operations like `Git.Tag()` (annotated tags) work without manual setup.

**Defaults:** `Ando Server` / `ando-server@localhost`

**Override via environment variables** (checked in order of priority):
1. `GIT_COMMITTER_NAME` / `GIT_COMMITTER_EMAIL` (standard git env vars)
2. `GIT_AUTHOR_NAME` / `GIT_AUTHOR_EMAIL`
3. `GIT_USER_NAME` / `GIT_USER_EMAIL`

Set these in your `.env.ando` file or CI environment to customize the identity used for tags and commits in your builds. If `git config user.name` is already set in the container image, the server will not override it.

### Docker Security

By default, ANDO CI Server validates that Docker is running in rootless mode for enhanced security. On platforms where Docker runs as root and cannot be configured for rootless mode (such as TrueNAS SCALE), you can bypass this check:

```bash
Build__AcknowledgeRootDockerRisk=true
```

**Warning**: Running Docker as root allows container escapes to gain root access to the host system. Only use this option on platforms that require it.

### Optional In-App Self-Update (Admin)

ANDO can optionally show admins when a newer `ghcr.io/aduggleby/ando-server:latest` image is available and let them trigger an update from the web UI.

Enable it in `/opt/ando/config/.env`:

```bash
SelfUpdate__Enabled=true
```

Optional overrides:

```bash
SelfUpdate__Image=ghcr.io/aduggleby/ando-server:latest
SelfUpdate__ComposeFilePath=/opt/ando/docker-compose.yml
SelfUpdate__ServiceName=ando-server
SelfUpdate__ContainerName=ando-server
SelfUpdate__CheckIntervalMinutes=5
SelfUpdate__HelperImage=docker:27-cli
```

How it works:

- The server checks for updates every 5 minutes (configurable).
- If a newer image is found, admins see an update bar in the UI.
- Clicking update queues a background job that starts a helper Docker CLI container to run:
  - `docker compose pull ando-server`
  - `docker compose up -d ando-server`

If disabled (default), no update checks or update UI are shown.

### User Registration

By default, anyone can register an account on the server. The first user to register automatically becomes an admin. Admins can disable new user self-registration from the Admin panel, which toggles the setting in the database (`SystemSettings.AllowUserRegistration`). When registration is disabled, the `/api/auth/register` endpoint returns an error for all users except the first (initial setup is always allowed).

### Rate Limiting

The server applies per-IP sliding-window rate limits to protect against abuse. Rate limiting is enabled by default. Configure limits in your `.env` file:

| Policy | Env Prefix | Default | Purpose |
|--------|-----------|---------|---------|
| `webhook` | `RateLimiting__Webhook__` | 30 req/60s | GitHub webhook endpoint |
| `api` | `RateLimiting__Api__` | 100 req/60s | Authenticated API endpoints (partitioned by user when logged in) |
| `auth` | `RateLimiting__Auth__` | 10 req/60s | Legacy auth fallback |
| `auth-sensitive` | `RateLimiting__AuthSensitive__` | 6 req/60s | Login, register, password reset |
| `auth-verification` | `RateLimiting__AuthVerification__` | 20 req/60s | Email verification, resend |

Each policy supports three settings: `PermitLimit`, `WindowSeconds`, and `QueueLimit`. Example override:

```bash
RateLimiting__AuthSensitive__PermitLimit=10
RateLimiting__AuthSensitive__WindowSeconds=120
```

To disable rate limiting entirely:

```bash
RateLimiting__Enabled=false
```

## Email Configuration

ANDO requires an email service for user registration, password reset, and build failure notifications. Configure one of the following providers in your `.env` file:

### Option A: Resend-Compatible API (Recommended)

```bash
Email__Provider=Resend
Email__FromAddress=noreply@yourdomain.com
Email__Resend__ApiKey=your_api_key
Email__Resend__BaseUrl=https://api.selfmx.com/
```

Works with any Resend-compatible email API such as [SelfMX](https://selfmx.com). For the official Resend service, omit `BaseUrl` or set it to `https://api.resend.com/`.

### Option B: SMTP

```bash
Email__Provider=Smtp
Email__FromAddress=noreply@yourdomain.com
Email__Smtp__Host=smtp.yourdomain.com
Email__Smtp__Port=587
Email__Smtp__Username=your-username
Email__Smtp__Password=your-password
Email__Smtp__UseSsl=true
```

Works with any SMTP provider (Gmail, SendGrid, Mailgun, your own mail server, etc.)

## API Tokens

Personal API tokens allow programmatic access to the ANDO CI Server REST API. Use tokens for CI scripts, automation, or any tool that needs to authenticate without a browser session.

### Creating a Token

Create tokens via the REST API (requires cookie-based authentication first):

```bash
# Login to get a session cookie
curl -c cookies.txt -X POST https://ci.yourdomain.com/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"email":"you@example.com","password":"..."}'

# Create a token
curl -b cookies.txt -X POST https://ci.yourdomain.com/api/auth/tokens \
  -H 'Content-Type: application/json' \
  -d '{"name":"CI automation"}'
```

The response includes the token value **once** — store it securely. Tokens have the format `ando_pat_<random>`.

### Authenticating with a Token

Pass the token via the `Authorization` header or the `X-Api-Token` header:

```bash
# Using Authorization header
curl -H 'Authorization: Bearer ando_pat_...' https://ci.yourdomain.com/api/projects

# Using X-Api-Token header
curl -H 'X-Api-Token: ando_pat_...' https://ci.yourdomain.com/api/projects
```

### Token Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/auth/tokens` | Create a new token. Body: `{"name":"..."}` |
| `GET` | `/api/auth/tokens` | List all your tokens (metadata only, not the raw value). |
| `DELETE` | `/api/auth/tokens/{id}` | Revoke a token. |

### Helper Script

A helper script is included for creating tokens via Playwright's API context:

```bash
ANDO_BASE_URL=https://ci.yourdomain.com \
ANDO_EMAIL=you@example.com \
ANDO_PASSWORD=... \
ANDO_TOKEN_NAME="CI automation" \
node tests/Ando.Server.E2E/tools/create-api-token.js
```

## Server Management

Management scripts installed to `/opt/ando/scripts/`:

```bash
sudo -u ando /opt/ando/scripts/status.sh   # View container status
sudo -u ando /opt/ando/scripts/logs.sh     # View logs
sudo -u ando /opt/ando/scripts/restart.sh  # Restart services
sudo -u ando /opt/ando/scripts/update.sh   # Pull latest and restart
sudo -u ando /opt/ando/scripts/backup.sh   # Run backup now
```

## File Locations

| Path | Description |
|------|-------------|
| `/opt/ando/docker-compose.yml` | Docker Compose configuration |
| `/opt/ando/config/.env` | Environment configuration |
| `/opt/ando/scripts/` | Management scripts |
| `/opt/ando/backups/` | Automated backups |
| `/opt/ando/data/sqldata/` | SQL Server database |
| `/opt/ando/data/keys/` | ASP.NET Data Protection keys (auth sessions) |
| `/opt/ando/data/artifacts/` | Build artifacts |

## Full Documentation

https://github.com/aduggleby/ando/blob/main/src/Ando.Server/README.md
