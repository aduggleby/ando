# SimpleSign365 feature requests (G1 to G10): overview for Mr. Ando

Status: analysis only, nothing implemented. Requested by the SimpleSign365 session (simplesign365-2f) for the owner on 2026-11-19.
Input: `~/Source/SimpleSign365/docs/plans/2026-11-19-ando-deployment-analysis.md` (section 4 holds the requests and API sketches).
Basis: the ANDO source at `364af4d` (0.9.164; no `src/Ando` change after the tag). Every claim below comes from reading the source, not from running it. Points marked **confirm at implementation** depend on external tool behavior (`az`, `dotnet ef`, .NET file locks, sqlcmd) that I did not check.

## Summary

| G | Request | Exists today? | Fits the design? | Effort | Risk | Release |
|---|---|---|---|---|---|---|
| G1 | Secret sources, scoped env | Partly: values are already redacted by value; missing are a user-secrets source and named-key loading, and every env-file key goes to every container command | Yes, as an opt-in source (`Secrets.*`) | M | Medium (redaction gaps, Cloudflare ops read env) | A |
| G2 | Run lock | No. Two runs on one warm container already break each other's Azure profile | Yes, but as an automatic per-container lock plus an optional named lock; it cannot be a step | S–M | Low | A |
| G3 | Failed artifact copy fails the run; in-order `Ando.CopyToHost` | No; failures only log | Yes. The `Sha256Of` helper is not needed | S | Medium: changes the exit code for every repo that uses `CopyArtifactsToHost` | A |
| G4 | Host steps (`Host.Run`) | The plumbing exists (host `git` steps), the operation does not | Breaks "containers by default". **Defer**: G5, G6 and G2 cover the named uses | S to build, high cost to the design | High (design) | Later, only if a real need remains |
| G5 | Tag message at execution time, `PushTag`, `RemoteTags` | No (`WithMessage(string)`, `PushTags` only, `ChangedFiles(string)`) | Yes. Version ordering (`Below`) stays in the script | S | Low | A |
| G6 | `AfterRun`, `StepTree` | No. The record path is only in a log line | Partly. First a machine-readable record path; `StepTree` as a child `ando run --dry-run`, not in-process; `AfterRun` without `Host.Run` | S (path, StepTree), M (AfterRun) | Low / Medium | A (record path), B (rest) |
| G7 | Child build with ref and explicit env | Partly: under `--ref` the child already runs in the worktree, but without the env file and its artifacts are deleted with the worktree | Yes, but child should **inherit** the parent's ref by default instead of `WithRef` | S–M | Medium: changes `Ando.Build` under `--ref` and `--no-env` | B |
| G8 | Bicep validate, structured what-if, several parameter files, secure parameters | No | Yes. `copyFromHost` not needed (`Ando.CopyToContainer` exists) | M | Medium (az output format) | C |
| G9 | `Ef.Script` with `to`; `Sql.*` with an Entra token | No | `to`: yes, trivial. `Sql.*`: defer until the owner decides on migrations | XS / L | Low / High | `to` in A; `Sql.*` deferred |
| G10 | `Cloudflare.EnsureDnsRecord`, `KeyVault.EnsureSecret` | No | Yes (curl and az in the container, like `PurgeCache`) | M each | Medium | Deferred (operator-only today) |

Release grouping: **A** = 0.9.165 (G1, G2, G3, G5, G9 `to`, G6.1 record path file), **B** = next (G6.2 `StepTree`, G7, optionally G6.3), **C** = Bicep (G8). G4, G9 `Sql.*` and G10 wait for a concrete need or the owner's policy decisions.

No proposal renames or changes the context of an existing step, so `scripts/release/deploy-tree*.txt` changes only where SimpleSign365 adopts a new operation. The two behavior changes that hit existing users are G3 (exit code) and G7 (`Ando.Build` under `--ref`/`--no-env`); details in "Breaking changes".

## Corrections to the SimpleSign365 analysis

