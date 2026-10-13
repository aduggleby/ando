#!/bin/bash
# =============================================================================
# install-ando-server.sh
#
# Deploys Ando.Server to a remote Ubuntu server with:
# - Rootless Docker (for security - no root container escapes)
# - SQL Server or PostgreSQL in an isolated container (or an external database)
# - Ando.Server container with Docker-in-Docker capability
# - Caddy reverse proxy for HTTPS (installed on host)
#
# Usage:
#   curl -fsSL https://andobuild.com/server-install.sh | bash -s user@server-ip
#   curl -fsSL https://andobuild.com/server-install.sh | bash -s -- --build-local user@server-ip
#   curl -fsSL https://andobuild.com/server-install.sh | bash -s -- --db postgres user@server-ip
#   curl -fsSL https://andobuild.com/server-install.sh | bash -s -- --external-db user@server-ip
#
# Options:
#   --build-local              Build the Docker image locally instead of pulling from ghcr.io
#   --db sqlserver|postgres    Database provider (default: sqlserver)
#   --external-db [conn-str]   Use an existing database of the selected provider instead
#                              of deploying one in a container. The connection string is
#                              prompted for when not given on the command line.
#   --external-sql [conn-str]  Alias for --external-db (kept for compatibility)
#
# =============================================================================

set -euo pipefail

# -----------------------------------------------------------------------------
# Configuration
# -----------------------------------------------------------------------------
# Handle both direct execution and piped execution (curl | bash)
if [[ -n "${BASH_SOURCE[0]:-}" && "${BASH_SOURCE[0]}" != "bash" ]]; then
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
else
    # Piped execution - these are only needed for --build-local
    SCRIPT_DIR=""
    PROJECT_ROOT=""
fi
DOCKER_IMAGE_NAME="ando-server"
DOCKER_IMAGE_TAG="latest"
GHCR_IMAGE="ghcr.io/aduggleby/ando-server"
SQL_SERVER_IMAGE="mcr.microsoft.com/mssql/server:2022-latest"
# Debian-based image on purpose: its postgres user is UID 999, which the
# rootless-Docker ownership mapping in setup_directories_remote relies on.
POSTGRES_IMAGE="postgres:17"
ANDO_USER="ando"
BUILD_LOCAL="false"
# DB_PROVIDER: sqlserver (default) or postgres. USE_EXTERNAL_SQL means "use an
# external database of the selected provider" (name kept from --external-sql).
DB_PROVIDER="sqlserver"
USE_EXTERNAL_SQL="false"
EXTERNAL_CONNECTION_STRING=""
TEST_MODE="false"

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
    read -r -p "$1 [y/N] " response < /dev/tty
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
        read -r -s -p "$prompt [$default]: " value < /dev/tty
        echo ""
    else
        read -r -p "$prompt [$default]: " value < /dev/tty
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
            read -r -s -p "$prompt: " value < /dev/tty
            echo ""
        else
            read -r -p "$prompt: " value < /dev/tty
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

# PostgreSQL has no complexity rules; use hex so the password is safe inside
# connection strings and docker compose .env interpolation.
generate_postgres_password() {
    openssl rand -hex 16
}

generate_encryption_key() {
    openssl rand -base64 32
}

