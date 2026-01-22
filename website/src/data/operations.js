// =============================================================================
// operations.js
//
// Summary: Shared operations data for all provider pages.
//
// This file contains all ANDO operations organized by provider. Each provider
// page imports this data and filters to show only its operations.
//
// Structure:
// - Each operation has: group (provider), name, desc, examples
// - Providers are derived from the group field
// =============================================================================

export const operations = [
  // Top-level operations (globals available in build.csando)
  {
    group: "Ando",
    name: "Root",
    desc: "The root path of the project (where build.csando is located). Supports path combining with the `/` operator.",
    examples: ['var output = Root / "dist";', "Dotnet.Publish(app, o => o.Output(output));"],
  },
  {
    group: "Ando",
    name: "Temp",
    desc: "Temporary files directory (root/.ando/tmp). Supports path combining with the `/` operator. Use for caches, intermediate files, etc.",
    examples: ['var cache = Temp / "cache";', 'var intermediate = Temp / "build-output";'],
  },
  {
    group: "Ando",
    name: "Env",
    desc: "Gets an environment variable. By default throws if not set. Pass `required: false` to return null instead.",
    examples: ['var apiKey = Env("API_KEY");', 'var optional = Env("OPTIONAL_VAR", required: false);'],
  },
  {
    group: "Ando",
    name: "Directory",
    desc: "Creates a reference to a directory. Used with Npm and Cloudflare operations. Supports path combining with the `/` operator.",
    examples: [
      'var frontend = Directory("./frontend");',
      "Npm.Ci(frontend);",
      'Cloudflare.PagesDeploy(frontend / "dist", "my-site");',
    ],
  },
  // DefineProfile (before Ando.* operations)
  {
    group: "Ando",
    name: "DefineProfile",
    desc: "Defines a build profile that can be activated via CLI. Returns a `Profile` object that evaluates to `true` when active. Use `-p` or `--profile` CLI flag to activate.",
    examples: [
      'var release = DefineProfile("release");\nvar push = DefineProfile("push");\n\nDotnet.Build(app);\n\nif (release) {\n  Git.Tag("v1.0.0");\n  GitHub.CreateRelease(o => o.WithTag("v1.0.0"));\n}',
      "// CLI usage:\n// ando -p release\n// ando -p push,release",
    ],
  },
  // Build configuration operations (top-level)
  {
    group: "Ando",
    name: "UseImage",
    desc: "Set the Docker image for the current build container. Must be called before build steps execute.",
    examples: ['UseImage("ubuntu:24.04");', 'UseImage("mcr.microsoft.com/dotnet/sdk:9.0");', 'UseImage("node:22");'],
  },
  {
    group: "Ando",
    name: "CopyArtifactsToHost",
    desc: "Register files to copy from the container to the host after the build completes. The first parameter is the path inside the container (relative to /workspace or absolute), and the second is the destination on the host (relative to project root or absolute).",
    examples: ['CopyArtifactsToHost("dist", "./dist");', 'CopyArtifactsToHost("bin/Release", "./output");'],
  },
  {
    group: "Ando",
    name: "CopyZippedArtifactsToHost",
    desc: "Register files to be archived and copied from the container to the host after the build completes. Creates a single archive file for faster transfer of many small files. Supports `.tar.gz` (default) and `.zip` formats. If the destination is a directory, creates `artifacts.tar.gz` in that directory.",
    examples: [
      'CopyZippedArtifactsToHost("dist", "./output");',
      'CopyZippedArtifactsToHost("dist", "./dist/binaries.tar.gz");',
      'CopyZippedArtifactsToHost("dist", "./dist/binaries.zip");',
    ],
  },
  {
    group: "Ando",
    name: "Build",
    desc: "Run a nested build script. Accepts a directory (runs build.csando in that directory) or a specific .csando file path. The child build runs in its own isolated container with its own environment file (`.env.ando` or `.env`) and context.",
    examples: [
      'Build(Directory("./website"));',
      'Build(Directory("./website") / "deploy.csando");',
      'Build(Directory("./api"), o => o.WithDind());',
    ],
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
  },
  {
    group: "Node",
    name: "Node.Install",
    desc: "Install Node.js globally in the container. Skips installation if already present (for warm containers). Calling this method disables automatic Node.js installation for subsequent npm operations. *Note: Npm operations (Ci, Install, Run, Test, Build) automatically install Node.js if not present.*",
    examples: ["Node.Install(); // Installs Node.js v22 (current LTS)", 'Node.Install("20"); // Installs Node.js v20'],
  },
  {
    group: "Dotnet",
    name: "Dotnet.SdkInstall",
    desc: "Install .NET SDK globally in the container. Skips installation if already present (for warm containers). Calling this method disables automatic SDK installation for subsequent operations. *Note: Dotnet operations (Build, Test, Restore, Publish) automatically install the SDK if not present.*",
    examples: ["Dotnet.SdkInstall(); // Installs .NET SDK 9.0", 'Dotnet.SdkInstall("8.0"); // Installs .NET SDK 8.0'],
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
  },
  {
    group: "Dotnet",
    name: "Dotnet.Restore",
    desc: "Restore NuGet packages for a project. Automatically installs .NET SDK if not present.",
    examples: ["Dotnet.Restore(App);", "Dotnet.Restore(App, o => o.NoCache = true);"],
  },
  {
    group: "Dotnet",
    name: "Dotnet.Build",
    desc: "Compile a project with optional configuration. Automatically installs .NET SDK if not present.",
    examples: ["Dotnet.Build(App);", "Dotnet.Build(App, o => o.Configuration = Configuration.Release);"],
  },
  {
    group: "Dotnet",
    name: "Dotnet.Test",
    desc: "Run unit tests for a project. Automatically installs .NET SDK if not present.",
    examples: ["Dotnet.Test(Tests);", 'Dotnet.Test(Tests, o => o.Filter = "Category=Unit");'],
  },
  {
    group: "Dotnet",
    name: "Dotnet.Publish",
    desc: "Create deployment artifacts with full publish options. Automatically installs .NET SDK if not present.",
    examples: [
      "Dotnet.Publish(App);",
      'Dotnet.Publish(App, o => o\n  .Output(Root / "dist")\n  .WithConfiguration(Configuration.Release)\n  .WithRuntime("linux-x64")\n  .AsSelfContained()\n  .AsSingleFile());',
    ],
  },
  {
    group: "Dotnet",
    name: "Dotnet.Tool",
    desc: "Create a reference to a .NET CLI tool for installation.",
    examples: ['var efTool = Dotnet.Tool("dotnet-ef");', 'var efTool = Dotnet.Tool("dotnet-ef", "9.0.0");'],
  },
  {
    group: "Ef",
    name: "Ef.DbContextFrom",
    desc: "Create a reference to a DbContext in a project.",
    examples: ["var db = Ef.DbContextFrom(DataProject);", 'var db = Ef.DbContextFrom(DataProject, "AppDbContext");'],
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
  },
  {
    group: "Ef",
    name: "Ef.AddMigration",
    desc: "Create a new EF Core migration.",
    examples: ['Ef.AddMigration(db, "InitialCreate");', 'Ef.AddMigration(db, "AddUsers", outputDir: "Migrations");'],
  },
  {
    group: "Ef",
    name: "Ef.Script",
    desc: "Generate an idempotent SQL migration script.",
    examples: [
      'Ef.Script(db, Root / "migration.sql");',
      'Ef.Script(db, Root / "migration.sql", fromMigration: "Init");',
    ],
  },
  {
    group: "Ef",
    name: "Ef.RemoveMigration",
    desc: "Remove the last migration.",
    examples: ["Ef.RemoveMigration(db);", "Ef.RemoveMigration(db, force: true);"],
  },
  {
    group: "Npm",
    name: "Npm.Install",
    desc: "Run 'npm install' to install dependencies. Automatically installs Node.js if not present.",
    examples: ['var frontend = Directory("./frontend");', "Npm.Install(frontend);"],
  },
  {
    group: "Npm",
    name: "Npm.Ci",
    desc: "Run 'npm ci' for clean, reproducible installs (preferred for CI). Automatically installs Node.js if not present.",
    examples: ['var frontend = Directory("./frontend");', "Npm.Ci(frontend);"],
  },
  {
    group: "Npm",
    name: "Npm.Run",
    desc: "Run an npm script from package.json. Automatically installs Node.js if not present.",
    examples: ['var frontend = Directory("./frontend");', 'Npm.Run(frontend, "build");', 'Npm.Run(frontend, "lint");'],
  },
  {
    group: "Npm",
    name: "Npm.Test",
    desc: "Run 'npm test'. Automatically installs Node.js if not present.",
    examples: ['var frontend = Directory("./frontend");', "Npm.Test(frontend);"],
  },
  {
    group: "Npm",
    name: "Npm.Build",
    desc: "Run 'npm run build'. Automatically installs Node.js if not present.",
    examples: ['var frontend = Directory("./frontend");', "Npm.Build(frontend);"],
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
  },
  {
    group: "Playwright",
    name: "Playwright.Install",
    desc: "Install Playwright browsers via `npx playwright install`. Call this after npm install and before running tests. Automatically installs Node.js if not present.",
    examples: ['var e2e = Directory("./tests/E2E");\nNpm.Ci(e2e);\nPlaywright.Install(e2e);\nPlaywright.Test(e2e);'],
  },
  {
    group: "Azure",
    name: "Azure.EnsureAuthenticated",
    desc: "Authenticate to Azure using the best available method. Checks for service principal credentials (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID), then falls back to existing CLI session or interactive login. Prompts to install Azure CLI if not available.",
    examples: [
      "Azure.EnsureAuthenticated(); // Auto-detects auth method",
      "// CI/CD: Uses env vars if set",
      "// Local: Uses az login session or prompts for login",
    ],
  },
  {
    group: "Azure",
    name: "Azure.EnsureLoggedIn",
    desc: "Verify the user is logged in to Azure CLI. Fails if not authenticated.",
    examples: ["Azure.EnsureLoggedIn();"],
  },
  {
    group: "Azure",
    name: "Azure.ShowAccount",
    desc: "Display current Azure account information.",
    examples: ["Azure.ShowAccount();"],
  },
  {
    group: "Azure",
    name: "Azure.LoginWithServicePrincipal",
    desc: "Login with a Service Principal (for CI/CD).",
    examples: [
      "Azure.LoginWithServicePrincipal(clientId, secret, tenantId);",
      "Azure.LoginWithServicePrincipal(); // Uses AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID env vars",
    ],
  },
  {
    group: "Azure",
    name: "Azure.LoginWithManagedIdentity",
    desc: "Login with Managed Identity (for Azure-hosted environments).",
    examples: [
      "Azure.LoginWithManagedIdentity(); // System-assigned",
      'Azure.LoginWithManagedIdentity("client-id"); // User-assigned',
    ],
  },
  {
    group: "Azure",
    name: "Azure.SetSubscription",
    desc: "Set the active Azure subscription.",
    examples: [
      'Azure.SetSubscription("subscription-id");',
      "Azure.SetSubscription(); // Uses AZURE_SUBSCRIPTION_ID env var",
    ],
  },
  {
    group: "Azure",
    name: "Azure.CreateResourceGroup",
    desc: "Create a resource group if it doesn't exist.",
    examples: ['Azure.CreateResourceGroup("my-rg", "eastus");', 'Azure.CreateResourceGroup("prod-rg", "westeurope");'],
  },
  {
    group: "Azure",
    name: "Azure.DeleteResourceGroup",
    desc: "Delete a resource group and all its resources.",
    examples: ['Azure.DeleteResourceGroup("my-rg");', 'Azure.DeleteResourceGroup("my-rg", noWait: true);'],
  },
  {
    group: "Bicep",
    name: "Bicep.DeployToResourceGroup",
    desc: 'Deploy a Bicep template to a resource group. Returns a `BicepDeployment` with typed access to outputs via `deployment.Output("name")`.',
    examples: [
      'var deployment = Bicep.DeployToResourceGroup("my-rg", "./infra/main.bicep");',
      'var deployment = Bicep.DeployToResourceGroup("my-rg", "./main.bicep", o => o\n  .WithParameterFile("./params.json")\n  .WithDeploymentSlot("staging"));\nEf.DatabaseUpdate(db, deployment.Output("sqlConnectionString"));',
    ],
  },
  {
    group: "Bicep",
    name: "Bicep.DeployToSubscription",
    desc: 'Deploy a Bicep template at subscription scope. Returns a `BicepDeployment` with typed access to outputs via `deployment.Output("name")`.',
    examples: [
      'var deployment = Bicep.DeployToSubscription("eastus", "./infra/sub.bicep");',
      'var deployment = Bicep.DeployToSubscription("eastus", "./sub.bicep", o => o\n  .WithParameter("environment", "prod"));\nvar resourceGroup = deployment.Output("resourceGroupName");',
    ],
  },
  {
    group: "Bicep",
    name: "Bicep.WhatIf",
    desc: "Preview what would be deployed (what-if analysis).",
    examples: [
      'Bicep.WhatIf("my-rg", "./infra/main.bicep");',
      'Bicep.WhatIf("my-rg", "./main.bicep", o => o.WithParameterFile("./params.json"));',
    ],
  },
  {
    group: "Bicep",
    name: "Bicep.Build",
    desc: "Compile a Bicep file to ARM JSON template.",
    examples: ['Bicep.Build("./infra/main.bicep");', 'Bicep.Build("./main.bicep", "./output/main.json");'],
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.EnsureAuthenticated",
    desc: "Verify Cloudflare credentials. Prompts interactively if environment variables are not set. See [authentication](/providers/cloudflare#authentication) for setup.",
    examples: ["Cloudflare.EnsureAuthenticated();"],
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.PagesDeploy",
    desc: "Deploy a directory to Cloudflare Pages. The directory should be the build output folder.",
    examples: [
      'var frontend = Directory("./frontend");',
      'Cloudflare.PagesDeploy(frontend / "dist", "my-site");',
      'Cloudflare.PagesDeploy(frontend / "build", o => o\n  .WithProjectName("my-site")\n  .WithBranch("main"));',
    ],
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.PagesListProjects",
    desc: "List all Cloudflare Pages projects.",
    examples: ["Cloudflare.PagesListProjects();"],
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.PagesCreateProject",
    desc: "Create a new Cloudflare Pages project.",
    examples: ['Cloudflare.PagesCreateProject("my-site");', 'Cloudflare.PagesCreateProject("my-site", "develop");'],
  },
  {
    group: "Cloudflare",
    name: "Cloudflare.PagesListDeployments",
    desc: "List deployments for a Cloudflare Pages project.",
    examples: [
      'Cloudflare.PagesListDeployments("my-site");',
      "Cloudflare.PagesListDeployments(); // Uses CLOUDFLARE_PROJECT_NAME env var",
    ],
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
  },
  {
    group: "Functions",
    name: "Functions.DeployZip",
    desc: "Deploy a function app using zip deploy.",
    examples: [
      'Functions.DeployZip("my-func", "./publish.zip");',
      'Functions.DeployZip("my-func", "./publish.zip", "my-rg", o => o\n  .WithDeploymentSlot("staging"));',
    ],
  },
  {
    group: "Functions",
    name: "Functions.Publish",
    desc: "Publish using Azure Functions Core Tools.",
    examples: [
      'Functions.Publish("my-func");',
      'Functions.Publish("my-func", "./src/MyFunc", o => o\n  .WithDeploymentSlot("staging")\n  .WithConfiguration("Release"));',
    ],
  },
  {
    group: "Functions",
    name: "Functions.DeployWithSwap",
    desc: "Deploy to a slot then swap to production (zero-downtime).",
    examples: [
      'Functions.DeployWithSwap("my-func", "./publish.zip");',
      'Functions.DeployWithSwap("my-func", "./publish.zip", "staging", "my-rg");',
    ],
  },
  {
    group: "Functions",
    name: "Functions.SwapSlots",
    desc: "Swap deployment slots.",
    examples: [
      'Functions.SwapSlots("my-func", "staging");',
      'Functions.SwapSlots("my-func", "staging", "my-rg", "production");',
    ],
  },
  {
    group: "Functions",
    name: "Functions.Restart",
    desc: "Restart a function app.",
    examples: ['Functions.Restart("my-func");', 'Functions.Restart("my-func", "my-rg", "staging");'],
  },
  {
    group: "Functions",
    name: "Functions.Start",
    desc: "Start a function app.",
    examples: ['Functions.Start("my-func");', 'Functions.Start("my-func", slot: "staging");'],
  },
  {
    group: "Functions",
    name: "Functions.Stop",
    desc: "Stop a function app.",
    examples: ['Functions.Stop("my-func");', 'Functions.Stop("my-func", slot: "staging");'],
  },
  {
    group: "AppService",
    name: "AppService.DeployZip",
    desc: "Deploy an app service using zip deploy.",
    examples: [
      'AppService.DeployZip("my-app", "./publish.zip");',
      'AppService.DeployZip("my-app", "./publish.zip", "my-rg", o => o\n  .WithDeploymentSlot("staging"));',
    ],
  },
  {
    group: "AppService",
    name: "AppService.DeployWithSwap",
    desc: "Deploy to a slot then swap to production (zero-downtime).",
    examples: [
      'AppService.DeployWithSwap("my-app", "./publish.zip");',
      'AppService.DeployWithSwap("my-app", "./publish.zip", "staging", "my-rg");',
    ],
  },
  {
    group: "AppService",
    name: "AppService.SwapSlots",
    desc: "Swap deployment slots.",
    examples: [
      'AppService.SwapSlots("my-app", "staging");',
      'AppService.SwapSlots("my-app", "staging", "my-rg", "production");',
    ],
  },
  {
    group: "AppService",
    name: "AppService.CreateSlot",
    desc: "Create a new deployment slot.",
    examples: [
      'AppService.CreateSlot("my-app", "staging");',
      'AppService.CreateSlot("my-app", "staging", "my-rg", "production");',
    ],
  },
  {
    group: "AppService",
    name: "AppService.DeleteSlot",
    desc: "Delete a deployment slot.",
    examples: ['AppService.DeleteSlot("my-app", "staging");', 'AppService.DeleteSlot("my-app", "staging", "my-rg");'],
  },
  {
    group: "AppService",
    name: "AppService.ListSlots",
    desc: "List deployment slots for an app service.",
    examples: ['AppService.ListSlots("my-app");', 'AppService.ListSlots("my-app", "my-rg");'],
  },
  {
    group: "AppService",
    name: "AppService.Restart",
    desc: "Restart an app service.",
    examples: ['AppService.Restart("my-app");', 'AppService.Restart("my-app", "my-rg", "staging");'],
  },
  {
    group: "AppService",
    name: "AppService.Start",
    desc: "Start an app service.",
    examples: ['AppService.Start("my-app");', 'AppService.Start("my-app", slot: "staging");'],
  },
  {
    group: "AppService",
    name: "AppService.Stop",
    desc: "Stop an app service.",
    examples: ['AppService.Stop("my-app");', 'AppService.Stop("my-app", slot: "staging");'],
  },
  {
    group: "Nuget",
    name: "Nuget.EnsureAuthenticated",
    desc: "Ensures NuGet API key is available for publishing. Prompts interactively if `NUGET_API_KEY` environment variable is not set. Call before Push.",
    examples: ['var app = Dotnet.Project("./src/MyLib/MyLib.csproj");\nNuget.EnsureAuthenticated();\nNuget.Push(app);'],
  },
  {
    group: "Nuget",
    name: "Nuget.Pack",
    desc: "Create a NuGet package from a project. **Defaults:** Release config, output to `bin/Release`.",
    examples: [
      'var app = Dotnet.Project("./src/MyLib/MyLib.csproj");\nNuget.Pack(app);',
      'Nuget.Pack(app, o => o.WithVersion("1.0.0"));',
    ],
  },
  {
    group: "Nuget",
    name: "Nuget.Push",
    desc: "Push packages to a feed. Pass a ProjectRef to push from `bin/Release/*.nupkg`, or a path/glob. **Defaults:** NuGet.org. Use <code>SkipDuplicates()</code> to avoid failure if version already exists (shows warning instead).",
    examples: [
      'var app = Dotnet.Project("./src/MyLib/MyLib.csproj");\nNuget.Pack(app);\nNuget.EnsureAuthenticated();\nNuget.Push(app, o => o.SkipDuplicates());',
      'Nuget.Push("./packages/MyLib.1.0.0.nupkg");',
    ],
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
  },
  {
    group: "Git",
    name: "Git.Push",
    desc: "Pushes the current branch to the remote repository. <strong>Runs on host</strong> (not in container).",
    examples: ["Git.Push();", 'Git.Push(o => o.ToRemote("upstream"));', "Git.Push(o => o.WithUpstream()); // -u flag"],
  },
  {
    group: "Git",
    name: "Git.PushTags",
    desc: "Pushes all tags to the remote repository. <strong>Runs on host</strong> (not in container).",
    examples: ["Git.PushTags();", 'Git.PushTags("upstream");'],
  },
  {
    group: "Git",
    name: "Git.Add",
    desc: "Adds files to the git staging area. <strong>Runs on host</strong> (not in container).",
    examples: ['Git.Add("."); // Add all', 'Git.Add("src/", "tests/");'],
  },
  {
    group: "Git",
    name: "Git.Commit",
    desc: "Commits staged changes with a message. <strong>Runs on host</strong> (not in container).",
    examples: ['Git.Commit("Release v1.0.0");', 'Git.Commit("Empty commit", o => o.WithAllowEmpty());'],
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
  },
  {
    group: "GitHub",
    name: "GitHub.PushImage",
    desc: "Pushes a Docker image to GitHub Container Registry (ghcr.io). The image must already be built locally. Automatically handles authentication using `GITHUB_TOKEN` or gh CLI config.",
    examples: [
      'GitHub.PushImage("myapp", o => o.WithTag("latest"));',
      'GitHub.PushImage("myapp", o => o\n  .WithTag("v1.0.0")\n  .WithOwner("my-org"));',
    ],
  },
  // Docker operations
  {
    group: "Docker",
    name: "Docker.Install",
    desc: "Installs the Docker CLI in the container. Required before using <code>Docker.Build</code> when running with <code>--dind</code>. Skips if already installed.",
    examples: ['Docker.Install();\nDocker.Build("Dockerfile", o => o.WithTag("myapp:latest"));'],
  },
  {
    group: "Docker",
    name: "Docker.Build",
    desc: "Builds a Docker image from a Dockerfile. <strong>Requires <code>--dind</code> CLI flag</strong> to mount the Docker socket. Call <code>Docker.Install()</code> first to install the Docker CLI in the container.",
    examples: [
      '// Run with: ando --dind\nDocker.Install();\nDocker.Build("Dockerfile", o => o.WithTag("myapp:latest"));',
      'Docker.Build("./src/MyApp/Dockerfile", o => o\n  .WithTag("myapp:v1.0.0")\n  .WithBuildArg("VERSION", "1.0.0")\n  .WithPlatform("linux/amd64"));',
    ],
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
      grouped[op.group] = [];
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
