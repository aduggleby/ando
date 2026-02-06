# ANDO Server Architecture

## Overview

Ando.Server is a cloud-native CI/CD platform built on ASP.NET Core that orchestrates builds using the ANDO CLI in Docker containers. It provides GitHub integration, real-time build logging, and a modern web interface.

## Core Architectural Characteristics

1. **Hybrid API Architecture** - Combines MVC controllers and FastEndpoints for transitional migration
2. **Asynchronous Background Jobs** - Hangfire-based build orchestration with worker pool
3. **Real-time Communication** - SignalR for streaming build logs to clients
4. **Containerized Build Execution** - Docker-based isolated build environments
5. **Multi-user Tenancy** - Per-user project ownership with role-based access control
6. **GitHub-centric** - Deep GitHub integration via GitHub App OAuth + webhooks

## High-Level Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                     Web Layer                                │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ MVC Views   │  │ FastEndpoints│  │ React ClientApp     │  │
│  │ (Legacy)    │  │ REST API     │  │ SPA                 │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                   Service Layer                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ BuildService│  │ProjectService│  │ GitHubService       │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │EmailService │  │ Encryption  │  │ ProfileDetector     │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                 Background Job Layer                         │
│  ┌────────────────────────────────────────────────────────┐ │
│  │                    Hangfire                             │ │
│  │  ┌──────────────┐  ┌───────────────┐  ┌─────────────┐  │ │
│  │  │ExecuteBuildJob│  │CleanupArtifacts│  │CleanupBuilds│  │ │
│  │  └──────────────┘  └───────────────┘  └─────────────┘  │ │
│  └────────────────────────────────────────────────────────┘ │
│                           │                                  │
│  ┌────────────────────────▼───────────────────────────────┐ │
│  │              BuildOrchestrator                          │ │
│  │  - Repository preparation (clone/fetch)                 │ │
│  │  - Container creation and management                    │ │
│  │  - Build execution (ando run)                           │ │
│  │  - Artifact collection                                  │ │
│  │  - Status reporting (GitHub, email)                     │ │
│  └────────────────────────────────────────────────────────┘ │
└──────────────────────────┬──────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                   Data Layer                                 │
│  ┌─────────────────────────────────────────────────────────┐│
│  │              Entity Framework Core                       ││
│  │  AndoDbContext (Identity + Custom Entities)              ││
│  └─────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   SQL Server                             ││
│  │  Users, Projects, Builds, Logs, Artifacts, Secrets       ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────┐
│                 External Integrations                        │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ GitHub API  │  │ Docker      │  │ Email Provider      │  │
│  │ (App/OAuth) │  │ Engine      │  │ (Resend/SMTP)       │  │
│  └─────────────┘  └─────────────┘  └─────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

---

## Directory Structure

