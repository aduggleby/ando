# Ando.Server Testing Guide

This document describes the testing strategy, current coverage, and guidelines for testing the Ando.Server CI/CD application.

## Test Structure

```
tests/
├── Ando.Server.Tests/           # Unit & integration tests (xUnit)
│   ├── Unit/                    # Unit tests
│   │   ├── WebhookSignatureValidatorTests.cs
│   │   ├── WebhooksControllerTests.cs
│   │   ├── BuildOrchestratorTests.cs
│   │   ├── AuthorizationTests.cs
│   │   ├── ErrorHandlingTests.cs
│   │   ├── Services/
│   │   │   ├── BuildServiceTests.cs
│   │   │   ├── EncryptionServiceTests.cs
│   │   │   └── ProjectServiceTests.cs
│   │   ├── Models/
│   │   │   ├── ProjectTests.cs
│   │   │   ├── BuildTests.cs
│   │   │   └── BuildArtifactTests.cs
│   │   ├── Controllers/
│   │   │   ├── ProjectsControllerValidationTests.cs
│   │   │   └── BuildsControllerValidationTests.cs
│   │   └── Jobs/
│   │       ├── CleanupArtifactsJobTests.cs
│   │       └── CleanupOldBuildsJobTests.cs
│   ├── Integration/             # Integration tests
│   │   ├── AndoWebApplicationFactory.cs  # Test server factory
│   │   └── WebhookIntegrationTests.cs    # Full HTTP pipeline tests
│   └── TestFixtures/
│       ├── MockServices.cs      # Mock implementations
│       └── TestDbContextFactory.cs
│
└── Ando.Server.E2E/             # End-to-end tests (Playwright)
    ├── tests/                   # Test specs
    ├── pages/                   # Page object models
    ├── fixtures/                # Test fixtures
    └── utils/                   # Helpers
```

## Running Tests

### Unit Tests

```bash
# Run all unit tests
dotnet test tests/Ando.Server.Tests/

# Run with verbose output
dotnet test tests/Ando.Server.Tests/ --verbosity normal

# Run specific test class
dotnet test tests/Ando.Server.Tests/ --filter "FullyQualifiedName~WebhooksControllerTests"

# Run with coverage
dotnet test tests/Ando.Server.Tests/ /p:CollectCoverage=true
```

### Integration Tests

```bash
# Run integration tests only
dotnet test tests/Ando.Server.Tests/ --filter "Category=Integration"

# Run with verbose output
dotnet test tests/Ando.Server.Tests/ --filter "Category=Integration" --verbosity normal
```

### E2E Tests

```bash
cd tests/Ando.Server.E2E

# Install dependencies
npm install
npx playwright install

# Run tests (requires server running in Testing environment)
ASPNETCORE_ENVIRONMENT=Testing dotnet run --project ../../src/Ando.Server &
npm test

# Run specific test file
npx playwright test tests/auth.spec.ts

# Run with UI
npx playwright test --ui
```

## Current Test Coverage

### Unit Tests (288 tests) + Integration Tests (19 tests) = 307 total

#### WebhookSignatureValidatorTests (16 tests)
| Test | Type | Description |
|------|------|-------------|
| `Validate_WithValidSignature_ReturnsTrue` | Happy path | Valid HMAC-SHA256 signature |
| `Validate_WithInvalidSignature_ReturnsFalse` | Error | Wrong signature |
| `Validate_WithWrongSecret_ReturnsFalse` | Error | Secret mismatch |
| `Validate_WithTamperedPayload_ReturnsFalse` | Security | Payload modification detected |
| `Validate_WithNullPayload_ReturnsFalse` | Edge case | Null input handling |
| `Validate_WithNullSignature_ReturnsFalse` | Edge case | Null input handling |
| `Validate_WithEmptySignature_ReturnsFalse` | Edge case | Empty string handling |
| `Validate_WithMalformedSignature_ReturnsFalse` | Error | Invalid format |
| `Validate_WithMissingSha256Prefix_ReturnsFalse` | Error | Missing algorithm prefix |
| `Validate_WithEmptyPayload_ReturnsFalse` | Edge case | Empty payload |
| `Validate_WithUnicodePayload_WorksCorrectly` | Edge case | Unicode content |
| `Validate_WithLargePayload_WorksCorrectly` | Edge case | Large payload (100KB) |
| `Constructor_WithNullSecret_ThrowsArgumentException` | Error | Invalid construction |
| `Constructor_WithEmptySecret_ThrowsArgumentException` | Error | Invalid construction |
| `Validate_IsCaseInsensitiveForPrefix` | Compatibility | SHA256 vs sha256 |