- Hooks (`ando-pre*.csando`, `ando-post*.csando`) never run for `ando run`; only `bump`, `release`, `docs` and `commit` call `HookRunner` (`Cli/Commands/*Command.cs`). Section 2 of the analysis does not claim otherwise, but hooks are not a route to G6.
- `Git.Tag` has no `AtCommit`. It tags `HEAD` of the source root, which under `--ref` is the worktree at the ref (`Operations/GitOperations.cs:329-357`, `HostGitExecutor` at `:56`). The G5 sketch works without it.
- Child build folder under `--ref` (the analysis left this open): `hostRootPath` is the worktree (`Cli/AndoCli.cs:510-514`, `defaultHostRoot` from `scriptPath`, which is the worktree script at `:386`), and `TranslateContainerToHostPath` maps `/workspace/...` onto it (`Scripting/BuildContext.cs:139-163`). So `Ando.Build(Directory("."))` already runs the child **in the worktree**, at the tagged commit, but: no `.env.ando` there (gitignored), the child's artifacts and run record land in the worktree and are deleted with it, and the child gets a different container name (named after the worktree path, `AndoCli.cs:563-565`).
- The child inherits the parent's whole process environment (`Execution/ProcessRunner.cs:46-48` only adds `options.Environment` to the inherited environment), including `ANDO_LOADED_ENV_KEYS`, and `BuildArgs()` forwards neither `--ref` nor `--yes` (`Operations/AndoBuildOptions.cs`). A child with an `Ando.Confirm` inside a `--yes` release run would fail without a console.
- Version statement confirmed: `git diff --stat v0.9.161..HEAD -- src/Ando` is 6 files, 48 lines.

## Per feature

### G1. Secret sources and env scoping

**Today.**
- Redaction is already by value: `SecretValues` holds values; names only decide which env values get registered (`Logging/SecretValues.cs:30-63`, `AndoCli.cs:413`). Operations that take a secret register it (`AzureOperations.cs:204,309`). Console and `build.csando.log` redact every line (`ConsoleLogger.cs:111`, `:204-244`), and so does the run record (`RunRecordWriter.cs:103`). **Gap:** `--dry-run` prints the tree with `Console.WriteLine` (`AndoCli.cs:474-480`) and `StepTreePrinter` does not redact, so a secret that ends up in a step context (for example a `Shell.Bash` first line) is printed by `--dry-run`. Route the tree through `SecretValues.Redact`; this is part of G1 and a precondition for G6.2.
- Gaps: `Env("X")` does not register its value (`Scripting/ScriptGlobals.cs:212`), so `Env("CLOUDFLARE_ACCOUNT_ID")`-style values with non-secret names are printed. Values shorter than 6 characters are never redacted.
- Every key loaded from the env file goes to **every** `docker exec` (`Execution/ContainerExecutor.cs:148`). That is the scoping problem.
- `Cloudflare.*` reads `$CLOUDFLARE_API_TOKEN` and `$CLOUDFLARE_ACCOUNT_ID` from the container env (`CloudflareOperations.cs:262,351,363`), so scoping must stay opt-in, or those operations need an explicit token.

