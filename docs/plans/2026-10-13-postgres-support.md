# Plan: PostgreSQL support for Ando Server (alongside SQL Server)

- Date: 2026-10-13
- Status: draft v2, reviewed by gpt-6.1-sol (section 9 lists what changed after review)
- File: `docs/plans/2026-10-13-postgres-support.md`

## 1. Goal

Operators can run Ando Server on either Microsoft SQL Server or PostgreSQL. The provider is chosen
by configuration, from one image and one codebase. Both providers get the same level of support:
EF Core migrations, Hangfire job storage, integration and E2E tests, installer, backup and restore,
and docs.

SQL Server stays the default so existing installs are unaffected.

### Non-goals

- Other providers (SQLite, MySQL). The abstraction should not block them, but nothing is built for them.
- The CLI's `EfOperations` (for user projects) are unrelated and unchanged.
- Any .NET version change (see section 3).
- Identity or Octokit refactoring beyond what the provider switch requires.

## 2. Operator experience (product level)

### Configuration

| Setting | Env var | Values | Default |
|---|---|---|---|
| `Database:Provider` | `Database__Provider` | `SqlServer`, `PostgreSql` | `SqlServer` |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | provider-native string | required |

- Unknown provider value fails startup with a clear message listing valid values.
- The connection string is parsed with the selected provider's own connection-string builder
  (`SqlConnectionStringBuilder` / `NpgsqlConnectionStringBuilder`) at startup. A parse failure fails
  startup with the builder's message. No keyword guessing: Npgsql accepts `Server=` as an alias for
  `Host=`, so a heuristic would reject valid strings.
- Startup logs the selected provider and the target host and database (never credentials).
- PostgreSQL TLS: document `SSL Mode=VerifyFull` for external databases and how to mount a CA
  certificate into the container. Npgsql pools by default with a maximum of 100 connections per
  string; EF and Hangfire share one string, so document the budget when running replicas.

### Installer and compose

- `server-install.sh --db sqlserver|postgres` (default `sqlserver`). `--external-sql` becomes
  `--external-db` and keeps `--external-sql` as an alias.
- With `--db postgres` the installer writes an `ando-postgres` service (image `postgres:17`,
  `pg_isready` healthcheck, `pgdata` volume at `/opt/ando/data/pgdata`) instead of `ando-sqlserver`.
  The connection string, `Database__Provider`, and the `depends_on` target are all written for the
  chosen provider; none of them is left pointing at SQL Server.
- Rootless Docker: the installer already `chown`s `sqldata` to the mapped UID of the SQL Server user
  (10001). The Postgres image runs as UID 999; do the same mapping for `pgdata`.
- Backup job: `pg_dump --format=custom` run inside the database container (no client tools in the
  application image), output `ando-<date>.dump`, same retention as today. Document and test the
  matching `pg_restore` path, and note that Data Protection keys live outside the database and must
  be backed up with it for either provider.
- Document PostgreSQL major-version upgrades (dump and restore, since the data directory format changes).
- Local dev: `docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d` and
  `run-dev.sh --db postgres`. An override file, not a profile, because the override must also replace
  the application's connection string, provider, and `depends_on` target. Host ports from the ANDO
  range: `17134` (dev Postgres), `17135` (Testcontainers Postgres). E2E keeps database ports private.
- TrueNAS guide gets a Postgres variant (TrueNAS ships a Postgres app).

### Docs to update

`website/src/content/pages/server.md`, `server-truenas.md`, `website/public/llms.txt`,
`src/Ando.Server/README.md`, `src/Ando.Server/ARCHITECTURE.md`, `AGENTS.md`,
`tests/Ando.Server.Tests/TESTING.md`, `website/src/content/examples/playwright-e2e-tests.md`.

## 3. .NET version: not a prerequisite

.NET 9 and .NET 8 both leave support on 2026-11-10. .NET 10 is the current LTS (to November 2028).
`AGENTS.md` forbids upgrading beyond .NET 9, and this plan respects that.