#### WebhooksControllerTests (14 tests)
| Test | Type | Description |
|------|------|-------------|
| `Push_WithValidSignature_QueuesBuild` | Happy path | Standard push event |
| `Push_InitiatesRepoDownloadViaBuildQueue` | Happy path | Commit SHA passed for clone |
| `Push_WithBranchNotInFilter_SkipsBuild` | Filter | Branch filter rejects |
| `Push_WithBranchInFilter_QueuesBuild` | Filter | Branch filter accepts |
| `Push_WithExactBranchMatch_QueuesBuild` | Filter | Exact branch matching |
| `Push_WithInvalidSignature_ReturnsUnauthorized` | Security | Signature validation |
| `Push_WithUnknownRepo_ReturnsOkButSkipsBuild` | Error | Unknown repository |
| `Push_IncludesCommitMessageAndAuthor` | Happy path | Metadata passed |
| `PullRequest_WithPrBuildsEnabled_QueuesBuild` | Happy path | PR build |
| `PullRequest_WithPrBuildsDisabled_SkipsBuild` | Config | PR builds off |
| `PullRequest_Synchronize_QueuesBuild` | Happy path | PR update |
| `PullRequest_Reopened_QueuesBuild` | Happy path | PR reopen |
| `PullRequest_Closed_SkipsBuild` | Filter | Closed PR ignored |
| `Ping_ReturnsOk` | Happy path | GitHub ping event |
| `UnknownEvent_ReturnsOk` | Edge case | Unknown event type |

#### CancellationTokenRegistryTests (8 tests)
| Test | Type | Description |
|------|------|-------------|
| `Register_AddsBuildToRegistry` | Happy path | Registration |
| `Unregister_RemovesBuildFromRegistry` | Happy path | Unregistration |
| `IsRunning_ReturnsFalseForUnregisteredBuild` | Edge case | Unknown build |
| `TryCancel_CancelsBuildAndReturnsTrue` | Happy path | Cancellation |
| `TryCancel_ReturnsFalseForUnregisteredBuild` | Edge case | Cancel unknown |
| `Register_OverwritesPreviousRegistration` | Edge case | Re-registration |
| `MultipleBuilds_TrackedIndependently` | Happy path | Multiple builds |
| `Registry_IsThreadSafe` | Concurrency | Thread safety |

#### Mock Service Tests (8 tests)
Verify mock implementations work correctly for test setup.

#### BuildServiceTests (22 tests)
| Test | Type | Description |
|------|------|-------------|
| `QueueBuildAsync_CreatesBuildWithQueuedStatus` | Happy path | Build created with Queued status |
| `QueueBuildAsync_SetsCommitDetails` | Happy path | Commit SHA, branch, message, author set |
| `QueueBuildAsync_SetsPullRequestNumber` | Happy path | PR number set for PR triggers |
| `QueueBuildAsync_SetsQueuedAt` | Happy path | Timestamp set correctly |
| `QueueBuildAsync_EnqueuesHangfireJob` | Happy path | Hangfire job created |
| `CancelBuildAsync_WithRegisteredBuild_ReturnsTrue` | Happy path | Registered running build cancelled |
| `CancelBuildAsync_CallsCancellationRegistry` | Happy path | Cancellation token triggered |
| `CancelBuildAsync_WithQueuedBuild_CancelsAndUpdatesStatus` | Happy path | Queued build cancelled, status updated |
| `CancelBuildAsync_WithQueuedBuildNoJobId_ReturnsFalse` | Edge case | Queued build without job ID can't cancel |
| `CancelBuildAsync_WithUnregisteredRunningBuild_ReturnsFalse` | Edge case | Unregistered builds can't be cancelled |
| `CancelBuildAsync_WithCompletedBuild_ReturnsFalse` | Edge case | Completed builds can't be cancelled |
| `CancelBuildAsync_WithNonExistentBuild_ReturnsFalse` | Edge case | Unknown build ID |
| `RetryBuildAsync_CreatesNewBuildWithSameSettings` | Happy path | Build settings copied |
| `RetryBuildAsync_SetsTriggerToManual` | Happy path | Retry uses Manual trigger |
| `GetBuildAsync_ReturnsBuildWithProject` | Happy path | Build with project navigation |
| `GetBuildAsync_WithNonExistentId_ReturnsNull` | Edge case | Unknown build ID |
| `GetBuildsForProjectAsync_ReturnsBuildsInDescendingOrder` | Happy path | Most recent first |
| `GetBuildsForProjectAsync_RespectsPagination` | Happy path | Skip/take works |
| `GetRecentBuildsForUserAsync_ReturnsOnlyUserBuilds` | Security | User isolation |
| `UpdateBuildStatusAsync_UpdatesStatus` | Happy path | Status update |
| `UpdateBuildStatusAsync_SetsErrorMessage` | Happy path | Error message set |
| `UpdateBuildStatusAsync_UpdatesStepCounts` | Happy path | Step counts updated |

