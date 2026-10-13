---
title: ANDO CI Server
description: Self-hosted CI/CD server for ANDO build scripts with GitHub integration.
toc: true
---

## Overview

The ANDO CI Server runs your `build.csando` scripts automatically when you push to GitHub. It provides a web interface for monitoring builds, viewing logs, and managing projects.

### Features

- **GitHub App Integration**: Receives webhooks on push events and reports build status back to GitHub.
- **Real-Time Updates**: Build status changes and system updates are pushed to the web UI via SignalR — dashboards and project lists refresh automatically.
- **Rootless Docker**: Runs under rootless Docker for enhanced security. Build containers are isolated.
- **Automatic HTTPS**: Caddy reverse proxy automatically obtains and renews Let's Encrypt certificates.
- **SQL Server or PostgreSQL**: Choose the database provider at install time (`--db sqlserver|postgres`).
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

# Choose the database provider (default: sqlserver)
./server-install.sh --db postgres user@your-server-ip

# Use an existing database instead of deploying a container.
# The connection string is prompted for, or can be passed directly.
./server-install.sh --external-db user@your-server-ip
./server-install.sh --db postgres --external-db 'Host=db.example.com;Database=AndoServer;Username=ando;Password=...;SSL Mode=VerifyFull' user@your-server-ip
```

`--external-sql` still works as an alias for `--external-db`. Passing the connection string on the command line puts the password in your shell history; omit it to be prompted instead.

With `--db postgres` the installer deploys an `ando-postgres` container (`postgres:17`, data in `/opt/ando/data/pgdata`) instead of `ando-sqlserver`, and writes `Database__Provider=PostgreSql`, the matching connection string, and the `depends_on` target for the chosen provider.

The database container runs as a non-root user (PostgreSQL UID/GID 999, SQL Server UID 10001). Under rootless Docker, container ID `N` maps to host ID `start + N - 1`, where `start` is the first ID of the `ando` range in `/etc/subuid` (or `/etc/subgid` for groups). The installer reads both ranges and sets the owner of `pgdata` or `sqldata` from them; with the default `ando:100000:65536` entries that is `100998` for PostgreSQL and `110000` for SQL Server. It stops with an error if the `ando` entry is missing or its range is too small.

### TrueNAS SCALE

For TrueNAS SCALE installations, see the dedicated guide: **[TrueNAS Installation](/server-truenas)**

This guide covers installing ANDO as a custom app using the TrueNAS Apps interface, with either SQL Server or PostgreSQL.

## Database

The server runs on **Microsoft SQL Server** or **PostgreSQL**, chosen by configuration. One image supports both. SQL Server is the default, so existing installs need no change.

| Setting | Env var | Values | Default |
|---------|---------|--------|---------|
| `Database:Provider` | `Database__Provider` | `SqlServer`, `PostgreSql` | `SqlServer` |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | Connection string for the selected provider | required |

- An unknown provider value fails startup with a message listing the valid values.
- The connection string is parsed with the selected provider's own parser at startup; a malformed string fails startup with the parser's message.
- Startup logs the provider, host, and database (never credentials). `GET /health` also reports the provider in its `database` field.
- To move existing data to another provider or host, use [export and import](#moving-an-instance-export-and-import).

### Connection String Formats

```bash
# SQL Server
Database__Provider=SqlServer
ConnectionStrings__DefaultConnection=Server=HOST,1433;Database=AndoServer;User Id=ando;Password=...;TrustServerCertificate=true;Encrypt=true

# PostgreSQL
Database__Provider=PostgreSql
ConnectionStrings__DefaultConnection=Host=HOST;Port=5432;Database=AndoServer;Username=ando;Password=...
```

### What the Server Creates

On startup the server applies its EF Core migrations, which create and update the Ando tables. Hangfire (background build jobs) creates its own schema on first start: `HangFire` on SQL Server, `hangfire` on PostgreSQL. EF migrations never touch the Hangfire tables. The database login therefore needs permission to create tables and schemas in the Ando database.

### External SQL Server

If you install with `--external-db` (SQL Server), create a dedicated database and login first.

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

Type `exit` to quit `sqlcmd`. `db_owner` covers the table creation (EF migrations) and the `HangFire` schema.

Then set your server connection in `/opt/ando/config/.env`:

```bash
Database__Provider=SqlServer
ConnectionStrings__DefaultConnection=Server=YOUR_SERVER_IP,1433;Database=AndoServer;User Id=ando;Password=YourSecurePassword123!;TrustServerCertificate=true;Encrypt=true
```

### External PostgreSQL

If you install with `--db postgres --external-db`, create a dedicated role and database first (use a currently supported PostgreSQL version; the bundled container uses 17):

```bash
psql -h YOUR_SERVER_IP -U postgres
```

```sql
CREATE ROLE ando LOGIN PASSWORD 'YourSecurePassword123';
CREATE DATABASE "AndoServer" OWNER ando;
```

Owning the database lets `ando` create the Ando tables (EF migrations) and the `hangfire` schema. Then set:

```bash
Database__Provider=PostgreSql
ConnectionStrings__DefaultConnection=Host=YOUR_SERVER_IP;Port=5432;Database=AndoServer;Username=ando;Password=YourSecurePassword123;SSL Mode=VerifyFull;Root Certificate=/app/config/postgres-ca.crt
```

**TLS:** for a database outside the Docker host, use `SSL Mode=VerifyFull` so the server certificate and host name are verified. If the certificate is signed by a private CA, copy the CA certificate to `/opt/ando/config/postgres-ca.crt` and mount it read-only into the server container in `/opt/ando/docker-compose.yml`:

```yaml
    volumes:
      - /opt/ando/config/postgres-ca.crt:/app/config/postgres-ca.crt:ro
