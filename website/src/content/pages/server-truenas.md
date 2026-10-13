---
title: TrueNAS Installation
description: Install ANDO CI Server as a custom app on TrueNAS SCALE.
toc: true
---

## Overview

This guide walks you through installing ANDO CI Server on TrueNAS SCALE using Docker Compose. ANDO connects to an existing database running on your TrueNAS: either **SQL Server** or **PostgreSQL** (the TrueNAS app catalog ships a Postgres app). Pick one; each step below has a variant for both.

## Prerequisites

Before starting, ensure you have:

- **TrueNAS SCALE 24.10 or later** (earlier versions used Kubernetes)
- **Apps pool configured** — a storage pool designated for application data
- **A database**, one of:
  - **SQL Server** — already running as a container on TrueNAS (install from the app catalog if needed)
  - **PostgreSQL** — the **Postgres** app from the TrueNAS app catalog (a currently supported PostgreSQL version; 17 recommended)
- **GitHub App credentials** — App ID, Client ID, Client Secret, Webhook Secret, and private key file
- **A domain name** pointing to your TrueNAS server (for HTTPS and GitHub webhooks)

### Gather Required Information

You will need these values during installation:

| Setting                 | Description                                                          |
| ----------------------- | -------------------------------------------------------------------- |
| `GitHub__AppId`         | Your GitHub App's numeric ID                                         |
| `GitHub__ClientId`      | OAuth Client ID from GitHub App settings                             |
| `GitHub__ClientSecret`  | OAuth Client Secret from GitHub App settings                         |
| `GitHub__WebhookSecret` | Secret for validating webhook payloads                               |
| `Encryption__Key`       | Base64-encoded 32-byte key (generate with `openssl rand -base64 32`) |
| `Server__BaseUrl`       | Your server's public URL (e.g., `https://ci.example.com`)            |
| Email provider          | API key or SMTP credentials (see Step 1)                             |

### Create Dataset and Directories

Before starting the installation, create a dataset for persistent storage.

1. Navigate to **Datasets** in TrueNAS
2. Create a dataset: `apps/ando` (or similar, under your apps pool)
3. Create subdirectories inside the dataset using the TrueNAS Shell or SSH:

```bash
mkdir -p /mnt/YOUR_POOL/apps/ando/{artifacts,repos,keys,config}
```

4. Upload your GitHub App private key to `/mnt/YOUR_POOL/apps/ando/config/github-app.pem`

## Step 1: Create Database and User

### Option A: SQL Server

Connect to your existing SQL Server and create a dedicated database and login for ANDO.

You can use any SQL client (Azure Data Studio, SSMS, etc.) or sqlcmd. Connect to your TrueNAS IP on the SQL Server port (typically 1433) as `sa`.

From a machine with sqlcmd installed:

```bash
sqlcmd -S YOUR_TRUENAS_IP,1433 -U sa -P 'YOUR_SA_PASSWORD' -C
```

Run these SQL commands to create the database and user:

```sql
-- Create database
CREATE DATABASE AndoServer;
GO

-- Create login
CREATE LOGIN ando WITH PASSWORD = 'YourAndoPassword123!';
GO

-- Create user and grant permissions
USE AndoServer;
GO

CREATE USER ando FOR LOGIN ando;
GO

ALTER ROLE db_owner ADD MEMBER ando;
GO
```

Type `exit` to quit sqlcmd.

> **Important**: Replace `YourAndoPassword123!` with a strong password. You'll use this in the connection string below.

### Option B: PostgreSQL (TrueNAS Postgres App)

1. Navigate to **Apps** → **Discover Apps**, search for **Postgres**, and click **Install**
2. In the app configuration set:
   - **Database User**: `ando`
   - **Database Password**: a strong password (letters and digits avoid escaping issues)
   - **Database Name**: `AndoServer`
   - **Storage**: a dataset under your apps pool for the PostgreSQL data