```
src/Ando.Server/
├── Program.cs              # Application bootstrap and configuration
├── AndoDbContext.cs        # Entity Framework database context
├── Controllers/            # MVC controllers (legacy)
│   ├── WebhooksController.cs   # GitHub webhook receiver
│   ├── BuildsController.cs     # Build views
│   ├── ProjectsController.cs   # Project management views
│   ├── AuthController.cs       # Authentication views
│   ├── AdminController.cs      # Admin panel
│   └── HomeController.cs       # Dashboard
├── Endpoints/              # FastEndpoints REST API
│   ├── Auth/               # Authentication endpoints
│   ├── Builds/             # Build management endpoints
│   ├── Projects/           # Project CRUD endpoints
│   ├── Admin/              # Admin-only endpoints
│   └── Home/               # Dashboard/health endpoints
├── Contracts/              # DTOs for API requests/responses
│   ├── Auth/               # Auth contracts
│   ├── Builds/             # Build contracts
│   ├── Projects/           # Project contracts
│   └── Admin/              # Admin contracts
├── GitHub/                 # GitHub integration
│   ├── IGitHubService.cs       # GitHub operations interface
│   ├── GitHubService.cs        # GitHub API implementation
│   ├── GitHubAppAuthenticator.cs # JWT generation
│   └── WebhookPayloads.cs      # Webhook payload models
├── Services/               # Business logic services
│   ├── IBuildService.cs        # Build lifecycle management
│   ├── BuildService.cs
│   ├── IProjectService.cs      # Project management
│   ├── ProjectService.cs
│   ├── IEncryptionService.cs   # AES-256 encryption
│   ├── EncryptionService.cs
│   ├── IRequiredSecretsDetector.cs # Secret detection
│   ├── RequiredSecretsDetector.cs
│   ├── IProfileDetector.cs     # Profile detection
│   ├── ProfileDetector.cs
│   └── IEmailService.cs        # Email notifications
├── Jobs/                   # Hangfire background jobs
│   ├── ExecuteBuildJob.cs      # Build execution
│   ├── CleanupArtifactsJob.cs  # Artifact cleanup
│   └── CleanupOldBuildsJob.cs  # Build retention
├── Orchestration/          # Build orchestration
│   ├── IBuildOrchestrator.cs   # Orchestration interface
│   ├── BuildOrchestrator.cs    # Main orchestration logic
│   └── ServerBuildLogger.cs    # DB + SignalR logger
├── Hubs/                   # SignalR real-time
│   └── BuildLogHub.cs          # Build log streaming
├── Models/                 # Entity models
│   ├── ApplicationUser.cs      # Extended Identity user
│   ├── ApplicationRole.cs      # Extended Identity role
│   ├── Project.cs              # Project entity
│   ├── Build.cs                # Build entity
│   ├── BuildLogEntry.cs        # Log entries
│   ├── BuildArtifact.cs        # Artifact metadata
│   └── ProjectSecret.cs        # Encrypted secrets
├── Views/                  # Razor views (legacy)
│   ├── Shared/_Layout.cshtml
│   ├── Builds/
│   ├── Projects/
│   └── Home/
├── ClientApp/              # React SPA frontend
│   ├── src/
│   ├── package.json
│   └── vite.config.ts
├── wwwroot/                # Static files
│   └── app/                # Compiled React app
├── Settings/               # Configuration classes
│   ├── GitHubSettings.cs
│   ├── BuildSettings.cs
│   ├── StorageSettings.cs
│   ├── EncryptionSettings.cs
│   └── EmailSettings.cs
└── Dockerfile              # Multi-stage Docker build
```

---

## Component Deep Dive

### 1. Entry Point (Program.cs)

Program.cs bootstraps the application in 8 distinct phases:

#### Phase 1: Configuration Binding
```csharp
- GitHubSettings (webhook secret, app ID, OAuth credentials, private key path)
- EmailSettings (provider selection and credentials)
- StorageSettings (artifacts path, retention policies)
- BuildSettings (timeouts, Docker image, worker count)
- EncryptionSettings (AES-256 key for secrets)
```

#### Phase 2: Database & EF Core
- SQL Server database via connection string
- AndoDbContext with ASP.NET Core Identity integration
- EnsureCreated() for schema creation

#### Phase 3: Data Protection
- ASP.NET Core Data Protection API
- Keys persisted to `/data/keys` directory for container restarts
- Application name: "AndoServer"

#### Phase 4: ASP.NET Core Identity
```csharp
- IdentityUser<int> with ApplicationUser extensions
- Identity roles (Admin, User) seeded on startup
- Password requirements: 8+ chars, digit, lower, upper
- Soft email verification
- Lockout: 15 minutes after 5 failed attempts
```

#### Phase 5: Hangfire Background Jobs
- SQL Server storage backend
- Two queues: "builds" (priority), "default"
- Configurable worker count (default 2)

#### Phase 6: SignalR Real-time
- Default in-memory message bus
- Hubs endpoint: `/hubs/build-logs`

#### Phase 7: Authentication & Authorization
```csharp
- Cookie authentication with 30-day expiration
- Sliding renewal on activity
- API requests return 401 instead of redirect
- "RequireAdmin" authorization policy
```

#### Phase 8: MVC & FastEndpoints
- FastEndpoints with `/api` route prefix
- Swagger documentation in dev/test environments
- SPA fallback to `app/index.html`

#### Middleware Pipeline Order
1. Configuration validation
2. Exception handling
3. HTTPS redirection
4. Static file serving
5. Routing
6. Session
7. Authentication
8. Authorization
9. FastEndpoints
10. Swagger (dev/test)
11. SignalR hub mapping
12. Hangfire dashboard (dev)
13. MVC routes
14. SPA fallback

---

### 2. Controllers & API Endpoints

