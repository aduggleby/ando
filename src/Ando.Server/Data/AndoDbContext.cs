// =============================================================================
// AndoDbContext.cs
//
// Summary: Entity Framework Core database context for Ando.Server.
//
// This context manages all database operations for the CI/CD server including
// users, projects, builds, and related entities. It configures relationships,
// indexes, and constraints via Fluent API.
//
// Design Decisions:
// - Using SQL Server for production (Hangfire also uses SQL Server)
// - Indexes on frequently queried columns (GitHubId, RepoFullName, Status)
// - Cascade delete from Project to Builds/Secrets for cleanup
// - BuildLogEntry uses long Id to support high volume
// =============================================================================

using Ando.Server.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Data;

/// <summary>
/// Entity Framework Core database context for Ando.Server.
/// Inherits from IdentityDbContext to provide ASP.NET Core Identity tables.
/// </summary>
public class AndoDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, int>
{
    public AndoDbContext(DbContextOptions<AndoDbContext> options) : base(options)
    {
    }

    // Note: Users DbSet is provided by IdentityDbContext as base.Users
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectSecret> ProjectSecrets => Set<ProjectSecret>();
    public DbSet<Build> Builds => Set<Build>();
    public DbSet<BuildLogEntry> BuildLogEntries => Set<BuildLogEntry>();
    public DbSet<BuildArtifact> BuildArtifacts => Set<BuildArtifact>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureUser(modelBuilder);
        ConfigureApiToken(modelBuilder);
        ConfigureProject(modelBuilder);
        ConfigureProjectSecret(modelBuilder);
        ConfigureBuild(modelBuilder);
        ConfigureBuildLogEntry(modelBuilder);
        ConfigureBuildArtifact(modelBuilder);
        ConfigureSystemSettings(modelBuilder);
    }

    // -------------------------------------------------------------------------
    // ApplicationUser Configuration (extends Identity user)
    // -------------------------------------------------------------------------
    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            // Display name and avatar
            entity.Property(e => e.DisplayName)
                .HasMaxLength(100);

            entity.Property(e => e.AvatarUrl)
                .HasMaxLength(500);

            // Soft email verification
            entity.Property(e => e.EmailVerificationToken)
                .HasMaxLength(200);

            // Optional GitHub connection (for repository access)
            entity.Property(e => e.GitHubLogin)
                .HasMaxLength(100);

            entity.Property(e => e.GitHubAccessToken)
                .HasMaxLength(500);

            // Index on GitHub ID for lookups (when connected)
            entity.HasIndex(e => e.GitHubId)
                .IsUnique()
                .HasFilter("[GitHubId] IS NOT NULL");

            // Relationship to projects
            entity.HasMany(e => e.Projects)
                .WithOne(p => p.Owner)
                .HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // -------------------------------------------------------------------------
    // ApiToken Configuration
    // -------------------------------------------------------------------------
    private static void ConfigureApiToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApiToken>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Prefix)
                .IsRequired()
                .HasMaxLength(20);

            entity.Property(e => e.TokenHash)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasIndex(e => new { e.UserId, e.Prefix });
            entity.HasIndex(e => e.RevokedAt);

            entity.HasOne(e => e.User)
                .WithMany(u => u.ApiTokens)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // -------------------------------------------------------------------------
    // Project Configuration
    // -------------------------------------------------------------------------
    private static void ConfigureProject(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.GitHubRepoId)
                .IsRequired();

            entity.Property(e => e.RepoFullName)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.RepoUrl)
                .IsRequired()
                .HasMaxLength(500);

            entity.Property(e => e.DefaultBranch)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValue("main");

            entity.Property(e => e.BranchFilter)
                .IsRequired()
                .HasMaxLength(500)
                .HasDefaultValue("main,master");

            entity.Property(e => e.TimeoutMinutes)
                .HasDefaultValue(15);

            entity.Property(e => e.DockerImage)
                .HasMaxLength(500);

            entity.Property(e => e.RequiredSecrets)
                .HasMaxLength(1000);

            entity.Property(e => e.NotificationEmail)
                .HasMaxLength(255);

            // Index for finding projects by repo
            entity.HasIndex(e => e.GitHubRepoId);

            // Index for finding projects by owner
            entity.HasIndex(e => e.OwnerId);

            entity.HasMany(e => e.Builds)
                .WithOne(b => b.Project)
                .HasForeignKey(b => b.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Secrets)
                .WithOne(s => s.Project)
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // -------------------------------------------------------------------------
    // ProjectSecret Configuration
    // -------------------------------------------------------------------------
    private static void ConfigureProjectSecret(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProjectSecret>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.EncryptedValue)
                .IsRequired()
                .HasMaxLength(2000);

            // Unique constraint: one secret per name per project
            entity.HasIndex(e => new { e.ProjectId, e.Name })
                .IsUnique();
        });
    }

    // -------------------------------------------------------------------------
    // Build Configuration
    // -------------------------------------------------------------------------
    private static void ConfigureBuild(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Build>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.CommitSha)
                .IsRequired()
                .HasMaxLength(40);

            entity.Property(e => e.Branch)
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.CommitMessage)
                .HasMaxLength(500);

            entity.Property(e => e.CommitAuthor)
                .HasMaxLength(200);

            entity.Property(e => e.Status)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Trigger)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.ErrorMessage)
                .HasMaxLength(2000);

            entity.Property(e => e.HangfireJobId)
                .HasMaxLength(100);

            // Index for listing builds by project (most recent first)
            entity.HasIndex(e => new { e.ProjectId, e.QueuedAt })
                .IsDescending(false, true);

            // Index for finding running builds
            entity.HasIndex(e => e.Status);

            entity.HasMany(e => e.LogEntries)
                .WithOne(l => l.Build)
                .HasForeignKey(l => l.BuildId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Artifacts)
                .WithOne(a => a.Build)
                .HasForeignKey(a => a.BuildId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    // -------------------------------------------------------------------------
    // BuildLogEntry Configuration
    // -------------------------------------------------------------------------
    private static void ConfigureBuildLogEntry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BuildLogEntry>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Type)
                .IsRequired()
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(e => e.Message)
                .IsRequired()
                .HasMaxLength(4000);

            entity.Property(e => e.StepName)
                .HasMaxLength(200);

            // Index for retrieving logs in order
            entity.HasIndex(e => new { e.BuildId, e.Sequence });
        });
    }

    // -------------------------------------------------------------------------
    // BuildArtifact Configuration
    // -------------------------------------------------------------------------
    private static void ConfigureBuildArtifact(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BuildArtifact>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Name)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.StoragePath)
                .IsRequired()
                .HasMaxLength(500);

            // Index for cleanup job to find expired artifacts
            entity.HasIndex(e => e.ExpiresAt);

            // Index for listing artifacts by build
            entity.HasIndex(e => e.BuildId);
        });
    }

    // -------------------------------------------------------------------------
    // SystemSettings Configuration
    // -------------------------------------------------------------------------
    private static void ConfigureSystemSettings(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SystemSettings>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .ValueGeneratedNever();

            entity.Property(e => e.AllowUserRegistration)
                .HasDefaultValue(true);
        });
    }
}