#### EncryptionServiceTests (9 tests)
| Test | Type | Description |
|------|------|-------------|
| `Encrypt_ReturnsNonEmptyString` | Happy path | Encryption produces output |
| `Decrypt_ReversesEncryption` | Happy path | Round-trip encryption |
| `Encrypt_ProducesDifferentOutputForSameInput` | Security | Random IV prevents patterns |
| `Decrypt_WithWrongKey_Throws` | Security | Wrong key fails |
| `Encrypt_WithEmptyString_Works` | Edge case | Empty input |
| `Encrypt_WithUnicode_Works` | Edge case | Unicode content |
| `Encrypt_WithLongString_Works` | Edge case | Large content |
| `Decrypt_WithCorruptedCiphertext_Throws` | Error | Tampered data fails |
| `Decrypt_WithInvalidBase64_Throws` | Error | Invalid format fails |

#### AuthorizationTests (8 tests)
| Test | Type | Description |
|------|------|-------------|
| `GetProject_ByOwner_ReturnsProject` | Happy path | Owner can access project |
| `GetProject_ByOtherUser_ReturnsNull` | Security | Other user can't access |
| `GetProjects_ForUser_ReturnsOnlyOwnedProjects` | Security | User sees only own projects |
| `DeleteProject_AfterAuthorizationCheck_Succeeds` | Happy path | Owner can delete |
| `DeleteProject_WithAuthorizationCheckFirst_BlocksOtherUser` | Security | Other user blocked |
| `GetRecentBuilds_ForUser_ReturnsOnlyOwnedBuilds` | Security | User sees only own builds |
| `GetBuildsForProject_ReturnsOnlyProjectBuilds` | Happy path | Builds scoped to project |
| `UserCannotAccessOtherUserSecrets` | Security | Secrets isolated |
| `UserCannotSeeBuildLogsFromOtherUser` | Security | Logs isolated |

#### ProjectServiceTests (34 tests)
| Test | Type | Description |
|------|------|-------------|
| `GetProjectsForUserAsync_ReturnsOnlyUserProjects` | Security | User isolation |
| `GetProjectsForUserAsync_OrdersByLastBuildDescending` | Happy path | Ordering |
| `GetProjectsForUserAsync_WithNoProjects_ReturnsEmptyList` | Edge case | Empty result |
| `GetProjectAsync_ReturnsProjectWithOwner` | Happy path | Includes navigation |
| `GetProjectAsync_WithNonExistentId_ReturnsNull` | Edge case | Unknown ID |
| `GetProjectForUserAsync_WithOwner_ReturnsProject` | Happy path | Owner access |
| `GetProjectForUserAsync_WithOtherUser_ReturnsNull` | Security | User isolation |
| `CreateProjectAsync_SetsOwnerAndRepoDetails` | Happy path | All fields set |
| `CreateProjectAsync_SetsBranchFilterToDefaultBranch` | Happy path | Default config |
| `CreateProjectAsync_SetsCreatedAt` | Happy path | Timestamp |
| `CreateProjectAsync_PersistsToDatabase` | Happy path | Persistence |
| `CreateProjectAsync_WithDuplicateRepo_ThrowsException` | Error | Duplicate prevention |
| `CreateProjectAsync_WithoutInstallationId_SetsNull` | Edge case | Optional param |
| `UpdateProjectSettingsAsync_UpdatesAllSettings` | Happy path | All fields |
| `UpdateProjectSettingsAsync_ClampsTimeoutToMinimum` | Validation | Min boundary |
| `UpdateProjectSettingsAsync_ClampsTimeoutToMaximum` | Validation | Max boundary |
| `UpdateProjectSettingsAsync_TrimsWhitespace` | Validation | Input cleaning |
| `UpdateProjectSettingsAsync_WithEmptyStrings_SetsNull` | Validation | Null conversion |
| `UpdateProjectSettingsAsync_WithNonExistentProject_ReturnsFalse` | Edge case | Unknown ID |
| `DeleteProjectAsync_RemovesProject` | Happy path | Deletion |
| `DeleteProjectAsync_WithNonExistentProject_ReturnsFalse` | Edge case | Unknown ID |
| `DeleteProjectAsync_CascadesToSecrets` | Happy path | Cascade delete |
| `SetSecretAsync_CreatesNewSecret` | Happy path | New secret |
| `SetSecretAsync_EncryptsValue` | Security | Encryption |
| `SetSecretAsync_UpdatesExistingSecret` | Happy path | Upsert |
| `SetSecretAsync_SetsCreatedAtForNew` | Happy path | Timestamp |
| `SetSecretAsync_SetsUpdatedAtForExisting` | Happy path | Update timestamp |
| `DeleteSecretAsync_RemovesSecret` | Happy path | Deletion |
| `DeleteSecretAsync_WithNonExistentSecret_ReturnsFalse` | Edge case | Unknown name |
| `DeleteSecretAsync_OnlyDeletesSpecifiedSecret` | Happy path | Targeted delete |
| `GetSecretNamesAsync_ReturnsAllSecretNames` | Happy path | List names |
| `GetSecretNamesAsync_ReturnsSortedNames` | Happy path | Alphabetical |
| `GetSecretNamesAsync_WithNoSecrets_ReturnsEmptyList` | Edge case | Empty result |
| `GetSecretNamesAsync_DoesNotReturnValues` | Security | Values hidden |