# -----------------------------------------------------------------------------
# Validation
# -----------------------------------------------------------------------------
validate_args() {
    # Parse options
    while [[ $# -gt 0 ]]; do
        case "$1" in
            --build-local)
                BUILD_LOCAL="true"
                shift
                ;;
            --db)
                if [[ $# -lt 2 ]]; then
                    echo "--db requires a value: sqlserver or postgres"
                    exit 1
                fi
                DB_PROVIDER="$2"
                shift 2
                ;;
            --db=*)
                DB_PROVIDER="${1#--db=}"
                shift
                ;;
            --external-db=*|--external-sql=*)
                USE_EXTERNAL_SQL="true"
                EXTERNAL_CONNECTION_STRING="${1#*=}"
                shift
                ;;
            --external-db|--external-sql)
                USE_EXTERNAL_SQL="true"
                shift
                # Optional value: connection strings always contain '=', while the
                # positional user@host argument never does.
                if [[ $# -gt 0 && "$1" != -* && "$1" == *=* ]]; then
                    EXTERNAL_CONNECTION_STRING="$1"
                    shift
                fi
                ;;
            --test)
                TEST_MODE="true"
                shift
                ;;
            -*)
                echo "Unknown option: $1"
                exit 1
                ;;
            *)
                break
                ;;
        esac
    done

    if [[ $# -lt 1 ]]; then
        echo "Usage: $0 [OPTIONS] <user@server-ip> [ssh-key-path]"
        echo ""
        echo "Options:"
        echo "  --build-local              Build image locally instead of pulling from ghcr.io"
        echo "  --db sqlserver|postgres    Database provider (default: sqlserver)"
        echo "  --external-db [conn-str]   Use an existing database instead of deploying a container"
        echo "  --external-sql [conn-str]  Alias for --external-db"
        echo ""
        echo "Example:"
        echo "  $0 root@138.199.223.171"
        echo "  $0 root@138.199.223.171 ~/.ssh/id_claude"
        echo "  $0 --build-local root@138.199.223.171"
        echo "  $0 --db postgres root@138.199.223.171"
        echo "  $0 --external-db root@138.199.223.171"
        echo "  $0 --db postgres --external-db 'Host=db.example.com;Database=AndoServer;Username=ando;Password=...;SSL Mode=VerifyFull' root@138.199.223.171"
        exit 1
    fi

    case "$DB_PROVIDER" in
        sqlserver|SqlServer)
            DB_PROVIDER="sqlserver"
            DATABASE_PROVIDER_SETTING="SqlServer"
            DB_SERVICE="ando-sqlserver"
            DB_LABEL="SQL Server"
            ;;
        postgres|postgresql|PostgreSql)
            DB_PROVIDER="postgres"
            DATABASE_PROVIDER_SETTING="PostgreSql"
            DB_SERVICE="ando-postgres"
            DB_LABEL="PostgreSQL"
            ;;
        *)
            echo "Unknown --db value: $DB_PROVIDER (expected sqlserver or postgres)"
            exit 1
            ;;
    esac

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

    # Always need ssh, scp, openssl
    for cmd in ssh scp openssl; do
        if ! command -v "$cmd" &> /dev/null; then
            missing+=("$cmd")
        fi
    done

    # Only need docker locally if building locally
    if [[ "$BUILD_LOCAL" == "true" ]]; then
        if ! command -v docker &> /dev/null; then
            missing+=("docker")
        fi
    fi

    if [[ ${#missing[@]} -gt 0 ]]; then
        log_error "Missing required commands: ${missing[*]}"
        exit 1
    fi

    # Only check for Dockerfile when building locally
    if [[ "$BUILD_LOCAL" == "true" ]]; then
        if [[ -z "$PROJECT_ROOT" ]]; then
            log_error "--build-local requires running the script directly, not via curl pipe."
            log_error "Download the script first: curl -fsSL https://andobuild.com/server-install.sh -o server-install.sh"
            exit 1
        fi
        if [[ ! -f "$PROJECT_ROOT/src/Ando.Server/Dockerfile" ]]; then
            log_error "Cannot find Ando.Server Dockerfile. Run this script from the ando project root."
            exit 1
        fi
        log_info "Build mode: local (will build from source)"
    else
        log_info "Build mode: pull from $GHCR_IMAGE:$DOCKER_IMAGE_TAG"
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

    # Test mode uses predefined values
    if [[ "$TEST_MODE" == "true" ]]; then
        log_info "Using test mode configuration"
        INSTALL_DIR="/opt/ando"
        USE_ISOLATED_NETWORK="true"
        NETWORK_NAME="ando-network"
        DOMAIN_NAME="test.andobuild.com"
        ADMIN_EMAIL="admin@test.andobuild.com"
        SA_PASSWORD="TestPassword123!Aa"
        POSTGRES_PASSWORD="TestPassword123Aa"
        DATABASE_NAME="AndoServer"
        if [[ "$USE_EXTERNAL_SQL" == "true" ]]; then
            CONNECTION_STRING="$EXTERNAL_CONNECTION_STRING"
            if [[ -z "$CONNECTION_STRING" ]]; then
                log_error "--test with --external-db requires the connection string on the command line"
                exit 1
            fi
        fi
        GITHUB_APP_ID="12345"
        GITHUB_APP_NAME="test-app"
        GITHUB_CLIENT_ID="Iv1.test123"
        GITHUB_CLIENT_SECRET="test-client-secret"
        GITHUB_WEBHOOK_SECRET="test-webhook-secret"
        # Create test PEM content
        GITHUB_PEM_MODE="content"
        GITHUB_PEM_CONTENT="-----BEGIN RSA PRIVATE KEY-----
MIIEowIBAAKCAQEA0Z3VS5JJcds3xfn/ygWyF8PbnGy0AHB7MmE1gFsVwMHOtest
TestKeyForTestingPurposesOnlyDoNotUseInProduction1234567890abcdef
-----END RSA PRIVATE KEY-----"
        ENCRYPTION_KEY=$(generate_encryption_key)
        EMAIL_PROVIDER="None"
        return
    fi

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
    if confirm "Use isolated Docker network? ($DB_LABEL not exposed to host)"; then
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

    # --- Database Configuration ---
    echo ""
    echo -e "${CYAN}=== Database Configuration ($DB_LABEL) ===${NC}"
    if [[ "$USE_EXTERNAL_SQL" == "true" ]]; then
        echo "Using external $DB_LABEL (no container will be deployed)"
        if [[ -n "$EXTERNAL_CONNECTION_STRING" ]]; then
            CONNECTION_STRING="$EXTERNAL_CONNECTION_STRING"
            echo "Using the connection string given on the command line"
        else
            prompt_required "$DB_LABEL connection string" CONNECTION_STRING
        fi
        SA_PASSWORD=""
        POSTGRES_PASSWORD=""
        DATABASE_NAME=""
    elif [[ "$DB_PROVIDER" == "postgres" ]]; then
        local default_pg_password=$(generate_postgres_password)
        prompt_with_default "PostgreSQL password for user 'ando'" "$default_pg_password" POSTGRES_PASSWORD true
        prompt_with_default "Database name" "AndoServer" DATABASE_NAME
        SA_PASSWORD=""
    else
        local default_sa_password=$(generate_password)
        prompt_with_default "SQL Server SA password" "$default_sa_password" SA_PASSWORD true
        prompt_with_default "Database name" "AndoServer" DATABASE_NAME
        POSTGRES_PASSWORD=""
    fi

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
    echo ""
    echo "  How do you want to provide the private key?"
    echo "    1) Paste the PEM content (recommended for curl | bash)"
    echo "    2) Local file path"
    echo "    3) Already uploaded to server"
    echo ""
    read -r -p "Select option [1-3]: " PEM_OPTION < /dev/tty

    case "$PEM_OPTION" in
        1)
            # Paste PEM content
            echo ""
            echo "Paste your PEM file content below, then press Enter twice:"
            GITHUB_PEM_CONTENT=""
            while IFS= read -r line < /dev/tty; do
                [[ -z "$line" ]] && break
                GITHUB_PEM_CONTENT+="$line"$'\n'
            done
            if [[ -z "$GITHUB_PEM_CONTENT" ]]; then
                log_error "No PEM content provided"
                exit 1
            fi
            GITHUB_PEM_PATH=""
            GITHUB_PEM_MODE="content"
            ;;
        2)
            # Local file path
            prompt_required "Path to local github-app.pem file" GITHUB_PEM_PATH
            # Expand tilde to home directory
            GITHUB_PEM_PATH="${GITHUB_PEM_PATH/#\~/$HOME}"
            if [[ ! -f "$GITHUB_PEM_PATH" ]]; then
                log_error "File not found: $GITHUB_PEM_PATH"
                exit 1
            fi
            GITHUB_PEM_MODE="local"
            ;;
        3)
            # Already on server
            prompt_with_default "Path on server" "$INSTALL_DIR/config/github-app.pem" GITHUB_PEM_SERVER_PATH
            GITHUB_PEM_MODE="server"
            ;;
        *)
            log_error "Invalid option"
            exit 1
            ;;
    esac

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
    echo "  2) Azure Communication Services"
    echo "  3) SMTP"
    echo "  4) Skip (no email notifications)"
    echo ""
    read -r -p "Select email provider [1-4]: " EMAIL_CHOICE < /dev/tty

    case "$EMAIL_CHOICE" in
        1)
            EMAIL_PROVIDER="Resend"
            prompt_required "Resend API Key" RESEND_API_KEY true
            prompt_required "From email address" EMAIL_FROM_ADDRESS
            prompt_with_default "From name" "Ando Server" EMAIL_FROM_NAME
            ;;
        2)
            EMAIL_PROVIDER="Azure"
            prompt_required "Azure Communication Services connection string" AZURE_EMAIL_CONNECTION_STRING true
            prompt_required "From email address" EMAIL_FROM_ADDRESS
            prompt_with_default "From name" "Ando Server" EMAIL_FROM_NAME
            ;;
        3)
            EMAIL_PROVIDER="Smtp"
            prompt_required "SMTP Host" SMTP_HOST
            prompt_with_default "SMTP Port" "587" SMTP_PORT
            prompt_required "SMTP Username" SMTP_USERNAME
            prompt_required "SMTP Password" SMTP_PASSWORD true
            prompt_with_default "Use SSL" "true" SMTP_USE_SSL
            prompt_required "From email address" EMAIL_FROM_ADDRESS
            prompt_with_default "From name" "Ando Server" EMAIL_FROM_NAME
            ;;
        4|*)
            EMAIL_PROVIDER="None"
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
    echo "  DB provider:     $DB_LABEL ($([[ "$USE_EXTERNAL_SQL" == "true" ]] && echo external || echo container))"
    echo "  Database:        ${DATABASE_NAME:-(from connection string)}"
    echo "  GitHub App:      $GITHUB_APP_NAME (ID: $GITHUB_APP_ID)"
    echo "  Email provider:  $EMAIL_PROVIDER"
    echo ""

    if ! confirm "Proceed with installation?"; then
        log_info "Installation cancelled"
        exit 0
    fi
}

