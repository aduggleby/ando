#!/bin/bash
# =============================================================================
# install-ando-server.sh
#
# Deploys Ando.Server to a remote Ubuntu server with:
# - Rootless Docker (for security - no root container escapes)
# - SQL Server in isolated container
# - Ando.Server container with Docker-in-Docker capability
# - Caddy reverse proxy for HTTPS (installed on host)
#
# Run from your local machine (where the Ando source code is):
#   ./scripts/install-ando-server.sh user@server-ip
#
# =============================================================================

set -euo pipefail

# -----------------------------------------------------------------------------
# Configuration
# -----------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOCKER_IMAGE_NAME="ando-server"
DOCKER_IMAGE_TAG="latest"
SQL_SERVER_IMAGE="mcr.microsoft.com/mssql/server:2022-latest"
ANDO_USER="ando"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
NC='\033[0m'

# -----------------------------------------------------------------------------
# Helper Functions
# -----------------------------------------------------------------------------
log_info() { echo -e "${BLUE}[INFO]${NC} $1"; }
log_success() { echo -e "${GREEN}[SUCCESS]${NC} $1"; }
log_warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
log_error() { echo -e "${RED}[ERROR]${NC} $1"; }
log_step() { echo -e "\n${CYAN}==>${NC} ${CYAN}$1${NC}"; }

confirm() {
    read -r -p "$1 [y/N] " response
    case "$response" in
        [yY][eE][sS]|[yY]) return 0 ;;
        *) return 1 ;;
    esac
}

prompt_with_default() {
    local prompt="$1"
    local default="$2"
    local var_name="$3"
    local is_secret="${4:-false}"

    if [[ "$is_secret" == "true" ]]; then
        read -r -s -p "$prompt [$default]: " value
        echo ""
    else
        read -r -p "$prompt [$default]: " value
    fi

    value="${value:-$default}"
    eval "$var_name=\"$value\""
}

prompt_required() {
    local prompt="$1"
    local var_name="$2"
    local is_secret="${3:-false}"
    local value=""

    while [[ -z "$value" ]]; do
        if [[ "$is_secret" == "true" ]]; then
            read -r -s -p "$prompt: " value
            echo ""
        else
            read -r -p "$prompt: " value
        fi

        if [[ -z "$value" ]]; then
            log_error "This field is required"
        fi
    done

    eval "$var_name=\"$value\""
}

generate_password() {
    openssl rand -base64 24 | tr -dc 'A-Za-z0-9!@#$%' | head -c 24
    echo "1Aa"  # Ensure SQL Server complexity requirements
}

generate_encryption_key() {
    openssl rand -base64 32
}