**Proposal (slight change to the sketch).**
```csharp
var sp = Secrets.UserSecrets("simplesign365-dev");                 // reads secrets.json on the host at evaluation; every value registered for redaction
var cf = Secrets.EnvFile(".env.ando", "CLOUDFLARE_API_TOKEN", "CLOUDFLARE_ACCOUNT_ID");   // named keys only, never forwarded by itself
Azure.EnsureAuthenticated(clientId: sp["Deploy:Azure:ProdClientId"], clientSecret: sp["Deploy:Azure:ProdClientSecret"], ...);
Cloudflare.PagesDeploy(dist, o => o.WithToken(cf["CLOUDFLARE_API_TOKEN"]).WithAccountId(cf["CLOUDFLARE_ACCOUNT_ID"]));  // per-command env only
```
- `Secrets.*` returns plain strings that are registered with `SecretValues` (no new `Secret` type; every API already takes `string`). A missing key fails at evaluation.
- User secrets: read `~/.microsoft/usersecrets/<id>/secrets.json` (Linux/macOS) or `%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json` directly, without `dotnet` (path **confirm at implementation**). Accept either the `UserSecretsId` or a project path that has one.
- Scoping: values from `Secrets.EnvFile` are never added to `ANDO_LOADED_ENV_KEYS`; they reach a command only as per-command env (`CommandOptions.Environment`, already passed by name, `ContainerExecutor.cs:143-180`). `readEnv`/`--read-env` stay as they are. This needs `WithToken`/`WithAccountId` on the Cloudflare operations (shared with G10).
- Also: make `Env()` register values whose name looks secret, or add `Env(name, secret: true)`.
- Design: reading a named user-secrets store is the same class of host read as `.env.ando`; it does not touch `~/.azure`.

**Effort** M. **Risk** medium: a missed redaction path leaks a secret; add tests that run a value through console, log file and run record. `Azure.Login.ServicePrincipal` keeps its name, so the deploy tree does not change.

### G2. Run lock

**Today.** No lock. Two runs of the same script share a warm container. At start, every run deletes all `ando-azure-*` profiles in it (`AndoCli.cs:617`), so a second run logs the first one out mid-deploy. `--cold` removes the container of a running run (`AndoCli.cs:587-590`).

