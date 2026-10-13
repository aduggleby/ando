Mr. Ando, the [plan](/home/alex/Source/ando/docs/plans/2026-10-13-postgres-support.md) has a sound direction, but I would address several correctness and verification gaps before implementation.

## 1. Correctness risks for existing SQL Server installs

**Moving migrations is safe only if their identities and discovery metadata remain intact.** Namespace changes do not require new migration IDs. Move each migration together with its designer partial, update all context attributes and the snapshot, and preserve existing SQL Server operations, migration IDs, and the history table’s name and schema. Do not regenerate the existing migration history.

The upgrade test should create a **populated database using the pre-change migration code**, then migrate it using the new context. Assert unchanged history, preserved records, and working inserts. Cover an older supported migration state as well as the latest one. The relocation should be a no-op; the later `NormalizedName` migration legitimately changes the schema. Keep snapshots synchronized: EF 9 can throw during startup migration when it detects pending model changes. [EF 9 migration behavior](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes)

**The proposed write converter is incorrect.** `SpecifyKind` changes the label, not the instant: 14:00 at UTC+02 becomes 14:00Z instead of 12:00Z. Preserve UTC values, convert Local values with `ToUniversalTime()`, and reject Unspecified values unless the ingestion boundary explicitly establishes UTC semantics. Tagging known UTC SQL Server reads is appropriate. Existing `UtcNow` writes remain safe. [SpecifyKind semantics](https://learn.microsoft.com/en-us/dotnet/api/system.datetime.specifykind?view=net-9.0)

Returning UTC kinds also changes serialized SQL Server timestamps to include `Z`; verify API and browser behavior. Adding a required normalized column can prevent older binaries from writing secrets, so document rollback compatibility.

The context split needs constructor plumbing: the current base accepts options typed to `AndoDbContext`, while registration creates options typed to each subclass.

## 2. The four open design decisions

**a. Choose `timestamptz` with a strict model-wide UTC converter.** These fields represent instants, and the application already writes UTC. This gives useful enforcement and consistent read semantics. `timestamp without time zone` would leave timezone assumptions implicit. Fix the converter as described above. [Npgsql timestamp mapping](https://www.npgsql.org/doc/types/datetime.html)

**b. Choose `NormalizedName`.** It provides indexed lookup and database-enforced uniqueness on both providers. Query-side uppercase comparison leaves uniqueness inconsistent; `citext` adds an extension and provider-specific behavior.

Define one normalization algorithm for writes, lookups, and backfill. Database `UPPER()` is not automatically equivalent to invariant application casing. Preserve the original display name, audit collisions before creating the unique index, and stage the backfill before making the column required. Do not assume all existing names obey the DTO’s uppercase-ASCII annotation.

**c. Choose two subclasses and two migration folders in one assembly.** This preserves one shared model and application-facing context without adding migration projects and publishing dependencies. Separate assemblies are valid but unnecessary here. A single portable migration set would require extensive branching around provider-generated types, identities, and annotations. The proposed layout follows an established EF approach. [Multiple-provider migrations](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/providers)

**d. Choose Hangfire.InMemory for ordinary Testing hosts.** Those tests already mock job submission; real storage adds unrelated infrastructure dependencies. Disable background workers where clients are mocked. Add a separate real-storage test suite for each provider—the substitutions cannot validate PostgreSQL Hangfire.

## 3. Anything missing from the plan

- **Hangfire lease renewal is a release blocker.** PostgreSQL storage defaults sliding invisibility off. With the proposed five-minute timeout, a longer build becomes eligible for redelivery while still executing. Explicitly enable `UseSlidingInvisibilityTimeout`. Test two workers, a job spanning several renewal intervals, and restart recovery against real storage. Current Testing substitutions and E2E stubs would miss this. [Pinned storage options](https://raw.githubusercontent.com/hangfire-postgres/Hangfire.PostgreSql/1.21.1/src/Hangfire.PostgreSql/PostgreSqlStorageOptions.cs), [dequeue behavior](https://raw.githubusercontent.com/hangfire-postgres/Hangfire.PostgreSql/1.21.1/src/Hangfire.PostgreSql/PostgreSqlJobQueue.cs)

- **Compose selection is incomplete.** Adding a PostgreSQL profile leaves the unprofiled SQL Server service enabled, the application’s SQL connection string unchanged, and its dependency pointing at SQL Server. Select provider, connection string, and healthy database dependency together. For E2E, parameterize setup, health checks, base URL, teardown, and container names; adding a compose file and package command alone can produce a PostgreSQL-labelled run against SQL Server.

- **Connection policy needs specifics.** Parse strings with the selected provider’s connection-string builder. Npgsql accepts `Server` as a `Host` alias, so the proposed mismatch example rejects valid configuration. Document external TLS with `SSL Mode=VerifyFull` and certificate mounting where needed. Pooling defaults on with a maximum of 100 connections; budget connections across EF, Hangfire, and replicas. [Accepted aliases](https://raw.githubusercontent.com/npgsql/npgsql/v9.0.4/src/Npgsql/NpgsqlConnectionStringBuilder.cs), [TLS defaults](https://www.npgsql.org/doc/security.html), [pool defaults](https://www.npgsql.org/doc/connection-string-parameters)

- **Behavioral parity needs explicit acceptance tests.** Exercise Identity normalized lookups, roles and lockout; nullable timestamps; PostgreSQL timestamp precision; boolean defaults, especially explicit `false`; cascade deletes; and secret uniqueness. Preserve post-migration seeding and verify its results. NUL sanitization should cover both log messages and step names.

- **CI needs the actual entrypoint.** Root `build.csando` currently tests the CLI project and can skip E2E. Name the changes that make server integration tests, both migration drift checks, and both E2E variants mandatory. Clean up containers in CI; copying `WithCleanUp(false)` and fixed names creates shared-runner conflicts.

- **Operational support needs restoration and observability.** Specify database provisioning and permissions for EF and Hangfire schema creation. Test `pg_restore`, preserve encryption configuration and Data Protection keys, and document PostgreSQL major-version upgrades. Installer work must handle PostgreSQL volume ownership under rootless Docker. Log the selected provider and migration failures without credentials; readiness should distinguish database connectivity from functioning job storage.

## 4. Anything unnecessary or over-engineered

Cut the **.NET 10 prerequisite**. Repository rules require .NET 9, and the supplied 9.x packages support this feature. Handle lifecycle maintenance separately. A later EF upgrade does not inherently require regenerating the PostgreSQL migration history.

Cut **connection-string auto-detection and keyword guessing**. Keep explicit provider selection with SQL Server as the default.

Cut **schema-equivalence checking through `GetTables()` model metadata**. It compares model descriptions, not the physical databases created by migrations. Prefer migration-created databases plus meaningful CRUD and constraint tests over building a general schema-comparison framework.

Avoid full build/test/E2E runs after every small step. Use targeted checks, then the complete provider matrix at meaningful integration points and before handoff.

Keep database ports private in E2E. Do not add PostgreSQL client tools to the application image when backup tools can run in the database container. Avoid unrelated Identity or Octokit refactoring.

## 5. Implementation ordering and effort estimate

The migration relocation before PostgreSQL scaffolding is sensible. However, move **Testing Hangfire isolation before PostgreSQL integration tests**, and implement real PostgreSQL Hangfire storage before claiming provider support.

Introduce CI drift checks alongside the context split, and provider automation alongside the tests—not near the end. Keep the SQL Server relocation test separate from subsequent schema changes so failures remain attributable.

**One week is optimistic for the stated operational parity.** It is plausible for the core implementation, but installer branching, rootless permissions, restoration, E2E lifecycle changes, and long-running-job verification deserve more time. I would budget **8–10 focused days**, excluding any framework upgrade.

## Top 5 changes I would make to this plan

1. Correct UTC conversion and define handling of ambiguous timestamps.
2. Strengthen populated SQL Server upgrade and rollback verification.
3. Enable PostgreSQL Hangfire lease renewal and test real workers.
4. Complete Compose/E2E provider selection and wire the actual CI entrypoint.
5. Remove the .NET upgrade prerequisite and add a tested restoration path.
