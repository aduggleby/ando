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

# Use PostgreSQL instead of SQL Server (default: --db sqlserver)
./install-ando-server.sh --db postgres user@your-server-ip

# Use an existing database instead of deploying a container
# (connection string is prompted for, or pass it after --external-db;
#  --external-sql is kept as an alias)
./install-ando-server.sh --external-db user@your-server-ip
./install-ando-server.sh --db postgres --external-db 'Host=db;Database=AndoServer;Username=ando;Password=...;SSL Mode=VerifyFull' user@your-server-ip

# Combine options
./install-ando-server.sh --build-local --db postgres user@your-server-ip
```

## What Gets Installed

The installer sets up:

| Component | Description |
|-----------|-------------|
| **Rootless Docker** | Docker running as unprivileged `ando` user |
| **SQL Server** or **PostgreSQL** | Database container (`ando-sqlserver` or `ando-postgres`, chosen with `--db`; none with `--external-db`) |
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
| `/opt/ando/data/sqldata/` | SQL Server database files (`--db sqlserver`) | Critical |
| `/opt/ando/data/pgdata/` | PostgreSQL database files (`--db postgres`) | Critical |
| `/opt/ando/data/keys/` | ASP.NET Data Protection keys | Critical |
| `/opt/ando/data/artifacts/` | Build artifacts | Important (regenerable) |
| `/opt/ando/data/repos/` | Cloned repository cache | Optional (re-cloned on build) |
| `/etc/caddy/Caddyfile` | Caddy reverse proxy config | Critical |

To migrate to a new server, copy `/opt/ando/` and `/etc/caddy/Caddyfile` to the new host.

## Database

The server supports **SQL Server** and **PostgreSQL** from the same image. The provider is chosen by configuration; SQL Server is the default, so existing installs are unchanged.

| Setting | Env var | Values | Default |
|---------|---------|--------|---------|
| `Database:Provider` | `Database__Provider` | `SqlServer`, `PostgreSql` | `SqlServer` |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | provider-native connection string | required |

```bash
# SQL Server
Database__Provider=SqlServer
ConnectionStrings__DefaultConnection=Server=ando-sqlserver;Database=AndoServer;User Id=sa;Password=...;TrustServerCertificate=true
# PostgreSQL
Database__Provider=PostgreSql
ConnectionStrings__DefaultConnection=Host=ando-postgres;Database=AndoServer;Username=ando;Password=...
```

- Startup validates the provider and parses the connection string with the provider's own builder; errors stop startup. The selected provider, host, and database are logged (no credentials), and `GET /health` reports the provider in `database`.
- EF Core migrations run on startup. Hangfire job storage follows the provider and creates its own schema (`HangFire` on SQL Server, `hangfire` on PostgreSQL).
- External PostgreSQL: use `SSL Mode=VerifyFull` (mount a private CA and reference it with `Root Certificate=`). Npgsql pools up to 100 connections per connection string by default and EF Core and Hangfire share it; set `Maximum Pool Size=` when running replicas.
- Moving data to another host or provider: see [Moving an Instance](#moving-an-instance-export-and-import).

See https://andobuild.com/server#database for permissions, TLS, and upgrade notes.

## Backup

The install script configures daily backups for every install (containerized or `--external-db`):

- **Schedule**: Daily at 2 AM
- **Location**: `/opt/ando/backups/`
- **Retention**: 7 daily backups + 12 monthly backups (1st of each month)
- **Log**: `/var/log/ando-backup.log`

Each backup includes:
- `*.bak` - SQL Server database (containerized `--db sqlserver` only)
- `*.dump` - PostgreSQL database (containerized `--db postgres` only, `pg_dump --format=custom` run inside `ando-postgres`)
- `*-config.tar.gz` - `config/` (`.env` with `Encryption__Key`, `github-app.pem`), docker-compose.yml, Caddyfile (always)
- `*-keys.tar.gz` - ASP.NET Data Protection keys (always)

To protect against data loss, copy the backups directory to a different location. Run this after 2 AM to get the latest backup:

```bash
# Copy backups to local machine
scp -r user@your-server-ip:/opt/ando/backups/ ./ando-backups/

# Or sync to another server
rsync -avz user@your-server-ip:/opt/ando/backups/ backup-server:/backups/ando/