#### ErrorHandlingTests (19 tests)
| Test | Type | Description |
|------|------|-------------|
| `Webhook_WhenBuildServiceThrows_ExceptionPropagates` | Error | Exception bubbles up to middleware |
| `Webhook_WhenBuildServiceThrows_NoBuildQueued` | Error | Failed queue leaves no partial state |
| `Webhook_WithEmptyBody_ReturnsUnauthorized` | Error | Empty body fails signature check |
| `Webhook_WithInvalidSignature_ReturnsUnauthorized` | Security | Invalid HMAC rejected |
| `Webhook_WithUnknownRepository_ReturnsOkButSkips` | Edge case | Unknown repo handled gracefully |
| `QueueBuildAsync_WithNonExistentProject_DoesNotUpdateLastBuildAt` | Edge case | FK not enforced in-memory |
| `RetryBuildAsync_WithNonExistentBuild_ThrowsException` | Error | Unknown build ID throws |
| `UpdateBuildStatusAsync_WithNonExistentBuild_NoOps` | Edge case | No-op for unknown build |
| `CancelBuildAsync_WithNonExistentBuild_ReturnsFalse` | Edge case | Unknown build returns false |
| `CancelBuildAsync_WithCompletedBuild_ReturnsFalse` | Edge case | Can't cancel completed build |
| `CreateProjectAsync_WithDuplicateRepo_ThrowsException` | Error | Duplicate repo ID rejected |
| `DeleteProjectAsync_WithNonExistentProject_ReturnsFalse` | Edge case | Unknown project returns false |
| `UpdateProjectSettingsAsync_WithNonExistentProject_ReturnsFalse` | Edge case | Unknown project returns false |
| `DeleteSecretAsync_WithNonExistentSecret_ReturnsFalse` | Edge case | Unknown secret returns false |
| `EncryptionService_WithInvalidKey_ThrowsOnConstruction` | Error | Invalid key rejected |
| `EncryptionService_Decrypt_WithCorruptedData_Throws` | Security | Tampered data fails |
| `EncryptionService_Decrypt_WithWrongKey_Throws` | Security | Wrong key fails |
| `CancellationRegistry_TryCancel_UnregisteredBuild_ReturnsFalse` | Edge case | Unknown build returns false |
| `CancellationRegistry_IsRunning_UnregisteredBuild_ReturnsFalse` | Edge case | Unknown build returns false |

#### ProjectTests (15 tests)
| Test | Type | Description |
|------|------|-------------|
| `MatchesBranchFilter_WithExactMatch_ReturnsTrue` | Happy path | Exact branch match |
| `MatchesBranchFilter_WithNoMatch_ReturnsFalse` | Edge case | No matching branch |
| `MatchesBranchFilter_WithMultipleBranches_MatchesAny` | Happy path | Multi-branch filter |
| `MatchesBranchFilter_IsCaseInsensitive` | Edge case | Case insensitivity |
| `MatchesBranchFilter_TrimsWhitespace` | Edge case | Whitespace handling |
| `MatchesBranchFilter_IgnoresEmptyEntries` | Edge case | Empty entries ignored |
| `MatchesBranchFilter_WithEmptyFilter_ReturnsFalse` | Edge case | Empty filter |
| `MatchesBranchFilter_WithSlashInBranchName_MatchesExactly` | Happy path | Feature branches |
| `MatchesBranchFilter_DefaultFilter_MatchesMainAndMaster` | Default | Default behavior |
| `GetNotificationEmail_WithOverride_ReturnsOverride` | Happy path | Email override |
| `GetNotificationEmail_WithoutOverride_ReturnsOwnerEmail` | Fallback | Owner email fallback |
| `GetNotificationEmail_WithNoEmails_ReturnsNull` | Edge case | No email |
| `GetNotificationEmail_WithEmptyOverride_ReturnsEmpty` | Edge case | Empty override |
| `Project_HasCorrectDefaults` | Default | Default values |

#### BuildTests (21 tests)
| Test | Type | Description |
|------|------|-------------|
| `ShortCommitSha_WithFullSha_ReturnsFirst8Characters` | Happy path | SHA truncation |
| `ShortCommitSha_WithExactly8Characters_ReturnsAll` | Edge case | Exact length |
| `ShortCommitSha_WithShorterThan8Characters_ReturnsAll` | Edge case | Short SHA |
| `ShortCommitSha_WithEmptyString_ReturnsEmpty` | Edge case | Empty SHA |
| `IsFinished_WithTerminalStatus_ReturnsTrue` | Theory | Success/Failed/Cancelled/TimedOut |
| `IsFinished_WithNonTerminalStatus_ReturnsFalse` | Theory | Queued/Running |
| `CanCancel_WithCancellableStatus_ReturnsTrue` | Theory | Running/Queued |
| `CanCancel_WithNonCancellableStatus_ReturnsFalse` | Theory | Terminal states |
| `CanRetry_WithFinishedStatus_ReturnsTrue` | Theory | Terminal states |
| `CanRetry_WithNonFinishedStatus_ReturnsFalse` | Theory | Queued/Running |
| `CanCancel_And_CanRetry_AreMutuallyExclusive` | Invariant | Mutual exclusivity |
| `IsFinished_Matches_CanRetry` | Invariant | Consistent properties |
| `Build_HasCorrectDefaults` | Default | Default values |
| `NewBuild_IsNotFinished` | Default | Initial state |