The reviewer's point, accepted: the upgrade is a separate lifecycle decision and does not block this
work. The 9.x package line covers everything needed (EF Core 9.0.20, Npgsql.EntityFrameworkCore.PostgreSQL
9.0.4, Hangfire.PostgreSql 1.21.1, Testcontainers.PostgreSql 4.16.0). A later EF upgrade keeps existing
migration files; only the snapshot's `ProductVersion` changes. The .NET 10 decision is reported
separately to the maintainer and is not part of this plan.

## 4. Architecture (code level)

### 4.1 Single switch point

- New `Configuration/DatabaseSettings.cs` with a `DatabaseProvider` enum, bound from the `Database` section.
- New `Infrastructure/Database/DatabaseServiceExtensions.cs` with `AddAndoDatabase(this WebApplicationBuilder)`.
  It reads settings, validates, and registers both the EF context and the Hangfire storage for the
  chosen provider. `Program.cs` lines 77-87 and 215-245 collapse into that one call.
- `Services/ConfigurationValidator.cs` validates the provider value and parses the connection string
  with the provider's builder.

### 4.2 DbContext and migration layout

EF Core ties migrations to a context type through the `[DbContext]` attribute, so two migration sets
in one assembly need two context types. This is the documented EF approach for multiple providers.

- `AndoDbContext` stays the single model definition. Nothing that injects it changes. Its constructor
  changes to the non-generic `DbContextOptions` so the subclasses can pass options typed to themselves.
- Add `Data/SqlServerAndoDbContext : AndoDbContext` and `Data/PostgreSqlAndoDbContext : AndoDbContext`.
  They are empty apart from the constructor. Registration uses
  `AddDbContext<AndoDbContext, SqlServerAndoDbContext>(...)` so `AndoDbContext` resolves to the
  provider-specific subclass.
- Move the five existing migrations, their `.Designer.cs` partials, and the snapshot to
  `Migrations/SqlServer/`. Change only the namespace and `[DbContext(typeof(SqlServerAndoDbContext))]`.
  Migration IDs, operations, and the `__EFMigrationsHistory` table name stay the same. Do not regenerate
  anything for SQL Server in this step.
- **Relocation test (SQL Server).** Build a database with the pre-change migration code (checked-in
  baseline of the old migration assembly or a SQL script captured from it), insert representative
  rows (users, roles, projects, secrets, builds, log entries, API tokens, system settings), then run the
  new binary's `Migrate()`. Assert: history table unchanged, row counts and sample values unchanged,
  inserts still work. Cover both the latest migration state and one older state
  (`AddBuildProfile`). Keep this test free of the later `NormalizedName` migration so failures stay
  attributable.
- Keep both snapshots in sync. EF 9 throws at `Migrate()` time when the snapshot lags the model, so
  drift is a startup failure, not a silent one. See CI in 4.9.
- Generate `Migrations/PostgreSql/` as one squashed `InitialCreate`. No PostgreSQL installs exist.
- Add `Data/Design/SqlServerDesignTimeFactory.cs` and `PostgreSqlDesignTimeFactory.cs`
  (`IDesignTimeDbContextFactory<T>`). They read `ANDO_DB_CONNECTION` or fall back to a dummy string;
  snapshot diffing does not need a live database. Today `dotnet ef` boots `Program.cs`, which after
  this change registers only one context.
- Add `scripts/add-migration.sh <Name>` that runs `dotnet ef migrations add` for both contexts with
  the right `--output-dir`, so the two sets cannot diverge by forgetting one.

### 4.3 Provider-conditional model configuration

In `AndoDbContext.OnModelCreating`, branch on `Database.IsSqlServer()` / `Database.IsNpgsql()` only
where needed:

- `HasFilter("[GitHubId] IS NOT NULL")` (line 84): keep on SQL Server; omit on PostgreSQL, where unique
  indexes already allow multiple NULLs. The SQL Server provider adds the same kind of filter to the two
  Identity unique indexes automatically; Npgsql does not, and that is correct.
