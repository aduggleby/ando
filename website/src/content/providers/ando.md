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
```

## Options Reference

### Build Options

| Option                  | Description                                                                                                                                                                            |
| ----------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WithVerbosity(string)` | Set output verbosity level. Values: "Quiet" (errors only), "Minimal" (warnings and errors), "Normal" (default), "Detailed" (debug output).                                             |
| `ColdStart(bool)`       | Force a fresh container for this build, ignoring any warm container cache. Use when you need a clean environment or are debugging container state issues.                              |
| `WithDind(bool)`        | Enable Docker-in-Docker mode. Required when the child build needs to run Docker commands (e.g., building images, running docker-compose). Mounts the Docker socket into the container. |
| `WithImage(string)`     | Override the Docker image for this child build. Use when the child build requires different tools than the parent (e.g., Node.js build in a .NET project).                             |

## Step model

Steps register while the script is evaluated and run afterwards, in order, stopping at the first failure. C# `try`/`finally`, `if` and `foreach` around step calls therefore run at evaluation time, not at execution time, and a step's output does not exist yet. These operations on `Ando` give you the same structure at execution time.

| Need                                                           | Use                                                      |
| -------------------------------------------------------------- | -------------------------------------------------------- |
| Group steps, handle failure, clean up                          | `Ando.Block(name, body).OnFailure(...).Finally(...)`     |
| Steps that always run at the end of the enclosing block or run | `Ando.Finally(body)`                                     |
| Run C# at execution time, then the steps it registers          | `Ando.Defer(body)`                                       |
| Run steps only if a runtime condition holds                    | `Ando.When(condition, then, otherwise?)`                 |
| Fail the build if a runtime condition is false                 | `Ando.Require(condition, message)`                       |
| Ask before continuing                                          | `Ando.Confirm(message)`                                  |
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

## Run record

Every `ando run` writes `.ando/runs/<utc-time>.json` in the directory you ran from. It lists the steps that ran, including steps inside blocks and on-failure and finally steps, with `depth`, `cleanup`, start and end time, result and error, plus the values added with `Ando.Report`. `.ando/runs/` has its own `.gitignore`. Secret values are redacted: environment variables whose names contain SECRET, PASSWORD, TOKEN, KEY and similar words are replaced with `[REDACTED]` in console output, the build log and run records.