# -----------------------------------------------------------------------------
# Validation
# -----------------------------------------------------------------------------
validate_args() {
    if [[ $# -lt 1 ]]; then
        echo "Usage: $0 <user@server-ip> [ssh-key-path]"
        echo ""
        echo "Example:"
        echo "  $0 root@138.199.223.171"
        echo "  $0 root@138.199.223.171 ~/.ssh/id_claude"
        exit 1
    fi

    REMOTE_HOST="$1"
    SSH_KEY="${2:-}"

    if [[ -n "$SSH_KEY" ]]; then
        SSH_OPTS="-i $SSH_KEY -o StrictHostKeyChecking=accept-new"
        SCP_OPTS="-i $SSH_KEY -o StrictHostKeyChecking=accept-new"
    else
        SSH_OPTS="-o StrictHostKeyChecking=accept-new"
        SCP_OPTS="-o StrictHostKeyChecking=accept-new"
    fi
}

check_local_requirements() {
    log_step "Checking local requirements"

    local missing=()

    for cmd in docker ssh scp openssl; do
        if ! command -v "$cmd" &> /dev/null; then
            missing+=("$cmd")
        fi
    done

    if [[ ${#missing[@]} -gt 0 ]]; then
        log_error "Missing required commands: ${missing[*]}"
        exit 1
    fi

    if [[ ! -f "$PROJECT_ROOT/src/Ando.Server/Dockerfile" ]]; then
        log_error "Cannot find Ando.Server Dockerfile. Run this script from the ando project root."
        exit 1
    fi

    log_success "Local requirements satisfied"
}

check_remote_connection() {
    log_step "Testing remote connection"

    if ! ssh $SSH_OPTS "$REMOTE_HOST" "echo 'Connection successful'" &>/dev/null; then
        log_error "Cannot connect to $REMOTE_HOST"
        exit 1
    fi

    log_success "Connected to $REMOTE_HOST"
}

# -----------------------------------------------------------------------------
# Configuration Gathering
# -----------------------------------------------------------------------------
gather_configuration() {
    log_step "Gathering configuration"
    echo ""
    echo "Please provide the following configuration values."
    echo "Press Enter to accept defaults shown in brackets."
    echo ""

    # --- Installation Path ---
    echo -e "${CYAN}=== Installation Path ===${NC}"
    prompt_with_default "Installation directory" "/opt/ando" INSTALL_DIR

    # --- Network Configuration ---
    echo ""
    echo -e "${CYAN}=== Network Configuration ===${NC}"
    if confirm "Use isolated Docker network? (SQL Server not exposed to host)"; then
        USE_ISOLATED_NETWORK="true"
        NETWORK_NAME="ando-network"
    else
        USE_ISOLATED_NETWORK="false"
        NETWORK_NAME=""
    fi

    # --- Domain Configuration ---
    echo ""
    echo -e "${CYAN}=== Domain Configuration ===${NC}"
    prompt_required "Domain name (e.g., ando.example.com)" DOMAIN_NAME
    prompt_with_default "Admin email (for Let's Encrypt)" "admin@${DOMAIN_NAME#*.}" ADMIN_EMAIL

    # --- SQL Server Configuration ---
    echo ""
    echo -e "${CYAN}=== SQL Server Configuration ===${NC}"
    local default_sa_password=$(generate_password)
    prompt_with_default "SQL Server SA password" "$default_sa_password" SA_PASSWORD true
    prompt_with_default "Database name" "AndoServer" DATABASE_NAME

    # --- GitHub Configuration ---
    echo ""
    echo -e "${CYAN}=== GitHub App Configuration ===${NC}"
    echo "Create a GitHub App at: https://github.com/settings/apps/new"
    echo ""
    prompt_required "GitHub App ID" GITHUB_APP_ID
    prompt_required "GitHub App Name (slug)" GITHUB_APP_NAME
    prompt_required "GitHub Client ID" GITHUB_CLIENT_ID
    prompt_required "GitHub Client Secret" GITHUB_CLIENT_SECRET true

    local default_webhook_secret=$(openssl rand -hex 20)
    prompt_with_default "GitHub Webhook Secret" "$default_webhook_secret" GITHUB_WEBHOOK_SECRET true

    echo ""
    echo "GitHub App Private Key:"
    echo "  Download the .pem file from your GitHub App settings"
    prompt_required "Path to local github-app.pem file" GITHUB_PEM_PATH

    if [[ ! -f "$GITHUB_PEM_PATH" ]]; then
        log_error "File not found: $GITHUB_PEM_PATH"
        exit 1
    fi

    # --- Encryption Key ---
    echo ""
    echo -e "${CYAN}=== Security Configuration ===${NC}"
    local default_encryption_key=$(generate_encryption_key)
    prompt_with_default "Encryption key (base64, 32 bytes)" "$default_encryption_key" ENCRYPTION_KEY true

    # --- Email Configuration ---
    echo ""
    echo -e "${CYAN}=== Email Configuration ===${NC}"
    echo "Email providers:"
    echo "  1) Resend (recommended)"
    echo "  2) Resend Compatible (custom base URL)"
    echo "  3) Azure Communication Services"
    echo "  4) SMTP"
    echo "  5) Skip (no email notifications)"
    echo ""
    read -r -p "Select email provider [1-5]: " EMAIL_CHOICE

    case "$EMAIL_CHOICE" in
        1)
            EMAIL_PROVIDER="Resend"
            RESEND_BASE_URL=""
            prompt_required "Resend API Key" RESEND_API_KEY true
            prompt_required "From email address" EMAIL_FROM_ADDRESS
            prompt_with_default "From name" "Ando CI" EMAIL_FROM_NAME
            ;;
        2)
            EMAIL_PROVIDER="Resend"
            prompt_required "Resend-compatible API Base URL (e.g., https://resendalternative.com/api)" RESEND_BASE_URL
            prompt_required "API Key" RESEND_API_KEY true
            prompt_required "From email address" EMAIL_FROM_ADDRESS
            prompt_with_default "From name" "Ando CI" EMAIL_FROM_NAME
            ;;
        3)
            EMAIL_PROVIDER="Azure"
            RESEND_BASE_URL=""
            prompt_required "Azure Communication Services connection string" AZURE_EMAIL_CONNECTION_STRING true
            prompt_required "From email address" EMAIL_FROM_ADDRESS
            prompt_with_default "From name" "Ando CI" EMAIL_FROM_NAME
            ;;
        4)
            EMAIL_PROVIDER="Smtp"
            RESEND_BASE_URL=""
            prompt_required "SMTP Host" SMTP_HOST
            prompt_with_default "SMTP Port" "587" SMTP_PORT
            prompt_required "SMTP Username" SMTP_USERNAME
            prompt_required "SMTP Password" SMTP_PASSWORD true
            prompt_with_default "Use SSL" "true" SMTP_USE_SSL
            prompt_required "From email address" EMAIL_FROM_ADDRESS
            prompt_with_default "From name" "Ando CI" EMAIL_FROM_NAME
            ;;
        5|*)
            EMAIL_PROVIDER="None"
            RESEND_BASE_URL=""
            log_info "Email notifications disabled"
            ;;
    esac

    # --- Summary ---
    echo ""
    echo -e "${CYAN}=== Configuration Summary ===${NC}"
    echo "  Remote host:     $REMOTE_HOST"
    echo "  Install dir:     $INSTALL_DIR"
    echo "  Domain:          $DOMAIN_NAME"
    echo "  Isolated network: $USE_ISOLATED_NETWORK"
    echo "  Database:        $DATABASE_NAME"
    echo "  GitHub App:      $GITHUB_APP_NAME (ID: $GITHUB_APP_ID)"
    echo "  Email provider:  $EMAIL_PROVIDER"
    echo ""

    if ! confirm "Proceed with installation?"; then
        log_info "Installation cancelled"
        exit 0
    fi
}

