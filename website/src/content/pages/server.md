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

### Docker Security

By default, ANDO CI Server validates that Docker is running in rootless mode for enhanced security. On platforms where Docker runs as root and cannot be configured for rootless mode (such as TrueNAS SCALE), you can bypass this check:

```bash
Build__AcknowledgeRootDockerRisk=true
```

**Warning**: Running Docker as root allows container escapes to gain root access to the host system. Only use this option on platforms that require it.

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
| `/opt/ando/data/artifacts/` | Build artifacts |

## Full Documentation

https://github.com/aduggleby/ando/blob/main/src/Ando.Server/README.md