#### MVC Controllers (Legacy)

| Controller | Purpose | Authentication |
|------------|---------|----------------|
| `WebhooksController` | GitHub webhook receiver | AllowAnonymous (signature validation) |
| `BuildsController` | Build detail views | Required |
| `ProjectsController` | Project management views | Required |
| `AuthController` | Registration, login, logout | Mixed |
| `AdminController` | User management, impersonation | Admin role |
| `HomeController` | Dashboard, health check | Mixed |

#### FastEndpoints (REST API)

**Auth Endpoints (7):**
- `POST /api/auth/register` - Register new user
- `POST /api/auth/login` - Login with email/password
- `POST /api/auth/logout` - Logout
- `GET /api/auth/me` - Get current user info
- `POST /api/auth/verify-email` - Verify email with token
- `POST /api/auth/resend-verification` - Resend verification email
- `POST /api/auth/forgot-password` / `POST /api/auth/reset-password` - Password recovery

**Projects Endpoints (9):**
- `GET /api/projects` - List user's projects
- `POST /api/projects` - Create project
- `GET /api/projects/{id}` - Get project details
- `GET /api/projects/{id}/settings` - Get project settings
- `POST /api/projects/{id}/settings` - Update project settings
- `DELETE /api/projects/{id}` - Delete project
- `POST /api/projects/{id}/secrets/{name}` - Set secret
- `DELETE /api/projects/{id}/secrets/{name}` - Delete secret
- `POST /api/projects/{id}/secrets/bulk-import` - Import multiple secrets

**Builds Endpoints (5):**
- `GET /api/builds/{id}` - Get build details with logs/artifacts
- `GET /api/builds/{id}/logs` - Stream build logs
- `POST /api/builds/{id}/cancel` - Cancel running build
- `POST /api/builds/{id}/retry` - Retry failed build
- `GET /api/builds/{id}/artifacts/{artifactId}/download` - Download artifact

**Admin Endpoints (10):**
- User management (CRUD, lock/unlock)
- Impersonation (start/stop)
- Admin dashboard

---

### 3. GitHub Integration (GitHub/)

#### GitHubAppAuthenticator
Generates JWT signed with RSA private key for GitHub App authentication.

```csharp
- JWT expires after 10 minutes (GitHub max)
- Private key loaded from file path
- Used to obtain installation access tokens
- Installation tokens cached for 50 minutes
```

#### GitHubService (IGitHubService)

**Repository Operations:**
```csharp
CloneRepositoryAsync()    // Shallow clone (depth 50)
FetchAndCheckoutAsync()   // Update existing repo + checkout commit
```

**API Operations:**
```csharp
SetCommitStatusAsync()        // Post build status (pending/success/failure)
GetInstallationTokenAsync()   // Generate installation tokens
GetFileContentAsync()         // Fetch file contents (build.csando detection)
GetBranchHeadShaAsync()       // Get latest commit SHA
```

**HttpClient Configuration:**
- Named client "GitHub" with base address `https://api.github.com/`
- Bearer token authentication
- GitHub API version: 2022-11-28

#### Webhook Processing

**Signature Validation:**
- HMAC-SHA256 using WebhookSecret
- Header: `X-Hub-Signature-256` or `X-Hub-Signature`
- Constant-time comparison to prevent timing attacks

**Event Routing:**
| Event | Handler | Action |
|-------|---------|--------|
| `push` | HandlePushEventAsync | Queue build for matching projects |
| `pull_request` | HandlePullRequestEventAsync | Queue build if PR builds enabled |
| `ping` | - | Return pong |
| Other | - | Log and return 200 OK |

**Push Event Handler:**
1. Skip branch deletions (after SHA = all zeros)
2. Find ALL projects with matching GitHubRepoId
3. For each project:
   - Check branch filter
   - Auto-detect required secrets from build.csando
   - Validate all required secrets configured
   - Update installation ID if changed
   - Queue build via BuildService

---

### 4. Services Layer (Services/)

#### BuildService (IBuildService)
Manages build lifecycle.

**Key Methods:**
```csharp
QueueBuildAsync()           // Create build record + schedule Hangfire job
GetBuildAsync()             // Fetch build with project
GetBuildsForProjectAsync()  // Paginated list (default 20)
GetRecentBuildsForUserAsync() // Recent builds across user's projects
CancelBuildAsync()          // Cancel running/queued builds
RetryBuildAsync()           // Create new build with same parameters
```

