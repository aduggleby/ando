---
title: Ando
description: Build configuration, artifacts, nested builds and the step model (Block, Defer, When, Require, Confirm, Report).
provider: Ando
---

The `Ando` object holds build configuration, artifact handling, nested builds and the execution-time step model. Bare globals such as `Root`, `Temp`, `Env()`, `Directory()`, `DefineProfile()` and `Log` are **not** members of `Ando`; they are on the [Globals](/providers/globals) page.

## Build Configuration

Build configuration, artifact copying, and nested builds. Use these to set the Docker image, copy outputs to host, and run child build scripts.

`Ando.Build` is the exception to "steps run in the container": its step runs **on the host** and starts a separate child `ando` build process, which creates its own container (own environment file, own context). The parent container does not run the child build.

```csharp
// ando-doc-snippet-test
// Set the Docker image for the build container
Ando.UseImage("mcr.microsoft.com/dotnet/sdk:9.0");

// Copy artifacts from container to host after build
Ando.CopyArtifactsToHost("dist", "./dist");

// Copy as compressed archive (faster for many small files)
Ando.CopyZippedArtifactsToHost("dist", "./output");  // Creates ./output/artifacts.tar.gz
Ando.CopyZippedArtifactsToHost("dist", "./dist/binaries.tar.gz");  // Specific filename
Ando.CopyZippedArtifactsToHost("dist", "./dist/binaries.zip");     // Zip format

// Run a child build in a subdirectory
Ando.Build(Directory("./website"));

// Run a specific build file
Ando.Build(Directory("./website") / "deploy.csando");

// Child build with Docker-in-Docker enabled
Ando.Build(Directory("./integration-tests"), o => o.WithDind());

// Child build with a variable that exists in the child process only
Ando.Build(Directory("./website"), o => o.WithEnv("CLOUDFLARE_API_TOKEN", Env("CLOUDFLARE_API_TOKEN")));
```

The child is the same CLI that runs the parent, so `./ando-dev` starts a source build and not the `ando` on PATH. A child inherits `--yes` and `--no-env`. When the parent runs with `--ref`, the child gets the same `--ref` and runs from the matching folder under the directory you ran from, not under the temporary worktree. Its artifacts, env file and run record then stay in the project. Without `--ref`, nothing changes.

`WithEnv(name, value, secret = false)` sets an environment variable in the child process only, never in the parent's. It can be repeated. When you use it, the child forwards exactly those variables into its containers instead of the variable names it inherits from the parent. This also holds with `--no-env`. The value is redacted in both builds when the name looks secret (TOKEN, PASSWORD, KEY and similar) or you pass `secret: true`.

After the child exits, the parent's run record gets an entry `child record <path>` (the child's path relative to the directory you ran from, for example `child record website/build.csando`; ` #2`, ` #3` when the same path is built again) with the path of the child's run record. It is missing when the child wrote no record.

## Step tree of another script

`Ando.StepTree(file, profiles)` returns the `--dry-run` tree of another script as the step's `Value`: one line per step, two spaces of indent per level, `\n` line endings, secrets redacted. It starts the running CLI on the host with `run --dry-run --no-env --no-color -f <file>` (plus `-p <profiles>`, and `--ref` when this run has one), from the directory you ran from, so the script's steps never run. A relative `file` is relative to that directory. A non-zero exit fails the step. It returns a `CommandStep`, so `WithTimeout`, `WithEnv` and `AllowFailure` work. The dry-run child inherits the parent's whole process environment (plus any `WithEnv` values). This is unlike `Ando.Build` with `WithEnv`, whose containers get only the `WithEnv` keys. Read the value at execution time.

```csharp
var tree = Ando.StepTree("deploy.csando", "eu");
Ando.Report("deploy tree", tree);
```

## Options Reference

### Build Options

| Option                          | Description                                                                                                                                                                                |
| ------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `WithVerbosity(string)`         | Set output verbosity level. Values: "Quiet" (errors only), "Minimal" (warnings and errors), "Normal" (default), "Detailed" (debug output).                                                 |
| `ColdStart(bool)`               | Force a fresh container for this build, ignoring any warm container cache. Use when you need a clean environment or are debugging container state issues.                                  |
| `WithDind(bool)`                | Enable Docker-in-Docker mode. Required when the child build needs to run Docker commands (e.g., building images, running docker-compose). Mounts the Docker socket into the container.     |
| `WithImage(string)`             | Override the Docker image for this child build. Use when the child build requires different tools than the parent (e.g., Node.js build in a .NET project).                                 |
| `WithEnv(string, string, bool)` | Set an environment variable in the child process only (repeatable). The child forwards exactly these variables into its containers. Redacted when the name looks secret or `secret: true`. |

## Step model

Steps register while the script is evaluated and run afterwards, in order, stopping at the first failure. C# `try`/`finally`, `if` and `foreach` around step calls therefore run at evaluation time, not at execution time, and a step's output does not exist yet. These operations on `Ando` give you the same structure at execution time.