# -----------------------------------------------------------------------------
# Docker Image Management
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

pull_docker_image_remote() {
    log_step "Pulling Docker image from GitHub Container Registry"

    # Get ando user's UID
    ANDO_UID=$(ssh $SSH_OPTS "$REMOTE_HOST" "id -u $ANDO_USER")

    # Pull and tag the image, capturing the exit code
    if ! ssh $SSH_OPTS "$REMOTE_HOST" bash << EOF
set -euo pipefail

# Pull image as ando user with rootless Docker
runuser -l $ANDO_USER -c '
set -euo pipefail
export DOCKER_HOST=unix:///run/user/$ANDO_UID/docker.sock
echo "Pulling $GHCR_IMAGE:$DOCKER_IMAGE_TAG..."
docker pull $GHCR_IMAGE:$DOCKER_IMAGE_TAG || exit 1
docker tag $GHCR_IMAGE:$DOCKER_IMAGE_TAG $DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG || exit 1
echo "Image pulled and tagged as $DOCKER_IMAGE_NAME:$DOCKER_IMAGE_TAG"
'
EOF
    then
        log_error "Failed to pull Docker image from ghcr.io"
        log_error "Make sure the image exists: $GHCR_IMAGE:$DOCKER_IMAGE_TAG"
        exit 1
    fi

    log_success "Docker image pulled from ghcr.io"
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

mkdir -p "$INSTALL_DIR"/{config,data/{artifacts,repos},logs}
chmod 700 "$INSTALL_DIR/config"

# Set ownership to ando user
chown -R $ANDO_USER:$ANDO_USER "$INSTALL_DIR"

# Rootless Docker maps container UID/GID N (N >= 1) to host ID "start + N - 1",
# where start is the first ID of the $ANDO_USER range in /etc/subuid (UIDs) or
# /etc/subgid (GIDs). Pre-existing ranges need not start at 100000 and UID and
# GID ranges may differ, so both are read from the actual entries.
subid_host_id() {
    local file="\$1" container_id="\$2" entry start count
    entry=\$(grep -E "^($ANDO_USER|$ANDO_UID):" "\$file" | head -1 || true)
    if [[ -z "\$entry" ]]; then
        echo "ERROR: no $ANDO_USER entry in \$file; rootless Docker cannot map container IDs" >&2
        return 1
    fi
    start=\$(echo "\$entry" | cut -d: -f2)
    count=\$(echo "\$entry" | cut -d: -f3)
    if ! [[ "\$start" =~ ^[0-9]+\$ && "\$count" =~ ^[0-9]+\$ ]]; then
        echo "ERROR: malformed entry in \$file: \$entry" >&2
        return 1
    fi
    if (( count < container_id )); then
        echo "ERROR: \$file range for $ANDO_USER has \$count IDs; container ID \$container_id needs at least \$container_id" >&2
        return 1
    fi
    echo \$((start + container_id - 1))
}

if [[ "$USE_EXTERNAL_SQL" == "true" ]]; then
    echo "External database: no database data directory needed"
elif [[ "$DB_PROVIDER" == "postgres" ]]; then
    # PostgreSQL (Debian image) runs as postgres (UID/GID 999 inside the container)
    DB_HOST_UID=\$(subid_host_id /etc/subuid 999)
    DB_HOST_GID=\$(subid_host_id /etc/subgid 999)
    mkdir -p "$INSTALL_DIR/data/pgdata"
    chown -R "\$DB_HOST_UID:\$DB_HOST_GID" "$INSTALL_DIR/data/pgdata"
    chmod 700 "$INSTALL_DIR/data/pgdata"
    echo "pgdata owned by host \$DB_HOST_UID:\$DB_HOST_GID (container postgres 999:999)"
else
    # SQL Server runs as mssql (UID 10001 inside the container)
    DB_HOST_UID=\$(subid_host_id /etc/subuid 10001)
    DB_HOST_GID=\$(subid_host_id /etc/subgid 10001)
    mkdir -p "$INSTALL_DIR/data/sqldata"
    chown -R "\$DB_HOST_UID:\$DB_HOST_GID" "$INSTALL_DIR/data/sqldata"
    echo "sqldata owned by host \$DB_HOST_UID:\$DB_HOST_GID (container mssql 10001)"
fi

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

    # Transfer GitHub PEM file based on mode
    case "$GITHUB_PEM_MODE" in
        content)
            # Write pasted content to server
            ssh $SSH_OPTS "$REMOTE_HOST" "cat > $INSTALL_DIR/config/github-app.pem << 'PEMEOF'