**Build Queueing Flow:**
1. Create Build record with Status=Queued
2. Save to database (generate build ID)
3. Update project.LastBuildAt
4. Enqueue ExecuteBuildJob in Hangfire
5. Store HangfireJobId in build record
6. Return build ID

#### ProjectService (IProjectService)
Manages projects and secrets.

**Key Methods:**
```csharp
GetProjectsForUserAsync()              // User's projects ordered by last build
CreateProjectAsync()                   // Create with auto-detection
UpdateProjectSettingsAsync()           // Update settings
SetSecretAsync()                       // Encrypt and store secret
DeleteSecretAsync()                    // Remove secret
DetectAndUpdateRequiredSecretsAsync()  // Parse build.csando
DetectAndUpdateProfilesAsync()         // Extract profiles
```

#### EncryptionService (IEncryptionService)
AES-256 encryption for secrets.

**Implementation:**
- AES-256 in CBC mode
- Random IV generated for each encryption
- IV prepended to ciphertext
- Key from configuration (32 bytes base64)

```csharp
Encrypt(plainText)   // Encrypt string → base64 ciphertext
Decrypt(cipherText)  // Decrypt base64 → plaintext
```

**Usage:**
- ProjectSecret.EncryptedValue storage
- ApplicationUser.GitHubAccessToken storage
- Decryption only when needed (container env vars)

#### RequiredSecretsDetector (IRequiredSecretsDetector)
Detects required environment variables from build scripts.

**Pattern Detection (via Regex):**
| Pattern | Detected Secrets |
|---------|------------------|
| `Env("VAR_NAME")` | VAR_NAME |
| `Nuget.EnsureAuthenticated()` | NUGET_API_KEY |
| `Cloudflare.EnsureAuthenticated()` | CLOUDFLARE_API_TOKEN, CLOUDFLARE_ACCOUNT_ID |
| `Azure.EnsureAuthenticated()` | AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID |
| `GitHub.EnsureAuthenticated()` | GITHUB_TOKEN |
| `Ando.Build(Directory("subdir"))` | Recursive sub-build detection |

#### ProfileDetector (IProfileDetector)
Detects available profiles from build scripts.

**Pattern Detection:**
- `DefineProfile("profile-name")` → Extract profile names
- Case-insensitive, sorted alphabetically

#### EmailService (IEmailService)
Pluggable email providers.

**Providers:**
1. **Resend** (default) - Transactional email via Resend .NET SDK (IResend)
2. **SMTP** - Direct SMTP connection

**Methods:**
```csharp
SendBuildFailedEmailAsync()  // Template-rendered failure notification
```

---

### 5. Background Jobs (Jobs/)

#### Hangfire Configuration

```csharp
- SQL Server storage backend
- Two queues: "builds" (priority), "default"
- Configurable worker count (default 2)
- Sliding invisibility timeout: 5 minutes
```

#### ExecuteBuildJob
Hangfire-scheduled job that executes builds.

```csharp
[Queue("builds")]
[AutomaticRetry(Attempts = 0)]
public async Task ExecuteAsync(int buildId, CancellationToken ct)
{
    await _buildOrchestrator.ExecuteBuildAsync(buildId);
}
```

#### CleanupArtifactsJob
Deletes expired build artifacts.

**Schedule:** Hourly

**Process:**
1. Batch process 100 artifacts at a time
2. Query: ExpiresAt <= UtcNow
3. Delete physical file from disk
4. Remove DB record
5. Log warnings for missing files

#### CleanupOldBuildsJob
Deletes old build records based on retention policy.

**Schedule:** Every 15 minutes

---

### 6. Build Orchestration (Orchestration/)

#### BuildOrchestrator (IBuildOrchestrator)

**ExecuteBuildAsync Flow:**

