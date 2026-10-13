IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [AspNetRoles] (
    [Id] int NOT NULL IDENTITY,
    [Description] nvarchar(max) NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] int NOT NULL IDENTITY,
    [DisplayName] nvarchar(100) NULL,
    [AvatarUrl] nvarchar(500) NULL,
    [EmailVerified] bit NOT NULL,
    [EmailVerificationSentAt] datetime2 NULL,
    [EmailVerificationToken] nvarchar(200) NULL,
    [GitHubId] bigint NULL,
    [GitHubLogin] nvarchar(100) NULL,
    [GitHubAccessToken] nvarchar(500) NULL,
    [GitHubConnectedAt] datetime2 NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastLoginAt] datetime2 NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] int NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] int NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] int NOT NULL,
    [RoleId] int NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] int NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Projects] (
    [Id] int NOT NULL IDENTITY,
    [OwnerId] int NOT NULL,
    [GitHubRepoId] bigint NOT NULL,
    [RepoFullName] nvarchar(200) NOT NULL,
    [RepoUrl] nvarchar(500) NOT NULL,
    [DefaultBranch] nvarchar(100) NOT NULL DEFAULT N'main',
    [InstallationId] bigint NULL,
    [BranchFilter] nvarchar(500) NOT NULL DEFAULT N'main,master',
    [EnablePrBuilds] bit NOT NULL,
    [TimeoutMinutes] int NOT NULL DEFAULT 15,
    [DockerImage] nvarchar(500) NULL,
    [RequiredSecrets] nvarchar(1000) NULL,
    [Profile] nvarchar(max) NULL,
    [AvailableProfiles] nvarchar(max) NULL,
    [NotifyOnFailure] bit NOT NULL,
    [NotificationEmail] nvarchar(255) NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastBuildAt] datetime2 NULL,
    CONSTRAINT [PK_Projects] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Projects_AspNetUsers_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Builds] (
    [Id] int NOT NULL IDENTITY,
    [ProjectId] int NOT NULL,
    [CommitSha] nvarchar(40) NOT NULL,
    [Branch] nvarchar(200) NOT NULL,
    [CommitMessage] nvarchar(500) NULL,
    [CommitAuthor] nvarchar(200) NULL,
    [PullRequestNumber] int NULL,
    [Status] nvarchar(20) NOT NULL,
    [Trigger] nvarchar(20) NOT NULL,
    [QueuedAt] datetime2 NOT NULL,
    [StartedAt] datetime2 NULL,
    [FinishedAt] datetime2 NULL,
    [Duration] time NULL,
    [StepsTotal] int NOT NULL,
    [StepsCompleted] int NOT NULL,
    [StepsFailed] int NOT NULL,
    [ErrorMessage] nvarchar(2000) NULL,
    [HangfireJobId] nvarchar(100) NULL,
    CONSTRAINT [PK_Builds] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Builds_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [ProjectSecrets] (
    [Id] int NOT NULL IDENTITY,
    [ProjectId] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [EncryptedValue] nvarchar(2000) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [UpdatedAt] datetime2 NULL,
    CONSTRAINT [PK_ProjectSecrets] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProjectSecrets_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [BuildArtifacts] (
    [Id] int NOT NULL IDENTITY,
    [BuildId] int NOT NULL,
    [Name] nvarchar(255) NOT NULL,
    [StoragePath] nvarchar(500) NOT NULL,
    [SizeBytes] bigint NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [ExpiresAt] datetime2 NOT NULL,
    CONSTRAINT [PK_BuildArtifacts] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BuildArtifacts_Builds_BuildId] FOREIGN KEY ([BuildId]) REFERENCES [Builds] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [BuildLogEntries] (
    [Id] bigint NOT NULL IDENTITY,
    [BuildId] int NOT NULL,
    [Sequence] int NOT NULL,
    [Type] nvarchar(20) NOT NULL,
    [Message] nvarchar(4000) NOT NULL,
    [StepName] nvarchar(200) NULL,
    [Timestamp] datetime2 NOT NULL,
    CONSTRAINT [PK_BuildLogEntries] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_BuildLogEntries_Builds_BuildId] FOREIGN KEY ([BuildId]) REFERENCES [Builds] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [IX_AspNetUsers_GitHubId] ON [AspNetUsers] ([GitHubId]) WHERE [GitHubId] IS NOT NULL;

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

CREATE INDEX [IX_BuildArtifacts_BuildId] ON [BuildArtifacts] ([BuildId]);

CREATE INDEX [IX_BuildArtifacts_ExpiresAt] ON [BuildArtifacts] ([ExpiresAt]);

CREATE INDEX [IX_BuildLogEntries_BuildId_Sequence] ON [BuildLogEntries] ([BuildId], [Sequence]);

CREATE INDEX [IX_Builds_ProjectId_QueuedAt] ON [Builds] ([ProjectId], [QueuedAt] DESC);

CREATE INDEX [IX_Builds_Status] ON [Builds] ([Status]);

CREATE INDEX [IX_Projects_GitHubRepoId] ON [Projects] ([GitHubRepoId]);

CREATE INDEX [IX_Projects_OwnerId] ON [Projects] ([OwnerId]);

CREATE UNIQUE INDEX [IX_ProjectSecrets_ProjectId_Name] ON [ProjectSecrets] ([ProjectId], [Name]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260130170929_InitialCreate', N'9.0.20');

CREATE TABLE [ApiTokens] (
    [Id] int NOT NULL IDENTITY,
    [UserId] int NOT NULL,
    [Name] nvarchar(100) NOT NULL,
    [Prefix] nvarchar(20) NOT NULL,
    [TokenHash] nvarchar(200) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    [LastUsedAt] datetime2 NULL,
    [RevokedAt] datetime2 NULL,
    CONSTRAINT [PK_ApiTokens] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ApiTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_ApiTokens_RevokedAt] ON [ApiTokens] ([RevokedAt]);

CREATE INDEX [IX_ApiTokens_UserId_Prefix] ON [ApiTokens] ([UserId], [Prefix]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260207125546_AddApiTokens', N'9.0.20');

ALTER TABLE [Builds] ADD [Profile] nvarchar(max) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260207144359_AddBuildProfile', N'9.0.20');

COMMIT;
GO