# -----------------------------------------------------------------------------
# Build Local Docker Image
# -----------------------------------------------------------------------------
build_local_image() {
    log_step "Building Ando.Server Docker image locally"

    cd "$PROJECT_ROOT"

    docker build \
        --no-cache \
        -t "$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG" \
        -f src/Ando.Server/Dockerfile \
        .

    log_success "Docker image built: $DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
}

# -----------------------------------------------------------------------------
# Remote Server Setup
# -----------------------------------------------------------------------------
install_docker_remote() {
    log_step "Installing Docker on remote server"

    ssh $SSH_OPTS "$REMOTE_HOST" bash << 'EOF'
set -euo pipefail

# Check if Docker is already installed
if command -v docker &> /dev/null; then
    echo "Docker already installed"
    docker --version
else
    echo "Installing Docker..."

    # Update and install prerequisites
    apt-get update
    apt-get install -y ca-certificates curl gnupg uidmap dbus-user-session

    # Add Docker's official GPG key
    install -m 0755 -d /etc/apt/keyrings
    curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
    chmod a+r /etc/apt/keyrings/docker.asc

    # Add Docker repository
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
        tee /etc/apt/sources.list.d/docker.list > /dev/null

    # Install Docker (needed for rootless setup script)
    apt-get update
    apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

    echo "Docker installed successfully"
    docker --version
fi

# Allow unprivileged ports from 80 (for rootless Docker)
if ! grep -q "net.ipv4.ip_unprivileged_port_start=80" /etc/sysctl.conf; then
    echo "net.ipv4.ip_unprivileged_port_start=80" >> /etc/sysctl.conf
    sysctl --system
fi
EOF

    log_success "Docker installed on remote server"
}

