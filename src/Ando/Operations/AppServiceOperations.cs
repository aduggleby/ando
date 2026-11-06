// =============================================================================
// AppServiceOperations.cs
//
// Summary: Provides Azure App Service deployment operations for build scripts.
//
// AppServiceOperations handles deploying Azure App Services using Azure CLI.
// Supports zip deploy, slot deployments, slot swapping, and app lifecycle
// management.
//
// Architecture:
// - Zip deploy uses `az webapp deployment source config-zip`
// - Slot swapping uses `az webapp deployment slot swap`
// - Authentication relies on prior Azure.Login* steps
//
// Design Decisions:
// - Follows OperationsBase pattern for step registration
// - Deployment slot is a first-class option for staging deployments
// - Resource group is optional for CLI commands (uses app's default)
// - Consistent API with FunctionsOperations for familiarity
// =============================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Provides Azure App Service deployment operations for build scripts.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class AppServiceOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    Func<TimeSpan, Task>? delayAsync = null)
    : OperationsBase(registry, logger, executorFactory)
{
    private const int MaxZipDeployAttempts = 3;
    private const int MaxZipDeployTimeoutAttempts = 2;
    private static readonly Regex IsoTimestampPattern = new(
        @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})",
        RegexOptions.Compiled);
    private readonly Func<TimeSpan, Task> _delayAsync = delayAsync ?? Task.Delay;

    /// <summary>
    /// Registers a step to deploy an app service using zip deploy.
    /// Requires the app service to already exist in Azure.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="zipPath">Path to the deployment zip file.</param>
    /// <param name="resourceGroup">Optional resource group name. If null, Azure CLI will determine it.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public void DeployZip(string appName, string zipPath,
        string? resourceGroup = null, Action<AppServiceDeployOptions>? configure = null)
    {
        var options = new AppServiceDeployOptions();
        configure?.Invoke(options);

        Registry.Register("AppService.DeployZip",
            () => ExecuteZipDeployAsync(appName, zipPath, resourceGroup, options),
            appName);
    }

    /// <summary>
    /// Registers steps to zip a published folder and deploy it with zip deploy.
    /// Use this instead of DeployZip when you have a publish directory rather than a
    /// pre-built zip: it archives the folder's CONTENTS at the archive root (no
    /// nested top-level directory), which is the layout config-zip expects. The zip
    /// step runs in the build container and installs the zip tool if the image does
    /// not already provide it (apt-get; Debian and Ubuntu images). For reproducible
    /// builds, zip at build time and use DeployZip. Deploys use
    /// `az webapp deployment source config-zip`.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="folderPath">Path to the published folder to deploy.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="configure">Optional deployment options (slot, restart, no-wait).</param>
    public void DeployFolder(string appName, string folderPath,
        string? resourceGroup = null, Action<AppServiceDeployOptions>? configure = null)
    {
        var folder = folderPath.TrimEnd('/', '\\');
        var zipPath = $"{folder}.zip";

        // Archive the folder contents at the root (cd into the folder, zip "."), so
        // the resulting zip has no nested top-level directory. Written as a sibling
        // file (folder.zip) so it is not included in its own archive.
        RegisterCommand("AppService.ZipFolder", "sh",
            () => new ArgumentBuilder()
                .Add("-c",
                    $"command -v zip >/dev/null 2>&1 || (apt-get update -qq && apt-get install -y -qq zip); cd '{folder}' && zip -rq '{zipPath}' ."),
            folder);

        DeployZip(appName, zipPath, resourceGroup, configure);
    }

    /// <summary>
    /// Registers a step to deploy to a specific slot and then swap to production.
    /// This enables zero-downtime deployments.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="zipPath">Path to the deployment zip file.</param>
    /// <param name="slotName">Staging slot name (defaults to "staging").</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void DeployWithSwap(string appName, string zipPath,
        string slotName = "staging", string? resourceGroup = null)
    {
        // Step 1: Deploy to the staging slot
        DeployZip(appName, zipPath, resourceGroup, opt => opt.WithDeploymentSlot(slotName));

        // Step 2: Swap staging to production
        SwapSlots(appName, slotName, resourceGroup);
    }

    /// <summary>
    /// Registers a step to swap deployment slots. A swap warms up the slot and can
    /// take several minutes, so it defaults to a 30 minute timeout (or
    /// Ando.CommandTimeout when that is longer); .WithTimeout(...) on the result
    /// overrides both.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="sourceSlot">Source slot name to swap from.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="targetSlot">Target slot name (defaults to "production").</param>
    public CommandStep SwapSlots(string appName, string sourceSlot,
        string? resourceGroup = null, string targetSlot = "production") =>
        AzStep("AppService.SwapSlots", $"{sourceSlot} -> {targetSlot}",
            new ArgumentBuilder()
                .Add("webapp", "deployment", "slot", "swap")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .Add("--slot", sourceSlot)
                .Add("--target-slot", targetSlot)
                .Add("--output", "none"),
            Math.Max(SwapDefaultTimeoutMs, Registry.DefaultCommandTimeoutMs ?? 0));

    private const int SwapDefaultTimeoutMs = 30 * 60 * 1000;

    /// <summary>Registers a step to restart an app service.</summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, restarts the production slot.</param>
    public CommandStep Restart(string appName, string? resourceGroup = null, string? slot = null) =>
        AzStep("AppService.Restart", appName, WebappAction("restart", appName, resourceGroup, slot));

    /// <summary>Registers a step to stop an app service.</summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, stops the production slot.</param>
    public CommandStep Stop(string appName, string? resourceGroup = null, string? slot = null) =>
        AzStep("AppService.Stop", appName, WebappAction("stop", appName, resourceGroup, slot));

    /// <summary>Registers a step to start an app service.</summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, starts the production slot.</param>
    public CommandStep Start(string appName, string? resourceGroup = null, string? slot = null) =>
        AzStep("AppService.Start", appName, WebappAction("start", appName, resourceGroup, slot));

    private static ArgumentBuilder WebappAction(string action, string appName, string? resourceGroup, string? slot) =>
        new ArgumentBuilder()
            .Add("webapp", action)
            .Add("--name", appName)
            .AddIfNotNull("--resource-group", resourceGroup)
            .AddIfNotNull("--slot", slot)
            .Add("--output", "none");

    /// <summary>
    /// Registers a step that reads the app's (or slot's) `az webapp show` JSON.
    /// Read fields at execution time with Json(), e.g. show.Json("state").
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, shows the production slot.</param>
    public CommandStep Show(string appName, string? resourceGroup = null, string? slot = null) =>
        AzStep("AppService.Show", appName,
            new ArgumentBuilder()
                .Add("webapp", "show", "--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "json"))
            .Quiet();

    /// <summary>
    /// Registers a step that reads the app's (or slot's) default host name, e.g.
    /// "myapp-release.azurewebsites.net".
    /// Usage: var host = AppService.GetHostName(app, rg, "release");
    ///        Http.WaitForHealthy(() => $"https://{host.Value}/healthz");
    /// </summary>
    public CommandStep GetHostName(string appName, string? resourceGroup = null, string? slot = null) =>
        AzStep("AppService.GetHostName", appName,
            new ArgumentBuilder()
                .Add("webapp", "show", "--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--query", "defaultHostName", "--output", "tsv"));

    /// <summary>
    /// Registers a step that reads the app's (or slot's) access restrictions
    /// (`az webapp config access-restriction show`).
    /// Usage: var rules = AppService.AccessRestrictions(app, rg, "release");
    ///        Ando.When(() => rules.Json("ipSecurityRestrictionsDefaultAction") == "Deny", ...);
    /// </summary>
    public CommandStep AccessRestrictions(string appName, string? resourceGroup = null, string? slot = null) =>
        AzStep("AppService.AccessRestrictions", appName,
            new ArgumentBuilder()
                .Add("webapp", "config", "access-restriction", "show", "--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "json"))
            .Quiet();

    /// <summary>
    /// Adds an Allow access rule for one IPv4 address to the app (or slot) and
    /// removes it again when the enclosing block (or the run) ends, whatever
    /// happened: the removal is registered as a finally step, followed by a
    /// check that the rule is gone. Any existing rule with the same name is
    /// replaced. Without <paramref name="ip"/>, uses this machine's public IPv4.
    /// Usage: AppService.TemporaryAccessRule(app, rg, "release", "deploy-gate");
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Resource group name.</param>
    /// <param name="slot">Slot name, or null for the production slot.</param>
    /// <param name="ruleName">Rule name (default "ando-deploy").</param>
    /// <param name="ip">IPv4 address or CIDR; null looks up this machine's public IPv4.</param>
    /// <param name="priority">Rule priority (default 50; lower runs first).</param>
    public void TemporaryAccessRule(string appName, string resourceGroup, string? slot = null,
        string ruleName = "ando-deploy", string? ip = null, int priority = 50)
    {
        var target = slot != null ? $"{appName}/{slot}" : appName;

        // Only remove the rule in cleanup when this run tried to add it, so a
        // failure before this step never deletes someone else's rule.
        var attempted = false;

        ArgumentBuilder Base(string action) => new ArgumentBuilder()
            .Add("webapp", "config", "access-restriction", action)
            .Add("--name", appName, "--resource-group", resourceGroup)
            .AddIfNotNull("--slot", slot);

        Registry.Register("AppService.TemporaryAccessRule", async () =>
        {
            var address = ip ?? await HttpOperations.LookUpPublicIpV4Async(Logger);
            if (address == null)
            {
                return false;
            }
            if (!address.Contains('/'))
            {
                address += "/32";
            }

            var executor = ExecutorFactory();
            attempted = true;
            // Replace a leftover rule with the same name (ignore "not found").
            await executor.ExecuteAsync("az", Base("remove").Add("--rule-name", ruleName, "--output", "none").Build(),
                new CommandOptions { SuppressOutput = true });

            var added = await executor.ExecuteAsync("az", Base("add")
                .Add("--rule-name", ruleName, "--priority", priority.ToString())
                .Add("--action", "Allow", "--ip-address", address, "--output", "none").Build());
            if (added.Success)
            {
                Logger.Info($"    Allowed {address} on {target} (rule {ruleName}); it is removed when the block ends.");
            }
            return added.Success;
        }, $"{target} {ruleName}");

        // Always remove the rule and verify it is gone, at the end of the enclosing block.
        Registry.RegisterFinally(AndoOperations.CreateFinallyStep(Registry, () =>
        {
            Registry.Register("AppService.RemoveAccessRule", async () =>
            {
                if (!attempted)
                {
                    Logger.Info($"    Rule {ruleName} was never added to {target}; nothing to remove.");
                    return true;
                }

                var executor = ExecutorFactory();
                await executor.ExecuteAsync("az", Base("remove").Add("--rule-name", ruleName, "--output", "none").Build(),
                    new CommandOptions { SuppressOutput = true });

                var remaining = await executor.ExecuteAsync("az", Base("show")
                    .Add("--query", "ipSecurityRestrictions[].name", "--output", "tsv").Build(),
                    new CommandOptions { SuppressOutput = true });
                if (!remaining.Success)
                {
                    Logger.Error($"    Could not verify that rule {ruleName} is gone from {target}: {remaining.Error ?? remaining.Output}");
                    return false;
                }

                var names = (remaining.Output ?? "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (names.Contains(ruleName))
                {
                    Logger.Error($"    Access rule {ruleName} is still on {target}. Remove it: az webapp config access-restriction remove " +
                                 $"-g {resourceGroup} -n {appName}{(slot != null ? $" --slot {slot}" : "")} --rule-name {ruleName}");
                    return false;
                }

                Logger.Info($"    Removed access rule {ruleName} from {target}.");
                return true;
            }, $"{target} {ruleName}");
        }));
    }

    private CommandStep AzStep(string stepName, string context, ArgumentBuilder args, int? defaultTimeoutMs = null) =>
        ShellCommandOperations.RegisterCommandStep(Registry, Logger, ExecutorFactory, stepName, "az", args.Build(), context,
            defaultTimeoutMs);

    /// <summary>
    /// Registers a step to list deployment slots for an app service.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void ListSlots(string appName, string? resourceGroup = null)
    {
        RegisterCommand("AppService.ListSlots", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "deployment", "slot", "list")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup),
            appName);
    }

    /// <summary>
    /// Registers a step to create a new deployment slot.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="slotName">Name for the new slot.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="configurationSource">Optional slot to clone configuration from.</param>
    public void CreateSlot(string appName, string slotName,
        string? resourceGroup = null, string? configurationSource = null)
    {
        RegisterCommand("AppService.CreateSlot", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "deployment", "slot", "create")
                .Add("--name", appName)
                .Add("--slot", slotName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--configuration-source", configurationSource)
                .Add("--output", "none"),
            slotName);
    }

    /// <summary>
    /// Registers a step to delete a deployment slot.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="slotName">Name of the slot to delete.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void DeleteSlot(string appName, string slotName, string? resourceGroup = null)
    {
        RegisterCommand("AppService.DeleteSlot", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "deployment", "slot", "delete")
                .Add("--name", appName)
                .Add("--slot", slotName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .Add("--output", "none"),
            slotName);
    }

    /// <summary>
    /// Builds the argument array for zip deploy command.
    /// </summary>
    private static ArgumentBuilder BuildZipDeployArgs(
        string appName,
        string zipPath,
        string? resourceGroup,
        AppServiceDeployOptions options)
    {
        return new ArgumentBuilder()
            .Add("webapp", "deployment", "source", "config-zip")
            .Add("--name", appName)
            .AddIfNotNull("--resource-group", resourceGroup)
            .Add("--src", zipPath)
            .AddIfNotNull("--slot", options.DeploymentSlot)
            .AddFlag(options.NoWait, "--async")
            .Add("--output", "none");
    }

    private async Task<bool> ExecuteZipDeployAsync(
        string appName,
        string zipPath,
        string? resourceGroup,
        AppServiceDeployOptions options)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var timeoutAttempts = 0;

        for (var attempt = 1; attempt <= MaxZipDeployAttempts; attempt++)
        {
            var timeoutMs = RemainingTimeoutMs(startedAt, options.TimeoutMs);
            if (timeoutMs <= 0)
            {
                Logger.Error($"App Service zip deploy timed out after {options.TimeoutMs}ms.");
                return false;
            }

            var result = await ExecutorFactory().ExecuteAsync(
                "az",
                BuildZipDeployArgs(appName, zipPath, resourceGroup, options).Build(),
                new CommandOptions { TimeoutMs = timeoutMs });

            if (result.Success)
            {
                return await RestartAfterDeployAsync(appName, resourceGroup, options);
            }

            if (IsTimeout(result))
            {
                timeoutAttempts++;
                Logger.Warning("App Service zip deploy timed out. Checking latest Kudu deployment status before retrying...");

                if (await LatestDeploymentSucceededAsync(appName, resourceGroup, options.DeploymentSlot, startedAt))
                {
                    Logger.Info("Latest Kudu deployment completed successfully after the zip deploy command timed out.");
                    return await RestartAfterDeployAsync(appName, resourceGroup, options);
                }

                if (timeoutAttempts >= MaxZipDeployTimeoutAttempts ||
                    attempt >= MaxZipDeployAttempts ||
                    RemainingTimeoutMs(startedAt, options.TimeoutMs) <= 0)
                {
                    Logger.Error("App Service zip deploy timed out and the latest Kudu deployment status did not confirm success.");
                    return false;
                }

                Logger.Warning("Retrying App Service zip deploy after timeout reconciliation did not confirm success...");
                continue;
            }

            if (IsTransientZipDeployFailure(result) && attempt < MaxZipDeployAttempts)
            {
                var delay = TimeSpan.FromSeconds(attempt * 10);
                Logger.Warning($"App Service zip deploy failed with a transient error. Retrying in {delay.TotalSeconds:0}s...");
                await _delayAsync(delay);
                continue;
            }

            return false;
        }

        return false;
    }

    // WithRestart: restart the same app/slot once the zip deploy succeeded.
    private async Task<bool> RestartAfterDeployAsync(
        string appName,
        string? resourceGroup,
        AppServiceDeployOptions options)
    {
        if (options.Restart && options.NoWait)
        {
            Logger.Warning("WithNoWait and WithRestart are both set: the restart may happen before the async deploy finishes.");
        }

        if (!options.Restart)
        {
            return true;
        }

        Logger.Info($"    Restarting {appName}{(options.DeploymentSlot != null ? $" ({options.DeploymentSlot})" : "")} after deployment...");
        var result = await ExecutorFactory().ExecuteAsync(
            "az",
            WebappAction("restart", appName, resourceGroup, options.DeploymentSlot).Build(),
            new CommandOptions { TimeoutMs = Registry.DefaultCommandTimeoutMs ?? CommandOptions.DefaultTimeoutMs });
        return result.Success;
    }

    private async Task<bool> LatestDeploymentSucceededAsync(
        string appName,
        string? resourceGroup,
        string? slot,
        DateTimeOffset startedAt)
    {
        var result = await ExecutorFactory().ExecuteAsync(
            "az",
            BuildDeploymentStatusArgs(appName, resourceGroup, slot).Build(),
            new CommandOptions { TimeoutMs = 60_000, SuppressOutput = true });

        if (!result.Success)
        {
            Logger.Warning("Unable to query latest Kudu deployment status after zip deploy timeout.");
            return false;
        }

        return DeploymentStatusIndicatesSuccess(result.Output, startedAt);
    }

    private static ArgumentBuilder BuildDeploymentStatusArgs(
        string appName,
        string? resourceGroup,
        string? slot)
    {
        return new ArgumentBuilder()
            .Add("webapp", "log", "deployment", "show")
            .Add("--name", appName)
            .AddIfNotNull("--resource-group", resourceGroup)
            .AddIfNotNull("--slot", slot);
    }

    private static int RemainingTimeoutMs(DateTimeOffset startedAt, int timeoutMs)
    {
        var elapsedMs = (int)(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
        return Math.Max(0, timeoutMs - elapsedMs);
    }

    private static bool IsTimeout(CommandResult result) =>
        result.Error?.Contains("timed out", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsTransientZipDeployFailure(CommandResult result)
    {
        var text = $"{result.Error}\n{result.Output}";
        return Regex.IsMatch(text, @"\b5\d\d\b", RegexOptions.IgnoreCase) ||
               text.Contains("Service Unavailable", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("connection reset", StringComparison.OrdinalIgnoreCase) ||
               text.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool DeploymentStatusIndicatesSuccess(string? output, DateTimeOffset startedAt)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var indicatesSuccess =
            output.Contains("Deployment successful", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(output, @"""?status""?\s*:\s*""?(?:4|success|complete|completed)""?", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(output, @"\bstatus\s*[:=]\s*(?:success|complete|completed)\b", RegexOptions.IgnoreCase);

        if (!indicatesSuccess)
        {
            return false;
        }

        var timestamps = IsoTimestampPattern.Matches(output)
            .Select(match => match.Value)
            .Select(value => DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var parsed)
                ? parsed
                : (DateTimeOffset?)null)
            .Where(parsed => parsed.HasValue)
            .Select(parsed => parsed!.Value);

        return !timestamps.Any() || timestamps.Any(timestamp => timestamp >= startedAt);
    }
}