$GITHUB_PEM_CONTENT
PEMEOF"
            ssh $SSH_OPTS "$REMOTE_HOST" "chown $ANDO_USER:$ANDO_USER $INSTALL_DIR/config/github-app.pem && chmod 600 $INSTALL_DIR/config/github-app.pem"
            ;;
        local)
            # SCP local file to server
            scp $SCP_OPTS "$GITHUB_PEM_PATH" "$REMOTE_HOST:$INSTALL_DIR/config/github-app.pem"
            ssh $SSH_OPTS "$REMOTE_HOST" "chown $ANDO_USER:$ANDO_USER $INSTALL_DIR/config/github-app.pem && chmod 600 $INSTALL_DIR/config/github-app.pem"
            ;;
        server)
            # File already on server - just copy/link to expected location if different
            if [[ "$GITHUB_PEM_SERVER_PATH" != "$INSTALL_DIR/config/github-app.pem" ]]; then
                ssh $SSH_OPTS "$REMOTE_HOST" "cp '$GITHUB_PEM_SERVER_PATH' '$INSTALL_DIR/config/github-app.pem' && chown $ANDO_USER:$ANDO_USER $INSTALL_DIR/config/github-app.pem && chmod 600 $INSTALL_DIR/config/github-app.pem"
            fi
            ;;
    esac

    log_success "Configuration files transferred"
}

