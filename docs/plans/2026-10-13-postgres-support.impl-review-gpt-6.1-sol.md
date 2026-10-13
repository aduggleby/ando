Mr. Ando, **the change is not ready to merge**. I found one SQL Server upgrade blocker and seven should-fix issues. I did not repeat the reported builds or tests; both-provider E2E results remain unconfirmed.

### Findings

1. **Blocker — Secret migration can normalize existing names differently from application lookups.**  
   [20261013070742_AddProjectSecretNormalizedName.cs:55](/home/alex/Source/ando/src/Ando.Server/Migrations/SqlServer/20261013070742_AddProjectSecretNormalizedName.cs:55)

   `UPPER([Name])` inherits the existing SQL collation, while application writes and lookups use `ToUpperInvariant()`. Under Turkish collations, lowercase ASCII `i` becomes `İ` in SQL but `I` in C#. Existing `i` and `I` secrets can therefore survive the collision audit as distinct rows even though application normalization maps both to `I`. A subsequent update or delete can address the wrong secret.

   The migration’s ASCII-only premise is also unenforced: [SetSecretEndpoint.cs:52](/home/alex/Source/ando/src/Ando.Server/Endpoints/Projects/SetSecretEndpoint.cs:52) checks whitespace only. The DTO’s validation attributes require [explicit FastEndpoints DataAnnotations support](https://fast-endpoints.com/docs/validation), which is absent. Microsoft documents [`UPPER` as collation-sensitive](https://learn.microsoft.com/en-us/sql/t-sql/statements/collation-precedence-transact-sql).

   **Fix:** Enforce the intended ASCII name policy at the secret boundary, audit incompatible legacy names, and backfill supported names using a fixed non-Turkish collation. Give `NormalizedName` binary comparison semantics so its index and lookups match normalized code points. Add an upgrade test containing legacy `i`/`I` secrets under a Turkish collation.

2. **Should-fix — The staged secret migration has no populated-upgrade or collision-rollback test.**  
   [SqlServerMigrationRelocationTests.cs:79](/home/alex/Source/ando/tests/Ando.Server.Tests/Integration/SqlServerMigrationRelocationTests.cs:79), [MigrationSchemaTestsBase.cs:87](/home/alex/Source/ando/tests/Ando.Server.Tests/Integration/MigrationSchemaTestsBase.cs:87)

   The relocation tests deliberately stop before `AddProjectSecretNormalizedName`. The schema tests migrate an empty database and insert secrets afterward. Consequently, the reported passing suite does not verify backfilling existing secrets, the collision `THROW`, transaction rollback, or preservation of the old index after failure.

   **Fix:** Keep relocation tests separate, and add dedicated normalization-upgrade tests. Verify successful backfill preserves names and encrypted values; verify a canonical-name collision fails clearly and leaves the previous schema, rows, index, and migration history intact.

3. **Should-fix — Hangfire recovery test can pass without interrupting a running job.**  
   [HangfireStorageTestsBase.cs:140](/home/alex/Source/ando/tests/Ando.Server.Tests/Integration/HangfireStorageTestsBase.cs:140), [HangfireStorageTestsBase.cs:223](/home/alex/Source/ando/tests/Ando.Server.Tests/Integration/HangfireStorageTestsBase.cs:223)

   `WaitUntilAsync` silently returns on timeout. If the first server never starts the job, the test can dispose it, let the second server execute the queued job, and pass because the final assertion requires only `Started >= 1`. That proves ordinary execution, not recovery.

   **Fix:** Throw when the wait expires, assert one execution has started before stopping the first server, and assert a second execution starts and completes afterward.

4. **Should-fix — Lease tests do not guard the production Hangfire configuration.**  
   [HangfireStorageTestsBase.cs:179](/home/alex/Source/ando/tests/Ando.Server.Tests/Integration/HangfireStorageTestsBase.cs:179)

   The tests construct PostgreSQL storage options independently and explicitly enable sliding invisibility. Removing `UseSlidingInvisibilityTimeout = true` from production registration would leave these tests green, despite reintroducing duplicate execution of long builds.

   **Fix:** Test the actual production registration’s storage options, or share their construction and override only test timing values. The suite should fail if production lease renewal is disabled.

5. **Should-fix — Fixtures can destroy another concurrent test run’s database.**  
   [PostgreSqlFixture.cs:71](/home/alex/Source/ando/tests/Ando.Server.Tests/TestFixtures/PostgreSqlFixture.cs:71), [SqlServerFixture.cs:71](/home/alex/Source/ando/tests/Ando.Server.Tests/TestFixtures/SqlServerFixture.cs:71)

   Both fixtures forcibly remove a fixed container name, bind fixed ports, disable cleanup, and leave containers running. Concurrent jobs sharing a Docker daemon can kill each other’s active databases. The advice in [TESTING.md:116](/home/alex/Source/ando/tests/Ando.Server.Tests/TESTING.md:116) to remove containers afterward does not prevent collisions during execution.

   **Fix:** Preserve inspection behavior locally, but provide a CI mode with unique container ownership, nonconflicting ports within the reserved range, and automatic cleanup. Alternatively, enforce serialization around these shared resources.

6. **Should-fix — PostgreSQL volume ownership assumes a subordinate-ID mapping the installer does not enforce.**  
   [server-install.sh:733](/home/alex/Source/ando/website/public/server-install.sh:733)

   The installer assigns `pgdata` to host UID/GID `100998` and mode `700`. However, rootless setup preserves existing `/etc/subuid` and `/etc/subgid` entries. When those ranges start somewhere other than `100000`, container UID/GID `999` maps elsewhere and cannot initialize the data directory. UID and GID ranges may also differ.

   **Fix:** Determine both mapped IDs from the actual rootless namespace, or perform ownership setup inside that namespace. Update the matching ownership advice in the deployment documentation. The SQL Server branch has the same assumption.

7. **Should-fix — Planned mandatory server and migration-drift gates remain bypassable.**  
   [build.csando:113](/home/alex/Source/ando/build.csando:113)

   The entire server test project is behind the E2E gate and Docker availability check. Ordinary builds on Ando.Server, or builds without DIND, skip every server test—including database-free migration-drift tests—and can continue into publishing. This does not satisfy the plan’s mandatory CI checks.

   **Fix:** Run server unit and drift tests independently of Docker. Require both database integration collections and both E2E variants for CI/release execution, explicitly set the provider matrix, and fail that execution if Docker is unavailable.

8. **Should-fix — External-database backup guidance refers to archives the installer never creates.**  
   [server-install.sh:1206](/home/alex/Source/ando/website/public/server-install.sh:1206), [README.md:145](/home/alex/Source/ando/src/Ando.Server/README.md:145)

   With `--external-db`, the installer returns before creating configuration and Data Protection key backups as well as database backups. The README nevertheless tells these operators to retain `-keys.tar.gz` archives. Losing the server host can therefore leave an external database backup without the configuration encryption key needed to decrypt project secrets.

   **Fix:** Retain configuration/key backups for external databases while omitting the database dump, or document a complete separate procedure for backing up and restoring those files. Keep the website and `llms.txt` consistent with that procedure.

9. **Nit — Adding migrations can leave only one provider updated after failure.**  
   [add-migration.sh:28](/home/alex/Source/ando/scripts/add-migration.sh:28)

   SQL Server scaffolding completes before PostgreSQL scaffolding begins. If the second command fails, the script exits with a partial migration pair.

   **Fix:** Report the partial result explicitly and document how to remove the newly generated SQL Server migration before retrying. Avoid broad cleanup that could remove unrelated work.

### Blockers

- **Finding 1:** Make legacy secret backfill, collision detection, uniqueness, and lookups agree with application normalization before merging.

### Top 10 should-fix

Seven supported findings, ordered by priority:

1. **Finding 2:** Test populated normalization upgrades and collision rollback.
2. **Finding 7:** Enforce server, drift, and both-provider release gates.
3. **Finding 6:** Resolve actual rootless UID/GID mappings.
4. **Finding 3:** Make the Hangfire restart test fail when its prerequisite never occurs.
5. **Finding 4:** Guard production sliding-invisibility configuration.
6. **Finding 5:** Isolate and clean up CI database containers.
7. **Finding 8:** Correct external-database configuration/key backup behavior and guidance.

### Verdict

**Not ready to merge.** The provider split is coherent: the five relocated SQL Server migration pairs differ only in namespace/context binding, with IDs and operations preserved; PostgreSQL migration types, identity strategy, indexes, and relationships appear consistent; the UTC converter and React timestamp handling match the plan. The main remaining risks are normalization of legacy SQL Server secrets, missing upgrade assertions, bypassable release checks, and deployment ownership assumptions. Resolve the blocker and should-fix findings, then obtain passing E2E results for both providers before merge.