# Or sync to cloud storage (requires rclone configured)
ssh user@your-server-ip "rclone sync /opt/ando/backups remote:ando-backups"
```

With `--external-db` the job writes only the config and keys archives; back up the external database with its own tooling. Project secrets are AES-encrypted with `Encryption__Key` from `config/.env`, so a database backup is useless for secrets without the matching `-config.tar.gz`; without the `-keys.tar.gz` every user is signed out after a restore. The config and keys archives are the same for both providers and for containerized or external databases.

### Restore from Backup

```bash
# Stop services
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock \
  docker compose -f /opt/ando/docker-compose.yml stop

# Restore configuration (from the config tarball)
sudo tar -xzf /path/to/ando-daily-YYYYMMDD-config.tar.gz -C /

# Restore data protection keys
sudo tar -xzf /path/to/ando-daily-YYYYMMDD-keys.tar.gz -C /opt/ando/data/

# --- SQL Server ---
# Start SQL Server
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock \
  docker compose -f /opt/ando/docker-compose.yml up -d ando-sqlserver

# Wait for SQL Server, then restore database
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock \
  docker exec ando-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -C \
  -Q "RESTORE DATABASE AndoServer FROM DISK = '/var/opt/mssql/backup/FILENAME.bak' WITH REPLACE"

# --- PostgreSQL ---
# Start PostgreSQL
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock \
  docker compose -f /opt/ando/docker-compose.yml up -d ando-postgres

# Wait for PostgreSQL, then restore the dump
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock \
  docker exec -i ando-postgres pg_restore -U ando -d AndoServer --clean --if-exists \
  < /opt/ando/backups/FILENAME.dump

# --- External database (--external-db) ---
# Restore the database with its own tooling; config and keys above still apply.

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

## Moving an Instance (Export and Import)

The image has a CLI mode that exports all Ando data (users, roles, projects, encrypted secrets, builds, logs, artifact metadata, API tokens, settings; IDs preserved) to a JSON Lines file and imports it into another instance of either provider.

```bash
# Source, with ando-server stopped
docker compose run --rm ando-server export --output /data/artifacts/export/ando-export.jsonl
# Target: start once (migrates, seeds roles), stop, then import
docker compose run --rm ando-server import --input /data/artifacts/export/ando-export.jsonl   # --force replaces existing data
# Inside a running container (TrueNAS-style compose app)
docker exec <container> dotnet Ando.Server.dll export --output /data/artifacts/export/ando-export.jsonl
```

- `Encryption__Key` must be identical on both instances and both must run the same Ando version.
- Copy `data/artifacts`, `data/repos`, `data/keys`, `github-app.pem` and `.env` separately.
- The Hangfire queue is not exported; `Queued`/`Running` builds are imported as `Cancelled`.
- SQL Server to PostgreSQL loses the seventh decimal of timestamps (100 ns to 1 µs).
- A leftover `<output>.partial` file is an aborted export.

Details and the file format: https://andobuild.com/server#moving-an-instance-export-and-import

## Health Endpoints and Logging

| Endpoint | Returns |
|----------|---------|
| `GET /health` | 200 `healthy` + `database` provider after a database query, else 503 (Docker healthcheck) |
| `GET /live` | 200 `{"status":"live"}`, no dependencies (liveness probe) |
| `GET /ready` | 200 `{"status":"ready","database":"<provider>"}` when the database accepts connections, else 503 `unready` |

All three are anonymous; block them on the public hostname (Caddy: `@probes path /health /health/* /ready` and `respond @probes 404`).

Logs always go to stdout. Optional daily files (`ando-yyyyMMdd.log`):

```bash
Logging__File__Enabled=true          # default false
Logging__File__Path=/var/log/ando    # directory in the container (default); mount a volume here
Logging__File__RetainedDays=90       # default 90
```

`Logging__LogLevel__*` applies to both outputs. An unwritable directory logs a startup warning and the server continues without the file.

## Optional In-App Self-Update (Admin)

Enable this in `/opt/ando/config/.env` to let admins trigger updates from the UI:

```bash
SelfUpdate__Enabled=true
```

