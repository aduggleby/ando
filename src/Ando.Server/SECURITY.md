# Security Model

This document describes the security architecture and considerations for Ando Server.

## Authentication & Authorization

### GitHub OAuth
- Users authenticate via GitHub OAuth 2.0
- OAuth access tokens are encrypted with AES-256 before database storage
- Session cookies use `HttpOnly` flag to prevent XSS access
- CSRF protection via state parameter with constant-time comparison

### Endpoint Protection
- All state-changing endpoints require `[Authorize]` and `[ValidateAntiForgeryToken]`
- Resource ownership is verified before access (users can only access their own projects/builds)
- Webhook endpoints validate GitHub signatures using HMAC-SHA256

## Data Encryption

### Encryption at Rest
- Project secrets are encrypted with AES-256-CBC before database storage
- OAuth tokens are encrypted with AES-256-CBC before database storage
- Random IV per encryption operation (IV prepended to ciphertext)

### Key Management
- Encryption key must be 32 bytes (256 bits), base64 encoded
- **NEVER** commit encryption keys to source control
- Use environment variables in production: `Encryption__Key`
- Generate key with: `openssl rand -base64 32`

## Docker-in-Docker Security Model

### Overview
Ando Server executes builds inside Docker containers for isolation. The Docker socket is mounted into build containers to enable Docker-in-Docker (DinD) workflows.

### Security Implications

**Risk Level: HIGH**

Mounting the Docker socket (`/var/run/docker.sock`) grants the container full access to the Docker daemon. This means:

1. **Container Escape**: A malicious build script could:
   - Create privileged containers
   - Mount the host filesystem
   - Access other containers on the same host
   - Execute commands on the host system

2. **Resource Exhaustion**: Build scripts could:
   - Create unlimited containers
   - Consume all available disk space
   - Exhaust memory and CPU resources

3. **Data Exfiltration**: Build scripts could:
   - Access secrets from other containers
   - Read environment variables from the Docker daemon
   - Inspect other running containers

### Mitigations

#### Currently Implemented
- **Rootless Docker Validation**: Server validates Docker is running in rootless mode on startup (production only). Running Docker as root would allow container escapes to gain full host access.
- **Isolated Build Network**: Build containers run on a dedicated `ando-builds` Docker network, isolated from the host network and other non-build containers. Containers have internet access for fetching dependencies.
- Containers are created with `--rm` flag (auto-cleanup)
- Build timeout enforcement prevents infinite resource consumption
- Container cleanup on build completion/failure
- Secrets are injected as environment variables (not files)

#### Recommended for Production

1. **Dedicated Build Hosts**
   - Run builds on isolated hosts with no access to production systems
   - Use separate Docker daemon per tenant if multi-tenant

2. **Resource Limits**
   ```bash
   docker run --memory=4g --cpus=2 --pids-limit=1000
   ```

3. **Seccomp Profiles**
   - Apply restrictive seccomp profiles to limit syscalls
   - Use AppArmor or SELinux for additional containment

4. **Alternative Approaches**
   - **Sysbox**: Provides true container isolation with nested Docker
   - **Kaniko**: Build images without Docker daemon
   - **Buildah**: Daemonless container builds
   - **Kata Containers**: VM-level isolation for containers

### Trust Model

The current implementation assumes:
- **Trusted Users**: Only authorized users can create projects and trigger builds
- **Semi-Trusted Code**: Build scripts come from user repositories
- **Untrusted External Dependencies**: npm packages, Docker images, etc.

For high-security environments, consider:
- Requiring code review for build.ando files
- Scanning dependencies before builds
- Running builds in ephemeral VMs instead of containers

## Secret Management

### Project Secrets
- Secrets are encrypted before database storage
- Secrets are decrypted at build time and injected as environment variables
- Secret values are never logged or returned via API
- Only secret names are visible in the UI

### Environment Variables for Configuration
Required environment variables for production:

| Variable | Description |
|----------|-------------|
| `Encryption__Key` | AES-256 key (base64, 32 bytes) |
| `GitHub__ClientId` | GitHub OAuth App Client ID |
| `GitHub__ClientSecret` | GitHub OAuth App Client Secret |
| `GitHub__WebhookSecret` | GitHub webhook signature secret |
| `GitHub__AppId` | GitHub App ID |
| `Resend__ApiKey` | Resend email API key |
| `ConnectionStrings__DefaultConnection` | Database connection string |

### Test Environment
For testing, set:
| Variable | Description |
|----------|-------------|
| `Test__ApiKey` | API key for test endpoints |

## Webhook Security

### GitHub Webhook Validation
- All webhooks are validated using HMAC-SHA256 signatures
- Constant-time comparison prevents timing attacks
- Invalid signatures are rejected with 401 Unauthorized

### Signature Validation Process
1. Extract `X-Hub-Signature-256` header
2. Compute HMAC-SHA256 of request body with webhook secret
3. Compare signatures using `CryptographicOperations.FixedTimeEquals`
4. Reject if signatures don't match

## Reporting Security Issues

If you discover a security vulnerability, please report it privately. Do not create public GitHub issues for security vulnerabilities.

## Security Checklist for Deployment

- [ ] Generate unique encryption key: `openssl rand -base64 32`
- [ ] Set all required environment variables
- [ ] Use HTTPS in production
- [ ] Configure secure cookie settings
- [x] ~~Set up network isolation for build containers~~ (automated: `ando-builds` network)
- [x] ~~Validate Docker rootless mode~~ (automated: startup validation)
- [ ] Implement resource limits on build containers
- [ ] Enable audit logging
- [ ] Regular security updates for base images
- [ ] Monitor for unusual build activity