#### BuildArtifactTests (24 tests)
| Test | Type | Description |
|------|------|-------------|
| `IsExpired_WithFutureExpiration_ReturnsFalse` | Happy path | Not expired |
| `IsExpired_WithPastExpiration_ReturnsTrue` | Happy path | Expired |
| `IsExpired_WithExactlyNow_ReturnsFalse` | Edge case | Boundary |
| `IsExpired_WithJustExpired_ReturnsTrue` | Edge case | Just expired |
| `FormattedSize_WithZeroBytes_ReturnsZeroB` | Edge case | Zero bytes |
| `FormattedSize_WithSmallBytes_ReturnsBytes` | Happy path | Small files |
| `FormattedSize_With1023Bytes_ReturnsBytes` | Boundary | Just under 1 KB |
| `FormattedSize_WithExactlyOneKB_ReturnsOneKB` | Boundary | Exactly 1 KB |
| `FormattedSize_WithKilobytes_ReturnsKB` | Happy path | KB range |
| `FormattedSize_WithFractionalKB_ShowsDecimals` | Precision | Fractional KB |
| `FormattedSize_WithExactlyOneMB_ReturnsOneMB` | Boundary | Exactly 1 MB |
| `FormattedSize_WithMegabytes_ReturnsMB` | Happy path | MB range |
| `FormattedSize_WithFractionalMB_ShowsDecimals` | Precision | Fractional MB |
| `FormattedSize_WithExactlyOneGB_ReturnsOneGB` | Boundary | Exactly 1 GB |
| `FormattedSize_WithGigabytes_ReturnsGB` | Happy path | GB range |
| `FormattedSize_WithLargeGB_StaysInGB` | Happy path | Large GB |
| `FormattedSize_WithVeryLargeSize_StaysInGB` | Boundary | TB as GB |
| `FormattedSize_TruncatesToTwoDecimals` | Precision | Decimal truncation |
| `FormattedSize_RemovesTrailingZeros` | Precision | Clean formatting |
| `BuildArtifact_HasCorrectDefaults` | Default | Default values |

#### ProjectsControllerValidationTests (28 tests)
| Test | Type | Description |
|------|------|-------------|
| `AddSecret_WithValidName_Succeeds` | Theory | Valid env var names (7 cases) |
| `AddSecret_WithInvalidName_ReturnsError` | Theory | Invalid names (8 cases) |
| `AddSecret_WithEmptyName_ReturnsError` | Validation | Empty name rejected |
| `AddSecret_WithEmptyValue_ReturnsError` | Validation | Empty value rejected |
| `AddSecret_WithWhitespaceOnlyName_ReturnsError` | Validation | Whitespace rejected |
| `AddSecret_TrimsSecretName` | Edge case | Whitespace handling |
| `AddSecret_WithNonExistentProject_ReturnsNotFound` | Ownership | Project must exist |
| `AddSecret_WithOtherUsersProject_ReturnsNotFound` | Ownership | Must own project |
| `Details_WithNonExistentProject_ReturnsNotFound` | Ownership | Project must exist |
| `Settings_WithNonExistentProject_ReturnsNotFound` | Ownership | Project must exist |
| `Delete_WithNonExistentProject_ReturnsNotFound` | Ownership | Project must exist |
| `TriggerBuild_WithNonExistentProject_ReturnsNotFound` | Ownership | Project must exist |
| `DeleteSecret_WithNonExistentProject_ReturnsNotFound` | Ownership | Project must exist |
| `Settings_Post_WithValidInput_UpdatesProject` | Happy path | Settings update |
| `Settings_Post_WithNonExistentProject_ReturnsNotFound` | Ownership | Project must exist |
| `Create_Post_WithNonExistentRepo_ReturnsErrorAndRedirects` | Validation | Repo not found |
| `Create_Post_WithDuplicateRepo_ReturnsErrorAndRedirects` | Validation | Duplicate rejected |
| `Create_Get_WithNoAccessToken_RedirectsToLogin` | Auth | Token required |
| `Create_Post_WithNoAccessToken_RedirectsToLogin` | Auth | Token required |