```
1. Entry Point
   ├─ Create scoped DbContext
   ├─ Load build + project + secrets + owner
   ├─ Detect/validate profiles
   ├─ Create cancellation token (timeout + manual)
   └─ Register with CancellationTokenRegistry

2. Repository Preparation
   ├─ Check if directory exists
   ├─ If exists: FetchAndCheckoutAsync (update + checkout)
   └─ If new: CloneRepositoryAsync (shallow clone)

3. Container Creation
   ├─ Ensure isolated Docker network ("ando-builds")
   ├─ docker run -d --rm --network ando-builds
   ├─ Mount repo volume: /workspace
   ├─ Mount Docker socket for nested builds
   ├─ Set env vars: All project secrets (decrypted)
   ├─ Set ANDO_HOST_ROOT for DinD path mapping
   └─ Return container ID

4. Build Execution
   ├─ docker exec: dotnet /ando/ando.dll run --dind --read-env [-p profile]
   ├─ Stream stdout/stderr to ServerBuildLogger
   └─ Return success/failure + step counts

5. Artifact Collection
   ├─ docker cp: {container}:/workspace/artifacts/ → host
   ├─ Scan for files recursively
   └─ Create BuildArtifact records with ExpiresAt

6. Finalization
   ├─ Update build status + timestamps
   ├─ Unregister cancellation token
   ├─ Stop container (docker rm -f)
   ├─ Post final status to GitHub
   └─ Send failure email (if configured)
```

**Error Handling:**
| Exception | Build Status |
|-----------|--------------|
| OperationCanceledException (timeout) | TimedOut |
| OperationCanceledException (manual) | Cancelled |
| Invalid profile | Failed (before execution) |
| Other exceptions | Failed + ErrorMessage |

#### ServerBuildLogger
Implements IBuildLogger for database and SignalR logging.

**For each log entry:**
1. Create BuildLogEntry + save to DB
2. Broadcast via SignalR to build group
3. Thread-safe via lock around DbContext

**Log Entry Sequence:**
- Sequential numbering within build
- Indexed by (BuildId, Sequence)

---

### 7. Real-time Communication (Hubs/)

#### BuildLogHub
SignalR hub for build log streaming.

**Methods:**
```csharp
JoinBuildLog(int buildId)   // Subscribe to build-{buildId} group
LeaveBuildLog(int buildId)  // Unsubscribe from group
```

**Broadcasting:**
```csharp
await _hubContext.Clients
    .Group($"build-{buildId}")
    .SendAsync("ReceiveLogEntry", logEntry);
```

---

### 8. Data Models (Models/)

#### Entity Relationships

```
ApplicationUser (1)
  ├─ (1:N) Projects
  └─ Encrypted: GitHubAccessToken

Project (N:1) ApplicationUser
  ├─ (1:N) Builds
  ├─ (1:N) ProjectSecrets
  └─ Indexes: GitHubRepoId, OwnerId

Build (N:1) Project
  ├─ (1:N) BuildLogEntries (cascade delete)
  ├─ (1:N) BuildArtifacts (cascade delete)
  └─ Indexes: (ProjectId, QueuedAt), Status

BuildLogEntry (N:1) Build
  └─ Index: (BuildId, Sequence)

BuildArtifact (N:1) Build
  └─ Index: ExpiresAt, BuildId

ProjectSecret (N:1) Project
  └─ Unique: (ProjectId, Name)
```

#### ApplicationUser
Extended Identity user.

```csharp
public class ApplicationUser : IdentityUser<int>
{
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool EmailVerified { get; set; }
    public string? EmailVerificationToken { get; set; }
    public long? GitHubId { get; set; }
    public string? GitHubLogin { get; set; }
    public string? GitHubAccessToken { get; set; }  // Encrypted
    public DateTime? GitHubConnectedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Navigation
    public ICollection<Project> Projects { get; set; }

    // Helpers
    public string EffectiveDisplayName { get; }
    public bool HasGitHubConnection { get; }
}
```

#### Project
Project entity with GitHub info and build settings.

```csharp
public class Project
{
    // Identity
    public int Id { get; set; }
    public int OwnerId { get; set; }

    // GitHub Info
    public long GitHubRepoId { get; set; }
    public string RepoFullName { get; set; }
    public string RepoUrl { get; set; }
    public string DefaultBranch { get; set; }
    public long InstallationId { get; set; }

    // Build Settings
    public string BranchFilter { get; set; } = "main,master";
    public bool EnablePrBuilds { get; set; }
    public int TimeoutMinutes { get; set; } = 15;
    public string? DockerImage { get; set; }
    public string? Profile { get; set; }
    public string? AvailableProfiles { get; set; }  // CSV

    // Auto-Detected
    public string? RequiredSecrets { get; set; }  // CSV

    // Notifications
    public bool NotifyOnFailure { get; set; }
    public string? NotificationEmail { get; set; }

    // Navigation
    public ApplicationUser Owner { get; set; }
    public ICollection<Build> Builds { get; set; }
    public ICollection<ProjectSecret> Secrets { get; set; }

    // Helpers
    public bool MatchesBranchFilter(string branch) { }
    public List<string> GetMissingSecrets(IEnumerable<string> configured) { }
    public bool IsConfigured { get; }
}
```