```

**Connection pool:** Npgsql pools connections with a default maximum of 100 per connection string. EF Core and Hangfire share the one connection string, so each server instance can use up to 100 connections. If you run several instances or your database has a low `max_connections`, set `Maximum Pool Size=` in the connection string so the total stays within the database limit.

### Backup and Restore

The installer sets up a daily backup job (2 AM, `/opt/ando/scripts/backup.sh`) for every install. Each run writes to `/opt/ando/backups/` with 7 daily and 12 monthly backups kept:

| File | Contents | Created |
|------|----------|---------|
| `ando-*.bak` | SQL Server database (`BACKUP DATABASE`) | Containerized SQL Server |
| `ando-*.dump` | PostgreSQL database (`pg_dump --format=custom`, run inside `ando-postgres`) | Containerized PostgreSQL |
| `ando-*-config.tar.gz` | `config/` (`.env` with `Encryption__Key`, `github-app.pem`), `docker-compose.yml`, Caddyfile | Always |
| `ando-*-keys.tar.gz` | ASP.NET Data Protection keys (`data/keys`) | Always |

With `--external-db` the job still writes the config and keys archives but does not back up the database. Back up the external database with its own tooling (managed-service backups, `pg_dump`, `BACKUP DATABASE`).

A restore needs three pieces from the same point in time:

- **The database backup**, from this job or from your external database's tooling.
- **The config archive.** Project secrets are stored AES-encrypted with `Encryption__Key` from `config/.env`. Without that key the secrets cannot be decrypted and must be re-entered.
- **The keys archive.** Without the Data Protection keys every user is signed out after a restore.

The config and keys archives are the same for both providers and for containerized or external databases. Restore them first (as root):

```bash
tar -xzf /opt/ando/backups/ando-daily-YYYYMMDD-config.tar.gz -C /
tar -xzf /opt/ando/backups/ando-daily-YYYYMMDD-keys.tar.gz -C /opt/ando/data/
```

**Restore SQL Server:**

```bash
export DOCKER_HOST=unix:///run/user/$(id -u ando)/docker.sock
MSSQL_SA_PASSWORD=$(grep -m1 '^MSSQL_SA_PASSWORD=' /opt/ando/config/.env | cut -d= -f2-)
docker compose -f /opt/ando/docker-compose.yml stop ando-server
cp /opt/ando/backups/ando-daily-YYYYMMDD.bak /opt/ando/data/sqldata/backup/
docker exec ando-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "RESTORE DATABASE AndoServer FROM DISK = '/var/opt/mssql/backup/ando-daily-YYYYMMDD.bak' WITH REPLACE"
docker compose -f /opt/ando/docker-compose.yml start ando-server
```

**Restore PostgreSQL:**

```bash
export DOCKER_HOST=unix:///run/user/$(id -u ando)/docker.sock
docker compose -f /opt/ando/docker-compose.yml stop ando-server
docker exec -i ando-postgres pg_restore -U ando -d AndoServer --clean --if-exists \
  < /opt/ando/backups/ando-daily-YYYYMMDD.dump