setup_rootless_docker() {
    log_step "Setting up rootless Docker for $ANDO_USER user"

    ssh $SSH_OPTS "$REMOTE_HOST" bash << EOF
set -euo pipefail

# Create ando user if it doesn't exist
if ! id -u $ANDO_USER &>/dev/null; then
    echo "Creating $ANDO_USER user..."
    useradd -m -s /bin/bash $ANDO_USER
fi

# Set up subuid/subgid for rootless Docker
if ! grep -q "^$ANDO_USER:" /etc/subuid; then
    echo "$ANDO_USER:100000:65536" >> /etc/subuid
fi
if ! grep -q "^$ANDO_USER:" /etc/subgid; then
    echo "$ANDO_USER:100000:65536" >> /etc/subgid
fi

# Enable systemd linger for the user (so user services run at boot)
loginctl enable-linger $ANDO_USER

# Get ando user's UID
ANDO_UID=\$(id -u $ANDO_USER)

# Install systemd-container for proper user session management
apt-get install -y systemd-container

# Create systemd user service directory
mkdir -p /home/$ANDO_USER/.config/systemd/user

# Create rootless Docker systemd service
cat > /home/$ANDO_USER/.config/systemd/user/docker.service << 'SERVICEEOF'
[Unit]
Description=Docker Application Container Engine (Rootless)
Documentation=https://docs.docker.com/go/rootless/

[Service]
Environment=PATH=/usr/bin:/sbin:/usr/sbin
ExecStart=/usr/bin/dockerd-rootless.sh
ExecReload=/bin/kill -s HUP \$MAINPID
TimeoutSec=0
RestartSec=2
Restart=always
StartLimitBurst=3
StartLimitInterval=60s
LimitNOFILE=infinity
LimitNPROC=infinity
LimitCORE=infinity
TasksMax=infinity
Delegate=yes
Type=notify
NotifyAccess=all
KillMode=mixed

[Install]
WantedBy=default.target
SERVICEEOF

chown -R $ANDO_USER:$ANDO_USER /home/$ANDO_USER/.config

# Install rootless Docker setup for the user
runuser -l $ANDO_USER -c "dockerd-rootless-setuptool.sh install" || true

# Enable and start rootless Docker service
runuser -l $ANDO_USER -c "export XDG_RUNTIME_DIR=/run/user/\\\$(id -u) && systemctl --user daemon-reload && systemctl --user enable docker.service && systemctl --user start docker.service"

# Verify rootless Docker is running
sleep 3
runuser -l $ANDO_USER -c "export DOCKER_HOST=unix:///run/user/\\\$(id -u)/docker.sock && docker info --format '{{.SecurityOptions}}'" | grep -q rootless && echo "Rootless Docker is running" || echo "Warning: Rootless Docker may not be fully configured"

echo "Rootless Docker setup complete for $ANDO_USER"
EOF

    log_success "Rootless Docker configured for $ANDO_USER user"
}

install_caddy_host() {
    log_step "Installing Caddy on host (for proper HTTPS certificate acquisition)"

    ssh $SSH_OPTS "$REMOTE_HOST" bash << 'EOF'
set -euo pipefail

# Check if Caddy is already installed
if command -v caddy &> /dev/null; then
    echo "Caddy already installed"
    caddy version
    exit 0
fi

# Install prerequisites
apt-get install -y debian-keyring debian-archive-keyring apt-transport-https curl

# Add Caddy repository
curl -1sLf "https://dl.cloudsmith.io/public/caddy/stable/gpg.key" | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf "https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt" | tee /etc/apt/sources.list.d/caddy-stable.list

# Install Caddy
apt-get update
apt-get install -y caddy

echo "Caddy installed successfully"
caddy version
EOF

    log_success "Caddy installed on host"
}

setup_directories_remote() {
    log_step "Setting up directories on remote server"

    # Get ando user's UID for rootless Docker UID mapping
    ANDO_UID=$(ssh $SSH_OPTS "$REMOTE_HOST" "id -u $ANDO_USER")

    ssh $SSH_OPTS "$REMOTE_HOST" bash << EOF
set -euo pipefail

mkdir -p "$INSTALL_DIR"/{config,data/{artifacts,repos,sqldata},logs}
chmod 700 "$INSTALL_DIR/config"

# Set ownership to ando user
chown -R $ANDO_USER:$ANDO_USER "$INSTALL_DIR"

# SQL Server runs as mssql user (UID 10001 inside container)
# With rootless Docker, this maps to UID 110000 on host (100000 + 10001 - 1)
chown -R 110000:110000 "$INSTALL_DIR/data/sqldata"

echo "Directories created at $INSTALL_DIR"
ls -la "$INSTALL_DIR"
ls -la "$INSTALL_DIR/data"
EOF

    log_success "Directories created"
}