#### Build
Build execution record.

```csharp
public class Build
{
    public int Id { get; set; }
    public int ProjectId { get; set; }

    // Git Info
    public string CommitSha { get; set; }
    public string Branch { get; set; }
    public string? CommitMessage { get; set; }
    public string? CommitAuthor { get; set; }
    public int? PullRequestNumber { get; set; }

    // Build State
    public BuildStatus Status { get; set; }
    public BuildTrigger Trigger { get; set; }
    public DateTime QueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    // Results
    public int StepsTotal { get; set; }
    public int StepsCompleted { get; set; }
    public int StepsFailed { get; set; }
    public string? ErrorMessage { get; set; }
    public string? HangfireJobId { get; set; }

    // Navigation
    public Project Project { get; set; }
    public ICollection<BuildLogEntry> LogEntries { get; set; }
    public ICollection<BuildArtifact> Artifacts { get; set; }

    // Helpers
    public string ShortCommitSha { get; }
    public TimeSpan? Duration { get; }
    public bool IsFinished { get; }
    public bool CanCancel { get; }
    public bool CanRetry { get; }
}

public enum BuildStatus
{
    Queued, Running, Success, Failed, Cancelled, TimedOut
}

public enum BuildTrigger
{
    Push, PullRequest, Manual
}
```

#### BuildLogEntry
Log entry for build execution.

```csharp
public class BuildLogEntry
{
    public long Id { get; set; }
    public int BuildId { get; set; }
    public int Sequence { get; set; }
    public LogEntryType Type { get; set; }
    public string Message { get; set; }
    public string? StepName { get; set; }
    public DateTime Timestamp { get; set; }
}

public enum LogEntryType
{
    StepStarted, StepCompleted, StepFailed,
    Info, Warning, Error, Debug, Output
}
```

#### BuildArtifact
Artifact metadata.

```csharp
public class BuildArtifact
{
    public int Id { get; set; }
    public int BuildId { get; set; }
    public string Name { get; set; }
    public string StoragePath { get; set; }  // {projectId}/{buildId}/{filename}
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }

    // Helpers
    public bool IsExpired { get; }
    public string FormattedSize { get; }  // Human-readable
}
```

#### ProjectSecret
Encrypted secret storage.

```csharp
public class ProjectSecret
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public string Name { get; set; }
    public string EncryptedValue { get; set; }  // AES-256
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

---

### 9. Docker Integration

#### Dockerfile Architecture

**Multi-stage Build (3 stages):**

**Stage 1: Frontend (Node)**
```dockerfile
FROM node:20-alpine AS frontend
# Build React ClientApp
# Output: /wwwroot/app
```

**Stage 2: Backend (.NET)**
```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
# Restore + build Ando.csproj + Ando.Server.csproj
# Copy frontend artifacts
# Publish to /app
```

**Stage 3: Runtime**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
# Install Docker CLI (build capability)
# Install git (repository cloning)
# Create data directories
# Expose port 8080
# Health check: curl http://localhost:8080/health
```

#### Volume Requirements

| Path | Purpose | Persistence |
|------|---------|-------------|
| `/data/artifacts` | Build artifacts | Must persist |
| `/data/repos` | Cloned repositories | Can be ephemeral |
| `/data/keys` | Data Protection keys | Must persist |
| `/var/run/docker.sock` | Host Docker socket | Host mount |

#### Build Container Configuration

**Network:**
- Name: "ando-builds"
- Driver: bridge
- Provides internet access + isolation

**Docker-in-Docker Support:**
- Host Docker socket mounted: `/var/run/docker.sock`
- ANDO_HOST_ROOT env var for nested volume mounts

---

### 10. Authentication & Authorization

#### Authentication Methods