#### BuildsControllerValidationTests (31 tests)
| Test | Type | Description |
|------|------|-------------|
| `Cancel_WithCancellableStatus_Succeeds` | Theory | Queued/Running (2 cases) |
| `Cancel_WithNonCancellableStatus_ReturnsError` | Theory | Terminal states (4 cases) |
| `Cancel_WhenServiceReturnsFalse_ReturnsError` | Error | Service failure |
| `Retry_WithRetryableStatus_Succeeds` | Theory | Failed/Cancelled/TimedOut (3 cases) |
| `Retry_WithNonRetryableStatus_ReturnsError` | Theory | Queued/Running/Success (3 cases) |
| `Retry_PreservesBuildParameters` | Happy path | Parameters preserved |
| `Details_WithNonExistentBuild_ReturnsNotFound` | Ownership | Build must exist |
| `Details_WithOtherUsersBuild_ReturnsNotFound` | Ownership | Must own build |
| `Cancel_WithNonExistentBuild_ReturnsNotFound` | Ownership | Build must exist |
| `Cancel_WithOtherUsersBuild_ReturnsNotFound` | Ownership | Must own build |
| `Retry_WithNonExistentBuild_ReturnsNotFound` | Ownership | Build must exist |
| `Retry_WithOtherUsersBuild_ReturnsNotFound` | Ownership | Must own build |
| `GetLogs_WithNonExistentBuild_ReturnsNotFound` | Ownership | Build must exist |
| `GetLogs_WithOtherUsersBuild_ReturnsNotFound` | Ownership | Must own build |
| `DownloadArtifact_WithNonExistentArtifact_ReturnsNotFound` | Validation | Artifact must exist |
| `DownloadArtifact_WithMismatchedBuildId_ReturnsNotFound` | Validation | Build ID match |
| `DownloadArtifact_WithOtherUsersArtifact_ReturnsNotFound` | Ownership | Must own artifact |
| `GetLogs_WithValidBuild_ReturnsJson` | Happy path | Returns logs |
| `GetLogs_WithAfterSequence_FiltersLogs` | Filtering | Sequence filtering |

#### WebhookIntegrationTests (19 tests)
Full HTTP pipeline tests using `WebApplicationFactory` with in-memory database.
| Test | Type | Description |
|------|------|-------------|
| `PushWebhook_CreatesBuildInDatabase` | Happy path | Full webhook flow creates build |
| `PushWebhook_EnqueuesHangfireJob` | Happy path | Hangfire job enqueued correctly |
| `PushWebhook_UpdatesProjectLastBuildAt` | Happy path | Project timestamp updated |
| `PushWebhook_UpdatesInstallationId` | Happy path | Installation ID synced from webhook |
| `PushWebhook_StoresCommitMetadata` | Happy path | Commit message/author stored |
| `PushWebhook_WithBranchNotInFilter_DoesNotCreateBuild` | Filter | Branch filter enforced |
| `PushWebhook_ForUnknownRepository_ReturnsOkButNoBuild` | Edge case | Unknown repo ignored |
| `PushWebhook_BranchDeletion_DoesNotCreateBuild` | Edge case | Branch deletion ignored |
| `PullRequestWebhook_WithPrBuildsEnabled_CreatesBuild` | Happy path | PR build created |
| `PullRequestWebhook_WithPrBuildsDisabled_DoesNotCreateBuild` | Config | PR builds off |
| `PullRequestWebhook_Synchronize_CreatesBuild` | Happy path | PR sync triggers build |
| `PullRequestWebhook_Closed_DoesNotCreateBuild` | Filter | Closed PR ignored |
| `PullRequestWebhook_StoresPrTitle` | Happy path | PR metadata stored |
| `Webhook_WithInvalidSignature_ReturnsUnauthorized` | Security | Invalid signature rejected |
| `Webhook_WithMissingSignature_ReturnsUnauthorized` | Security | Missing signature rejected |
| `MultipleWebhooks_CreateMultipleBuilds` | Happy path | Sequential webhooks work |
| `WebhooksForDifferentProjects_CreateSeparateBuilds` | Happy path | Project isolation |
| `PushWebhook_ReturnsJsonWithBuildId` | Response | Response format verified |
| `PingEvent_ReturnsPong` | Happy path | GitHub ping handled |

#### CleanupArtifactsJobTests (14 tests)
Tests for the Hangfire job that cleans up expired build artifacts.
| Test | Type | Description |
|------|------|-------------|
| `ExecuteAsync_WithExpiredArtifact_DeletesFromDatabase` | Happy path | Expired artifact removed |
| `ExecuteAsync_WithNonExpiredArtifact_DoesNotDelete` | Happy path | Valid artifact preserved |
| `ExecuteAsync_WithExactlyExpiredArtifact_Deletes` | Edge case | Boundary expiration |
| `ExecuteAsync_WithMixedArtifacts_DeletesOnlyExpired` | Happy path | Selective deletion |
| `ExecuteAsync_WithExpiredArtifact_DeletesPhysicalFile` | Happy path | File system cleanup |
| `ExecuteAsync_WithMissingFile_StillDeletesRecord` | Edge case | Orphaned DB record |
| `ExecuteAsync_WithMultipleExpiredArtifacts_DeletesAllFiles` | Happy path | Batch deletion |
| `ExecuteAsync_WithMoreThanBatchSize_ProcessesAllInBatches` | Batch | Handles > 100 artifacts |
| `ExecuteAsync_WithNoArtifacts_CompletesSuccessfully` | Edge case | Empty database |
| `ExecuteAsync_WithOnlyValidArtifacts_LeavesAllIntact` | Edge case | Nothing to delete |
| `ExecuteAsync_TracksDeletedBytes` | Logging | Size tracking |
| `ExecuteAsync_WithArtifactFromDifferentBuilds_DeletesCorrectly` | Isolation | Multi-build handling |