docker compose -f /opt/ando/docker-compose.yml start ando-server
```

Run these as the `ando` user (for example `sudo -u ando -i`).

**External database:** stop `ando-server`, restore the database with its own tooling, restore the config and keys archives as shown above, then start `ando-server`.

### PostgreSQL Major-Version Upgrades

The PostgreSQL data directory format changes between major versions (for example 17 to 18), so changing the image tag in place does not work. Upgrade by dump and restore:

1. Take a fresh dump: `sudo -u ando /opt/ando/scripts/backup.sh`.
2. Stop the stack and move `/opt/ando/data/pgdata` aside.
3. Change the `ando-postgres` image tag in `/opt/ando/docker-compose.yml`, recreate an empty `pgdata` directory with the same owner, and start only `ando-postgres`. Check the new image's documentation for its data directory path; the official image changed it in PostgreSQL 18, so adjust the volume mount if needed.
4. Restore the dump with `pg_restore` as shown above, then start `ando-server`.

### Moving an Instance (Export and Import)

The server image has a command-line mode that writes all Ando data to one file and loads it into another instance. Use it to move to a new host, or to switch the database provider (SQL Server to PostgreSQL or back). For a same-provider restore on the same host, the [backups](#backup-and-restore) are simpler.

The export contains users, roles, projects, project secrets (still encrypted), builds, build logs, artifact metadata, API tokens and system settings, with all IDs preserved. It does not contain files on disk or the Hangfire job queue.

**Checklist:**

- **Same `Encryption__Key`** on both instances. Project secrets are exported encrypted; the import decrypts every secret as a check and rolls back if any fails.
- **Same Ando version** on both sides. The import compares a schema fingerprint and refuses a file from a different schema.
- **Stop the source** server (`ando-server` only, not the database) during the export, so no build or user writes while the file is written.
- **Prepare the target:** set `Database__Provider`, the connection string and `Encryption__Key`, start it once so it creates and migrates the database and seeds the roles, then stop it. Import with the target stopped.
- **Copy files separately:** `data/artifacts` (build artifacts), `data/repos`, `data/keys` (Data Protection keys; without them every user is signed out), `config/github-app.pem` and `config/.env`.
- **Hangfire is not migrated.** Builds that were `Queued` or `Running` are imported as `Cancelled` with the message "Cancelled by data import". Recurring jobs are registered again when the target starts.
- **SQL Server to PostgreSQL:** SQL Server stores timestamps to 100 ns, PostgreSQL to 1 µs, so the seventh decimal of a timestamp is lost. Nothing else changes.
- **`.partial` files:** the export writes `<output>.partial` and renames it when finished. A leftover `.partial` file is an aborted export; delete it and export again.

**Export** (on the source host, with `ando-server` stopped):

```bash
docker compose stop ando-server
docker compose run --rm ando-server export --output /data/artifacts/export/ando-export.jsonl
```

On a TrueNAS-style compose app where the container keeps running, run the same command with `docker exec`:

```bash
docker exec <container> dotnet Ando.Server.dll export --output /data/artifacts/export/ando-export.jsonl
```

`--overwrite` replaces an existing file. The path is inside the container; `/data/artifacts` is a mounted volume, so the file appears under `data/artifacts/export/` on the host. Copy it to the target's artifacts volume.

**Import** (on the target host, after its first start and with `ando-server` stopped):

```bash
docker compose run --rm ando-server import --input /data/artifacts/export/ando-export.jsonl
# or
docker exec <container> dotnet Ando.Server.dll import --input /data/artifacts/export/ando-export.jsonl
docker compose start ando-server
```

The target must be empty (no users, projects or builds). `--force` deletes all existing Ando data in the target first. The import runs in one transaction: it deletes the target's rows, inserts the file, moves identity sequences past the imported IDs, checks the row count of every table and decrypts every secret. Any failure rolls back everything. Both commands print a per-table summary and exit with `0` on success and `1` on error; `--help` prints the usage.

**File format:** JSON Lines (UTF-8, one JSON object per line). The first line is a manifest (`format`, `formatVersion`, Ando version, source provider, last migration, schema fingerprint, export time). Then one line per row, `{"table":"Projects","row":{...}}`, grouped by table in foreign-key order. The last line is `{"table":"__end","rowCounts":{...}}`; a file without it is treated as truncated and refused.

### Upgrade Notes

- **Secret names (existing SQL Server installs):** the upgrade adds a `NormalizedName` column to project secrets, fills it with the uppercase form of each existing name (using a fixed, collation-independent uppercase so the database agrees with the server), and puts a unique index on it per project. Existing names are kept as they are, including names that the new [naming rule](#project-secrets) would reject; such legacy secrets are still found by their exact stored name, so they can be deleted (and then re-created under a valid name), but not updated in place.
  - If two secrets in one project differ only by letter case (for example `token` and `TOKEN`), the migration stops with SQL error `50001` and the new server does not start. The migration runs in one transaction, so the database is left unchanged. Delete one of the two secrets in the web UI and re-create it under a different name if you still need it. Do this before upgrading, or after a failed upgrade by starting the previous server image again; then start the new image.
  - A server image older than this change cannot insert new secrets into the upgraded schema, so rolling back after a successful upgrade requires restoring the database backup taken before the upgrade.
- **Timestamps:** API responses now always mark timestamps as UTC with a trailing `Z` (for example `2026-10-13T08:30:00Z`), on both providers.

## Project Secrets

Project secrets are passed to builds as environment variables. The server encrypts each value with AES-256 using `Encryption__Key` from `config/.env` before storing it; the key itself is not stored in the database. Back up `config/.env` (the backup job's config archive does) and keep the key unchanged: if it is lost or replaced, existing secrets cannot be decrypted and must be entered again.

- **Names** must match `^[A-Za-z_][A-Za-z0-9_]*$` (ASCII letters, digits, and underscores, not starting with a digit) and be at most 100 characters.
- **Names are case-insensitive** per project: `API_KEY` and `api_key` are the same secret. The name is shown as entered.
- **Older names** created before this rule are kept and still reach builds. To change such a secret, delete it and create it again under a valid name.

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

### File Logging

The server always logs to stdout (`docker compose logs`). It can also write daily log files:

| Setting | Env var | Default |
|---------|---------|---------|
| `Logging:File:Enabled` | `Logging__File__Enabled` | `false` |
| `Logging:File:Path` | `Logging__File__Path` | `/var/log/ando` (directory inside the container) |
| `Logging:File:RetainedDays` | `Logging__File__RetainedDays` | `90` (older files are deleted) |

Files are named `ando-yyyyMMdd.log`, one per day. The levels in `Logging:LogLevel` (for example `Logging__LogLevel__Default=Information`) apply to the file and to stdout alike. Mount a volume at the log path so the files survive container updates:

```yaml
    environment:
      - Logging__File__Enabled=true
    volumes:
      - /opt/ando/data/logs:/var/log/ando