setup_docker_network_remote() {
    if [[ "$USE_ISOLATED_NETWORK" != "true" ]]; then
        return
    fi

    log_step "Creating isolated Docker network (rootless)"

    ssh $SSH_OPTS "$REMOTE_HOST" bash << EOF
set -euo pipefail

# Create network in rootless Docker as ando user
runuser -l $ANDO_USER -c "export DOCKER_HOST=unix:///run/user/\$(id -u $ANDO_USER)/docker.sock && docker network rm $NETWORK_NAME 2>/dev/null || true"
runuser -l $ANDO_USER -c "export DOCKER_HOST=unix:///run/user/\$(id -u $ANDO_USER)/docker.sock && docker network create --driver bridge $NETWORK_NAME"

echo "Network '$NETWORK_NAME' created in rootless Docker"
EOF

    log_success "Docker network created: $NETWORK_NAME"
}

# -----------------------------------------------------------------------------
# Transfer Files
# -----------------------------------------------------------------------------
transfer_docker_image() {
    log_step "Transferring Docker image to remote server"

    log_info "Saving and transferring Docker image (this may take a while)..."

    # Stream directly to avoid large temp files
    docker save "$DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG" | gzip | \
        ssh $SSH_OPTS "$REMOTE_HOST" "runuser -l $ANDO_USER -c 'export DOCKER_HOST=unix:///run/user/\$(id -u $ANDO_USER)/docker.sock && gunzip | docker load'"

    log_success "Docker image transferred and loaded into rootless Docker"
}

transfer_config_files() {
    log_step "Transferring configuration files"

    # Transfer GitHub PEM file
    scp $SCP_OPTS "$GITHUB_PEM_PATH" "$REMOTE_HOST:$INSTALL_DIR/config/github-app.pem"
    ssh $SSH_OPTS "$REMOTE_HOST" "chown $ANDO_USER:$ANDO_USER $INSTALL_DIR/config/github-app.pem && chmod 600 $INSTALL_DIR/config/github-app.pem"

    log_success "Configuration files transferred"
}

# -----------------------------------------------------------------------------
# Generate Configuration Files
# -----------------------------------------------------------------------------
generate_env_file() {
    log_step "Generating .env file"

    # Build connection string based on network mode
    if [[ "$USE_ISOLATED_NETWORK" == "true" ]]; then
        CONNECTION_STRING="Server=ando-sqlserver;Database=$DATABASE_NAME;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=true"
    else
        CONNECTION_STRING="Server=localhost,1433;Database=$DATABASE_NAME;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=true"
    fi

    # Build email configuration
    local email_env=""
    case "$EMAIL_PROVIDER" in
        Resend)
            email_env="Email__Provider=Resend
Email__FromAddress=$EMAIL_FROM_ADDRESS
Email__FromName=$EMAIL_FROM_NAME
Email__Resend__ApiKey=$RESEND_API_KEY"
            # Add base URL if using Resend-compatible provider
            if [[ -n "$RESEND_BASE_URL" ]]; then
                email_env="$email_env
Email__Resend__BaseUrl=$RESEND_BASE_URL"
            fi
            ;;
        Azure)
            email_env="Email__Provider=Azure
Email__FromAddress=$EMAIL_FROM_ADDRESS
Email__FromName=$EMAIL_FROM_NAME
Email__Azure__ConnectionString=$AZURE_EMAIL_CONNECTION_STRING"
            ;;
        Smtp)
            email_env="Email__Provider=Smtp
Email__FromAddress=$EMAIL_FROM_ADDRESS
Email__FromName=$EMAIL_FROM_NAME
Email__Smtp__Host=$SMTP_HOST
Email__Smtp__Port=$SMTP_PORT
Email__Smtp__Username=$SMTP_USERNAME
Email__Smtp__Password=$SMTP_PASSWORD
Email__Smtp__UseSsl=$SMTP_USE_SSL"
            ;;
        *)
            email_env="# Email disabled"
            ;;
    esac

    # Get ando user's UID for the Docker socket path
    ANDO_UID=$(ssh $SSH_OPTS "$REMOTE_HOST" "id -u $ANDO_USER")

    # Create .env file content
    local env_content="# =============================================================================
# Ando.Server Environment Configuration
# Generated: $(date -Iseconds)
# =============================================================================

# --- ASP.NET Core ---
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080