3. Note the port the app publishes on the host (shown in the app's network settings; `5432` unless you changed it)
4. Click **Install**

The app creates the `ando` role as owner of `AndoServer`, which is all ANDO needs: on first start it creates its tables (EF Core migrations) and the `hangfire` schema for background jobs.

If you use an existing PostgreSQL instance instead, create the role and database yourself:

```sql
CREATE ROLE ando LOGIN PASSWORD 'YourAndoPassword123';
CREATE DATABASE "AndoServer" OWNER ando;
```

## Step 2: Note Your Database Connection

ANDO connects to the database via TCP on the mapped port. Use your TrueNAS host IP (e.g., `192.168.1.100`) with the port your database is mapped to (typically `1433` for SQL Server, `5432` for PostgreSQL).

| Provider   | `Database__Provider` | Connection string                                                                                                    |
| ---------- | -------------------- | -------------------------------------------------------------------------------------------------------------------- |
| SQL Server | `SqlServer`          | `Server=YOUR_TRUENAS_IP,1433;Database=AndoServer;User Id=ando;Password=YOUR_ANDO_PASSWORD;TrustServerCertificate=true` |
| PostgreSQL | `PostgreSql`         | `Host=YOUR_TRUENAS_IP;Port=5432;Database=AndoServer;Username=ando;Password=YOUR_ANDO_PASSWORD`                         |

## Step 3: Open the YAML Installation Wizard

1. Navigate to **Apps** → **Discover Apps**
2. Click the three-dot menu (⋮) in the top right
3. Select **Install via YAML**

## Step 4: Configure the Application

### Application Name

Enter: `ando`

### Docker Compose Configuration

Paste the following YAML, replacing the placeholder values:

```yaml
services:
  ando-server:
    image: ghcr.io/aduggleby/ando-server:latest
    pull_policy: always
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
      # TrueNAS runs Docker as root - acknowledge this security consideration
      - Build__AcknowledgeRootDockerRisk=true
      # Database: SQL Server (default). For PostgreSQL, use the two
      # commented lines below instead (see Step 2).
      - Database__Provider=SqlServer
      - ConnectionStrings__DefaultConnection=Server=YOUR_TRUENAS_IP,1433;Database=AndoServer;User Id=ando;Password=YOUR_ANDO_PASSWORD;TrustServerCertificate=true
      # - Database__Provider=PostgreSql
      # - ConnectionStrings__DefaultConnection=Host=YOUR_TRUENAS_IP;Port=5432;Database=AndoServer;Username=ando;Password=YOUR_ANDO_PASSWORD
      # Replace with your GitHub App credentials
      - GitHub__AppId=YOUR_APP_ID
      - GitHub__ClientId=YOUR_CLIENT_ID
      - GitHub__ClientSecret=YOUR_CLIENT_SECRET
      - GitHub__WebhookSecret=YOUR_WEBHOOK_SECRET
      # Generate with: openssl rand -base64 32
      - Encryption__Key=YOUR_ENCRYPTION_KEY
      # Public URL for email links (verification, password reset)
      - Server__BaseUrl=https://your-domain.com
      # Email configuration (choose one provider)
      # Option A: Resend-compatible API (recommended) - e.g., SelfMX
      - Email__FromAddress=noreply@yourdomain.com
      - Email__Provider=Resend
      - Email__Resend__ApiKey=YOUR_API_KEY
      - Email__Resend__BaseUrl=https://api.selfmx.com/
      # For official Resend, omit BaseUrl or use: https://api.resend.com/
      # Option B: SMTP
      # - Email__Provider=Smtp
      # - Email__Smtp__Host=smtp.yourdomain.com
      # - Email__Smtp__Port=587
      # - Email__Smtp__Username=YOUR_SMTP_USER
      # - Email__Smtp__Password=YOUR_SMTP_PASSWORD
    volumes:
      # Docker socket for running builds
      - /var/run/docker.sock:/var/run/docker.sock
      # Persistent storage - update paths to match your pool
      - /mnt/YOUR_POOL/apps/ando/artifacts:/data/artifacts
      - /mnt/YOUR_POOL/apps/ando/repos:/data/repos
      - /mnt/YOUR_POOL/apps/ando/keys:/data/keys
      - /mnt/YOUR_POOL/apps/ando/config/github-app.pem:/app/github-app.pem:ro
    restart: unless-stopped
    privileged: true
```

### Values to Replace

| Placeholder               | Replace With                                                     |
| ------------------------- | ---------------------------------------------------------------- |
| `YOUR_TRUENAS_IP`         | Your TrueNAS host IP address (e.g., `192.168.1.100`)             |
| `1433` / `5432`           | The port your SQL Server / PostgreSQL is mapped to               |
| `YOUR_ANDO_PASSWORD`      | The password you set for the `ando` login in Step 1              |
| `YOUR_POOL`               | Your TrueNAS pool name (e.g., `tank`, `data`)                    |
| `YOUR_APP_ID`             | GitHub App numeric ID                                            |
| `YOUR_CLIENT_ID`          | GitHub OAuth Client ID                                           |
| `YOUR_CLIENT_SECRET`      | GitHub OAuth Client Secret                                       |
| `YOUR_WEBHOOK_SECRET`     | GitHub webhook secret                                            |
| `YOUR_ENCRYPTION_KEY`     | Base64 key from `openssl rand -base64 32`                        |
| `https://your-domain.com` | Your server's public URL (for email links)                       |
| `YOUR_API_KEY`            | API key for your Resend-compatible email provider (e.g., SelfMX) |
| `https://api.selfmx.com/` | Base URL for your email provider's API                           |
| `noreply@yourdomain.com`  | Your verified sender email address                               |

## Step 5: Install

Click **Install** to deploy the container. TrueNAS will pull the Docker image and start ANDO.

## Step 6: Configure Reverse Proxy

For HTTPS access and GitHub webhooks, configure a reverse proxy.

### Option A: TrueNAS Built-in or Traefik

If your TrueNAS has a public IP:

1. Install Traefik from the TrueNAS app catalog
2. Configure it to proxy `your-domain.com` → `localhost:8080`

### Option B: External Reverse Proxy

If using Caddy, nginx, or another external proxy:

```
# Example Caddy configuration
your-domain.com {
    reverse_proxy YOUR_TRUENAS_IP:8080
}
```

## Step 7: Configure GitHub App

Update your GitHub App settings:

| Setting      | Value                                          |
| ------------ | ---------------------------------------------- |
| Homepage URL | `https://your-domain.com`                      |
| Callback URL | `https://your-domain.com/auth/github/callback` |
| Webhook URL  | `https://your-domain.com/webhooks/github`      |

## Verification

1. Access the ANDO dashboard at `http://YOUR_TRUENAS_IP:8080` (or your HTTPS domain)
2. Click **Sign in with GitHub** to test OAuth
3. Connect a repository and trigger a test build

## Updating ANDO

To update to a new version:

1. Navigate to **Apps** → **Installed Applications**
2. Click on `ando`
3. Click **Edit** → update the image tag or use `latest` → **Save**

The container will restart with the new version.

## Troubleshooting

### View Logs

1. Navigate to **Apps** → **Installed Applications**
2. Click on `ando`
3. Click **Logs** to view the container output

### Container Won't Start

Check that:

- All dataset paths exist and are accessible
- The `github-app.pem` file is uploaded to the config directory
- Environment variables are correctly formatted (no extra spaces)

### Database Connection Failed

1. Verify the TrueNAS IP and port are correct
2. Test the connection: `nc -zv YOUR_TRUENAS_IP 1433` (SQL Server) or `nc -zv YOUR_TRUENAS_IP 5432` (PostgreSQL)
3. Verify the `ando` login (SQL Server) or role (PostgreSQL) was created correctly
4. Check the password matches what you set in Step 1
5. Check that `Database__Provider` matches the database: `SqlServer` with a `Server=...` string, `PostgreSql` with a `Host=...` string. `http://YOUR_TRUENAS_IP:8080/health` reports the provider in its `database` field once the server is up.

### Backups

ANDO's tables live in your database, so back it up with the database's own tooling or TrueNAS snapshots of its dataset. For PostgreSQL, a logical dump works too:

```bash
docker exec <postgres-container> pg_dump -U ando -Fc AndoServer > ando-YYYYMMDD.dump
docker exec -i <postgres-container> pg_restore -U ando -d AndoServer --clean --if-exists < ando-YYYYMMDD.dump
```

The Linux installer's backup job does not run on TrueNAS, so also back up, on their own schedule and independent of the database provider:

- **`Encryption__Key`** (and the rest of the app's environment variables). Project secrets are stored AES-encrypted with this key; without it they cannot be decrypted after a restore and must be entered again.
- **`/mnt/YOUR_POOL/apps/ando/keys`** (Data Protection keys). Without them every user is signed out after a restore.

Neither is stored in the database. Keep copies from the same point in time as each database backup.

### Builds Fail with Docker Errors

Verify:

- The Docker socket path `/var/run/docker.sock` exists on your TrueNAS system
- The `privileged: true` setting is in place

### Permission Denied on Mounted Volumes

TrueNAS may need ACL configuration:

1. Navigate to the dataset in **Datasets**
2. Click **Edit Permissions**
3. Add an ACL entry for UID 0 (root) with full access

## More Information

- [Main CI Server Documentation](/server)
- [GitHub App Configuration](/server#github-app-configuration)
- [TrueNAS Custom Apps Documentation](https://apps.truenas.com/managing-apps/installing-custom-apps/)