#### CleanupOldBuildsJobTests (18 tests)
Tests for the Hangfire job that cleans up orphaned builds (stuck in Running/Queued states).
| Test | Type | Description |
|------|------|-------------|
| `ExecuteAsync_WithOrphanedRunningBuild_MarksAsTimedOut` | Happy path | Running > 2hr → TimedOut |
| `ExecuteAsync_WithRecentRunningBuild_DoesNotModify` | Happy path | Running < 2hr preserved |
| `ExecuteAsync_WithRunningBuildJustUnderCutoff_DoesNotModify` | Boundary | Nearly expired preserved |
| `ExecuteAsync_OrphanedRunningBuild_SetsDuration` | Happy path | Duration calculated |
| `ExecuteAsync_WithOrphanedQueuedBuild_MarksAsFailed` | Happy path | Queued > 24hr → Failed |
| `ExecuteAsync_WithRecentQueuedBuild_DoesNotModify` | Happy path | Queued < 24hr preserved |
| `ExecuteAsync_WithQueuedBuildJustUnderCutoff_DoesNotModify` | Boundary | Nearly expired preserved |
| `ExecuteAsync_WithCompletedBuild_DoesNotModify` | Theory | Terminal states preserved (4) |
| `ExecuteAsync_WithMixedBuilds_UpdatesOnlyOrphaned` | Happy path | Selective updates |
| `ExecuteAsync_WithMultipleOrphanedBuilds_UpdatesAll` | Happy path | Batch processing |
| `ExecuteAsync_WithNoBuilds_CompletesSuccessfully` | Edge case | Empty database |
| `ExecuteAsync_WithOnlyCompletedBuilds_DoesNothing` | Edge case | Nothing to update |
| `ExecuteAsync_WithRunningBuildNoStartedAt_DoesNotCrash` | Edge case | Null StartedAt handled |
| `ExecuteAsync_PreservesExistingBuildData` | Data integrity | Build fields preserved |
| `ExecuteAsync_BuildsFromDifferentProjects_HandledCorrectly` | Isolation | Multi-project handling |

### E2E Tests (Playwright)

| File | Tests | Description |
|------|-------|-------------|
| `auth.spec.ts` | 3 | Login flow, session, redirect |
| `dashboard.spec.ts` | 4 | Stats, recent builds, navigation |
| `projects.spec.ts` | 6 | CRUD, settings, secrets |
| `builds.spec.ts` | 6 | Status, cancel, retry, logs, artifacts |
| `realtime-logs.spec.ts` | 3 | SignalR live streaming |

## Gaps & Recommended Additional Tests

### High Priority

#### 1. Authorization Tests ✅ IMPLEMENTED
Authorization and user isolation tests have been added in `AuthorizationTests.cs`.

#### 2. Service Layer Tests ✅ IMPLEMENTED
All core service tests have been added:
- `BuildServiceTests.cs` - Build queuing, cancellation, retry, status updates
- `EncryptionServiceTests.cs` - AES-256 encryption, security properties
- `ProjectServiceTests.cs` - CRUD operations, settings, secrets

#### 3. Error Handling Tests ✅ IMPLEMENTED
Error handling tests have been added in `ErrorHandlingTests.cs` covering:
- Webhook error scenarios (service exceptions, invalid signatures, empty bodies)
- Build service edge cases (non-existent builds/projects)
- Project service edge cases (non-existent projects/secrets, duplicates)
- Encryption service errors (invalid keys, corrupted data)
- Cancellation registry edge cases

Note: Some orchestrator-level error tests (timeout, clone failures) require
integration testing with real containers and are documented for future work.

### Medium Priority

#### 4. Integration Tests ✅ IMPLEMENTED
Integration tests have been added in `Integration/WebhookIntegrationTests.cs` covering:
- Full HTTP pipeline testing via `WebApplicationFactory`
- Push webhook flow (build creation, job enqueuing, project updates)
- Pull request webhook flow (PR builds, metadata storage)
- Security validation (signature verification)
- Multi-webhook scenarios (sequential builds, project isolation)
- Response format verification

Uses:
- `AndoWebApplicationFactory` for test server configuration
- In-memory database for isolation
- Mock `IBackgroundJobClient` for Hangfire verification