# --- Database ---
ConnectionStrings__DefaultConnection=$CONNECTION_STRING

# --- GitHub App ---
GitHub__AppId=$GITHUB_APP_ID
GitHub__AppName=$GITHUB_APP_NAME
GitHub__ClientId=$GITHUB_CLIENT_ID
GitHub__ClientSecret=$GITHUB_CLIENT_SECRET
GitHub__WebhookSecret=$GITHUB_WEBHOOK_SECRET
GitHub__PrivateKeyPath=/app/config/github-app.pem

# --- Security ---
Encryption__Key=$ENCRYPTION_KEY

# --- Storage ---
Storage__ArtifactsPath=/data/artifacts
Storage__ArtifactRetentionDays=30
Storage__BuildLogRetentionDays=90

# --- Build ---
Build__ReposPath=/data/repos
Build__DefaultTimeoutMinutes=15
Build__MaxTimeoutMinutes=60
Build__WorkerCount=2

# --- Email ---
$email_env

# --- SQL Server (for docker-compose) ---
MSSQL_SA_PASSWORD=$SA_PASSWORD

# --- Rootless Docker ---
ANDO_UID=$ANDO_UID
"

    # Write to remote server
    ssh $SSH_OPTS "$REMOTE_HOST" "cat > $INSTALL_DIR/config/.env << 'ENVEOF'
$env_content
ENVEOF
chown $ANDO_USER:$ANDO_USER $INSTALL_DIR/config/.env
chmod 600 $INSTALL_DIR/config/.env
cp $INSTALL_DIR/config/.env $INSTALL_DIR/.env
chown $ANDO_USER:$ANDO_USER $INSTALL_DIR/.env
chmod 600 $INSTALL_DIR/.env"

    log_success ".env file created"
}

generate_caddyfile() {
    log_step "Generating Caddyfile"

    # Caddy runs on host and proxies to localhost:8080
    local caddyfile_content="# =============================================================================
# Caddyfile for Ando.Server
# Automatic HTTPS via Let's Encrypt
# Caddy runs on host, proxies to rootless Docker container
# =============================================================================

$DOMAIN_NAME {
    reverse_proxy localhost:8080
}
"

    ssh $SSH_OPTS "$REMOTE_HOST" "cat > /etc/caddy/Caddyfile << 'CADDYEOF'
$caddyfile_content
CADDYEOF"

    log_success "Caddyfile created"
}

generate_compose_file() {
    log_step "Generating docker-compose.yml"

    # Get ando user's UID for the Docker socket path
    ANDO_UID=$(ssh $SSH_OPTS "$REMOTE_HOST" "id -u $ANDO_USER")

    # Network configuration
    local network_config=""
    local sql_network=""
    local server_network=""
    local sql_ports=""

    if [[ "$USE_ISOLATED_NETWORK" == "true" ]]; then
        network_config="
networks:
  $NETWORK_NAME:
    external: true"
        sql_network="
    networks:
      - $NETWORK_NAME"
        server_network="
    networks:
      - $NETWORK_NAME"
    else
        sql_ports="
    ports:
      - \"1433:1433\""
    fi

    # Docker socket path for rootless Docker
    DOCKER_SOCKET="/run/user/$ANDO_UID/docker.sock"

    local compose_content="# =============================================================================
# docker-compose.yml for Ando.Server Production (Rootless Docker)
# Generated: $(date -Iseconds)
#
# This runs under rootless Docker for security.
# Caddy runs on the host for proper TLS certificate acquisition.
# =============================================================================

services:
  # ---------------------------------------------------------------------------
  # SQL Server Database
  # ---------------------------------------------------------------------------
  ando-sqlserver:
    image: $SQL_SERVER_IMAGE
    container_name: ando-sqlserver
    environment:
      - ACCEPT_EULA=Y
      - MSSQL_SA_PASSWORD=\${MSSQL_SA_PASSWORD}
      - MSSQL_PID=Developer
    volumes:
      - $INSTALL_DIR/data/sqldata:/var/opt/mssql$sql_ports
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P \"\${MSSQL_SA_PASSWORD}\" -C -Q \"SELECT 1\" || exit 1
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s
    restart: unless-stopped$sql_network

  # ---------------------------------------------------------------------------
  # Ando CI Server
  # ---------------------------------------------------------------------------
  ando-server:
    image: $DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG
    container_name: ando-server
    env_file:
      - $INSTALL_DIR/config/.env
    ports:
      - \"127.0.0.1:8080:8080\"
    volumes:
      - $DOCKER_SOCKET:/var/run/docker.sock
      - $INSTALL_DIR/data/artifacts:/data/artifacts
      - $INSTALL_DIR/data/repos:/data/repos
      - $INSTALL_DIR/config/github-app.pem:/app/config/github-app.pem:ro
    depends_on:
      ando-sqlserver:
        condition: service_healthy
    healthcheck:
      test: curl -f http://localhost:8080/health || exit 1
      interval: 30s
      timeout: 10s
      start_period: 30s
      retries: 3
    restart: unless-stopped$server_network
$network_config
"

    ssh $SSH_OPTS "$REMOTE_HOST" "cat > $INSTALL_DIR/docker-compose.yml << 'COMPOSEEOF'
$compose_content
COMPOSEEOF
chown $ANDO_USER:$ANDO_USER $INSTALL_DIR/docker-compose.yml"

    log_success "docker-compose.yml created"
}