**ASP.NET Core Identity:**
- Email/password registration + login
- Password requirements: 8+ chars, digit, lowercase, uppercase
- Email verification (soft: users can log in unverified)

**Cookie Authentication:**
- 30-day expiration
- Sliding renewal on activity
- HttpOnly + Secure flags
- SameSite protection

#### Authorization

**Role-Based Access Control:**
| Role | Access |
|------|--------|
| Admin | Full system control, user management, impersonation |
| User | Own projects only |

**First User as Admin:**
- On registration, if no admins exist, user becomes admin

---

### 11. Configuration Management

#### Settings Classes

**GitHubSettings:**
```csharp
public class GitHubSettings
{
    public const string SectionName = "GitHub";
    public int AppId { get; set; }
    public string AppName { get; set; }
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string WebhookSecret { get; set; }
    public string PrivateKeyPath { get; set; }
}
```

**BuildSettings:**
```csharp
public class BuildSettings
{
    public const string SectionName = "Build";
    public int DefaultTimeoutMinutes { get; set; } = 15;
    public int MaxTimeoutMinutes { get; set; } = 60;
    public string DefaultDockerImage { get; set; }
    public int WorkerCount { get; set; } = 2;
    public string ReposPath { get; set; }
    public string ReposPathInContainer { get; set; }
    public string BaseUrl { get; set; }
    public string DockerSocketPath { get; set; }
}
```

**StorageSettings:**
```csharp
public class StorageSettings
{
    public const string SectionName = "Storage";
    public string ArtifactsPath { get; set; }
    public int ArtifactRetentionDays { get; set; } = 30;
    public int BuildLogRetentionDays { get; set; } = 90;
}
```

**EncryptionSettings:**
```csharp
public class EncryptionSettings
{
    public const string SectionName = "Encryption";
    public string Key { get; set; }  // Base64-encoded 32-byte AES key
}
```

**EmailSettings:**
```csharp
public class EmailSettings
{
    public const string SectionName = "Email";
    public string Provider { get; set; } = "Resend";
    public string FromAddress { get; set; }
    public string FromName { get; set; }
    // Provider-specific nested settings
}
```

#### Configuration Sources

**Priority (highest to lowest):**
1. Environment variables
2. appsettings.{Environment}.json
3. appsettings.json
4. Default values in setting class

---

### 12. Frontend (ClientApp/)

#### React SPA Structure

```
ClientApp/
├── src/
│   ├── components/     # Reusable UI components
│   ├── pages/          # Route pages
│   ├── hooks/          # Custom React hooks
│   ├── context/        # React Context (state management)
│   ├── services/       # API client services
│   ├── types/          # TypeScript interfaces
│   └── App.tsx         # Root component
├── package.json
└── vite.config.ts
```

**Build Configuration:**
- Vite-based build
- `npm run build` → Outputs to `../wwwroot/app/`
- TypeScript support
- ESLint for code quality

**API Integration:**
- `/api/` endpoints
- Cookie authentication
- Error handling + toast notifications

---

## Data Flow: Webhook to Build Completion

```
1. GitHub Push Event
   │
   ▼
2. POST /webhooks/github
   ├─ Validate signature (HMAC-SHA256)
   ├─ Parse payload (PushEventPayload)
   ├─ Find matching projects (GitHubRepoId)
   ├─ For each project:
   │  ├─ Check branch filter
   │  ├─ Auto-detect required secrets
   │  ├─ Validate all secrets configured
   │  └─ Queue build (BuildService)
   └─ Return 200 OK with build IDs
   │
   ▼
3. Hangfire Worker picks up ExecuteBuildJob
   │
   ▼
4. BuildOrchestrator.ExecuteBuildAsync()
   ├─ Prepare repository (clone/fetch)
   ├─ Create build container
   ├─ Execute: ando run --dind --read-env
   ├─ Stream logs → ServerBuildLogger
   │  ├─ Save to database
   │  └─ Broadcast via SignalR
   ├─ Collect artifacts
   └─ Finalize (status, email, GitHub)
   │
   ▼
5. Real-time UI Updates (SignalR)
   ├─ Clients subscribe to build group
   └─ Receive logs as streamed
   │
   ▼
6. Build Complete
   ├─ Status updated (Success/Failed)
   ├─ GitHub commit status posted
   └─ Email notification (on failure)
```