#### 5. Model Validation Tests ✅ IMPLEMENTED
Model validation tests have been added in `Unit/Models/`:
- `ProjectTests.cs` - Branch filter matching (9 tests), notification email fallback (4 tests), defaults (1 test)
- `BuildTests.cs` - Status helpers (IsFinished, CanCancel, CanRetry), short commit SHA, invariants
- `BuildArtifactTests.cs` - Expiration checking, human-readable file size formatting

```csharp
// Example: Unit/Models/ProjectTests.cs
[Fact]
public void MatchesBranchFilter_WithMultipleBranches_MatchesAny()

[Fact]
public void MatchesBranchFilter_IsCaseInsensitive()

[Fact]
public void GetNotificationEmail_FallsBackToOwnerEmail()
```

#### 6. Controller Input Validation ✅ IMPLEMENTED
Controller input validation tests have been added in `Unit/Controllers/`:
- `ProjectsControllerValidationTests.cs` - Secret name validation (regex), required fields, ownership
- `BuildsControllerValidationTests.cs` - Cancel/retry state validation, ownership verification

Tests cover:
- Secret name format validation (must be `^[A-Z_][A-Z0-9_]*$`)
- Required field validation (name, value)
- Build state transition validation (cancel only Queued/Running, retry only terminal)
- Ownership verification for all CRUD operations
- Authentication requirements

#### 7. Cleanup Job Tests ✅ IMPLEMENTED
Cleanup job tests have been added in `Unit/Jobs/`:
- `CleanupArtifactsJobTests.cs` - Artifact expiration, file deletion, batch processing
- `CleanupOldBuildsJobTests.cs` - Orphaned build detection, status transitions

Tests cover:
- Artifact expiration logic (expired vs valid, boundary conditions)
- Physical file deletion and missing file handling
- Batch processing for large artifact counts
- Orphaned running builds (> 2 hours → TimedOut)
- Orphaned queued builds (> 24 hours → Failed)
- Completed build preservation
- Data integrity during status updates

### Lower Priority

#### 8. Performance/Load Tests
- Concurrent webhook handling
- Large log streaming
- Many artifacts

#### 9. Security Tests
- XSS in build logs display
- Secret masking in logs
- CSRF token validation

## Test Patterns

### Using MockServices

```csharp
public class MyTest : IDisposable
{
    private readonly AndoDbContext _db;
    private readonly MockBuildService _buildService;

    public MyTest()
    {
        _db = TestDbContextFactory.Create();
        _buildService = new MockBuildService();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task MyTest_Scenario()
    {
        // Arrange
        _buildService.NextBuildId = 100;

        // Act
        // ... call code under test ...

        // Assert
        _buildService.QueueBuildCalls.ShouldHaveSingleItem();
    }
}
```

### Testing Error Scenarios

```csharp
[Fact]
public async Task CloneRepo_WhenGitHubFails_ThrowsException()
{
    // Arrange
    var service = new MockGitHubService
    {
        ThrowOnCloneRepo = new Exception("Network error")
    };

    // Act & Assert
    await Should.ThrowAsync<Exception>(() =>
        service.CloneRepositoryAsync(...));
}
```

### E2E Test Isolation

```typescript
// Each test gets a unique user
test('user can create project', async ({ authenticatedPage, testProject }) => {
    // testProject is automatically created and cleaned up
    await authenticatedPage.goto(`/projects/${testProject.id}`);
});
```

## Adding New Tests

1. **Unit tests**: Add to `tests/Ando.Server.Tests/Unit/`
2. **Integration tests**: Add to `tests/Ando.Server.Tests/Integration/`
3. **E2E tests**: Add to `tests/Ando.Server.E2E/tests/`

### Naming Conventions

- Unit test classes: `{ClassName}Tests.cs`
- Test methods: `{Method}_{Scenario}_{ExpectedResult}`
- E2E test files: `{feature}.spec.ts`

### Test Categories

For integration/E2E tests that require infrastructure:

```csharp
[Fact]
[Trait("Category", "Integration")]
public async Task DatabaseIntegrationTest() { }
```

```bash
# Run only unit tests
dotnet test --filter "Category!=Integration&Category!=E2E"
```

## CI/CD Integration

Tests should be run in CI pipeline:

```yaml
# .github/workflows/test.yml
jobs:
  test:
    steps:
      - name: Unit Tests
        run: dotnet test tests/Ando.Server.Tests/ --filter "Category!=Integration"

      - name: E2E Tests
        run: |
          cd tests/Ando.Server.E2E
          npm ci
          npx playwright install --with-deps
          npm test
```

## References

- [xUnit Documentation](https://xunit.net/)
- [Shouldly Assertions](https://docs.shouldly.org/)
- [Playwright Testing](https://playwright.dev/)
- [EF Core InMemory Testing](https://docs.microsoft.com/ef/core/testing/)