# -----------------------------------------------------------------------------
# Start Services
# -----------------------------------------------------------------------------
start_services() {
    log_step "Starting services"

    # Get ando user's UID
    ANDO_UID=$(ssh $SSH_OPTS "$REMOTE_HOST" "id -u $ANDO_USER")

    ssh $SSH_OPTS "$REMOTE_HOST" bash << EOF
set -euo pipefail

# Start containers as ando user with rootless Docker
runuser -l $ANDO_USER -c "
export DOCKER_HOST=unix:///run/user/$ANDO_UID/docker.sock
cd $INSTALL_DIR

# Pull SQL Server image
echo 'Pulling SQL Server image...'
docker pull $SQL_SERVER_IMAGE

# Start SQL Server first
echo 'Starting SQL Server...'
docker compose up -d ando-sqlserver

# Wait for SQL Server to be healthy
echo 'Waiting for SQL Server to be healthy (up to 90 seconds)...'
for i in {1..18}; do
    if docker compose ps ando-sqlserver 2>/dev/null | grep -q 'healthy'; then
        echo 'SQL Server is healthy'
        break
    fi
    echo \"  Waiting... (\\\$i/18)\"
    sleep 5
done

# Start Ando.Server
echo 'Starting Ando.Server...'
docker compose up -d ando-server

# Wait for Ando.Server to be healthy
echo 'Waiting for Ando.Server to be healthy...'
for i in {1..12}; do
    if docker compose ps ando-server 2>/dev/null | grep -q 'healthy'; then
        echo 'Ando.Server is healthy'
        break
    fi
    echo \"  Waiting... (\\\$i/12)\"
    sleep 5
done

# Check status
docker compose ps
"

# Restart Caddy to pick up configuration
echo "Starting Caddy..."
systemctl restart caddy
systemctl status caddy --no-pager || true
EOF

    log_success "Services started"
}

# -----------------------------------------------------------------------------
# Verification
# -----------------------------------------------------------------------------
verify_deployment() {
    log_step "Verifying deployment"

    ANDO_UID=$(ssh $SSH_OPTS "$REMOTE_HOST" "id -u $ANDO_USER")

    ssh $SSH_OPTS "$REMOTE_HOST" bash << EOF
set -euo pipefail

echo "=== Container Status ==="
runuser -l $ANDO_USER -c "export DOCKER_HOST=unix:///run/user/$ANDO_UID/docker.sock && cd $INSTALL_DIR && docker compose ps"

echo ""
echo "=== Basic Health Check ==="
sleep 5
if curl -sf http://localhost:8080/health > /dev/null 2>&1; then
    echo "Basic health check: OK"
    curl -s http://localhost:8080/health
    echo ""
else
    echo "Basic health check: PENDING (app may still be starting)"
fi

echo ""
echo "=== Docker-in-Docker Health Check ==="
if curl -sf http://localhost:8080/health/docker > /dev/null 2>&1; then
    echo "Docker health check: OK"
    curl -s http://localhost:8080/health/docker
    echo ""
else
    echo "Docker health check: PENDING (may still be pulling hello-world image)"
fi

echo ""
echo "=== HTTPS Health Check ==="
if curl -sf https://$DOMAIN_NAME/health > /dev/null 2>&1; then
    echo "HTTPS health check: OK"
    curl -s https://$DOMAIN_NAME/health
    echo ""
else
    echo "HTTPS health check: PENDING (certificate may still be issuing)"
fi

echo ""
echo "=== Rootless Docker Security Check ==="
runuser -l $ANDO_USER -c "export DOCKER_HOST=unix:///run/user/$ANDO_UID/docker.sock && docker info --format '{{.SecurityOptions}}'" | grep -q rootless && echo "Rootless mode: VERIFIED" || echo "Rootless mode: WARNING - may not be enabled"

echo ""
echo "=== Recent Ando.Server Logs ==="
runuser -l $ANDO_USER -c "export DOCKER_HOST=unix:///run/user/$ANDO_UID/docker.sock && cd $INSTALL_DIR && docker compose logs --tail=20 ando-server"
EOF
}