| Need                                                           | Use                                                      |
| -------------------------------------------------------------- | -------------------------------------------------------- |
| Group steps, handle failure, clean up                          | `Ando.Block(name, body).OnFailure(...).Finally(...)`     |
| Steps that always run at the end of the enclosing block or run | `Ando.Finally(body)`                                     |
| Run C# at execution time, then the steps it registers          | `Ando.Defer(body)`                                       |
| Run steps only if a runtime condition holds                    | `Ando.When(condition, then, otherwise?)`                 |
| Fail the build if a runtime condition is false                 | `Ando.Require(condition, message)`                       |
| Ask before continuing                                          | `Ando.Confirm(string \| Func<string>)`                   |
| Record a value in the run record                               | `Ando.Report(key, string \| StepOutput \| Func<string>)` |
| Default timeout for command steps                              | `Ando.CommandTimeout(TimeSpan)`                          |
| Tell a Ctrl+C from a failure in `OnFailure`/`Finally`          | `Ando.Cancelled` (read at execution time)                |

### Ordering on failure and cancellation

- A failing step stops its scope (the run, a block or a deferred body).
- For a failed block, the `OnFailure` steps run first, then the `Finally` steps. The block still fails, and the failure moves to the enclosing scope.
- With nested blocks the order is: inner `OnFailure`, inner `Finally`, outer `OnFailure`, outer `Finally`.
- `Finally` steps of a scope run last-registered first, and every one runs even if an earlier one failed. Within one `Finally` lambda, all steps run even if one fails.
- Within one `OnFailure` lambda, the first failing step stops the remaining steps of that lambda. Other `OnFailure` calls on the same block still run.
- Finally steps belong to the enclosing `Ando.Block` (or the run). `Defer` and `When` bodies are transparent: a finally step registered inside one, including the removal registered by `AppService.TemporaryAccessRule`, runs when the enclosing block ends, not when the body ends.
- Ctrl+C: the first Ctrl+C stops the running step (it is marked cancelled), stops any command it left running in the build container, then runs the on-failure and finally steps. Commands started after the first press ignore further Ctrl+C presses, so a second press only prints a warning. A third press exits immediately (exit code 130) and skips the remaining cleanup; within 5 seconds it still tries to remove the `--ref` worktree and writes the run record with `"aborted": true` (for warm containers the temporary Azure profile is removed at the start of the next run; `--cold` removes it with the container). Any run interrupted with Ctrl+C exits with 130 (a failure exits with 1). `OnFailure` steps run after Ctrl+C as well; read `Ando.Cancelled` to tell the two apart.

### Ando.Cancelled

`Ando.Cancelled` is a `bool` that is `true` once the user pressed Ctrl+C. Read it at execution time, inside `Ando.When`, `Ando.Require`, `Ando.Defer`, `OnFailure` and `Finally` bodies. While the script is evaluated it is always `false`. `OnFailure` steps run after Ctrl+C too, so use it to tell a cancellation from a failure:

```csharp
Ando.Block("deploy", () => { /* ... */ })
    .OnFailure(() => Ando.When(() => !Ando.Cancelled, () => AppService.SwapSlots(app, "staging", rg)));
```

### Reading step output

`Shell.Run`, `Shell.Bash`, `Azure.Cli`, `Git.RevParse`, `Git.LatestTag`, `Git.ChangedFiles`, `AppService.SwapSlots`, `AppService.Stop`, `AppService.Start`, `AppService.Restart`, `AppService.Show`, `AppService.GetHostName` and `AppService.AccessRestrictions` (and the same operations on `Functions`) return a `CommandStep`. `Http.Get`, `Http.PublicIpV4` and `Azure.Vm.RunCommand` return a `StepOutput`. Both have:

- `Value`: trimmed stdout, or the response body for HTTP steps.
- `ExitCode`: exit code of a command step.
- `StatusCode`: HTTP status code for HTTP steps; throws for other steps.
- `Json(path)`: a dotted path with optional array indexes, such as `siteConfig.ipSecurityRestrictions[0].name`. Property names match case-insensitively. Returns the string value, the raw JSON for objects and arrays, or `null` when the path is missing or the value is JSON null.

Reading any of these before the step ran throws. Read them inside an execution-time lambda (`Defer`, `When`, `Require`, `Report`, or a `Func<>` overload such as `Http.WaitForHealthy(() => ...)`).

`CommandStep` adds fluent configuration that takes effect when the step runs: `InDirectory(path)`, `WithTimeout(TimeSpan)`, `WithEnv(name, value)`, `AllowFailure()` and `Quiet()`.

### Example: slot deploy with gates and swap-back