- PostgreSQL truncates identifiers at 63 characters. Current longest names are under 40. Add a unit
  test that asserts every table, column, index, and constraint name in the model is 63 or fewer so a
  future entity cannot break it silently.

### 4.4 Type mapping decisions

| Type | SQL Server today | PostgreSQL | Decision |
|---|---|---|---|
| `DateTime` | `datetime2`, Kind Unspecified on read | `timestamp with time zone`, requires Kind Utc on write | Keep `timestamptz` and add a model-wide converter (see below). |
| `DateTimeOffset?` (`LockoutEnd`) | `datetimeoffset` | `timestamptz`, offset must be 0 | Written from `DateTimeOffset.UtcNow`. OK. |
| `TimeSpan?` (`Build.Duration`) | `time` (caps at 24h) | `interval` | OK. Only assigned and projected, never aggregated in SQL. |
| `string` | `nvarchar(n/max)` | `text` / `varchar(n)` | Default mapping. |
| int/long identity | `IDENTITY(1,1)` | `GENERATED BY DEFAULT AS IDENTITY` | Default mapping. |
| enums | string via `HasConversion<string>()` | same | Portable already. |
| `bool` | `bit` | `boolean` | Default mapping; add a test for explicit `false` writes against defaults. |

**UTC converter.** Registered in `ConfigureConventions` for `DateTime` and `DateTime?` on both providers:

- Write: `Kind == Utc` passes through. `Kind == Local` is converted with `ToUniversalTime()`.
  `Kind == Unspecified` is tagged as UTC with `SpecifyKind`. This preserves today's SQL Server
  semantics, where an Unspecified value is stored as-is and the code base treats every stored value as
  UTC. The only sources of non-UTC kinds are deserialized DTOs and webhook payloads; JSON timestamps
  with a `Z` or offset deserialize as Utc or Local, and only bare timestamps become Unspecified.
- Read: tag as `Utc`. This is a visible change on SQL Server: serialized timestamps now carry `Z`.
  Verify the React client and API consumers handle that (they should, since today's values are
  already UTC without a marker, so clients that parse them as local time are wrong today).
- Do not use `SpecifyKind` on Local values; it changes the label, not the instant.

Alternative considered: map all `DateTime` to `timestamp without time zone` on PostgreSQL. Rejected
because it hides Kind bugs rather than fixing them and gives different semantics per provider.

### 4.5 Behavioral differences in application code

- **Case sensitivity.** SQL Server's default collation is case-insensitive; PostgreSQL is
  case-sensitive. Affected: secret lookup by name (`Services/ProjectService.cs:173, 203`) and the unique
  index on `(ProjectId, Name)`. Decision: add a `NormalizedName` column with the unique index on it,
  the way Identity does. Deterministic on both providers, index-friendly, and uniqueness is enforced by
  the database on both. Rules:
  - One normalization function in application code (`ToUpperInvariant()`), used for writes and lookups.
  - Migration in three stages: add the column nullable; backfill with `UPPER(Name)` (portable SQL;
    for the ASCII names the DTO allows this equals the invariant uppercase); audit for collisions and
    fail the migration with a clear message if two secrets in one project collapse to the same value;
    then make the column required and create the unique index. Drop the old `(ProjectId, Name)` index.
  - `Name` keeps the original display value.
  - Rollback note for release notes: a binary older than this change cannot insert secrets once the
    column is required. Operators who roll back must restore the database backup taken before upgrade.
  Options rejected: `ToUpper()` in queries (uniqueness still differs per provider), `citext`
  (extension and provider-specific behavior).
  Lower-risk sites: `ApiTokenService.cs:93` (`Prefix` is generated; exact match is fine),
  `DatabaseSeeding.cs:84` (dev seed only), `OrderBy(RepoFullName)` (cosmetic; leave).
- **NUL bytes in text.** PostgreSQL `text` rejects `\0`. `BuildExecution/ServerBuildLogger.cs:149-166`
  stores raw build output. Strip `\0` from log messages and step names before saving, on both providers.
- **No raw SQL exists** in application code today. Add a repo rule (AGENTS.md) that raw SQL must be
  provider-conditional or portable.