# -----------------------------------------------------------------------------
# Print Summary
# -----------------------------------------------------------------------------
print_summary() {
    echo ""
    echo -e "${GREEN}=============================================================================${NC}"
    echo -e "${GREEN}Installation Complete!${NC}"
    echo -e "${GREEN}=============================================================================${NC}"
    echo ""
    echo "Server Details:"
    echo "  URL:              https://$DOMAIN_NAME"
    echo "  Install Path:     $INSTALL_DIR"
    echo "  Config:           $INSTALL_DIR/config/.env"
    echo "  Docker Mode:      Rootless (user: $ANDO_USER)"
    echo ""
    echo "Docker Containers (rootless):"
    echo "  - ando-server     (Ando CI Server)"
    echo "  - ando-sqlserver  (SQL Server 2022)"
    echo ""
    echo "System Services:"
    echo "  - caddy           (Reverse proxy on host)"
    echo ""
    echo "Management Commands (run on server):"
    echo "  # View container status"
    echo "  sudo -u $ANDO_USER DOCKER_HOST=unix:///run/user/\$(id -u $ANDO_USER)/docker.sock docker compose -f $INSTALL_DIR/docker-compose.yml ps"
    echo ""
    echo "  # View logs"
    echo "  sudo -u $ANDO_USER DOCKER_HOST=unix:///run/user/\$(id -u $ANDO_USER)/docker.sock docker compose -f $INSTALL_DIR/docker-compose.yml logs -f"
    echo ""
    echo "  # Restart services"
    echo "  sudo -u $ANDO_USER DOCKER_HOST=unix:///run/user/\$(id -u $ANDO_USER)/docker.sock docker compose -f $INSTALL_DIR/docker-compose.yml restart"
    echo ""
    echo "  # Caddy"
    echo "  systemctl status caddy"
    echo "  systemctl restart caddy"
    echo ""
    echo "GitHub App Configuration:"
    echo "  1. Set Homepage URL:    https://$DOMAIN_NAME"
    echo "  2. Set Callback URL:    https://$DOMAIN_NAME/auth/github/callback"
    echo "  3. Set Webhook URL:     https://$DOMAIN_NAME/webhooks/github"
    echo "  4. Set Webhook Secret:  (the one you provided)"
    echo ""
    echo "Health Endpoints:"
    echo "  Basic:     https://$DOMAIN_NAME/health"
    echo "  Docker:    https://$DOMAIN_NAME/health/docker"
    echo ""
    echo -e "${YELLOW}Note: DNS must point $DOMAIN_NAME to the server IP for HTTPS to work.${NC}"
    echo -e "${GREEN}=============================================================================${NC}"
}

# -----------------------------------------------------------------------------
# Main
# -----------------------------------------------------------------------------
main() {
    echo ""
    echo -e "${CYAN}=============================================================================${NC}"
    echo -e "${CYAN}Ando.Server Installation Script (Rootless Docker)${NC}"
    echo -e "${CYAN}=============================================================================${NC}"
    echo ""

    validate_args "$@"
    check_local_requirements
    check_remote_connection
    gather_configuration

    build_local_image
    install_docker_remote
    setup_rootless_docker
    install_caddy_host
    setup_directories_remote
    setup_docker_network_remote
    transfer_docker_image
    transfer_config_files
    generate_env_file
    generate_caddyfile
    generate_compose_file
    start_services
    verify_deployment
    print_summary
}

main "$@"