```

If the directory cannot be created or written, the server logs a warning at startup and continues with stdout logging only.

### Build Timeouts

The CI server applies a whole-build timeout to each build. The default is 15 minutes and the maximum is 60 minutes.

```bash
Build__DefaultTimeoutMinutes=15
Build__MaxTimeoutMinutes=60
```

Project settings can lower or raise an individual project's timeout within the server maximum. This server timeout is separate from ANDO's per-command timeout: standard command operations, including `Npm.Install`, `Npm.Ci`, `Npm.Run`, `Npm.Test`, and `Npm.Build`, time out after 5 minutes. If a command timeout occurs, the build log reports the command timeout as the failed-step error.

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
- After triggering an update, a full-screen overlay appears with a 30-second countdown while the container restarts, then automatically reconnects by polling the `/health` endpoint and reloads the page when the server is back.
- The web UI also detects version changes independently — if the server is updated externally (e.g., by another admin or via SSH), all open browser tabs automatically clear caches and reload within 60 seconds.

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

## Health Checks

The server exposes health endpoints for monitoring and load balancers.

### Basic Health (`GET /health`)

Returns `200` with `{"status": "healthy", "database": "SqlServer"}` (or `"PostgreSql"`) when the server can connect to and query the database. Returns `503` with `{"status": "unhealthy", ...}` when the database is unreachable. The `database` field reports the configured provider. No authentication required.

Use this endpoint for Docker health checks, load balancer probes, and uptime monitoring.

### Liveness (`GET /live`)

Returns `200` with `{"status": "live"}` whenever the process serves HTTP. It has no dependencies, so use it as a liveness probe: a database outage does not make it fail and does not restart the container.

### Readiness (`GET /ready`)

Returns `200` with `{"status": "ready", "database": "SqlServer"}` (or `"PostgreSql"`) when the database accepts connections, and `503` with `{"status": "unready", "database": ..., "error": ...}` when it does not. Use it to hold back traffic until the database is reachable. No authentication required.

The Docker healthcheck in the image keeps using `/health`.

### Reverse Proxy

The probes are anonymous. Answer them on the internal port only and block them on the public hostname, for example in the Caddyfile:

```text
ci.yourdomain.com {
    @probes path /health /health/* /ready
    respond @probes 404
    reverse_proxy localhost:8080
}
```

Monitoring on the same host or network calls `http://localhost:8080/ready` (or the container's address) directly.

### System Health (`GET /api/admin/system-health`)

Admin-only endpoint that runs live probes against all subsystems:

| Check | What It Tests |
|-------|---------------|
| Database | Connectivity and query execution against the configured provider |
| Job Storage | Hangfire storage reachable (SQL Server or PostgreSQL, reported separately from the database check) |
| Background Jobs | Hangfire worker availability |
| GitHub Integration | GitHub App authentication |

Each check returns a status of `healthy`, `warning`, or `error` with a descriptive message. Results are displayed in the admin dashboard.

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
| `/opt/ando/data/sqldata/` | SQL Server database (`--db sqlserver`) |
| `/opt/ando/data/pgdata/` | PostgreSQL database (`--db postgres`) |
| `/opt/ando/data/keys/` | ASP.NET Data Protection keys (auth sessions) |
| `/opt/ando/data/artifacts/` | Build artifacts |
| `/var/log/ando/` (in the container) | Daily log files, when `Logging__File__Enabled=true` |

## Full Documentation

https://github.com/aduggleby/ando/blob/main/src/Ando.Server/README.md
