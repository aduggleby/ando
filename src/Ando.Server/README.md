# Ando.Server Deployment Guide

This guide covers deploying Ando.Server to a fresh Ubuntu server.

## Prerequisites

- **Ubuntu 22.04+** (24.04 LTS recommended)
- A server with at least 2 CPU cores and 4GB RAM
- A domain name pointing to your server's IP address
- A GitHub App configured for webhook integration
- Docker installed on your local machine (only if using `--build-local`)

For detailed prerequisites and GitHub App configuration, see: https://andobuild.com/server

## Quick Start

Run the installer from your local machine (where the Ando source code is):

```bash
curl -fsSL https://andobuild.com/server-install.sh | bash -s user@your-server-ip
```

Or download and run the script manually:

```bash
# Download the script
curl -fsSL https://andobuild.com/server-install.sh -o install-ando-server.sh
chmod +x install-ando-server.sh

# Run it (optionally specify an SSH key)
./install-ando-server.sh user@your-server-ip
./install-ando-server.sh user@your-server-ip ~/.ssh/id_rsa
```

The script will:
1. Prompt for all configuration values (GitHub App credentials, domain, etc.)
2. Install Docker (rootless) and Caddy on the remote server
3. Pull the Docker image from GitHub Container Registry
4. Transfer configuration files
5. Start all services

**Options:**

```bash
# Build image locally instead of pulling from ghcr.io
./install-ando-server.sh --build-local user@your-server-ip

# Use an existing SQL Server instead of deploying a container
./install-ando-server.sh --external-sql user@your-server-ip

# Combine options
./install-ando-server.sh --build-local --external-sql user@your-server-ip
```

## What Gets Installed

The installer sets up:

| Component | Description |
|-----------|-------------|
| **Rootless Docker** | Docker running as unprivileged `ando` user |
| **SQL Server** | Container for database storage |
| **Ando.Server** | The CI server container |
| **Caddy** | Reverse proxy with automatic HTTPS |

## File Locations

All data is stored under `/opt/ando/` on the host and persists across container restarts:

| Path | Description | Backup Priority |
|------|-------------|-----------------|
| `/opt/ando/docker-compose.yml` | Docker Compose configuration | Critical |
| `/opt/ando/config/.env` | Environment (encryption key, credentials) | Critical |
| `/opt/ando/config/github-app.pem` | GitHub App private key | Critical |
| `/opt/ando/scripts/` | Management scripts (update, backup, etc.) | - |
| `/opt/ando/backups/` | Automated backups | Copy offsite |
| `/opt/ando/data/sqldata/` | SQL Server database files | Critical |
| `/opt/ando/data/keys/` | ASP.NET Data Protection keys | Critical |
| `/opt/ando/data/artifacts/` | Build artifacts | Important (regenerable) |
| `/opt/ando/data/repos/` | Cloned repository cache | Optional (re-cloned on build) |
| `/etc/caddy/Caddyfile` | Caddy reverse proxy config | Critical |

To migrate to a new server, copy `/opt/ando/` and `/etc/caddy/Caddyfile` to the new host.

## Backup

The install script automatically configures daily backups of all critical data:

- **Schedule**: Daily at 2 AM
- **Location**: `/opt/ando/backups/`
- **Retention**: 7 daily backups + 12 monthly backups (1st of each month)
- **Log**: `/var/log/ando-backup.log`

Each backup includes:
- `*.bak` - SQL Server database
- `*-config.tar.gz` - Configuration files, docker-compose.yml, Caddyfile
- `*-keys.tar.gz` - ASP.NET Data Protection keys

To protect against data loss, copy the backups directory to a different location. Run this after 2 AM to get the latest backup:

```bash
# Copy backups to local machine
scp -r user@your-server-ip:/opt/ando/backups/ ./ando-backups/

# Or sync to another server
rsync -avz user@your-server-ip:/opt/ando/backups/ backup-server:/backups/ando/

# Or sync to cloud storage (requires rclone configured)
ssh user@your-server-ip "rclone sync /opt/ando/backups remote:ando-backups"
```

### Restore from Backup

```bash
# Stop services
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock \
  docker compose -f /opt/ando/docker-compose.yml stop

# Restore configuration (from the config tarball)
sudo tar -xzf /path/to/ando-daily-YYYYMMDD-config.tar.gz -C /

# Restore data protection keys
sudo tar -xzf /path/to/ando-daily-YYYYMMDD-keys.tar.gz -C /opt/ando/data/

# Start SQL Server
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock \
  docker compose -f /opt/ando/docker-compose.yml up -d ando-sqlserver

# Wait for SQL Server, then restore database
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock \
  docker exec ando-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "RESTORE DATABASE AndoServer FROM DISK = '/var/opt/mssql/backup/FILENAME.bak' WITH REPLACE"

# Start all services
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock \
  docker compose -f /opt/ando/docker-compose.yml up -d
```

## Server Management

```bash
sudo -u ando /opt/ando/scripts/status.sh   # View container status
sudo -u ando /opt/ando/scripts/logs.sh     # View logs (Ctrl+C to exit)
sudo -u ando /opt/ando/scripts/restart.sh  # Restart services
sudo -u ando /opt/ando/scripts/update.sh   # Pull latest image and restart
sudo -u ando /opt/ando/scripts/backup.sh   # Run backup now

systemctl status caddy                      # Check Caddy status
```

## Troubleshooting

### Docker Socket Permission Denied

```bash
# Check the socket exists
ls -la /run/user/1000/docker.sock

# Restart rootless Docker
sudo -u ando XDG_RUNTIME_DIR=/run/user/1000 systemctl --user restart docker
```

### Database Connection Failed

```bash
# Check SQL Server is healthy
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock docker ps

# Check SQL Server logs
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock docker logs ando-sqlserver
```

### HTTPS Not Working

```bash
# Check Caddy status
systemctl status caddy

# View Caddy logs
journalctl -u caddy -f

# Test config
caddy validate --config /etc/caddy/Caddyfile
```

## More Information

- Full documentation: https://andobuild.com/server
- GitHub App setup: https://andobuild.com/server#github-app
- Developer guide: See [README.md](../../README.md#developer-guide) in the repository root