# -----------------------------------------------------------------------------
# Generate Configuration Files
# -----------------------------------------------------------------------------
generate_env_file() {
    log_step "Generating .env file"

    # Build connection string based on configuration
    # If using an external database, CONNECTION_STRING is already set from gather_configuration
    if [[ "$USE_EXTERNAL_SQL" != "true" && "$DB_PROVIDER" == "postgres" ]]; then
        # The service name resolves on both the isolated and the default compose network
        CONNECTION_STRING="Host=ando-postgres;Database=$DATABASE_NAME;Username=ando;Password=$POSTGRES_PASSWORD"
    elif [[ "$USE_EXTERNAL_SQL" != "true" ]]; then
        if [[ "$USE_ISOLATED_NETWORK" == "true" ]]; then
            CONNECTION_STRING="Server=ando-sqlserver;Database=$DATABASE_NAME;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=true"
        else
            CONNECTION_STRING="Server=localhost,1433;Database=$DATABASE_NAME;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=true"
        fi
    fi

    # Build email configuration
    local email_env=""
    case "$EMAIL_PROVIDER" in
        Resend)
            email_env="Email__Provider=Resend
Email__FromAddress=$EMAIL_FROM_ADDRESS
Email__FromName=$EMAIL_FROM_NAME
Email__Resend__ApiKey=$RESEND_API_KEY"
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
# Provider: SqlServer or PostgreSql. The connection string must match it.
Database__Provider=$DATABASE_PROVIDER_SETTING
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

# --- Rootless Docker ---
ANDO_UID=$ANDO_UID
"

    # Add database container credentials only if using a containerized database
    if [[ "$USE_EXTERNAL_SQL" != "true" && "$DB_PROVIDER" == "postgres" ]]; then
        env_content+="
# --- PostgreSQL (for docker-compose) ---
POSTGRES_USER=ando
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
POSTGRES_DB=$DATABASE_NAME
"
    elif [[ "$USE_EXTERNAL_SQL" != "true" ]]; then
        env_content+="
# --- SQL Server (for docker-compose) ---
MSSQL_SA_PASSWORD=$SA_PASSWORD
"
    fi

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

    if [[ "$USE_ISOLATED_NETWORK" == "true" && "$USE_EXTERNAL_SQL" != "true" ]]; then
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
    elif [[ "$USE_EXTERNAL_SQL" != "true" && "$DB_PROVIDER" == "postgres" ]]; then
        sql_ports="
    ports:
      - \"5432:5432\""
    elif [[ "$USE_EXTERNAL_SQL" != "true" ]]; then
        sql_ports="
    ports:
      - \"1433:1433\""
    fi

    # Docker socket path for rootless Docker
    DOCKER_SOCKET="/run/user/$ANDO_UID/docker.sock"

    # Database service (only if not using an external database)
    local sql_service=""
    local depends_on=""
    if [[ "$USE_EXTERNAL_SQL" != "true" && "$DB_PROVIDER" == "postgres" ]]; then
        sql_service="  # ---------------------------------------------------------------------------
  # PostgreSQL Database
  # ---------------------------------------------------------------------------
  ando-postgres:
    image: $POSTGRES_IMAGE
    container_name: ando-postgres
    environment:
      - POSTGRES_USER=\${POSTGRES_USER}
      - POSTGRES_PASSWORD=\${POSTGRES_PASSWORD}
      - POSTGRES_DB=\${POSTGRES_DB}
    volumes:
      - $INSTALL_DIR/data/pgdata:/var/lib/postgresql/data$sql_ports
    healthcheck:
      test: [\"CMD-SHELL\", \"pg_isready -h localhost -U ando -d $DATABASE_NAME\"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 30s
    restart: unless-stopped$sql_network

"
        depends_on="
    depends_on:
      ando-postgres:
        condition: service_healthy"
    elif [[ "$USE_EXTERNAL_SQL" != "true" ]]; then
        sql_service="  # ---------------------------------------------------------------------------
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

"
        depends_on="
    depends_on:
      ando-sqlserver:
        condition: service_healthy"
    fi

    local compose_content="# =============================================================================
# docker-compose.yml for Ando.Server Production (Rootless Docker)
# Generated: $(date -Iseconds)
#
# This runs under rootless Docker for security.
# Caddy runs on the host for proper TLS certificate acquisition.
# =============================================================================

services:
$sql_service  # ---------------------------------------------------------------------------
  # Ando Server
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
      - $INSTALL_DIR/data/keys:/data/keys
      - $INSTALL_DIR/config/github-app.pem:/app/config/github-app.pem:ro$depends_on
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

    if [[ "$USE_EXTERNAL_SQL" == "true" ]]; then
        # External SQL - just start Ando.Server
        ssh $SSH_OPTS "$REMOTE_HOST" bash << EOF
set -euo pipefail

# Start container as ando user with rootless Docker
runuser -l $ANDO_USER -c "
export DOCKER_HOST=unix:///run/user/$ANDO_UID/docker.sock
cd $INSTALL_DIR

# Start Ando.Server
echo 'Starting Ando.Server (using external $DB_LABEL)...'
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
    else
        # Containerized database (SQL Server or PostgreSQL)
        local db_image="$SQL_SERVER_IMAGE"
        if [[ "$DB_PROVIDER" == "postgres" ]]; then
            db_image="$POSTGRES_IMAGE"
        fi

        ssh $SSH_OPTS "$REMOTE_HOST" bash << EOF
set -euo pipefail

# Start containers as ando user with rootless Docker
runuser -l $ANDO_USER -c "
export DOCKER_HOST=unix:///run/user/$ANDO_UID/docker.sock
cd $INSTALL_DIR

# Pull database image
echo 'Pulling $DB_LABEL image...'
docker pull $db_image

# Start the database first
echo 'Starting $DB_LABEL...'
docker compose up -d $DB_SERVICE

# Wait for the database to be healthy
echo 'Waiting for $DB_LABEL to be healthy (up to 90 seconds)...'
for i in {1..18}; do
    if docker compose ps $DB_SERVICE 2>/dev/null | grep -q 'healthy'; then
        echo '$DB_LABEL is healthy'
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
    fi

    log_success "Services started"
}