```csharp
// ando-doc-snippet-test
// Run with: ando run --ref v1.4.0 --yes
var app = "my-web-app";
var rg = "my-rg";

Ando.UseImage("mcr.microsoft.com/dotnet/sdk:10.0");

// Evaluation-time checks: fail before any step runs.
Git.RequireClean("src", "web");
var manifest = Ando.ImportArtifacts("artifacts");

Azure.EnsureAuthenticated();

var releaseHost = AppService.GetHostName(app, rg, "release");
var prodHost = AppService.GetHostName(app, rg);

Ando.Block("deploy", () =>
{
    // Removed again when this block ends, whatever happened.
    AppService.TemporaryAccessRule(app, rg, "release", "deploy-gate");

    AppService.DeployFolder(app, manifest.PathOf("web"), rg, o => o.WithDeploymentSlot("release"));

    Http.WaitForHealthy(
        () => new[] { $"https://{releaseHost.Value}/healthz", $"https://{releaseHost.Value}/healthz/db" },
        o => o.ConsecutiveSuccesses(3).MinGapSeconds(10).ExpectJson("version", manifest.Version));

    Ando.Confirm("Swap release into production?");

    Ando.Block("swap", () =>
    {
        AppService.SwapSlots(app, "release", rg);
        Http.WaitForHealthy(() => $"https://{prodHost.Value}/healthz", o => o.ConsecutiveSuccesses(2));
    })
    .OnFailure(() => AppService.SwapSlots(app, "release", rg)); // swap back
})
.Finally(() => AppService.Stop(app, rg, "release"));

Ando.Report("version", manifest.Version);
```

If the post-swap health check fails, the swap-back runs first (inner `OnFailure`), then the outer `Finally` steps in reverse registration order: the `release` slot is stopped, then the `deploy-gate` rule is removed and its removal verified. The build still fails.

## Build once, deploy the same bytes

`Ando.WriteManifest(directory, version, commit)` registers a step that writes `manifest.json` into a container directory with the version, commit, and size and SHA-256 of every file. Copy the directory to the host with `Ando.CopyArtifactsToHost`. In a later run, `Ando.ImportArtifacts(hostDirectory, containerDirectory = null)` reads the manifest while the script is evaluated and verifies every file's hash on the host. Missing, changed or extra files throw before any step runs. A registered step then copies the directory into the container and verifies the hashes again there.

`ImportArtifacts` returns an `ArtifactManifest`:

| Member               | Description                                                                        |
| -------------------- | ---------------------------------------------------------------------------------- |
| `Version`, `Commit`  | Values recorded by the build.                                                      |
| `Files`              | Every file with `Path`, `Size` and `Sha256`.                                       |
| `File(pathOrName)`   | Finds a file by relative path, or by file name when unique. Throws if not found.   |
| `PathOf(pathOrName)` | Container path of a file, or of a directory that contains files (`PathOf("web")`). |
| `ContainerPath`      | Container path of the imported directory.                                          |

Relative host paths are relative to the directory ANDO was run from. The default container directory is the same relative path under `/workspace`.

```csharp
// ando-doc-snippet-test
// Build run
Ando.WriteManifest(Root / "artifacts", "1.4.0", Git.Commit);
Ando.CopyArtifactsToHost("artifacts", "./artifacts");
```

## Host files and the invocation directory

`Ando.CopyToContainer(hostPath, containerPath)` copies a host file or directory into the build container. Relative host paths are relative to the directory ANDO was run from, also with `ando run --ref`, so gitignored files such as local certificates can be used. Relative container paths are relative to `/workspace`. A directory replaces an existing directory at the container path. `Ando.InvocationRoot` is that host directory as a string, for reading local files while the script is evaluated. `Ando.SourceRoot` is the host directory of the source tree being built: the temporary worktree with `--ref`, otherwise the project directory.

```csharp
Ando.CopyToContainer(".run/seal-1.pem", ".run/seal-1.pem");
```

## Copying from the container in step order

`Ando.CopyToHost(containerPath, hostPath)` is the mirror of `Ando.CopyToContainer`: it copies a file or directory from the build container to the host as a step, in order with the other steps. Relative host paths are relative to the directory ANDO was run from (also with `--ref`). The existing destination is deleted first, and the project folder and its parents are refused. Any error fails the step. `Ando.CopyArtifactsToHost` copies after the last step instead; a failed copy there makes the run exit with 1.

```csharp
Ando.CopyToHost("artifacts/manifest.json", "artifacts/manifest.json");
```

## Locks

Each run takes a lock on its build container, so a second run of the same script fails at once with exit code 9 (see [Run locks](/cli#run-locks)). `Ando.Lock("name")` takes a named lock while the script is evaluated and holds it until the run ends, for work that must not overlap, such as a release. It is not a step, so it is not in `--dry-run` output, and `--dry-run` takes no lock. Call it at the top level of the script: inside `Ando.Defer` it runs at execution time, where a held lock fails that step (exit code 1, not 9). Names that map to the same lock file (`a/b` and `a_b`) are one lock. Child builds skip the names their parent holds. The run record lists it as `lock <name>`. It uses its own lock files under `~/.ando/locks` (`name-<name>.lock`), so it does not exclude other tools or scripts that `flock` a different file on the same target. Use one locking mechanism per target.

```csharp
Ando.Lock("release");
```

## Run record

Every `ando run` writes `.ando/runs/<utc-time>.json` in the directory you ran from. It lists the steps that ran, including steps inside blocks and on-failure and finally steps, with `depth`, `cleanup`, start and end time, result and error, plus the values added with `Ando.Report`. `.ando/runs/` has its own `.gitignore`. Secret values are redacted: environment variables whose names contain SECRET, PASSWORD, TOKEN, KEY and similar words are replaced with `[REDACTED]` in console output, the build log and run records.