- **Seeding.** `DatabaseSeeding` runs after `Migrate()`; keep that order and assert the seeded roles and
  dev data in the provider matrix tests.

### 4.6 Hangfire

- SQL Server: unchanged (`Hangfire.SqlServer`, existing options).
- PostgreSQL: `Hangfire.PostgreSql` 1.21.1 (supports Hangfire 1.8). Register with
  `UsePostgreSqlStorage(o => o.UseNpgsqlConnection(cs), options)` where options set
  `SchemaName = "hangfire"`, `QueuePollInterval`, `InvisibilityTimeout = 5 min`, and
  **`UseSlidingInvisibilityTimeout = true`**. That last one is off by default in the PostgreSQL storage;
  without it a build job longer than five minutes becomes visible again and a second worker picks it
  up while the first is still running. The SQL Server options `UseRecommendedIsolationLevel` and
  `DisableGlobalLocks` have no equivalent.
- Hangfire creates its own schema per provider; the Ando EF migrations do not touch Hangfire tables.
  Document the database permissions needed for schema creation (both EF and Hangfire).
- Testing environment: `Program.cs` currently still binds SQL Server storage in `Testing` using the
  `appsettings.Testing.json` connection string. Replace that with `Hangfire.InMemory` for `Testing`
  and do not start the Hangfire server there, since those tests mock `IBackgroundJobClient` anyway.
- Real-storage test per provider (Testcontainers): two workers, a job that runs longer than two
  renewal intervals (use a short `InvisibilityTimeout` in the test), assert it executes exactly once;
  then restart the server mid-job and assert recovery. The substitutions in Testing and the no-op
  services in E2E cannot catch lease bugs.
- Readiness: the admin health endpoint should distinguish database connectivity from working job
  storage. `JobStorage.Current.GetMonitoringApi()` in `GetSystemHealthEndpoint` is storage-agnostic. OK.

### 4.7 Tests

- Add `TestFixtures/PostgreSqlFixture.cs` using `Testcontainers.PostgreSql` (image `postgres:17`,
  fixed host port 17135). Fixed container names and `WithCleanUp(false)` match the SQL Server fixture
  for local reuse, but CI must set `CLEANUP` so shared runners do not collide.
- Generalize `AndoWebApplicationFactory` to take a provider plus connection string; keep the
  parameterless InMemory path for unit-style tests.
- Run the database-backed integration tests against both providers: two collections (`SqlServer`,
  `PostgreSql`) over a shared abstract test base. CI runs both; locally the
  `ANDO_TEST_DB_PROVIDERS` env var (default `SqlServer,PostgreSql`) selects which containers start; set it to one provider to shorten local runs.
- Migration test per provider: fresh database, `Migrate()`, then assert no pending model changes and
  run a CRUD and constraint pass on the migrated schema (unique secret name per project, cascade
  delete of a project with builds and log entries, nullable timestamps, Identity lookups by normalized
  name, roles, lockout, explicit `false` booleans, PostgreSQL microsecond timestamp precision).
  Today tests use `EnsureCreated` only, so migrations are never exercised. No generic schema-comparison
  tool; the behavioral pass is the check.
- Unit tests for the identifier-length rule (4.3), the UTC converter (4.4, all three Kinds), the
  normalization function (4.5), and NUL stripping.
- E2E: parameterize `global-setup.ts` by provider (compose file, health wait target, container names,
  teardown) and add `tests/docker-compose.test.postgres.yml` with `Database__Provider=PostgreSql` and
  the matching connection string; `npm run test:postgres`. A run must fail if the server reports a
  different provider than requested; add that assertion to global setup via the health endpoint.

### 4.8 Compose, scripts, installer

- `docker-compose.postgres.yml` override: replaces the database service, the application's
  connection string and provider, and `depends_on`. `docker-compose.yml` stays SQL Server.
- `run-dev.sh --db postgres`.
- `website/public/server-install.sh` is the published installer and differs from
  `scripts/install-ando-server.sh` (older, no `--external-sql`). Only the published one should be
  updated; the stale copy should be removed or made a symlink. See unresolved questions.