# -----------------------------------------------------------------------------
# Setup Automated Backups
# -----------------------------------------------------------------------------
setup_automated_backups() {
    # With an external database the job still backs up config/ (.env with
    # Encryption__Key, github-app.pem) and the Data Protection keys; only the
    # database dump is skipped (the external database has its own backups).
    if [[ "$USE_EXTERNAL_SQL" == "true" ]]; then
        log_step "Setting up automated backups (configuration and keys only; external $DB_LABEL is not dumped)"
    else
        log_step "Setting up automated backups"
    fi

    ANDO_UID=$(ssh $SSH_OPTS "$REMOTE_HOST" "id -u $ANDO_USER")

    ssh $SSH_OPTS "$REMOTE_HOST" bash << BACKUP_EOF
set -euo pipefail

# Create directories
mkdir -p "$INSTALL_DIR/scripts"
mkdir -p "$INSTALL_DIR/backups"
chown -R $ANDO_USER:$ANDO_USER "$INSTALL_DIR/scripts" "$INSTALL_DIR/backups"
if [[ "$USE_EXTERNAL_SQL" != "true" && "$DB_PROVIDER" == "sqlserver" ]]; then
    # SQL Server writes .bak files inside its volume; the script copies them out
    mkdir -p "$INSTALL_DIR/data/sqldata/backup"
    chown -R $ANDO_USER:$ANDO_USER "$INSTALL_DIR/data/sqldata/backup"
fi

# Write backup script
cat > "$INSTALL_DIR/scripts/backup.sh" << 'SCRIPT_EOF'
#!/bin/bash
# =============================================================================
# Ando Server Automated Backup Script
#
# Runs daily at 2 AM via cron. Creates backups with retention:
# - Daily backups: kept for 7 days
# - Monthly backups (1st of month): kept for 12 months
#
# Backs up:
# - Database: SQL Server (BACKUP DATABASE -> .bak) or
#   PostgreSQL (pg_dump --format=custom -> .dump).
#   Skipped for an external database (EXTERNAL_DB=true): back that database up
#   with its own tooling (managed-service backups, pg_dump, BACKUP DATABASE).
# - Configuration files (config/ incl. .env and github-app.pem,
#   docker-compose.yml, Caddyfile) -> -config.tar.gz
# - Data protection keys (data/keys/) -> -keys.tar.gz
#
# Project secrets in the database are AES-encrypted with Encryption__Key from
# config/.env. Keep the -config archive with every database backup: without
# that key the secrets cannot be decrypted. The config and keys archives do not
# depend on the database provider.
#
# Restore (PostgreSQL):
#   docker exec -i ando-postgres pg_restore -U ando -d <db> --clean --if-exists < file.dump
# Restore (SQL Server): see https://andobuild.com/server (Backup and Restore)
# =============================================================================
set -euo pipefail

INSTALL_DIR="@@INSTALL_DIR@@"
BACKUP_DIR="\$INSTALL_DIR/backups"
DOCKER_HOST="unix:///run/user/@@ANDO_UID@@/docker.sock"
DB_PROVIDER="@@DB_PROVIDER@@"
EXTERNAL_DB="@@EXTERNAL_DB@@"
DATABASE_NAME="@@DATABASE_NAME@@"
DATE=\$(date +%Y%m%d)
DAY_OF_MONTH=\$(date +%d)

export DOCKER_HOST

# Create backup directory if needed
mkdir -p "\$BACKUP_DIR"

# Determine if this is a monthly or daily backup
if [[ "\$DAY_OF_MONTH" == "01" ]]; then
    BACKUP_PREFIX="ando-monthly-\$(date +%Y%m)"
else
    BACKUP_PREFIX="ando-daily-\$DATE"
fi

echo "[\$(date '+%Y-%m-%d %H:%M:%S')] Starting backup: \$BACKUP_PREFIX"

# 1. Database backup
if [[ "\$EXTERNAL_DB" == "true" ]]; then
    # External database: not backed up by this script. Back it up with its own
    # tooling; restoring it needs the Encryption__Key in the -config archive.
    echo "  Skipping database backup (external database - use its own backup tooling)"
elif [[ "\$DB_PROVIDER" == "postgres" ]]; then
    # pg_dump runs inside the database container (local socket, no password
    # needed); custom format is compressed and restorable with pg_restore.
    echo "  Backing up database (pg_dump)..."
    docker exec ando-postgres pg_dump -U ando -Fc "\$DATABASE_NAME" > "\$BACKUP_DIR/\$BACKUP_PREFIX.dump.partial"
    mv "\$BACKUP_DIR/\$BACKUP_PREFIX.dump.partial" "\$BACKUP_DIR/\$BACKUP_PREFIX.dump"
else
    # Read only the SA password; sourcing the whole .env would execute values
    # that contain spaces or semicolons (connection string, email names).
    MSSQL_SA_PASSWORD=\$(grep -m1 '^MSSQL_SA_PASSWORD=' "\$INSTALL_DIR/config/.env" | cut -d= -f2-)
    echo "  Backing up database..."
    docker exec ando-sqlserver /opt/mssql-tools18/bin/sqlcmd \\
        -S localhost -U sa -P "\$MSSQL_SA_PASSWORD" -C \\
        -Q "BACKUP DATABASE [\$DATABASE_NAME] TO DISK = '/var/opt/mssql/backup/\$BACKUP_PREFIX.bak' WITH FORMAT, INIT"
    cp "\$INSTALL_DIR/data/sqldata/backup/\$BACKUP_PREFIX.bak" "\$BACKUP_DIR/"
fi

# 2. Configuration files backup (config dir + docker-compose.yml + Caddyfile)
echo "  Backing up configuration..."
tar -czf "\$BACKUP_DIR/\$BACKUP_PREFIX-config.tar.gz" \\
    -C / \\
    "\${INSTALL_DIR#/}/config" \\
    "\${INSTALL_DIR#/}/docker-compose.yml" \\
    "etc/caddy/Caddyfile" \\
    2>/dev/null || true

# 3. Data protection keys backup
echo "  Backing up data protection keys..."
if [[ -d "\$INSTALL_DIR/data/keys" ]]; then
    tar -czf "\$BACKUP_DIR/\$BACKUP_PREFIX-keys.tar.gz" \\
        -C "\$INSTALL_DIR/data" keys 2>/dev/null || true
fi

# Clean up old daily backups (older than 7 days)
find "\$BACKUP_DIR" -name "ando-daily-*" -mtime +7 -delete 2>/dev/null || true

# Clean up old monthly backups (older than 12 months / 365 days)
find "\$BACKUP_DIR" -name "ando-monthly-*" -mtime +365 -delete 2>/dev/null || true

# Clean up backup files in SQL Server volume (keep only latest)
if [[ -d "\$INSTALL_DIR/data/sqldata/backup" ]]; then
    find "\$INSTALL_DIR/data/sqldata/backup" -name "*.bak" -mtime +1 -delete 2>/dev/null || true
fi

echo "[\$(date '+%Y-%m-%d %H:%M:%S')] Backup completed: \$BACKUP_PREFIX"

# List current backups
echo "Current backups:"
ls -lh "\$BACKUP_DIR"/ 2>/dev/null || echo "  (none)"
SCRIPT_EOF

# Replace placeholders in backup script
sed -i "s|@@INSTALL_DIR@@|$INSTALL_DIR|g" "$INSTALL_DIR/scripts/backup.sh"
sed -i "s|@@ANDO_UID@@|$ANDO_UID|g" "$INSTALL_DIR/scripts/backup.sh"
sed -i "s|@@DB_PROVIDER@@|$DB_PROVIDER|g" "$INSTALL_DIR/scripts/backup.sh"
sed -i "s|@@EXTERNAL_DB@@|$USE_EXTERNAL_SQL|g" "$INSTALL_DIR/scripts/backup.sh"
sed -i "s|@@DATABASE_NAME@@|$DATABASE_NAME|g" "$INSTALL_DIR/scripts/backup.sh"

chmod +x "$INSTALL_DIR/scripts/backup.sh"
chown $ANDO_USER:$ANDO_USER "$INSTALL_DIR/scripts/backup.sh"

# Setup cron job to run daily at 2 AM as ando user
cat > /etc/cron.d/ando-backup << CRON_EOF
# Ando Server automated backup (database unless external, config, keys)
# Runs daily at 2 AM
0 2 * * * $ANDO_USER $INSTALL_DIR/scripts/backup.sh >> /var/log/ando-backup.log 2>&1
CRON_EOF

chmod 644 /etc/cron.d/ando-backup

# Create log file
touch /var/log/ando-backup.log
chown $ANDO_USER:$ANDO_USER /var/log/ando-backup.log

echo "Backup script installed: $INSTALL_DIR/scripts/backup.sh"
echo "Cron job installed: /etc/cron.d/ando-backup (daily at 2 AM)"
echo "Backup directory: $INSTALL_DIR/backups"

# Create management scripts
for script in update status logs restart; do
    cat > "$INSTALL_DIR/scripts/\$script.sh" << MGMT_EOF
#!/bin/bash
set -euo pipefail
export DOCKER_HOST="unix:///run/user/$ANDO_UID/docker.sock"
MGMT_EOF
done

# Update script
cat >> "$INSTALL_DIR/scripts/update.sh" << 'EOF'
echo "Pulling latest image..."
docker compose -f "@@INSTALL_DIR@@/docker-compose.yml" pull
echo "Restarting services..."
docker compose -f "@@INSTALL_DIR@@/docker-compose.yml" up -d
echo "Update complete."
docker compose -f "@@INSTALL_DIR@@/docker-compose.yml" ps
EOF

# Status script
cat >> "$INSTALL_DIR/scripts/status.sh" << 'EOF'
docker compose -f "@@INSTALL_DIR@@/docker-compose.yml" ps
EOF

# Logs script
cat >> "$INSTALL_DIR/scripts/logs.sh" << 'EOF'
docker compose -f "@@INSTALL_DIR@@/docker-compose.yml" logs -f "\$@"
EOF

# Restart script
cat >> "$INSTALL_DIR/scripts/restart.sh" << 'EOF'
docker compose -f "@@INSTALL_DIR@@/docker-compose.yml" restart
docker compose -f "@@INSTALL_DIR@@/docker-compose.yml" ps
EOF

# Replace placeholders and set permissions
for script in update status logs restart; do
    sed -i "s|@@INSTALL_DIR@@|$INSTALL_DIR|g" "$INSTALL_DIR/scripts/\$script.sh"
    chmod +x "$INSTALL_DIR/scripts/\$script.sh"
    chown $ANDO_USER:$ANDO_USER "$INSTALL_DIR/scripts/\$script.sh"
done

echo "Management scripts installed: update.sh, status.sh, logs.sh, restart.sh"
BACKUP_EOF

    log_success "Automated backups configured (daily at 2 AM)"
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
    echo "  Database:         $DB_LABEL (Database__Provider=$DATABASE_PROVIDER_SETTING)"
    echo ""
    echo "Docker Containers (rootless):"
    echo "  - ando-server     (Ando Server)"
    if [[ "$USE_EXTERNAL_SQL" == "true" ]]; then
        echo "  (external $DB_LABEL, no database container)"
    elif [[ "$DB_PROVIDER" == "postgres" ]]; then
        echo "  - ando-postgres   (PostgreSQL 17)"
    else
        echo "  - ando-sqlserver  (SQL Server 2022)"
    fi
    echo ""
    echo "System Services:"
    echo "  - caddy           (Reverse proxy on host)"
    echo ""
    echo "Automated Backups:"
    echo "  Schedule:         Daily at 2 AM"
    echo "  Location:         $INSTALL_DIR/backups/"
    echo "  Retention:        7 daily + 12 monthly"
    echo "  Log:              /var/log/ando-backup.log"
    echo "  Config/keys:      ando-*-config.tar.gz (.env incl. Encryption__Key, github-app.pem), ando-*-keys.tar.gz"
    if [[ "$USE_EXTERNAL_SQL" == "true" ]]; then
        echo "  Database:         NOT backed up (external $DB_LABEL) - use its own backup tooling"
    elif [[ "$DB_PROVIDER" == "postgres" ]]; then
        echo "  Database dumps:   ando-*.dump (pg_dump custom format)"
        echo "  Restore:          docker exec -i ando-postgres pg_restore -U ando -d $DATABASE_NAME --clean --if-exists < FILE.dump"
    else
        echo "  Database backups: ando-*.bak (SQL Server BACKUP DATABASE)"
    fi
    echo -e "  ${YELLOW}Keep the config archive with every database backup: project secrets cannot be decrypted without Encryption__Key.${NC}"
    echo ""
    echo "Management Scripts (run on server):"
    echo "  sudo -u $ANDO_USER $INSTALL_DIR/scripts/status.sh   # View container status"
    echo "  sudo -u $ANDO_USER $INSTALL_DIR/scripts/logs.sh     # View logs (Ctrl+C to exit)"
    echo "  sudo -u $ANDO_USER $INSTALL_DIR/scripts/restart.sh  # Restart services"
    echo "  sudo -u $ANDO_USER $INSTALL_DIR/scripts/update.sh   # Pull latest and restart"
    echo "  sudo -u $ANDO_USER $INSTALL_DIR/scripts/backup.sh   # Run backup now"
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

    # Build locally if requested
    if [[ "$BUILD_LOCAL" == "true" ]]; then
        build_local_image
    fi

    install_docker_remote
    setup_rootless_docker
    install_caddy_host
    setup_directories_remote
    setup_docker_network_remote

    # Transfer local image or pull from ghcr.io
    if [[ "$BUILD_LOCAL" == "true" ]]; then
        transfer_docker_image
    else
        pull_docker_image_remote
    fi

    transfer_config_files
    generate_env_file
    generate_caddyfile
    generate_compose_file
    start_services
    setup_automated_backups
    verify_deployment
    print_summary
}

main "$@"