---

## Error Handling Strategy

### Build Execution Errors

| Error | Build Status | Action |
|-------|--------------|--------|
| Timeout | TimedOut | Log, stop container |
| Manual cancel | Cancelled | Log, stop container |
| Invalid profile | Failed | Immediate failure |
| Repository error | Failed | "Failed to prepare repository" |
| Other exception | Failed | Store ErrorMessage |

### API Errors

| Code | Meaning |
|------|---------|
| 400 | Validation failures |
| 401 | Missing authentication |
| 403 | Insufficient authorization |
| 404 | Project not owned by user |
| 500 | Unexpected exceptions |

### Webhook Errors
- Always return 200 OK (prevent GitHub retries)
- Log errors internally
- Skip builds with missing secrets (warn in logs)

---

## Key Design Patterns

### 1. Service-Oriented Architecture
- Scoped services for per-request operations
- Singleton services for application-wide concerns
- Dependency injection throughout

### 2. Repository Pattern
- Entity Framework Core DbContext as repository
- Scoped DbContext per request to avoid tracking issues

### 3. Job Queue Pattern
- Hangfire for asynchronous job scheduling
- Separate queues for priority management
- Worker pool with configurable concurrency

### 4. Hub-based Real-time Pattern
- SignalR for group-based log streaming
- Clients subscribe to build-specific groups

### 5. Options Pattern
- Configuration binding to strongly-typed classes
- Each setting class has SectionName constant

### 6. Scoped DbContext Per Build
- BuildOrchestrator creates scoped context
- Avoids tracking issues in long-running operations

### 7. Write-Only Secrets
- Secrets encrypted at rest
- Values never returned to UI
- Only decrypted when passing to container

---

## Security Considerations

### Webhook Security
- HMAC-SHA256 signature validation
- Constant-time comparison prevents timing attacks
- Webhook secret stored securely

### Secret Storage
- AES-256 encryption at application layer
- Random IV per encryption (semantic security)
- Keys stored securely (environment/config)

### Container Isolation
- Builds run in isolated Docker network
- No privileged mode (socket mount instead)
- Project files copied, not mounted
- Secrets passed as environment variables

### Authentication
- Password hashing via ASP.NET Identity
- Secure cookie settings (HttpOnly, Secure, SameSite)
- Session-based impersonation for support

---

## Deployment Architecture

### Hetzner VPS Deployment

```
┌─────────────────────────────────────────────────────────────┐
│                       Hetzner VPS                           │
│  ┌─────────────────────────────────────────────────────┐   │
│  │                    Caddy (Reverse Proxy)              │   │
│  │                    Port 80/443 → 8080                 │   │
│  └─────────────────────────────────────────────────────┘   │
│                             │                               │
│  ┌──────────────────────────▼──────────────────────────┐   │
│  │              Docker (Rootless - ando user)           │   │
│  │  ┌─────────────────┐  ┌───────────────────────────┐ │   │
│  │  │  ando-server    │  │    ando-sqlserver         │ │   │
│  │  │  Port 8080      │  │    Port 1433 (internal)   │ │   │
│  │  └─────────────────┘  └───────────────────────────┘ │   │
│  │                                                      │   │
│  │  Volumes:                                            │   │
│  │  - /opt/ando/data/artifacts                          │   │
│  │  - /opt/ando/data/repos                              │   │
│  │  - /opt/ando/data/keys                               │   │
│  │  - /opt/ando/data/sqldata                            │   │
│  └──────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

**Key Points:**
- Rootless Docker under `ando` user (UID 1000)
- Docker socket at `/run/user/1000/docker.sock`
- SQL Server in container (no public ports)
- Web server proxied by Caddy

---

## Summary

Ando.Server provides a complete CI/CD platform with:

1. **GitHub Integration** - Webhooks, commit status, installation tokens
2. **Real-time Logging** - SignalR streaming to web clients
3. **Containerized Builds** - Docker isolation for reproducibility
4. **Secret Management** - AES-256 encrypted storage
5. **Multi-user Support** - Role-based access control
6. **Background Processing** - Hangfire job queue
7. **Modern Frontend** - React SPA with REST API

The architecture emphasizes security (encrypted secrets, webhook validation), reliability (job queue, error handling), and real-time feedback (SignalR, GitHub status updates).
