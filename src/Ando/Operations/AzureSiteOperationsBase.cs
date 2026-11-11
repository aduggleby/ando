// =============================================================================
// AzureSiteOperationsBase.cs
//
// Summary: Shared implementation behind AppService.* (`az webapp`) and
// Functions.* (`az functionapp`).
//
// Both Azure CLI command groups take the same arguments for zip deploy, slot
// management, restart/stop/start, show, access restrictions and slot swaps, so
// the step builders live here once and the two public operation classes only
// supply the command group ("webapp" or "functionapp"), the step-name prefix and
// the label used in log messages.
//
// Design Decisions:
// - Step names stay "<Prefix>.<Operation>" so existing scripts and logs are unchanged.
// - Zip deploy retries transient failures and reconciles timeouts against the
//   latest deployment log, because OneDeploy can time out client-side while the
//   deployment still succeeds on the server.
// =============================================================================

using System.Globalization;
using System.Text.RegularExpressions;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>Zip deploy settings shared by AppService and Functions deploy options.</summary>
internal sealed record SiteZipDeploy(string? Slot, bool Restart, bool NoWait, int TimeoutMs, bool TimeoutExplicit);

/// <summary>
/// Common `az webapp` / `az functionapp` step builders. Not part of the script API.
/// </summary>
public abstract class AzureSiteOperationsBase(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    string group,
    string stepPrefix,
    string label,
    Func<TimeSpan, Task>? delayAsync)
    : OperationsBase(registry, logger, executorFactory)
{
    private const int MaxZipDeployAttempts = 3;
    private const int MaxZipDeployTimeoutAttempts = 2;
    private const int SwapDefaultTimeoutMs = 30 * 60 * 1000;
    private static readonly Regex IsoTimestampPattern = new(
        @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})",
        RegexOptions.Compiled);
    private readonly Func<TimeSpan, Task> _delayAsync = delayAsync ?? Task.Delay;

    internal CommandStep AzStep(string stepName, string context, ArgumentBuilder args, int? defaultTimeoutMs = null) =>
        ShellCommandOperations.RegisterCommandStep(Registry, Logger, ExecutorFactory, stepName, "az", args.Build(), context,
            defaultTimeoutMs);

    private string Step(string operation) => $"{stepPrefix}.{operation}";

    // ---- Deploy ------------------------------------------------------------

    internal void RegisterZipDeploy(string appName, string zipPath, string? resourceGroup, SiteZipDeploy options) =>
        Registry.Register(Step("DeployZip"),
            () => ExecuteZipDeployAsync(appName, zipPath, resourceGroup, options),
            appName);

    internal ArgumentBuilder BuildZipDeployArgs(
        string appName, string zipPath, string? resourceGroup, SiteZipDeploy options, string? timeoutMs = null) =>
        new ArgumentBuilder()
            .Add(group, "deploy")
            .Add("--name", appName)
            .AddIfNotNull("--resource-group", resourceGroup)
            .Add("--src-path", zipPath)
            .Add("--type", "zip")
            .AddIfNotNull("--slot", options.Slot)
            // OneDeploy restarts the site by default; only restart on request, so a
            // stopped slot is not started by the deploy.
            .Add("--restart", options.Restart ? "true" : "false")
            .Add("--async", options.NoWait ? "true" : "false")
            // az's own deployment timeout is only set when a timeout was requested.
            .AddIfNotNull("--timeout", timeoutMs)
            .Add("--output", "none");

    private async Task<bool> ExecuteZipDeployAsync(
        string appName, string zipPath, string? resourceGroup, SiteZipDeploy options)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var timeoutAttempts = 0;

        for (var attempt = 1; attempt <= MaxZipDeployAttempts; attempt++)
        {
            var timeoutMs = RemainingTimeoutMs(startedAt, options.TimeoutMs);
            if (timeoutMs <= 0)
            {
                Logger.Error($"{label} zip deploy timed out after {options.TimeoutMs}ms.");
                return false;
            }

            var result = await ExecutorFactory().ExecuteAsync(
                "az",
                BuildZipDeployArgs(appName, zipPath, resourceGroup, options,
                    options.TimeoutExplicit ? Math.Max(1_000, timeoutMs - 10_000).ToString(CultureInfo.InvariantCulture) : null).Build(),
                new CommandOptions { TimeoutMs = timeoutMs });

            if (result.Success)
            {
                return true;
            }

            if (IsTimeout(result))
            {
                timeoutAttempts++;
                Logger.Warning($"{label} zip deploy timed out. Checking latest Kudu deployment status before retrying...");

                if (await LatestDeploymentSucceededAsync(appName, resourceGroup, options.Slot, startedAt))
                {
                    Logger.Info("Latest Kudu deployment completed successfully after the zip deploy command timed out.");
                    return true;
                }

                if (timeoutAttempts >= MaxZipDeployTimeoutAttempts ||
                    attempt >= MaxZipDeployAttempts ||
                    RemainingTimeoutMs(startedAt, options.TimeoutMs) <= 0)
                {
                    Logger.Error($"{label} zip deploy timed out and the latest Kudu deployment status did not confirm success.");
                    return false;
                }

                Logger.Warning($"Retrying {label} zip deploy after timeout reconciliation did not confirm success...");
                continue;
            }

            if (IsTransientZipDeployFailure(result) && attempt < MaxZipDeployAttempts)
            {
                var delay = TimeSpan.FromSeconds(attempt * 10);
                Logger.Warning($"{label} zip deploy failed with a transient error. Retrying in {delay.TotalSeconds:0}s...");
                await _delayAsync(delay);
                continue;
            }

            return false;
        }

        return false;
    }

    private async Task<bool> LatestDeploymentSucceededAsync(
        string appName, string? resourceGroup, string? slot, DateTimeOffset startedAt)
    {
        var result = await ExecutorFactory().ExecuteAsync(
            "az",
            new ArgumentBuilder()
                .Add(group, "log", "deployment", "show")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Build(),
            new CommandOptions { TimeoutMs = 60_000, SuppressOutput = true });

        if (!result.Success)
        {
            Logger.Warning("Unable to query latest Kudu deployment status after zip deploy timeout.");
            return false;
        }

        return DeploymentStatusIndicatesSuccess(result.Output, startedAt);
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

    // ---- Lifecycle, queries, slots ------------------------------------------

    internal CommandStep SwapSlotsStep(string appName, string sourceSlot, string? resourceGroup, string targetSlot) =>
        AzStep(Step("SwapSlots"), $"{sourceSlot} -> {targetSlot}",
            new ArgumentBuilder()
                .Add(group, "deployment", "slot", "swap")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .Add("--slot", sourceSlot)
                .Add("--target-slot", targetSlot)
                .Add("--output", "none"),
            Math.Max(SwapDefaultTimeoutMs, Registry.DefaultCommandTimeoutMs ?? 0));

    internal CommandStep ActionStep(string action, string appName, string? resourceGroup, string? slot) =>
        AzStep(Step(char.ToUpperInvariant(action[0]) + action[1..]), appName,
            new ArgumentBuilder()
                .Add(group, action)
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "none"));

    internal CommandStep ShowStep(string appName, string? resourceGroup, string? slot) =>
        AzStep(Step("Show"), appName,
            new ArgumentBuilder()
                .Add(group, "show", "--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "json"))
            .Quiet();

    internal CommandStep HostNameStep(string appName, string? resourceGroup, string? slot) =>
        AzStep(Step("GetHostName"), appName,
            new ArgumentBuilder()
                .Add(group, "show", "--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--query", "defaultHostName", "--output", "tsv"));

    internal CommandStep AccessRestrictionsStep(string appName, string? resourceGroup, string? slot) =>
        AzStep(Step("AccessRestrictions"), appName,
            new ArgumentBuilder()
                .Add(group, "config", "access-restriction", "show", "--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "json"))
            .Quiet();

    internal void RegisterListSlots(string appName, string? resourceGroup) =>
        RegisterCommand(Step("ListSlots"), "az",
            () => new ArgumentBuilder()
                .Add(group, "deployment", "slot", "list")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup),
            appName);

    internal void RegisterCreateSlot(string appName, string slotName, string? resourceGroup, string? configurationSource) =>
        RegisterCommand(Step("CreateSlot"), "az",
            () => new ArgumentBuilder()
                .Add(group, "deployment", "slot", "create")
                .Add("--name", appName)
                .Add("--slot", slotName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--configuration-source", configurationSource)
                .Add("--output", "none"),
            slotName);

    internal void RegisterDeleteSlot(string appName, string slotName, string? resourceGroup) =>
        RegisterCommand(Step("DeleteSlot"), "az",
            () => new ArgumentBuilder()
                .Add(group, "deployment", "slot", "delete")
                .Add("--name", appName)
                .Add("--slot", slotName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .Add("--output", "none"),
            slotName);

    // ---- Temporary access rule ----------------------------------------------

    internal void RegisterTemporaryAccessRule(string appName, string resourceGroup, string? slot,
        string ruleName, string? ip, int priority)
    {
        var target = slot != null ? $"{appName}/{slot}" : appName;

        // Only remove the rule in cleanup when this run tried to add it, so a
        // failure before this step never deletes someone else's rule.
        var attempted = false;

        ArgumentBuilder Base(string action) => new ArgumentBuilder()
            .Add(group, "config", "access-restriction", action)
            .Add("--name", appName, "--resource-group", resourceGroup)
            .AddIfNotNull("--slot", slot);

        Registry.Register(Step("TemporaryAccessRule"), async () =>
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
            Registry.Register(Step("RemoveAccessRule"), async () =>
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
                    Logger.Error($"    Access rule {ruleName} is still on {target}. Remove it: az {group} config access-restriction remove " +
                                 $"-g {resourceGroup} -n {appName}{(slot != null ? $" --slot {slot}" : "")} --rule-name {ruleName}");
                    return false;
                }

                Logger.Info($"    Removed access rule {ruleName} from {target}.");
                return true;
            }, $"{target} {ruleName}");
        }));
    }
}