- Backup and restore as in section 2.

### 4.9 CI

The repo's CI entrypoint is `build.csando` (and `ando ship`). Today it builds and tests the CLI project
and can skip E2E. Add, as mandatory steps: build of `src/Ando.Server` and `tests/Ando.Server.Tests`
(`Ando.sln` does not include them), the server test project for both providers, the migration drift
check for both contexts (`dotnet ef migrations has-pending-model-changes --context <each>`), and both
E2E variants. Containers are cleaned up in CI.

## 5. Implementation steps (ordered)

Use targeted checks per step; run the full provider matrix at the integration points marked (M) and
before handoff.

1. **Packages.** Add `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4, `Hangfire.PostgreSql` 1.21.1,
   `Hangfire.InMemory`, `Testcontainers.PostgreSql` 4.16.0.
2. **Provider setting and single switch point** (4.1). Pure refactor; behavior identical with the
   default.
3. **Hangfire InMemory for Testing** (4.6). Removes the hidden SQL Server dependency before any
   PostgreSQL test runs.
4. **Context split, migration relocation, design-time factories, relocation test, drift check in CI**
   (4.2, 4.9). (M, SQL Server only.) This is the one step that can corrupt production; it ships alone.
5. **Portable model fixes** (4.3, 4.4, 4.5): conditional filter, UTC converter, NUL stripping,
   identifier-length test, `NormalizedName` migration with staged backfill. (M, SQL Server.)
6. **PostgreSQL migrations, fixture, migration and behavioral tests, provider matrix** (4.7).
   Expect findings here; budget time.
7. **Hangfire PostgreSQL storage with lease renewal and the real-storage worker test** (4.6). (M, both.)
8. **Compose override, run-dev, E2E parameterization and PostgreSQL E2E** (4.8, 4.7). (M, both E2E.)
9. **Installer, backup and restore, rootless ownership, docs, llms.txt, AGENTS.md, ARCHITECTURE.md**
   (section 2).
10. **Release notes**: new `Database__Provider` setting, default unchanged, `NormalizedName` rollback
    note, timestamps now serialized with `Z`.

## 6. Risks

- **Snapshot drift** between the two migration sets. Mitigated by the script and the CI drift check;
  EF 9 also fails startup on drift.
- **Existing SQL Server installs** must see migration IDs unchanged after the namespace move. Covered
  by the populated relocation test in step 4; this is the one place a mistake corrupts production.
- **Hangfire.PostgreSql** is community-maintained. Pin the version; the worker test in step 7 is the
  acceptance gate.
- **`NormalizedName` migration** on existing installs: collisions fail the migration by design;
  release notes tell operators to resolve duplicates first. Rollback needs the pre-upgrade backup.
- **Timestamps now carry `Z`** on SQL Server reads. Low risk, but check the client.
- **Test runtime** roughly doubles for integration and E2E when both providers run. Keep local default
  to one provider.

## 7. Unresolved questions

1. Case sensitivity: the plan recommends `NormalizedName`. Confirm, since it changes the schema for
   existing installs and has the rollback caveat.
2. Remove the stale `scripts/install-ando-server.sh` in favor of the published installer?
3. Is a data migration tool between providers wanted later? It affects whether the squashed PG
   `InitialCreate` should mirror SQL Server column types more closely.
4. Should the dev compose default stay SQL Server, or should `run-dev.sh` default to Postgres because
   it is lighter to run locally?

## 8. Effort estimate

Steps 1-7 (core, with the provider test matrix and the Hangfire worker test) are around four to five
focused days. Steps 8-10 (E2E lifecycle, installer branching, rootless permissions, restore testing,
docs) are another three to four days. Total eight to ten focused days, excluding any .NET upgrade.

## 9. Review by gpt-6.1-sol

Run on 2026-10-13 against draft v1 with the server source, tests, compose files, and installer
available to the reviewer. Findings and what changed:

Accepted and applied:

- The v1 UTC converter used `SpecifyKind` on every write, which relabels Local values instead of
  converting them. Fixed in 4.4: Local uses `ToUniversalTime()`, Utc passes through, Unspecified is
  tagged UTC to preserve today's semantics. Added the note that SQL Server reads will now serialize with `Z`.
- The relocation test now uses a populated database built with the pre-change migration code, covers
  an older migration state, and is kept separate from the `NormalizedName` change (4.2).
- `NormalizedName` chosen for case sensitivity, with a single normalization function, staged
  backfill, collision audit, and a rollback note (4.5).
- Two context subclasses and two migration folders in one assembly confirmed (4.2). Constructor
  plumbing for the subclasses added.
- `Hangfire.InMemory` for Testing confirmed and moved to step 3 (4.6, section 5).
- **Hangfire.PostgreSql sliding invisibility timeout is off by default**; enabled explicitly, plus a
  real-storage two-worker test. This was the most valuable finding (4.6).
- Compose: a profile alone would leave the application pointed at SQL Server. Replaced with an
  override file that switches provider, connection string, and `depends_on` together (2, 4.8). E2E
  global setup parameterized and asserts the running provider (4.7).
- Connection-string heuristic cut; validate by parsing with the provider's builder instead (2).
  TLS and pool-size guidance added.
- `.NET 10` dropped as a prerequisite; reported separately (3).
- `GetTables()` schema comparison cut; replaced by a CRUD and constraint pass on migrated schemas (4.7).
- CI entrypoint named (`build.csando`), drift check moved next to the context split, container cleanup
  in CI (4.9).
- Restore path, rootless volume ownership, Postgres major-version upgrades, provider logging, and
  readiness split added (2, 4.6).
- Effort raised to eight to ten days (8).

Not applied:

- "Reject Unspecified-kind values." Throwing inside a value converter would turn a request with a
  bare timestamp into a 500 at save time. Tagging as UTC matches what SQL Server stores today, so the
  behavior is unchanged rather than newly strict. Revisit if a real non-UTC ingestion path appears.

## 10. Release notes draft

**PostgreSQL support for Ando Server.** Ando Server now runs on SQL Server or PostgreSQL from the same image.

- **New setting `Database__Provider`** (`SqlServer` | `PostgreSql`). The default is `SqlServer`, so existing installs need no change. The connection string stays `ConnectionStrings__DefaultConnection` and must match the provider. `GET /health` now reports the provider in a `database` field.
- **Local development:** `docker compose -f docker-compose.yml -f docker-compose.postgres.yml up -d` (PostgreSQL on port 17134) or `./run-dev.sh --db postgres`. `docker compose up -d` and `./run-dev.sh` stay on SQL Server.
- **Installer:** `server-install.sh --db sqlserver|postgres` (default `sqlserver`). `--db postgres` deploys an `ando-postgres` container (`postgres:17`, data in `/opt/ando/data/pgdata`) with daily `pg_dump` backups (`ando-*.dump`, same retention). `--external-sql` is now `--external-db [connection-string]`; `--external-sql` keeps working as an alias. Restore a PostgreSQL dump with `docker exec -i ando-postgres pg_restore -U ando -d <db> --clean --if-exists < file.dump`. Database data directories are now owned by the host IDs read from the `ando` entries in `/etc/subuid` and `/etc/subgid` instead of the fixed `100000` base; the installer stops if the entries are missing or too small.
- **Backups with `--external-db`:** the daily backup job is now installed for external databases too. It archives `config/` (`.env` with `Encryption__Key`, `github-app.pem`) and the Data Protection keys and skips the database dump; back up the external database with its own tooling. Project secrets cannot be decrypted without `Encryption__Key`, so keep the config archive with every database backup.
- **Secret names (schema change):** new or changed secret names must match `^[A-Za-z_][A-Za-z0-9_]*$` and be at most 100 characters; existing names are kept and stay deletable by their exact stored name (they cannot be updated in place). Project secrets gain a `NormalizedName` column (binary collation on SQL Server) with a unique index per project, so names are case-insensitive on both providers. On existing SQL Server installs the upgrade backfills it with a collation-independent uppercase. If two secrets in one project differ only by case, the migration fails with SQL error 50001 and rolls back completely; delete one of them (before upgrading, or by starting the previous image again) and retry. **Rollback:** an older server image cannot insert new secrets into the upgraded schema. To roll back after a successful upgrade, restore the database backup taken before the upgrade.
- **CI gates:** `build.csando` always runs the server unit tests (`Category!=Integration`, including migration drift); with Docker it runs the integration tests (`Category=Integration`, both providers) and both E2E variants. The `publish` profile fails if Docker is unavailable.
- **Timestamps:** API timestamps are now always UTC with a trailing `Z` (for example `2026-10-13T08:30:00Z`), on SQL Server too. Clients that parsed the old offset-less values as local time now get the correct instant.
- **Moving data:** `dotnet Ando.Server.dll export --output <file>` and `import --input <file> [--force]` move all Ando data to another instance of either provider (see section 13).

## 11. Implementation review ledger (gpt-6.1-sol, extra-high effort)

Raw report: `docs/plans/2026-10-13-postgres-support.impl-review-gpt-6.1-sol.md` (reviewed the full
uncommitted change set on 2026-10-13). A focused re-review of the fixes followed; see section 12.

| # | Finding | Status | What changed |
|---|---|---|---|
| 1 | Blocker: `UPPER(Name)` in the backfill follows the database collation, so Turkish collations normalize `i` differently from `ToUpperInvariant()`. | Applied | Backfill uses `UPPER([Name] COLLATE Latin1_General_100_CI_AS)`; `NormalizedName` has `Latin1_General_100_BIN2` collation on SQL Server (binary index and lookups); secret names are validated as environment-variable names (`^[A-Za-z_][A-Za-z0-9_]*$`, max 100) at both endpoints and in the service; migration regenerated as `20261013075421_AddProjectSecretNormalizedName`; Turkish-collation upgrade tests added. |
| 2 | No populated-upgrade or collision-rollback test for the staged migration. | Applied | Tests on a populated pre-change database: backfill and index swap; collision on `Latin1_General_CS_AS` rolls back completely (column absent, old index present, history unchanged, rows intact). |
| 3 | Hangfire restart test could pass without interrupting a running job. | Applied | Waits throw on timeout; the test asserts one start and no completion before stopping the first server, then a second start and completion. |
| 4 | Lease tests did not guard the production Hangfire options. | Applied | `HangfireStorageOptionsFactory` builds the production options for both storages; tests override timings only; a unit test pins `UseSlidingInvisibilityTimeout = true` and schema `hangfire`. |
| 5 | Fixtures with fixed names and ports can destroy a concurrent run's database. | Applied | `ANDO_TEST_DB_CONTAINERS=isolated` (unique names, random ports, cleanup) is the default when `CI` or `ANDO_HOST_ROOT` is set; `shared` stays the default on developer machines. |
| 6 | Installer hardcodes rootless UID/GID offsets. | Applied | The installer reads the user's `/etc/subuid` and `/etc/subgid` entries and computes `base + N - 1` for both the PostgreSQL (999) and SQL Server (10001) branches, failing clearly if the entry or range is missing. |
| 7 | Server and drift gates in `build.csando` were bypassable. | Applied, with a limit | Unit and drift tests (`Category!=Integration`) always run; integration tests and both E2E variants run under the Docker gate; the `publish` profile fails without Docker. The script cannot set environment variables for `Dotnet.Test`, so the provider matrix relies on the test default (both providers) and the container mode on the CI default. |
| 8 | External-database installs got no config or key backups, and the docs described archives that did not exist. | Applied | The backup job is installed for external databases and archives config and Data Protection keys while skipping the dump; docs describe the archives per setup and the restore order. |
| 9 | `add-migration.sh` could leave a partial migration pair silently. | Applied | On PostgreSQL scaffolding failure the script lists the SQL Server files created and the removal command, and exits non-zero. |

Additional changes made during the fix pass: the React secret form now honors a `success: false`
response and shows the server's message; the client health types carry `key` and `provider`.

## 12. Re-review ledger (gpt-6.1-sol, extra-high effort, focused on the fixes)

Raw report: `docs/plans/2026-10-13-postgres-support.impl-rereview-gpt-6.1-sol.md`. Findings 2, 3, 4, 5,
6, 8, 9 and the client changes were confirmed resolved. Residual points and what changed:

| Point | Status | What changed |
|---|---|---|
| Finding 1 residual: legacy names with non-ASCII characters could get a `NormalizedName` from SQL `UPPER` that differs from `ToUpperInvariant()`, so a later delete by name might miss the row. | Applied | Secret lookups in `ProjectService` match the normalized name or the exact stored name, preferring the exact match. Legacy secrets stay deletable; they cannot be updated in place (the naming rule rejects the name), which the docs now say. Two unit tests cover the divergent-normalization and precedence cases. The migration does not reject legacy names, so upgrades are never blocked by old data except for true case collisions. |
| Finding 7 residual: a `.env.ando` value of `ANDO_TEST_DB_PROVIDERS` could narrow the release matrix; the first E2E step inherited `E2E_DB_PROVIDER`. | Applied | `build.csando` fails a `publish` build when the variable is set to anything but both providers, and the first E2E step now calls the explicit `test:sqlserver` script. |
| New: the ANDO build container does not forward `CI`/`ANDO_HOST_ROOT`, so fixtures could fall back to shared mode on CI. | Applied | Isolated container mode is also the default whenever the test process runs inside a container (`/.dockerenv`), so builds executed by Ando.Server use it without configuration. |
| Docs overstated agreement for retained legacy names; "cannot write secrets" was too broad. | Applied | Docs now say legacy names are kept, deletable by exact name, not updatable in place; rollback wording says older images cannot insert new secrets. |

Not applied: rejecting unsupported legacy names in the migration (would block upgrades on data that
is otherwise harmless; the exact-name fallback makes such rows addressable instead).

## 13. Data export/import

Built after the provider work so operators can move an instance to a new host or switch provider.

- **CLI mode of the server binary** (`DataPortability/`): `Program.cs` hands `args[0]` of `export`
  or `import` to `PortabilityCli` before the web host is built. It reads the same configuration as
  the server (`Database__Provider`, connection string, `Encryption__Key`) and registers only the
  DbContext and the encryption service: no Kestrel, Hangfire or startup migrations.
- **Format:** JSON Lines. Line 1 is a manifest (`format`, `formatVersion`, app version, source
  provider, last migration, provider-neutral model fingerprint, export time); then
  `{"table":...,"row":{...}}` lines grouped by table in foreign-key order; the last line is
  `{"table":"__end","rowCounts":{...}}`. A file without it is refused as truncated. The export
  writes `<file>.partial` and renames it on success.
- **Import:** requires a migrated target with the same model fingerprint (same Ando version) and no
  users, projects or builds unless `--force`. One transaction deletes existing rows, inserts with
  explicit keys (`IDENTITY_INSERT` on SQL Server; sequences moved past the maximum on
  PostgreSQL), restores values that EF would otherwise replace with a store default (a project's
  `TimeoutMinutes = 0`; audited by a unit test over every `HasDefaultValue` column), checks row
  counts and decrypts every project secret. Any failure rolls back.
- **Not exported:** the Hangfire queue (Queued/Running builds become Cancelled), files on disk
  (artifacts, repos, Data Protection keys, `github-app.pem`, `.env`).
- **Precision:** SQL Server to PostgreSQL drops the seventh decimal of timestamps (100 ns to 1 µs).
- **Tests:** same-provider and cross-provider round trips (every column of every row hashed, IDs and
  gaps preserved, identity seeds moved), refusal paths (non-empty target, fingerprint mismatch,
  truncated file, wrong encryption key, invalid value) with rollback checks.
- **Operator docs:** website `server.md` section "Moving an Instance (Export and Import)".
