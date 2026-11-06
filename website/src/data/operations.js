// =============================================================================
// operations.js
//
// Summary: Shared operations data for all provider pages.
//
// This file contains all ANDO operations organized by provider. Each provider
// page imports this data and filters to show only its operations.
//
// Structure:
// - Each operation has: group (provider), name, desc, examples, sourceFile
// - Providers are derived from the group field
// - sourceFile is the path to the C# source file (relative to src/Ando/)
// =============================================================================

// GitHub repository URL for source links
export const GITHUB_REPO = "https://github.com/aduggleby/ando";

// Generate GitHub source URL from relative path
export function getSourceUrl(sourceFile) {
  return `${GITHUB_REPO}/blob/main/src/Ando/${sourceFile}`;
}

export const operations = [
  // Top-level operations (globals available in build.csando)
  {
    group: "Ando",
    name: "Root",
    desc: 'The root path of the project (where build.csando is located). Returns <code>BuildPath</code>, not <code>DirectoryRef</code>. Use for path construction and string arguments. For operations requiring <code>DirectoryRef</code> (Npm, Cloudflare, etc.), use <code>Directory(".")</code> instead. Supports path combining with the <code>/</code> operator.',
    examples: ['var output = Root / "dist";', "Dotnet.Publish(app, o => o.Output(output));"],
    sourceFile: "Scripting/ScriptGlobals.cs",
  },
  {
    group: "Ando",
    name: "Temp",
    desc: "Temporary files directory (root/.ando/tmp). Supports path combining with the `/` operator. Use for caches, intermediate files, etc.",
    examples: ['var cache = Temp / "cache";', 'var intermediate = Temp / "build-output";'],
    sourceFile: "Scripting/ScriptGlobals.cs",
  },
  {
    group: "Ando",
    name: "Env",
    desc: "Gets an environment variable. <strong>Global function</strong> — call as <code>Env()</code>, not <code>Ando.Env()</code>. By default throws if not set. Pass <code>required: false</code> to return null. The second parameter is a <code>bool</code>, not a default value — use null-coalescing (<code>??</code>) for defaults.",
    examples: [
      'var apiKey = Env("API_KEY");',
      'var optional = Env("OPTIONAL_VAR", required: false);',
      'var lang = Env("SITE_LANG", required: false) ?? "en";',
    ],
    sourceFile: "Scripting/ScriptGlobals.cs",
  },
  {
    group: "Ando",
    name: "Directory",
    desc: 'Creates a <code>DirectoryRef</code> — required by operations that need a working directory (Npm, Cloudflare, Playwright, Ando.Build). <strong>Global function</strong> — call as <code>Directory()</code>. Note: <code>Root</code> returns <code>BuildPath</code>, not <code>DirectoryRef</code>. Use <code>Directory(".")</code> when an operation needs a reference to the project root. Supports path combining with the <code>/</code> operator.',
    examples: [
      'var frontend = Directory("./frontend");',
      "Npm.Ci(frontend);",
      'Cloudflare.PagesDeploy(frontend / "dist", "my-site");',
      'Npm.Ci(Directory(".")); // Use Directory(".") instead of Root',
    ],
    sourceFile: "Scripting/ScriptGlobals.cs",
  },
  // DefineProfile (before Ando.* operations)
  {
    group: "Ando",
    name: "DefineProfile",
    desc: "Defines a build profile that can be activated via CLI. Takes a <strong>single argument</strong> (the profile name). Returns a <code>Profile</code> object with implicit <code>bool</code> conversion — use directly in <code>if</code> statements. There is no <code>HasProfile()</code> or <code>Profile()</code> function. Use <code>-p</code> or <code>--profile</code> CLI flag to activate.",
    examples: [
      'var release = DefineProfile("release");\nvar publish = DefineProfile("publish");\n\nDotnet.Build(app);\n\nif (release) {\n  Git.Tag("v1.0.0");\n  GitHub.CreateRelease(o => o.WithTag("v1.0.0"));\n}',
      "// CLI usage:\n// ando -p release\n// ando -p publish,release",
    ],
    sourceFile: "Scripting/ScriptGlobals.cs",
  },
  // Build configuration operations (top-level)
  {
    group: "Ando",
    name: "UseImage",
    desc: "Set the Docker image for the current build container. Must be called before build steps execute.",
    examples: [
      'Ando.UseImage("ubuntu:24.04");',
      'Ando.UseImage("mcr.microsoft.com/dotnet/sdk:9.0");',
      'Ando.UseImage("node:22");',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "CopyArtifactsToHost",
    desc: "Register files to copy from the container to the host after the build completes. The first parameter is the path inside the container (relative to /workspace or absolute), and the second is the destination on the host (relative to project root or absolute).",
    examples: ['Ando.CopyArtifactsToHost("dist", "./dist");', 'Ando.CopyArtifactsToHost("bin/Release", "./output");'],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "CopyZippedArtifactsToHost",
    desc: "Register files to be archived and copied from the container to the host after the build completes. Creates a single archive file for faster transfer of many small files. Supports `.tar.gz` (default) and `.zip` formats. If the destination is a directory, creates `artifacts.tar.gz` in that directory.",
    examples: [
      'Ando.CopyZippedArtifactsToHost("dist", "./output");',
      'Ando.CopyZippedArtifactsToHost("dist", "./dist/binaries.tar.gz");',
      'Ando.CopyZippedArtifactsToHost("dist", "./dist/binaries.zip");',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Build",
    desc: "Run a nested build script. Accepts a directory (runs build.csando in that directory) or a specific .csando file path. The child build runs in its own isolated container with its own environment file (`.env.ando` or `.env`) and context.",
    examples: [
      'Ando.Build(Directory("./website"));',
      'Ando.Build(Directory("./website") / "deploy.csando");',
      'Ando.Build(Directory("./api"), o => o.WithDind());',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.Block",
    desc: "Groups steps into a named block with optional failure and cleanup steps. The body runs at evaluation time to capture its steps; they run in order later. <code>.OnFailure(Action)</code> steps run only if a block step failed or the run was canceled (may be called more than once). <code>.Finally(Action)</code> steps always run. On failure the order is inner OnFailure, inner Finally, outer OnFailure, outer Finally. The block still fails the build after its OnFailure steps ran. C# <code>try</code>/<code>finally</code> around steps does not work, because steps run after the script is evaluated.",
    examples: [
      'Ando.Block("deploy", () =>\n{\n  AppService.DeployFolder("my-app", "dist/api", "my-rg", o => o.WithDeploymentSlot("release"));\n  AppService.SwapSlots("my-app", "release", "my-rg");\n})\n.OnFailure(() => AppService.SwapSlots("my-app", "release", "my-rg"))\n.Finally(() => AppService.Stop("my-app", "my-rg", "release"));',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.Finally",
    desc: "Registers steps that always run at the end of the enclosing block, or at the end of the run at top level: after success, after a failure and after Ctrl+C. Finally steps run last-registered first and every one runs, even if an earlier one failed. All steps inside one Finally lambda run even if one fails. A Finally registered inside <code>Ando.Defer</code> or <code>Ando.When</code> runs when that body ends.",
    examples: ['Ando.Finally(() => AppService.Stop("my-app", "my-rg", "release"));'],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.Defer",
    desc: "Runs the lambda at execution time, when the steps before it have run, then runs the steps it registers. Use it to pass step outputs to later steps or to use plain C# <code>if</code>/<code>foreach</code> on runtime values.",
    examples: [
      'var host = AppService.GetHostName("my-app", "my-rg", "release");\nAndo.Defer(() => Http.WaitForHealthy($"https://{host.Value}/healthz"));',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.When",
    desc: "Runs the steps of <code>then</code> only if the condition is true at execution time, otherwise the steps of the optional <code>otherwise</code>. The condition can read step outputs.",
    examples: [
      'var rules = AppService.AccessRestrictions("my-app", "my-rg", "release");\nAndo.When(\n  () => rules.Json("ipSecurityRestrictionsDefaultAction") == "Deny",\n  () => Log.Info("Slot restricts access"),\n  () => Log.Info("Slot is open"));',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.Require",
    desc: "Fails the build with the message if the condition is false at execution time.",
    examples: [
      'var pending = Shell.Bash("./scripts/pending-migrations.sh");\nAndo.Require(() => pending.Value == "", "Database has pending migrations");',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.Cancelled",
    desc: "A <code>bool</code> that is <code>true</code> once the user pressed Ctrl+C. Read it at execution time, inside <code>Ando.When</code>, <code>Ando.Require</code>, <code>Ando.Defer</code>, <code>OnFailure</code> and <code>Finally</code> bodies; while the script is evaluated it is always <code>false</code>. <code>OnFailure</code> steps run after Ctrl+C too, so use it to tell a cancellation from a failure.",
    examples: [
      'Ando.Block("deploy", () => { /* ... */ })\n  .OnFailure(() => Ando.When(() => !Ando.Cancelled, () => AppService.SwapSlots("my-app", "staging", "my-rg")));',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.Confirm",
    desc: "Asks <code>[y/N]</code> before the build continues. Declining fails the build. <code>ando run --yes</code> accepts every confirmation. Without <code>--yes</code>, a run with no interactive console fails at this step.",
    examples: ['Ando.Confirm("Swap release into production?");'],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.Report",
    desc: "Adds a value to the run record (<code>.ando/runs/*.json</code>) when the step runs, and logs it. Overloads take a string, a <code>StepOutput</code> or a <code>Func&lt;string&gt;</code> evaluated at execution time. Secret values are redacted.",
    examples: [
      'var health = Http.Get("https://app.example.com/healthz");\nAndo.Report("healthz", health);\nAndo.Report("commit", Git.Commit);',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.CommandTimeout",
    desc: "Sets the timeout for command steps registered after this call (built-in default 5 minutes). Operations with their own longer default, such as AppService zip deploy, keep it. Use a step's <code>WithTimeout</code> for one step only.",
    examples: ["Ando.CommandTimeout(TimeSpan.FromMinutes(15));"],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.CopyToContainer",
    desc: "Copies a host file or directory into the build container. Relative host paths are relative to the directory ANDO was run from (also with <code>--ref</code>), so gitignored files such as local certificates can be used. Relative container paths are relative to <code>/workspace</code>. A directory replaces an existing directory at the container path.",
    examples: ['Ando.CopyToContainer(".run/seal-1.pem", ".run/seal-1.pem");'],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.InvocationRoot",
    desc: "Host directory ANDO was run from (a string). Use it to read local, for example gitignored, files while the script is evaluated, also with <code>--ref</code>.",
    examples: ['var local = Path.Combine(Ando.InvocationRoot, ".run", "settings.json");'],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.WriteManifest",
    desc: "Registers a step that writes <code>manifest.json</code> into a container directory with the version, commit, and size and SHA-256 of every file. Copy the directory to the host with <code>Ando.CopyArtifactsToHost</code> and deploy it in a later run with <code>Ando.ImportArtifacts</code>.",
    examples: [
      'Ando.WriteManifest(Root / "artifacts", "1.4.0", Git.Commit);\nAndo.CopyArtifactsToHost("artifacts", "./artifacts");',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  {
    group: "Ando",
    name: "Ando.ImportArtifacts",
    desc: "Imports a build's artifacts for deployment and returns an <code>ArtifactManifest</code> with <code>Version</code>, <code>Commit</code>, <code>Files</code> (<code>Path</code>, <code>Size</code>, <code>Sha256</code>), <code>File(pathOrName)</code>, <code>PathOf(pathOrName)</code> and <code>ContainerPath</code>. While the script is evaluated, ANDO verifies every file's SHA-256 on the host (missing, changed or extra files throw before any step runs). A registered step copies the directory into the container and verifies the hashes again. Relative host paths are relative to the directory ANDO was run from. The default container directory is the same relative path under <code>/workspace</code>.",
    examples: [
      'var m = Ando.ImportArtifacts("artifacts");\nAppService.DeployFolder("my-app", m.PathOf("web"), "my-rg");\nLog.Info($"Deploying {m.Version} ({m.Commit})");',
    ],
    sourceFile: "Operations/AndoOperations.cs",
  },
  // Log operation (top-level with methods)
  {
    group: "Ando",
    name: "Log",
    desc: "Logging operations for outputting messages during the build. Has four methods: `Log.Info()` (visible at Normal verbosity), `Log.Warning()` (visible at Minimal+), `Log.Error()` (always visible), and `Log.Debug()` (only at Detailed verbosity).",
    examples: [
      'Log.Info("Starting deployment...");',
      'Log.Warning("Cache is stale, rebuilding");',
      'Log.Error("Failed to connect to server");',
      'Log.Debug("Processing item 5 of 10");',
    ],
    sourceFile: "Operations/LogOperations.cs",
  },
  {
    group: "Node",
    name: "Node.Install",
    desc: "Install Node.js globally in the container. Skips installation if already present (for warm containers). Calling this method disables automatic Node.js installation for subsequent npm operations. *Note: Npm operations (Ci, Install, Run, Test, Build) automatically install Node.js if not present.*",
    examples: ["Node.Install(); // Installs Node.js v22 (current LTS)", 'Node.Install("20"); // Installs Node.js v20'],
    sourceFile: "Operations/ToolInstallOperations.cs",
  },
  {
    group: "Dotnet",
    name: "Dotnet.SdkInstall",
    desc: "Install .NET SDK globally in the container. Skips installation if already present (for warm containers). Calling this method disables automatic SDK installation for subsequent operations. *Note: Dotnet operations (Build, Test, Restore, Publish) automatically install the SDK if not present.*",
    examples: ["Dotnet.SdkInstall(); // Installs .NET SDK 9.0", 'Dotnet.SdkInstall("8.0"); // Installs .NET SDK 8.0'],
    sourceFile: "Operations/DotnetOperations.cs",
  },
  {
    group: "Dotnet",
    name: "Dotnet.Project",
    desc: "Creates a reference to a .NET project file (.csproj). Used with Dotnet and Ef operations. The returned <code>ProjectRef</code> has properties: <code>Path</code>, <code>Name</code>, <code>Directory</code>, and <code>Version</code> (reads from csproj).",
    examples: [
      'var app = Dotnet.Project("./src/MyApp/MyApp.csproj");',
      "Dotnet.Build(app);",
      'Log.Info($"Building {app.Name} version {app.Version}");',
    ],
    sourceFile: "Operations/DotnetOperations.cs",
  },
  {
    group: "Dotnet",
    name: "Dotnet.Restore",
    desc: "Restore NuGet packages for a project. Automatically installs .NET SDK if not present.",
    examples: ["Dotnet.Restore(App);", "Dotnet.Restore(App, o => o.NoCache = true);"],
    sourceFile: "Operations/DotnetOperations.cs",
  },
  {
    group: "Dotnet",
    name: "Dotnet.Build",
    desc: "Compile a project with optional configuration. Automatically installs .NET SDK if not present.",
    examples: ["Dotnet.Build(App);", "Dotnet.Build(App, o => o.Configuration = Configuration.Release);"],
    sourceFile: "Operations/DotnetOperations.cs",
  },
  {
    group: "Dotnet",
    name: "Dotnet.Test",
    desc: "Run unit tests for a project. Automatically installs .NET SDK if not present.",
    examples: ["Dotnet.Test(Tests);", 'Dotnet.Test(Tests, o => o.Filter = "Category=Unit");'],
    sourceFile: "Operations/DotnetOperations.cs",
  },
  {
    group: "Dotnet",
    name: "Dotnet.Publish",
    desc: "Create deployment artifacts with full publish options. Automatically installs .NET SDK if not present.",
    examples: [
      "Dotnet.Publish(App);",
      'Dotnet.Publish(App, o => o\n  .Output(Root / "dist")\n  .WithConfiguration(Configuration.Release)\n  .WithRuntime("linux-x64")\n  .AsSelfContained()\n  .AsSingleFile());',
    ],
    sourceFile: "Operations/DotnetOperations.cs",
  },
  {
    group: "Dotnet",
    name: "Dotnet.Tool",
    desc: "Create a reference to a .NET CLI tool for installation.",
    examples: ['var efTool = Dotnet.Tool("dotnet-ef");', 'var efTool = Dotnet.Tool("dotnet-ef", "9.0.0");'],
    sourceFile: "Operations/DotnetOperations.cs",
  },
  {
    group: "Ef",
    name: "Ef.DbContextFrom",
    desc: "Create a reference to a DbContext in a project.",
    examples: ["var db = Ef.DbContextFrom(DataProject);", 'var db = Ef.DbContextFrom(DataProject, "AppDbContext");'],
    sourceFile: "Operations/EfOperations.cs",
  },
  {
    group: "Ef",
    name: "Ef.DatabaseUpdate",
    desc: "Apply pending EF Core migrations to the database. Can accept a connection string or an `OutputRef` from a Bicep deployment.",
    examples: [
      "Ef.DatabaseUpdate(db);",
      'Ef.DatabaseUpdate(db, connectionString: "Server=...");',
      'Ef.DatabaseUpdate(db, deployment.Output("sqlConnectionString"));',
    ],
    sourceFile: "Operations/EfOperations.cs",
  },
  {
    group: "Ef",
    name: "Ef.Script",
    desc: "Generate an idempotent SQL migration script.",
    examples: [
      'Ef.Script(db, Root / "migration.sql");',
      'Ef.Script(db, Root / "migration.sql", fromMigration: "Init");',
    ],
    sourceFile: "Operations/EfOperations.cs",
  },
  {
    group: "Npm",
    name: "Npm.Install",
    desc: "Run 'npm install' to install dependencies. Automatically installs Node.js if not present. Uses the standard 5-minute command timeout.",
    examples: ['var frontend = Directory("./frontend");', "Npm.Install(frontend);"],
    sourceFile: "Operations/NpmOperations.cs",
  },
  {
    group: "Npm",
    name: "Npm.Ci",
    desc: "Run 'npm ci' for clean, reproducible installs (preferred for CI). Automatically installs Node.js if not present. Uses the standard 5-minute command timeout.",
    examples: ['var frontend = Directory("./frontend");', "Npm.Ci(frontend);"],
    sourceFile: "Operations/NpmOperations.cs",
  },
  {
    group: "Npm",
    name: "Npm.Run",
    desc: "Run an npm script from package.json. Automatically installs Node.js if not present. Uses the standard 5-minute command timeout.",
    examples: ['var frontend = Directory("./frontend");', 'Npm.Run(frontend, "build");', 'Npm.Run(frontend, "lint");'],
    sourceFile: "Operations/NpmOperations.cs",
  },
  {
    group: "Npm",
    name: "Npm.Test",
    desc: "Run 'npm test'. Automatically installs Node.js if not present. Uses the standard 5-minute command timeout.",
    examples: ['var frontend = Directory("./frontend");', "Npm.Test(frontend);"],
    sourceFile: "Operations/NpmOperations.cs",
  },
  {
    group: "Npm",
    name: "Npm.Build",
    desc: "Run 'npm run build'. Automatically installs Node.js if not present. Uses the standard 5-minute command timeout.",
    examples: ['var frontend = Directory("./frontend");', "Npm.Build(frontend);"],
    sourceFile: "Operations/NpmOperations.cs",
  },
  {
    group: "Playwright",
    name: "Playwright.Test",
    desc: "Run Playwright E2E tests. By default uses `npx playwright test`. Set `UseNpmScript` to use `npm run test` instead. Automatically installs Node.js if not present.",
    examples: [
      'var e2e = Directory("./tests/E2E");\nPlaywright.Test(e2e);',
      'Playwright.Test(e2e, o => {\n  o.Project = "chromium";\n  o.Headed = true;\n});',
      'Playwright.Test(e2e, o => {\n  o.Workers = 4;\n  o.Reporter = "html";\n});',
      "// Use npm script instead of npx\nPlaywright.Test(e2e, o => o.UseNpmScript = true);",
    ],
    sourceFile: "Operations/PlaywrightOperations.cs",
  },
  {
    group: "Playwright",
    name: "Playwright.Install",
    desc: "Install Playwright browsers via `npx playwright install`. Call this after npm install and before running tests. Automatically installs Node.js if not present.",
    examples: ['var e2e = Directory("./tests/E2E");\nNpm.Ci(e2e);\nPlaywright.Install(e2e);\nPlaywright.Test(e2e);'],
    sourceFile: "Operations/PlaywrightOperations.cs",
  },
  {
    group: "Azure",
    name: "Azure.EnsureAuthenticated",
    desc: "Authenticate to Azure using the best available method. Uses service principal credentials when provided (explicit arguments or AZURE_CLIENT_ID/SECRET/TENANT_ID), otherwise reuses an existing az session in the build container or runs <code>az login --use-device-code</code> inside the container (no host-side az checks or prompts). Pass requireServicePrincipal: true (or set ANDO_REQUIRE_SERVICE_PRINCIPAL) to force service principal auth and fail instead of ever using a developer's az login session. Default behavior is unchanged (fallback allowed). Every run uses a private AZURE_CONFIG_DIR inside the build container, deleted when the run ends, so the host's ~/.azure profile is never read or modified.",
    examples: [
      "Azure.EnsureAuthenticated(); // Auto-detects: SP env vars, else CLI session/interactive",
      "Azure.EnsureAuthenticated(clientId, secret, tenantId); // explicit SP, no env reliance",
      "Azure.EnsureAuthenticated(clientId, secret, tenantId, requireServicePrincipal: true); // never fall back to az login",
    ],
    sourceFile: "Operations/AzureOperations.cs",
  },
  {
    group: "Azure",
    name: "Azure.EnsureLoggedIn",
    desc: "Verify the user is logged in to Azure CLI. Fails if not authenticated.",
    examples: ["Azure.EnsureLoggedIn();"],
    sourceFile: "Operations/AzureOperations.cs",
  },
  {
    group: "Azure",
    name: "Azure.ShowAccount",
    desc: "Display current Azure account information.",
    examples: ["Azure.ShowAccount();"],
    sourceFile: "Operations/AzureOperations.cs",
  },
  {
    group: "Azure",
    name: "Azure.LoginWithServicePrincipal",
    desc: "Login with a Service Principal (for CI/CD). Runs against the run's private AZURE_CONFIG_DIR inside the build container, so the host's ~/.azure profile is never read or modified; it is deleted when the run ends.",
    examples: [
      "Azure.LoginWithServicePrincipal(clientId, secret, tenantId);",
      "Azure.LoginWithServicePrincipal(); // Uses AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID env vars",
    ],
    sourceFile: "Operations/AzureOperations.cs",
  },
  {
    group: "Azure",
    name: "Azure.LoginWithManagedIdentity",
    desc: "Login with Managed Identity (for Azure-hosted environments). Runs against the run's private AZURE_CONFIG_DIR inside the build container, so the host's ~/.azure profile is never read or modified; it is deleted when the run ends.",
    examples: [
      "Azure.LoginWithManagedIdentity(); // System-assigned",
      'Azure.LoginWithManagedIdentity("client-id"); // User-assigned',
    ],
    sourceFile: "Operations/AzureOperations.cs",
  },
  {
    group: "Azure",
    name: "Azure.SetSubscription",
    desc: "Set the active Azure subscription.",
    examples: [
      'Azure.SetSubscription("subscription-id");',
      "Azure.SetSubscription(); // Uses AZURE_SUBSCRIPTION_ID env var",
    ],
    sourceFile: "Operations/AzureOperations.cs",
  },
  {
    group: "Azure",
    name: "Azure.CreateResourceGroup",
    desc: "Create a resource group if it doesn't exist.",
    examples: ['Azure.CreateResourceGroup("my-rg", "eastus");', 'Azure.CreateResourceGroup("prod-rg", "westeurope");'],
    sourceFile: "Operations/AzureOperations.cs",
  },
  {
    group: "Azure",
    name: "Azure.Cli",
    desc: "Runs <code>az</code> with the given arguments as the build's identity, in the build container. Returns a <code>CommandStep</code> whose stdout is available to later steps (<code>Value</code>, <code>Json(path)</code>, <code>ExitCode</code>). Configure it with <code>InDirectory</code>, <code>WithTimeout</code>, <code>WithEnv</code>, <code>AllowFailure</code> and <code>Quiet</code>. The Azure CLI is installed automatically in the container (Debian and Ubuntu images) before the first az step if the image does not include it.",
    examples: [
      'var show = Azure.Cli("webapp", "show", "-g", "my-rg", "-n", "my-app", "--slot", "release", "-o", "json").Quiet();\nAndo.When(() => show.Json("state") == "Running", () => Log.Info("running"));',
    ],
    sourceFile: "Operations/AzureOperations.cs",
  },
  {
    group: "Azure",
    name: "Azure.Vm.RunCommand",
    desc: "Runs a shell script as root on a Linux VM with <code>az vm run-command invoke</code> and fails the step when the script exits non-zero (run-command itself exits 0). Returns a <code>StepOutput</code> whose <code>Value</code> is the script's stdout. Signature: <code>RunCommand(resourceGroup, vmName, script, TimeSpan? timeout = null)</code>; default timeout 30 minutes.",
    examples: [
      'Azure.Vm.RunCommand("rg-eu", "docs-vm", "/usr/local/sbin/update-worker pkg.tar.gz", TimeSpan.FromMinutes(10));',
    ],
    sourceFile: "Operations/AzureVmOperations.cs",
  },
  {
    group: "Bicep",
    name: "Bicep.DeployToResourceGroup",
    desc: 'Deploy a Bicep template to a resource group. Returns a `BicepDeployment` with typed access to outputs via `deployment.Output("name")`.',
    examples: [
      'var deployment = Bicep.DeployToResourceGroup("my-rg", "./infra/main.bicep");',
      'var deployment = Bicep.DeployToResourceGroup("my-rg", "./main.bicep", o => o\n  .WithParameterFile("./params.json")\n  .WithDeploymentSlot("staging"));\nEf.DatabaseUpdate(db, deployment.Output("sqlConnectionString"));',
    ],
    sourceFile: "Operations/BicepOperations.cs",
  },
  {
    group: "Bicep",
    name: "Bicep.DeployToSubscription",
    desc: 'Deploy a Bicep template at subscription scope. Returns a `BicepDeployment` with typed access to outputs via `deployment.Output("name")`.',
    examples: [
      'var deployment = Bicep.DeployToSubscription("eastus", "./infra/sub.bicep");',
      'var deployment = Bicep.DeployToSubscription("eastus", "./sub.bicep", o => o\n  .WithParameter("environment", "prod"));\nvar resourceGroup = deployment.Output("resourceGroupName");',
    ],
    sourceFile: "Operations/BicepOperations.cs",
  },
  {
    group: "Bicep",
    name: "Bicep.WhatIf",
    desc: "Preview what would be deployed (what-if analysis).",
    examples: [
      'Bicep.WhatIf("my-rg", "./infra/main.bicep");',
      'Bicep.WhatIf("my-rg", "./main.bicep", o => o.WithParameterFile("./params.json"));',
    ],
    sourceFile: "Operations/BicepOperations.cs",
  },
  {
    group: "Bicep",
    name: "Bicep.Build",
    desc: "Compile a Bicep file to ARM JSON template.",
    examples: ['Bicep.Build("./infra/main.bicep");', 'Bicep.Build("./main.bicep", "./output/main.json");'],
    sourceFile: "Operations/BicepOperations.cs",
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.EnsureAuthenticated",
    desc: "Verify the Cloudflare API token with Cloudflare's token verification endpoint. Prompts interactively if environment variables are not set. See [authentication](/providers/cloudflare#authentication) for setup.",
    examples: ["Cloudflare.EnsureAuthenticated();"],
    sourceFile: "Operations/CloudflareOperations.cs",
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.EnsurePagesProject",
    desc: "Verify that the current Cloudflare credentials can read a specific Pages project. `PagesDeploy()` registers this check automatically before deploying.",
    examples: ['Cloudflare.EnsurePagesProject("my-site");'],
    sourceFile: "Operations/CloudflareOperations.cs",
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.PagesDeploy",
    desc: "Deploy a directory to Cloudflare Pages. The directory should be the build output folder. Automatically verifies Pages project access before running wrangler deploy.",
    examples: [
      'var frontend = Directory("./frontend");',
      'Cloudflare.PagesDeploy(frontend / "dist", "my-site");',
      'Cloudflare.PagesDeploy(frontend / "build", o => o\n  .WithProjectName("my-site")\n  .WithBranch("main"));',
    ],
    sourceFile: "Operations/CloudflareOperations.cs",
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.PagesListProjects",
    desc: "List all Cloudflare Pages projects.",
    examples: ["Cloudflare.PagesListProjects();"],
    sourceFile: "Operations/CloudflareOperations.cs",
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.PagesCreateProject",
    desc: "Create a new Cloudflare Pages project.",
    examples: ['Cloudflare.PagesCreateProject("my-site");', 'Cloudflare.PagesCreateProject("my-site", "develop");'],
    sourceFile: "Operations/CloudflareOperations.cs",
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.PagesListDeployments",
    desc: "List deployments for a Cloudflare Pages project.",
    examples: [
      'Cloudflare.PagesListDeployments("my-site");',
      "Cloudflare.PagesListDeployments(); // Uses CLOUDFLARE_PROJECT_NAME env var",
    ],
    sourceFile: "Operations/CloudflareOperations.cs",
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.PurgeCache",
    desc: "Purge the entire Cloudflare cache for a zone. Accepts either a Zone ID or domain name (domain is resolved automatically). Useful after deployments to ensure visitors see the latest content.",
    examples: [
      'Cloudflare.PurgeCache("example.com"); // Resolves domain to Zone ID',
      'Cloudflare.PurgeCache("zone-id-123"); // Direct Zone ID',
      "Cloudflare.PurgeCache(); // Uses CLOUDFLARE_ZONE_ID env var",
    ],
    sourceFile: "Operations/CloudflareOperations.cs",
  },
  {
    group: "Functions",
    name: "Functions.DeployZip",
    desc: "Deploy a function app using zip deploy.",
    examples: [
      'Functions.DeployZip("my-func", "./publish.zip");',
      'Functions.DeployZip("my-func", "./publish.zip", "my-rg", o => o\n  .WithDeploymentSlot("staging"));',
    ],
    sourceFile: "Operations/FunctionsOperations.cs",
  },
  {
    group: "Functions",
    name: "Functions.Publish",
    desc: "Publish using Azure Functions Core Tools.",
    examples: [
      'Functions.Publish("my-func");',
      'Functions.Publish("my-func", "./src/MyFunc", o => o\n  .WithDeploymentSlot("staging")\n  .WithConfiguration("Release"));',
    ],
    sourceFile: "Operations/FunctionsOperations.cs",
  },
  {
    group: "Functions",
    name: "Functions.DeployWithSwap",
    desc: "Deploy to a slot then swap to production (zero-downtime).",
    examples: [
      'Functions.DeployWithSwap("my-func", "./publish.zip");',
      'Functions.DeployWithSwap("my-func", "./publish.zip", "staging", "my-rg");',
    ],
    sourceFile: "Operations/FunctionsOperations.cs",
  },
  {
    group: "Functions",
    name: "Functions.SwapSlots",
    desc: "Swap deployment slots.",
    examples: [
      'Functions.SwapSlots("my-func", "staging");',
      'Functions.SwapSlots("my-func", "staging", "my-rg", "production");',
    ],
    sourceFile: "Operations/FunctionsOperations.cs",
  },
  {
    group: "Functions",
    name: "Functions.Restart",
    desc: "Restart a function app.",
    examples: ['Functions.Restart("my-func");', 'Functions.Restart("my-func", "my-rg", "staging");'],
    sourceFile: "Operations/FunctionsOperations.cs",
  },
  {
    group: "Functions",
    name: "Functions.Start",
    desc: "Start a function app.",
    examples: ['Functions.Start("my-func");', 'Functions.Start("my-func", slot: "staging");'],
    sourceFile: "Operations/FunctionsOperations.cs",
  },
  {
    group: "Functions",
    name: "Functions.Stop",
    desc: "Stop a function app.",
    examples: ['Functions.Stop("my-func");', 'Functions.Stop("my-func", slot: "staging");'],
    sourceFile: "Operations/FunctionsOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.DeployZip",
    desc: "Deploy an app service using zip deploy.",
    examples: [
      'AppService.DeployZip("my-app", "./publish.zip");',
      'AppService.DeployZip("my-app", "./publish.zip", "my-rg", o => o\n  .WithDeploymentSlot("staging"));',
    ],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.DeployFolder",
    desc: "Zip a published folder (contents at the archive root) and deploy it with zip deploy. Use when you have a publish directory rather than a pre-built zip. Supports <code>WithDeploymentSlot</code>. Does not restart the app unless <code>WithRestart()</code> is set (then <code>az webapp restart</code> runs after a successful deploy) and waits for the deployment unless <code>WithNoWait()</code> is set. Deploys use <code>az webapp deployment source config-zip</code>. Installs <code>zip</code> with apt in the container if missing; for reproducible builds zip at build time and use <code>DeployZip</code>.",
    examples: [
      'AppService.DeployFolder("my-app", "dist/api");',
      'AppService.DeployFolder("my-app", "dist/api", "my-rg", o => o\n  .WithDeploymentSlot("release"));',
    ],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.DeployWithSwap",
    desc: "Deploy to a slot then swap to production (zero-downtime).",
    examples: [
      'AppService.DeployWithSwap("my-app", "./publish.zip");',
      'AppService.DeployWithSwap("my-app", "./publish.zip", "staging", "my-rg");',
    ],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.SwapSlots",
    desc: "Swap deployment slots. Returns a <code>CommandStep</code> (chain <code>.WithTimeout(...)</code>). The default timeout is 30 minutes, or <code>Ando.CommandTimeout</code> when that is longer; an explicit <code>.WithTimeout</code> always wins.",
    examples: [
      'AppService.SwapSlots("my-app", "staging");',
      'AppService.SwapSlots("my-app", "staging", "my-rg", "production");',
      'AppService.SwapSlots("my-app", "staging", "my-rg").WithTimeout(TimeSpan.FromMinutes(45));',
    ],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.CreateSlot",
    desc: "Create a new deployment slot.",
    examples: [
      'AppService.CreateSlot("my-app", "staging");',
      'AppService.CreateSlot("my-app", "staging", "my-rg", "production");',
    ],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.DeleteSlot",
    desc: "Delete a deployment slot.",
    examples: ['AppService.DeleteSlot("my-app", "staging");', 'AppService.DeleteSlot("my-app", "staging", "my-rg");'],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.ListSlots",
    desc: "List deployment slots for an app service.",
    examples: ['AppService.ListSlots("my-app");', 'AppService.ListSlots("my-app", "my-rg");'],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.Restart",
    desc: "Restart an app service. Returns a <code>CommandStep</code>.",
    examples: ['AppService.Restart("my-app");', 'AppService.Restart("my-app", "my-rg", "staging");'],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.Start",
    desc: "Start an app service. Returns a <code>CommandStep</code>.",
    examples: ['AppService.Start("my-app");', 'AppService.Start("my-app", slot: "staging");'],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.Stop",
    desc: "Stop an app service. Returns a <code>CommandStep</code>.",
    examples: ['AppService.Stop("my-app");', 'AppService.Stop("my-app", slot: "staging");'],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.Show",
    desc: "Reads the app's (or slot's) <code>az webapp show</code> JSON. Returns a <code>CommandStep</code>; the output is quiet. Read fields at execution time with <code>Json(path)</code>.",
    examples: [
      'var show = AppService.Show("my-app", "my-rg", "release");\nAndo.Require(() => show.Json("state") == "Running", "Slot is not running");',
    ],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.GetHostName",
    desc: "Reads the app's (or slot's) default host name, for example <code>myapp-release.azurewebsites.net</code>. Returns a <code>CommandStep</code>.",
    examples: [
      'var host = AppService.GetHostName("my-app", "my-rg", "release");\nHttp.WaitForHealthy(() => $"https://{host.Value}/healthz");',
    ],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.AccessRestrictions",
    desc: "Reads the app's (or slot's) access restrictions (<code>az webapp config access-restriction show</code>). Returns a quiet <code>CommandStep</code> with JSON.",
    examples: [
      'var rules = AppService.AccessRestrictions("my-app", "my-rg", "release");\nAndo.When(() => rules.Json("ipSecurityRestrictionsDefaultAction") == "Deny", () => Log.Info("restricted"));',
    ],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "AppService",
    name: "AppService.TemporaryAccessRule",
    desc: 'Adds an Allow access rule for one IPv4 address to the app or slot and removes it again when the enclosing block (or the run) ends, whatever happened. The removal is a finally step, followed by a check that the rule is gone. A rule with the same name is replaced. Without <code>ip</code>, uses this machine\'s public IPv4; a bare address gets <code>/32</code>. Signature: <code>TemporaryAccessRule(appName, resourceGroup, slot = null, ruleName = "ando-deploy", ip = null, priority = 50)</code>. Call it directly in the <code>Ando.Block</code> that needs it: inside <code>Ando.When</code> or <code>Ando.Defer</code> the rule is removed when that body ends.',
    examples: [
      'Ando.Block("deploy", () =>\n{\n  AppService.TemporaryAccessRule("my-app", "my-rg", "release", "deploy-gate");\n  AppService.DeployFolder("my-app", "dist/api", "my-rg", o => o.WithDeploymentSlot("release"));\n});',
    ],
    sourceFile: "Operations/AppServiceOperations.cs",
  },
  {
    group: "Http",
    name: "Http.WaitForHealthy",
    desc: "Poll a URL until it returns the expected status code (default 200) or the timeout elapses. Runs in-process on the build host, so it needs no curl in the image. Use it to gate a deploy on a health check, for example before swapping a slot into production. Overloads take a <code>string</code>, an <code>IEnumerable&lt;string&gt;</code> (every URL must pass), a <code>Func&lt;string&gt;</code> or a <code>Func&lt;IEnumerable&lt;string&gt;&gt;</code> (computed at execution time). Extra options: <code>ConsecutiveSuccesses(n)</code> needs n passing checks in a row (a failure resets the count), <code>MinGapSeconds(s)</code> waits at least s seconds between counted passes, and <code>ExpectJson(path, expected)</code> requires a dotted-path value in the JSON body. <code>ExpectJson</code> applies to every URL of the call.",
    examples: [
      'Http.WaitForHealthy(() => $"https://{host.Value}/healthz", o => o\n  .ConsecutiveSuccesses(3)\n  .MinGapSeconds(10)\n  .ExpectJson("version", "1.4.0"));',
      'Http.WaitForHealthy(new[] { "https://a.example.com/healthz", "https://a.example.com/healthz/db" });',
      'Http.WaitForHealthy("https://app.example.com/healthz");',
      'Http.WaitForHealthy("https://app.example.com/healthz/db", o => o\n  .WithTimeoutSeconds(300)\n  .WithIntervalSeconds(5));',
    ],
    sourceFile: "Operations/HttpOperations.cs",
  },
  {
    group: "Http",
    name: "Http.Get",
    desc: "Sends a GET request (from the build host) and keeps the response. Returns a <code>StepOutput</code>: <code>Value</code> is the body and <code>StatusCode</code> the status code. Fails on a non-2xx status unless <code>allowAnyStatus</code> is true. Signatures: <code>Get(string url, bool allowAnyStatus = false)</code> and <code>Get(Func&lt;string&gt; url, bool allowAnyStatus = false)</code>.",
    examples: [
      'var health = Http.Get("https://eu.example.com/healthz");\nAndo.Report("eu.healthz", health);',
      'var probe = Http.Get(() => $"https://{host.Value}/healthz", allowAnyStatus: true);\nAndo.Require(() => probe.StatusCode < 500, "Server error");',
    ],
    sourceFile: "Operations/HttpOperations.cs",
  },
  {
    group: "Http",
    name: "Http.PublicIpV4",
    desc: "Looks up this machine's public IPv4 address (as seen by public services). Returns a <code>StepOutput</code> whose <code>Value</code> is the address.",
    examples: ['var ip = Http.PublicIpV4();\nAndo.Report("buildIp", ip);'],
    sourceFile: "Operations/HttpOperations.cs",
  },
  {
    group: "Nuget",
    name: "Nuget.EnsureAuthenticated",
    desc: "Ensures NuGet API key is available for publishing. Prompts interactively if `NUGET_API_KEY` environment variable is not set. Call before Push.",
    examples: ['var app = Dotnet.Project("./src/MyLib/MyLib.csproj");\nNuget.EnsureAuthenticated();\nNuget.Push(app);'],
    sourceFile: "Operations/NugetOperations.cs",
  },
  {
    group: "Nuget",
    name: "Nuget.Pack",
    desc: "Create a NuGet package from a project. **Defaults:** Release config, output to `bin/Release`.",
    examples: [
      'var app = Dotnet.Project("./src/MyLib/MyLib.csproj");\nNuget.Pack(app);',
      'Nuget.Pack(app, o => o.WithVersion("1.0.0"));',
    ],
    sourceFile: "Operations/NugetOperations.cs",
  },
  {
    group: "Nuget",
    name: "Nuget.Push",
    desc: "Push packages to a feed. Pass a ProjectRef to push from `bin/Release/*.nupkg`, or a path/glob. **Defaults:** NuGet.org. Use <code>SkipDuplicates()</code> to avoid failure if version already exists (shows warning instead).",
    examples: [
      'var app = Dotnet.Project("./src/MyLib/MyLib.csproj");\nNuget.Pack(app);\nNuget.EnsureAuthenticated();\nNuget.Push(app, o => o.SkipDuplicates());',
      'Nuget.Push("./packages/MyLib.1.0.0.nupkg");',
    ],
    sourceFile: "Operations/NugetOperations.cs",
  },
  // Git operations - ALL run on HOST (not in container)
  {
    group: "Git",
    name: "Git.Tag",
    desc: "Creates a git tag. By default creates annotated tags. <strong>Runs on host</strong> (not in container).",
    examples: [
      'Git.Tag("v1.0.0");',
      'Git.Tag("v1.0.0", o => o.WithMessage("Release notes here"));',
      'Git.Tag("v1.0.0", o => o.AsLightweight()); // Lightweight tag',
    ],
    sourceFile: "Operations/GitOperations.cs",
  },
  {
    group: "Git",
    name: "Git.Push",
    desc: "Pushes the current branch to the remote repository. <strong>Runs on host</strong> (not in container).",
    examples: ["Git.Push();", 'Git.Push(o => o.ToRemote("upstream"));', "Git.Push(o => o.WithUpstream()); // -u flag"],
    sourceFile: "Operations/GitOperations.cs",
  },
  {
    group: "Git",
    name: "Git.PushTags",
    desc: "Pushes tags to the remote repository, skipping tags that already exist on the remote. Tags are pushed individually for safety. <strong>Runs on host</strong> (not in container).",
    examples: ["Git.PushTags();", 'Git.PushTags("upstream");'],
    sourceFile: "Operations/GitOperations.cs",
  },
  {
    group: "Git",
    name: "Git.Commit",
    desc: "Full SHA of the commit being built: HEAD, or the commit given with <code>ando run --ref</code>. A property read while the script is evaluated. <strong>Runs on host</strong>.",
    examples: ["var commit = Git.Commit;", 'Ando.WriteManifest(Root / "artifacts", "1.4.0", Git.Commit);'],
    sourceFile: "Operations/GitOperations.cs",
  },
  {
    group: "Git",
    name: "Git.ShortCommit",
    desc: "Short SHA of the commit being built. A property read while the script is evaluated. <strong>Runs on host</strong>.",
    examples: ['Log.Info($"Building {Git.ShortCommit}");'],
    sourceFile: "Operations/GitOperations.cs",
  },
  {
    group: "Git",
    name: "Git.RequireClean",
    desc: "Fails the build before any step runs if tracked files are modified, staged or deleted, or non-ignored untracked files exist, under the given paths (the whole repository if none). With <code>--ref</code> the build uses a clean checkout, so it always passes. <code>.ando/runs/</code> has its own <code>.gitignore</code> and never counts. <strong>Runs on host</strong> at evaluation time.",
    examples: ['Git.RequireClean("src", "web", "infra", "Directory.Build.props");'],
    sourceFile: "Operations/GitOperations.cs",
  },
  {
    group: "Git",
    name: "Git.ChangedSince",
    desc: "Returns true if any file under the paths differs between <code>sinceRef</code> and the commit being built (committed changes only). Checks the whole repository when no paths are given. Evaluation time, <strong>runs on host</strong>.",
    examples: [
      'if (Git.ChangedSince(Git.LastTag("v*") ?? "HEAD~1", "src/Worker", "services/signing"))\n{\n  Log.Info("Worker changed");\n}',
    ],
    sourceFile: "Operations/GitOperations.cs",
  },
  {
    group: "Git",
    name: "Git.LastTag",
    desc: "Returns the most recent tag reachable from the commit being built that matches the glob pattern (default <code>*</code>), or <code>null</code> when there is none. Evaluation time, <strong>runs on host</strong>.",
    examples: ['var last = Git.LastTag("v*");'],
    sourceFile: "Operations/GitOperations.cs",
  },
  // GitHub operations
  {
    group: "GitHub",
    name: "GitHub.CreatePr",
    desc: "Creates a GitHub pull request using the gh CLI. Requires GitHub authentication via `GITHUB_TOKEN` env var or `gh auth login`.",
    examples: [
      'GitHub.CreatePr(o => o\n  .WithTitle("Add new feature")\n  .WithBody("Description here")\n  .WithBase("main"));',
      'GitHub.CreatePr(o => o.WithTitle("Fix bug").AsDraft());',
    ],
    sourceFile: "Operations/GitHubOperations.cs",
  },
  {
    group: "GitHub",
    name: "GitHub.CreateRelease",
    desc: "Creates a GitHub release with optional file uploads. Automatically prefixes version with 'v' if not present. Use <code>WithFiles()</code> to upload release assets. Supports <code>path#name</code> syntax to rename files.",
    examples: [
      'GitHub.CreateRelease(o => o.WithTag("v1.0.0"));',
      'GitHub.CreateRelease(o => o\n  .WithTag("1.0.0")\n  .WithGeneratedNotes());',
      'GitHub.CreateRelease(o => o\n  .WithTag("v1.0.0")\n  .WithNotes("## Changes\\n- Fixed bug")\n  .AsPrerelease());',
      'GitHub.CreateRelease(o => o\n  .WithTag("v1.0.0")\n  .WithGeneratedNotes()\n  .WithFiles(\n    "dist/linux-x64/app#app-linux-x64",\n    "dist/win-x64/app.exe#app-win-x64.exe"\n  ));',
    ],
    sourceFile: "Operations/GitHubOperations.cs",
  },
  {
    group: "GitHub",
    name: "GitHub.PushImage",
    desc: "Pushes a Docker image to GitHub Container Registry (ghcr.io). <strong>Deprecated:</strong> Use <code>Docker.Build</code> with <code>WithPush()</code> instead for atomic builds.",
    examples: [
      '// Use Docker.Build with WithPush() for atomic build+push\nDocker.Build("Dockerfile", o => o\n  .WithPlatforms("linux/amd64", "linux/arm64")\n  .WithTag("ghcr.io/myorg/myapp:v1.0.0")\n  .WithTag("ghcr.io/myorg/myapp:latest")\n  .WithPush());',
    ],
    sourceFile: "Operations/GitHubOperations.cs",
  },
  // Docker operations
  {
    group: "Docker",
    name: "Docker.Install",
    desc: "Installs the Docker CLI in the container. Required before using <code>Docker.Build</code> when running with <code>--dind</code>. Skips if already installed.",
    examples: ['Docker.Install();\nDocker.Build("Dockerfile", o => o.WithTag("myapp:latest"));'],
    sourceFile: "Operations/DockerOperations.cs",
  },
  {
    group: "Docker",
    name: "Docker.IsAvailable",
    desc: "Check if Docker CLI and daemon are accessible. Executes immediately (not registered as a step) and returns a boolean. Useful for conditional logic in build scripts.",
    examples: [
      'if (Docker.IsAvailable()) {\n  Docker.Build("Dockerfile", o => o.WithTag("myapp:latest"));\n} else {\n  Log.Warning("Docker not available, skipping container build");\n}',
    ],
    sourceFile: "Operations/DockerOperations.cs",
  },
  {
    group: "Docker",
    name: "Docker.Build",
    desc: "Builds a Docker image using buildx. Supports single or multi-platform builds with optional push to registry. <strong>Requires <code>--dind</code> CLI flag</strong>. Call <code>Docker.Install()</code> first to install the Docker CLI. Automatically creates a buildx builder for multi-platform builds and handles ghcr.io authentication when pushing. <strong>Recommended</strong>: Use <code>WithPush()</code> for atomic build+push to ensure both version and latest tags point to the same manifest.",
    examples: [
      '// Recommended: Atomic multi-arch build+push to ghcr.io\n// Ensures all tags point to the same manifest\nDocker.Install();\nDocker.Build("./Dockerfile", o => o\n  .WithPlatforms("linux/amd64", "linux/arm64")\n  .WithTag("ghcr.io/myorg/myapp:v1.0.0")\n  .WithTag("ghcr.io/myorg/myapp:latest")\n  .WithPush());',
      '// Single platform build with push\nDocker.Build("./src/MyApp/Dockerfile", o => o\n  .WithTag("ghcr.io/myorg/myapp:v1.0.0")\n  .WithBuildArg("VERSION", "1.0.0")\n  .WithPush());',
      '// Local build only (no push, loads into local docker)\nDocker.Build("Dockerfile", o => o.WithTag("myapp:dev"));',
    ],
    sourceFile: "Operations/DockerOperations.cs",
  },
  // DocFX operations
  {
    group: "Docfx",
    name: "Docfx.Install",
    desc: "Install DocFX as a dotnet global tool if not already installed. DocFX generates API documentation from C# XML documentation comments.",
    examples: ["Docfx.Install();"],
    sourceFile: "Operations/DocfxOperations.cs",
  },
  {
    group: "Docfx",
    name: "Docfx.GenerateDocs",
    desc: "Generate API documentation from a docfx.json configuration file. Runs both 'docfx metadata' to extract API metadata and 'docfx build' to generate HTML.",
    examples: ['Docfx.GenerateDocs("./docfx.json");', "Docfx.GenerateDocs(); // Uses default ./docfx.json"],
    sourceFile: "Operations/DocfxOperations.cs",
  },
  {
    group: "Docfx",
    name: "Docfx.CopyToDirectory",
    desc: "Copy generated documentation from the DocFX output directory to a target directory.",
    examples: ['Docfx.CopyToDirectory("_apidocs", "./website/public/apidocs");'],
    sourceFile: "Operations/DocfxOperations.cs",
  },
  {
    group: "Docfx",
    name: "Docfx.BuildAndCopy",
    desc: "Generate documentation and copy it to the target directory in one step. Combines GenerateDocs, CopyToDirectory, and Cleanup. Creates a redirect index.html that points to the API namespace page.",
    examples: ['Docfx.Install();\nDocfx.BuildAndCopy("./docfx.json", "_apidocs", "./website/public/apidocs");'],
    sourceFile: "Operations/DocfxOperations.cs",
  },
  {
    group: "Docfx",
    name: "Docfx.Cleanup",
    desc: "Clean up intermediate DocFX files (api/ and output directories).",
    examples: ['Docfx.Cleanup("_apidocs");', "Docfx.Cleanup(); // Uses default _apidocs"],
    sourceFile: "Operations/DocfxOperations.cs",
  },
  {
    group: "Docfx",
    name: "Docfx.IsInstalled",
    desc: "Check if DocFX is installed as a dotnet global tool. Returns true if available, false otherwise.",
    examples: ["if (!Docfx.IsInstalled()) {\n  Docfx.Install();\n}"],
    sourceFile: "Operations/DocfxOperations.cs",
  },
];

// Get unique provider names sorted alphabetically
export const providers = [...new Set(operations.map((op) => op.group))].sort();

// Get operations for a specific provider
export function getOperationsForProvider(provider) {
  return operations.filter((op) => op.group === provider);
}

// Get all operations sorted by provider then by name
export function getAllOperationsSorted() {
  return [...operations].sort((a, b) => {
    const groupCompare = a.group.localeCompare(b.group);
    if (groupCompare !== 0) return groupCompare;
    return a.name.localeCompare(b.name);
  });
}

// Get operations grouped by provider, sorted alphabetically
// Note: Ando group preserves array order (top-level, then Ando.*, then Log.*)
export function getOperationsGroupedByProvider() {
  const grouped = {};
  for (const op of operations) {
    if (!grouped[op.group]) {
      grouped[op.group] = [
        {
          group: "Shell",
          name: "Shell.Run",
          desc: "Runs a command with arguments in the build container, in order with the other steps. Arguments are passed as a list with no shell parsing. Fails on a non-zero exit code unless <code>AllowFailure()</code>. Returns a <code>CommandStep</code> (<code>Value</code>, <code>ExitCode</code>, <code>Json(path)</code>; fluent <code>InDirectory</code>, <code>WithTimeout</code>, <code>WithEnv</code>, <code>AllowFailure</code>, <code>Quiet</code>). Signature: <code>Run(string command, params string[] args)</code>.",
          examples: ['Shell.Run("zip", "-qr", "../web.zip", ".").InDirectory("artifacts/web");'],
          sourceFile: "Operations/ShellCommandOperations.cs",
        },
        {
          group: "Shell",
          name: "Shell.Bash",
          desc: "Runs a script with <code>bash -euo pipefail -c</code> in the build container, for pipes, redirection and sourcing helper scripts. Returns a <code>CommandStep</code>. Signature: <code>Bash(string script)</code>.",
          examples: ['Shell.Bash("source scripts/lib/deploy-gates.sh && require_spa_build artifacts/web");'],
          sourceFile: "Operations/ShellCommandOperations.cs",
        },
        {
          group: "Bun",
          name: "Bun.Install",
          desc: 'Installs Bun into <code>/usr/local/bin</code> so later steps (including MSBuild targets run by <code>Dotnet.Publish</code>) find <code>bun</code> on PATH. Skips the download when the container already has that version. Installs <code>curl</code> and <code>unzip</code> with apt if missing; needs a Debian or Ubuntu image. Signature: <code>Install(string version = "latest", string? sha256 = null)</code>. With <code>sha256</code> (the hash of the release zip for the container architecture, <code>bun-linux-x64.zip</code> or <code>bun-linux-aarch64.zip</code>; the hashes differ) <code>version</code> must be exact, the zip is downloaded from GitHub and verified, and the step fails on a mismatch.',
          examples: ['Bun.Install("1.2.23");', 'Bun.Install("1.2.23", sha256: "cf0ed0a920799d576ffde4e0cae66d732bf23c2530407f26f59c7831dffe1f0e");'],
          sourceFile: "Operations/ToolInstallOperations.cs",
        },
      ];
    }
    grouped[op.group].push(op);
  }
  // Sort operations within each group by name, except Ando which preserves array order
  for (const group of Object.keys(grouped)) {
    if (group !== "Ando") {
      grouped[group].sort((a, b) => a.name.localeCompare(b.name));
    }
  }
  // Return as array of [provider, operations] pairs, sorted by provider
  return Object.entries(grouped).sort((a, b) => a[0].localeCompare(b[0]));
}

// Generate anchor ID from operation name (e.g., "Cloudflare.PagesDeploy" -> "pagesdeploy")
export function getAnchorId(name) {
  const parts = name.split(".");
  return parts[parts.length - 1].toLowerCase();
}
