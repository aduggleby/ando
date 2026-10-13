Mr. Ando, **not ready to merge yet**. Most fixes are resolved; legacy-name compatibility and release gating still need attention. This was a read-only review. I relied on the reported verification; the re-running E2E matrix remains unconfirmed.

1. **Finding 1 — Partially resolved.**  
   The [SQL Server migration](/home/alex/Source/ando/src/Ando.Server/Migrations/SqlServer/20261013075421_AddProjectSecretNormalizedName.cs:58) correctly stages the nullable BIN2 column, fixed-collation backfill, collision `THROW`, non-null alteration, and index swap. `Down()` reverses those schema operations. The [model collation](/home/alex/Source/ando/src/Ando.Server/Data/AndoDbContext.cs:260) is SQL Server-only; PostgreSQL is unaffected.

   For every name accepted by [the ASCII validation rule](/home/alex/Source/ando/src/Ando.Server/Models/ProjectSecret.cs:100), SQL uppercase and `ToUpperInvariant()` agree. Endpoint and service validation now enforce that storage domain.

   **Residual P2 — Legacy compatibility remains unverified.** [The backfill](/home/alex/Source/ando/src/Ando.Server/Migrations/SqlServer/20261013075421_AddProjectSecretNormalizedName.cs:69) processes unrestricted legacy Unicode, while [deletion](/home/alex/Source/ando/src/Ando.Server/Services/ProjectService.cs:216) uses invariant casing. The earlier requested incompatible-name audit is absent. For example, [.NET 9 explicitly preserves dotless `ı` during invariant uppercase](https://raw.githubusercontent.com/dotnet/runtime/v9.0.0/src/native/libs/System.Globalization.Native/pal_casing.c); its exact SQL result was not independently established here, so I am **not claiming a reproduced mismatch**. Either backfill retained legacy names with the application algorithm, or reject unsupported names with an actionable migration error. Add a legacy Unicode upgrade-and-delete test.

2. **Finding 2 — Resolved.**  
   The [populated-upgrade test](/home/alex/Source/ando/tests/Ando.Server.Tests/Integration/SqlServerMigrationRelocationTests.cs:138) operates on a real pre-change baseline with multiple secrets and projects. The [case-sensitive collision test](/home/alex/Source/ando/tests/Ando.Server.Tests/Integration/SqlServerMigrationRelocationTests.cs:194) and Turkish tests seed collisions that the old index permits, require error `50001`, and verify rollback of the column/index/history plus preservation of values. These are non-vacuous. They still need the legacy-domain coverage described above.

3. **Finding 3 — Resolved.**  
   The [restart test](/home/alex/Source/ando/tests/Ando.Server.Tests/Integration/HangfireStorageTestsBase.cs:146) now requires one started, incomplete execution before shutdown, then exactly two starts and one completion after recovery. Timeout throws. It cannot pass merely because the second server executes a previously untouched queued job. Coverage models graceful shutdown cancellation, rather than abrupt process death.

4. **Finding 4 — Resolved.**  
   Production registration and integration tests share [the options factory](/home/alex/Source/ando/src/Ando.Server/Infrastructure/Database/HangfireStorageOptionsFactory.cs:78). [Unit assertions](/home/alex/Source/ando/tests/Ando.Server.Tests/Unit/Infrastructure/HangfireStorageOptionsFactoryTests.cs:26) pin sliding invisibility to `true`. The pinned dependency defaults it to `false`, so removing the factory assignment fails a test.

5. **Finding 5 — Resolved at fixture level.**  
   [Mode selection](/home/alex/Source/ando/tests/Ando.Server.Tests/TestFixtures/DatabaseTestContainerMode.cs:56) defaults to isolated when either marker is present. Both fixtures use unique names, random mapped ports, and cleanup in isolated mode; fixed-name removal is confined to shared mode. The [SQL Server fixture](/home/alex/Source/ando/tests/Ando.Server.Tests/TestFixtures/SqlServerFixture.cs:94) and [PostgreSQL host rewrite](/home/alex/Source/ando/tests/Ando.Server.Tests/TestFixtures/PostgreSqlFixture.cs:159) retain the actual mapped port. There is a separate build-environment issue below.

6. **Finding 6 — Resolved.**  
   [The helper](/home/alex/Source/ando/website/public/server-install.sh:733) reads UID and GID ranges separately, validates missing/malformed/insufficient entries, and calculates `start + container_id - 1`. Both database branches use those results. Mapping failures terminate the remote script under `set -euo pipefail`.

7. **Finding 7 — Partially resolved.**  
   [Server builds and non-integration tests](/home/alex/Source/ando/build.csando:91) now run unconditionally outside the docs-only profile. Publishing requires Docker, and integration plus both E2E steps are registered.

   **Residual P2 — The required matrix remains bypassable.** [Integration tests](/home/alex/Source/ando/build.csando:147) accept a tracked `ANDO_TEST_DB_PROVIDERS=SqlServer`, skipping PostgreSQL while publishing continues. [The first E2E step](/home/alex/Source/ando/build.csando:159) inherits `E2E_DB_PROVIDER`; setting it to PostgreSQL runs PostgreSQL twice.

   Defaults are acceptable for ordinary local runs, but insufficient to guarantee release coverage. Reject narrowed integration selection or explicitly set both providers. Invoke the existing `test:sqlserver` script for the first E2E step.

8. **Finding 8 — Resolved.**  
   External-database installs now receive the backup script and cron job. [Only the database dump is skipped](/home/alex/Source/ando/website/public/server-install.sh:1313); configuration and key archives remain enabled. Guidance agrees in [server.md](/home/alex/Source/ando/website/src/content/pages/server.md:160), [TrueNAS documentation](/home/alex/Source/ando/website/src/content/pages/server-truenas.md:290), [llms.txt](/home/alex/Source/ando/website/public/llms.txt:169), and [README](/home/alex/Source/ando/src/Ando.Server/README.md:119). TrueNAS correctly requires separate backups.

9. **Finding 9 — Resolved.**  
   [The failure handler](/home/alex/Source/ando/scripts/add-migration.sh:44) identifies the partial SQL Server result, mentions the changed snapshot, supplies the scoped removal command, and exits nonzero without destructive cleanup.

10. **Secret-policy documentation — Partially resolved.**  
    The new-name policy, collision failure, transactional rollback, and backup advice match the code. However, [server.md](/home/alex/Source/ando/website/src/content/pages/server.md:222), [llms.txt](/home/alex/Source/ando/website/public/llms.txt:180), [README](/home/alex/Source/ando/src/Ando.Server/README.md:273), [ARCHITECTURE](/home/alex/Source/ando/src/Ando.Server/ARCHITECTURE.md:819), and [release notes](/home/alex/Source/ando/docs/plans/2026-10-13-postgres-support.md:352) overstate agreement for retained legacy names. **P2:** align these claims with the resolution of finding 1. **P3 wording:** older binaries cannot *insert new secrets*; saying they cannot *write secrets* also excludes existing-row updates, which may still work.

11. **Client changes — Resolved.**  
    [ProjectSettings](/home/alex/Source/ando/src/Ando.Server/ClientApp/src/pages/projects/ProjectSettings.tsx:44) handles `success:false`, displays the server error, and preserves entered fields. [Health-check types](/home/alex/Source/ando/src/Ando.Server/ClientApp/src/types/index.ts:331) match the server’s `key` and nullable `provider` contract.

**New issue — P2: automatic CI isolation does not reach containerized build steps.**  
[ContainerExecutor](/home/alex/Source/ando/src/Ando/Execution/ContainerExecutor.cs:75) forwards explicit options and tracked environment-file variables. Host `CI`/`ANDO_HOST_ROOT` are not automatically forwarded by [container creation](/home/alex/Source/ando/src/Ando/Cli/AndoCli.cs:433). Consequently, the new integration gate can select shared mode on CI and remove another run’s fixed-name database container. [The build comments](/home/alex/Source/ando/build.csando:143) acknowledge the manual configuration requirement. Force `ANDO_TEST_DB_CONTAINERS=isolated` for this gate or explicitly forward the CI marker.

**Verdict:** resolve the legacy-name verification gap, enforce the release provider matrix and container isolation, align the documentation, and confirm passing E2E results for both providers before merging.