The server checks for a newer `ghcr.io/aduggleby/ando-server:latest` image every 5 minutes and shows an admin banner when available. Clicking update queues a background job that runs:

```bash
docker compose -f /opt/ando/docker-compose.yml pull ando-server
docker compose -f /opt/ando/docker-compose.yml up -d ando-server
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
# Check the database container is healthy
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock docker ps

# Check database logs (ando-sqlserver or ando-postgres)
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock docker logs ando-sqlserver
sudo -u ando DOCKER_HOST=unix:///run/user/1000/docker.sock docker logs ando-postgres

# Confirm the provider the server is using
curl -s http://localhost:8080/health
```

`Database__Provider` must match the connection string: `SqlServer` with `Server=...`, `PostgreSql` with `Host=...`.

### PostgreSQL Major-Version Upgrade

The data directory format changes between PostgreSQL major versions. Upgrade by dump and restore: run `backup.sh`, stop the stack, move `/opt/ando/data/pgdata` aside, change the `ando-postgres` image tag, start it on an empty `pgdata` with the same owner and mode as the old one (`chown --reference`, `chmod 700`), restore with `pg_restore`, then start `ando-server`.

### HTTPS Not Working

```bash
# Check Caddy status
systemctl status caddy

# View Caddy logs
journalctl -u caddy -f

# Test config
caddy validate --config /etc/caddy/Caddyfile
```

## Development: Database Providers

- **Contexts:** `AndoDbContext` holds the model; `SqlServerAndoDbContext` and `PostgreSqlAndoDbContext` are thin subclasses so each provider has its own migration set and snapshot. The provider is selected once at startup from `Database:Provider`.
- **Migrations:** `Migrations/SqlServer/` and `Migrations/PostgreSql/`. Always add migrations for both with `./scripts/add-migration.sh <Name>` (uses the dotnet-ef version pinned in `.config/dotnet-tools.json`; run `dotnet tool restore` first). Check for drift with:

  ```bash
  dotnet ef migrations has-pending-model-changes -p src/Ando.Server/Ando.Server.csproj --context SqlServerAndoDbContext
  dotnet ef migrations has-pending-model-changes -p src/Ando.Server/Ando.Server.csproj --context PostgreSqlAndoDbContext
  ```

- **Raw SQL** must be portable or provider-conditional.
- **Timestamps:** all `DateTime` values are stored and read as UTC on both providers and serialize with a trailing `Z`.
- **Secrets:** new or updated names must match `^[A-Za-z_][A-Za-z0-9_]*$` and be at most 100 characters; legacy names are kept and remain deletable by their exact stored name (lookups match the normalized name or the exact name). `ProjectSecret.NormalizedName` (uppercase invariant, binary collation on SQL Server) carries the unique index per project, so names are case-insensitive on both providers. The SQL Server upgrade migration backfills it with a collation-independent `UPPER` and fails with error 50001 (rolled back) if two secrets in one project collapse to the same name; delete one before retrying. Rolling back past this migration requires restoring a pre-upgrade backup.
- **Local dev:** `docker compose up -d` (SQL Server) or `docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d` (PostgreSQL, port 17134); `./run-dev.sh --db postgres`.
- **Tests:** `dotnet test tests/Ando.Server.Tests --filter "Category!=Integration"` runs the unit tests, including the migration drift tests, without Docker. `--filter "Category=Integration"` runs the Testcontainers tests and needs Docker; both providers by default (`ANDO_TEST_DB_PROVIDERS=SqlServer` limits it). `ANDO_TEST_DB_CONTAINERS=isolated` gives unique container names, random ports, and automatic cleanup; it is the default when `CI` or `ANDO_HOST_ROOT` is set or the tests run inside a container, while `shared` (the default elsewhere) keeps fixed names/ports for inspection. E2E: `npm test` and `npm run test:postgres` in `tests/Ando.Server.E2E`.
- **`build.csando`:** always runs the server unit tests; with Docker it runs the integration tests and both E2E variants. On Ando.Server these are skipped unless `-p e2e`; the `publish` profile fails without Docker.

## More Information

- Full documentation: https://andobuild.com/server
- GitHub App setup: https://andobuild.com/server#github-app
- Developer guide: See [README.md](../../README.md#developer-guide) in the repository root