**Proposal.**
- **Automatic container lock (new default):** before step 3 (`AndoCli.cs:515`), take an exclusive, non-blocking lock on `~/.ando/locks/<container-name>.lock`. If it is held, fail with exit code 9 and "another ando run uses container X". This fixes the `:617` problem for everyone. With `--ref` the container is named after the invocation root, so a `--ref` run and a plain run of the same script exclude each other, which is right.
- **Named lock in the script:** `Ando.Lock("ssg-release")` is called at evaluation, takes the lock immediately and holds it until the process exits. It is **not a step**: a step runs too late (after `--cold` removal). It does not appear in the step tree. It adds `lock: <name>` to the run record.
- **`--dry-run`** takes no lock, because SimpleSign365 runs `--dry-run` while holding the release lock.
- **Child builds:** the parent passes the named locks it holds in `ANDO_HELD_LOCKS`, and a child skips those (otherwise `Ando.Lock` in a shared helper would block itself). The container name is `ando-<folder name>-<script hash>` (`AndoCli.cs:1020-1031`), so `Ando.Build(Directory("./website"))` gets its own container and its own lock. A child that runs the **same script from the same folder** (another profile of the parent's script) shares the parent's container today, and its start (`:617`) wipes the parent's Azure profile. With the lock it fails at once with a clear message instead. That is the rule: one run per container, children included.
- **Ando Server:** the server runs `ando run` inside a per-build container (`src/Ando.Server/BuildExecution/BuildOrchestrator.RepositoryAndPaths.cs:276-290`), so each run has its own `~/.ando/locks`. The lock neither blocks nor protects server builds. Two concurrent server builds of one project can still pick the same inner container name on the shared Docker host; that is a separate, existing issue (a Docker-level guard such as a container label would cover it).
- **Mechanism:** a `FileStream` with `FileShare.None` on Linux takes `flock(LOCK_EX|LOCK_NB)`, and .NET opens files with `O_CLOEXEC`, so children (docker, git, ando) do not inherit it (the `flock -o` problem does not arise). **Confirm at implementation** on Linux and macOS, and add a test with two processes.
- A `wait:` option is not needed for SimpleSign365 (`flock -n` today). Leave it out.

**Effort** S–M. **Risk** low; the automatic lock can surprise someone who runs the same script twice in parallel on purpose. That was already unsafe. Server builds are unaffected (see above).

### G3. Artifact copy failures fail the run; in-order copy

**Today.** Confirmed: refused destination (`Operations/ArtifactOperations.cs:155`), failed delete (`:182`), failed `docker cp` (`:195`) and the zipped variants (`:222-263`) only log. `AndoCli.cs:651-660` returns 0.

**Proposal.**
- Make `CopyToHostAsync`/`CopyZippedToHostAsync` return a failure. The run exits 1 and the record gets an `artifact-copy-failed` entry.
- Add a step `Ando.CopyToHost(containerPath, hostPath)`, the mirror of `Ando.CopyToContainer`. It runs in order, fails the step on any error, and uses the same root rules (invocation root under `--ref`, `BuildContext.cs:204`).
- Push back on `copied.Sha256Of(...)`: the hash is already available in the container: `var hash = Shell.Bash("sha256sum artifacts/manifest.json | cut -d' ' -f1");` gives a `StepOutput`. Together with G5 `WithMessage(Func<string>)` that covers the tag.

**Effort** S. **Risk** medium for others: six repos call `CopyArtifactsToHost`/`CopyZippedArtifactsToHost` (ando, SimpleSign365, hawk, agora, Pyrite, SimpleWebsite365). Any of them that copies over a root-owned file today exits 0 and will exit 1. That is the intended fix, but it goes in the changelog as a behavior change. No step tree change (end-of-run copies are not steps).

### G4. Host steps (`Host.Run`)

**Today.** Host commands exist only inside operations: `Git.*` through `HostGitExecutor` (`GitOperations.cs:56`), registered with `RegisterCommandStep` (`ShellCommandOperations.cs:55`). These get timeouts, logging and redaction, and a step entry in the record. So the plumbing for `Host.Run` already exists.

**The uses the analysis names, and what covers them instead:**

| Use | Covered by |
|---|---|
| `git ls-remote` for the previous release | G5 `Git.RemoteTags` |
| `docker ps` for leftover containers | G2 container lock; `--cold` removal after a third Ctrl+C (`AndoCli.cs:139`) |
| Report script after the run | G6 record path (wrapper) or `AfterRun` |
| `ando run --dry-run` for the tree | G6 `Ando.StepTree` |
| `.pending` file and manifest scan in `deploy-region.sh` | C# in an `Ando.Defer` body (runs on the host at execution time and can read and write host files today, but is not a logged step), or `Ando.CopyToHost`/`CopyToContainer` |

**Recommendation: defer.** Every named use has a narrower answer. `Host.Run` would make "the build runs in a container" depend on per-repo discipline. If a need remains after A and B, add it with an `ando.config` allowlist (`"hostCommands": ["bash scripts/release-report.sh"]`), matching the whole command, not only the program name.

### G5. Tag handling

**Today.**
- `GitTagOptions.WithMessage(string)` (`GitOperations.cs:456`). Tags are annotated by default.
- `PushTags` pushes every local tag the remote lacks (`:381-420`). Six repos (ando, alexandria, octoporty, Pyrite, selfmx, Tongs) use `Git.Tag` plus `PushTags`; `PushTags` stays as it is.
- `ChangedFiles(string sinceRef, ...)` takes the ref at evaluation (`:281`), so it cannot use a tag found at execution time.

**Proposal.**
```csharp
Git.Tag($"v{version}", o => o.WithMessage(() => $"v{version}\n\nmanifest-sha256: {hash.Value}"));   // Func overload
Git.PushTag($"v{version}");                                          // pushes refs/tags/<name> only; fails if missing locally
var remote = Git.RemoteTags("v*");                                   // StepOutput, newline list from `git ls-remote --tags --refs origin`
var changed = Git.ChangedFiles(() => previous, "infra");             // Func<string> overload
```
- Push back on `.Below(version, VersionScheme.YearMinorPatch)`. Ordering of tags is release policy; ANDO has no year-minor-patch scheme. The script computes `previous` in `Ando.Defer` from `remote.Value` with a few lines of C#.
- `PushTags` stays unchanged.
- Step names are new (`Git.PushTag`, `Git.RemoteTags`), so existing trees are unaffected.

**Effort** S. **Risk** low.

### G6. Work after the run record exists; step tree as a value

**Today.** The record path appears only as the `Run record: <path>` log line (`Workflow/RunRecordWriter.cs:84`). `--dry-run` prints `StepTreePrinter.Render` output to stdout and exits (`AndoCli.cs:474-482`).

**Proposal, in order of value per effort.**
1. **Machine-readable record path (S):** `ando run --record-path-file <file>` writes the path there. This replaces scraping today and does not depend on anything else.
2. **`Ando.StepTree(file, profiles)` (S–M):** a step that spawns `ando run --dry-run -f <file> -p <profiles> --no-color` on the host as a child process, like `Ando.Build`. Its `Value` is the tree lines without the header.
   - Under `--ref` the child runs from the invocation root with the parent's `--ref`, because the imported artifacts live there (same rule as G7).
   - Not in-process: evaluating a second script would share static `SecretValues`, the process env, cwd changes (`AndoCli.cs:441-457`) and evaluation side effects (`Git.RequireClean`, `ImportArtifacts` reading the host).
   - Normalization (hashes, versions, paths) stays in the script; it is SimpleSign365-specific. It must run after `Ando.CopyToHost` (G3), because the deploy tree reads the artifacts at evaluation.
3. **`Ando.AfterRun(Action<RunRecordInfo>)` (M):** C# on the host after the record is written. A thrown exception makes the exit code 1, and ANDO rewrites the record with an `afterRun` failure entry.
   - It does **not** run steps: the container is still up, but the workflow is over.
   - The analysis's sketch calls `Host.Run` inside it, so as written it depends on G4.
   - Recommendation: ship (1) and (2), keep the report in the wrapper, and decide on (3) after seeing whether the wrapper is still a burden.

**Risk** low for (1) and (2); medium for (3), because rewriting the record changes a file other tools read.

### G7. Child builds with ref and env

**Today.** See "Corrections": under `--ref` the child runs in the worktree, but with no env file, and its outputs are deleted with the worktree. `--no-env` is forwarded; `--ref` and `--yes` are not; the whole parent environment is inherited.

**Proposal.**
- When the parent runs with `--ref`, the child runs from the matching folder **under the invocation root** with `--ref <same commit>`. Its artifacts and record then land in the project, and it builds the same commit. No `WithRef` needed; add `WithRef(string)` only for a different commit (YAGNI until asked).
- Forward `--yes` when the parent has it.
- `WithEnv(name, value)`: the value is set in the child's process env, added to its `ANDO_LOADED_ENV_KEYS`, and registered for redaction. **Only when `WithEnv` is used**, the child's `ANDO_LOADED_ENV_KEYS` is reset to exactly those keys, so `--no-env` plus `WithEnv` gives exactly the keys named. Without `WithEnv` the inherited keys stay as today, so the website children of ando and selfmx keep working.
- Add the child's record path to the parent's record (uses G6.1).

**Effort** S–M. **Risk** medium: ando's own `build.csando` and selfmx call `Ando.Build(Directory("./website"))`. Neither runs under `--ref` today as far as their scripts show, but the move from the worktree to the invocation root changes where a `--ref` child writes. Changelog it.

### G8. Bicep

**Today.**
- One parameter file (`BicepOperations.cs:135-138`). Inline parameters go to `az` argv (`:141-144`), which `docker exec` puts on the host command line (`ContainerExecutor.cs:166-168`; only env is passed by name).
- `WhatIf` runs `az deployment group create --what-if` with `--query properties.outputs -o json` appended and returns nothing (`:93-107`). No validate.

**Proposal.**
```csharp
var o = (BicepDeployOptions x) => x.WithParameterFile("infra/params/frontdoor.parameters.json")
                                   .WithParameterFile(".run/frontdoor-edge.json")          // after Ando.CopyToContainer
                                   .WithSecureParameter("edgeKey", key);                   // temp 0600 JSON in the container, @file, deleted after
Bicep.Validate(rg, "infra/frontDoor.bicep", o);
var plan = Bicep.WhatIf(rg, "infra/frontDoor.bicep", o);   // now returns BicepWhatIfResult: Changes (ResourceId, ChangeType), Summary
Ando.Require(() => plan.Changes.All(c => ...), "...");
```
- `WithParameterFile` becomes additive. Calling it twice currently keeps the last file, so a script that relied on override would change; no `.csando` under `~/Source` calls `WithParameterFile` or `Bicep.WhatIf`.
- `WhatIf` switches to `az deployment group what-if --no-pretty-print -o json` and parses `changes[].changeType`/`resourceId`. It still prints a readable summary. The flags and JSON shape are to **confirm at implementation**.
- The return type changes from `void` to a result. That is source-compatible for scripts that ignore it.
- Step name `Bicep.WhatIf` and its context (resource group) stay.
- Push back on `copyFromHost: true`: `Ando.CopyToContainer` already copies a host file in (`AndoOperations.cs:463`).
- `Ando.Confirm(Func<string>)` is needed for `$"Deploy ({plan.Summary})?"`; today `Confirm` takes a string only (`:382`). Small; ship with this.
- Subscription-scope `WhatIf`/`Validate` belong in the same change.

**Effort** M. **Risk** medium: parsing `az` output across versions. Pin-test against the `az` version `Azure.InstallCli` installs.

### G9. Migration helpers

- `Ef.Script(context, output, fromMigration, toMigration)`: append `to` after `from` (`EfOperations.cs:85-92`). With `from` empty and `to` set, `dotnet ef` needs `0` as the `from` (**confirm at implementation**). Add a `Func<string>` overload for an execution-time `from`. Effort XS. Ship in A; it helps option A of the analysis (scripts in the build, applied by hand).
- `Sql.Query`/`Sql.RunScript` with an Entra token: a new operation family, a new tool in the image (sqlcmd/go-sqlcmd; its auth modes are to **confirm at implementation**), and it only makes sense after the owner's migration policy decision (analysis 5.3, options B or C). **Defer.**

### G10. Cloudflare DNS, Key Vault secrets

Both fit: `Cloudflare.EnsureDnsRecord` as curl plus jq in the container like `PurgeCache` (`CloudflareOperations.cs:253-262`), and `Azure.KeyVault.EnsureSecret` with a value generated in the container and passed by file or stdin, never argv. Both run as the operator, which the analysis keeps outside ANDO runs (R13 rejected, single identity per run). **Defer** until the operator wants a scripted runbook; then build the explicit-token Cloudflare option from G1 first.

## Dependencies

- G3 `Ando.CopyToHost` → G6.2 `StepTree` (the tree reads artifacts at evaluation).
- G3 (`Shell.Bash` hash) and G5 `WithMessage(Func)` together → tag with the manifest hash.
- G1 explicit Cloudflare token → G7 `WithEnv` (or the child uses `Secrets.EnvFile` itself) and → G10.
- G6.1 record path → G7 (child record linked) and → `AfterRun`.
- G6.3 as sketched → G4; without G4 it is C# only.
- G8 `Confirm(Func<string>)` is independent and small.
- G9 `Sql.*` → owner decision (analysis 5.3).

## Suggested releases

| Release | Contents | Why together |
|---|---|---|
| A (0.9.165) | G2 (container lock + `Ando.Lock`), G3 (fail + `Ando.CopyToHost`), G5 (`WithMessage(Func)`, `PushTag`, `RemoteTags`, `ChangedFiles(Func)`), G1 (`Secrets.UserSecrets`, `Secrets.EnvFile`, `Env` redaction, Cloudflare explicit token), G9 `Ef.Script` `to`, G6.1 record path file | Matches the analysis's step 2 ("G1, G2, G3, G5 together, one drill"); all small and independent of az output formats |
| B | G6.2 `StepTree`, G7 (ref inheritance, `--yes`, `WithEnv`, child record link), optionally G6.3 `AfterRun` | Removes `release.sh` down to the report |
| C | G8 (validate, structured what-if, several and secure parameters, `Confirm(Func<string>)`) | Needs its own drill against a throwaway resource group (VM deletion) |
| Deferred | G4, G9 `Sql.*`, G10 | Need a concrete remaining use or a policy decision |

If A is too large for one drill, split G1 into its own release; it carries the most redaction risk.

## Breaking changes and step tree stability

- **Step tree:** every new operation adds new step names (`Ando.CopyToHost`, `Git.PushTag`, `Git.RemoteTags`, `Ando.StepTree`, `Bicep.Validate`). `Ando.Lock`, the container lock, `Secrets.*` and `AfterRun` are not steps. No existing step name or context changes; `Bicep.WhatIf` keeps name and context, `Azure.Login.ServicePrincipal` stays. SimpleSign365's `deploy-tree*.txt` therefore changes only where its scripts adopt something.
- **Behavior changes for all users:**
  - G3: a failed end-of-run copy now exits 1 (six repos use it).
  - G2: a second parallel run of the same script fails at once instead of breaking the first.
  - G7: under `--ref`, child builds move from the worktree to the invocation root; `--yes` is forwarded.
  - G8: repeated `WithParameterFile` becomes additive (no known user).
  - G1: `--dry-run` output gets redacted (only visible where a secret was in a step context).
- **Exit codes:** G2 adds one (proposed 9). Wrappers that map exit codes should know.

## Decisions (Mr. Ando, 2026-11-19)

1. Run lock: on by default (automatic per-container lock, plus `Ando.Lock("name")`).
2. G3: ship as a patch-level bug fix, no flag; a failed copy exits 1.
3. G4 `Host.Run`: deferred; build the narrower operations first.
4. G7: child builds inherit the parent's `--ref` (and `--yes`) automatically; no `WithRef` for now.

## Status

- Release A shipped in 0.9.165 (G1, G2, G3, G5, G6.1, G9 `Ef.Script` `to`).
- Releases B and C shipped in 0.9.166 (G6.2 `Ando.StepTree`, G7, `Ando.Confirm(Func<string>)`, G8). G7 adds no `WithRef`; children inherit the parent's ref.
- Still deferred: G6.3 `Ando.AfterRun`, G4 `Host.Run`, G9 `Sql.*`, G10.

## Open questions for Mr. Ando (answered above, kept for context)

1. Automatic per-container lock as a default (recommended), or only the opt-in `Ando.Lock`?
2. G3: ship the exit-code change in a patch release (recommended, it is a bug fix), or behind a flag first?
3. G4 `Host.Run`: agree to defer?
4. G7: should a child under `--ref` inherit the ref automatically (recommended) or only with `WithRef`?
5. Is the R1–R17 list from 2026-11-06 kept anywhere? Neither repository has it, and the 2026-11-05 report that defined it is missing too. SimpleSign365 confirms that most of R1 to R17 shipped in 0.9.153 to 0.9.161. The only known rejections are R6 (parallel regions), R11 (typed blob upload) and R13 (named identities), and this overview checks only against those.

## SimpleSign365 response (2026-11-19)

The SimpleSign365 session accepted the corrections (its commit `adbc5475`) and agreed to:
- dropping `.Below()`, `Sha256Of` and `copyFromHost`;
- the breaking changes in G3 (exit code) and G7 (`--ref` children run from the invocation root).

Its website child needs the env scoping from G1/G7 either way. It needs nothing further; implementation is the owner's decision.

## Verification

- Read-only: no code changed in ANDO or SimpleSign365, nothing built, run or deployed.
- File and line references are against commit `364af4d`.
- Usage counts in other repos come from `grep` over `~/Source/*/*.csando` and `~/Source/*/*/*.csando` (one pattern at a time).
- The R1 to R17 list was searched for in this repository's docs and `AGENTS.md`; it is not there.
