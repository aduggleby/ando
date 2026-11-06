// =============================================================================
// AzureVmOperations.cs
//
// Summary: Azure virtual machine operations (Linux run-command).
//
// `az vm run-command invoke` exits 0 even when the script inside the VM fails,
// and returns stdout and stderr inside one JSON message. RunCommand wraps the
// script so its exit code is printed as a marker line, parses the message, and
// fails the step when the script failed.
//
// Design Decisions:
// - The marker is the last stdout line, so it survives run-command's output
//   truncation (it keeps the end of the output)
// - Linux only (RunShellScript)
// =============================================================================

using System.Text.Json;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Azure virtual machine operations.
/// </summary>
public class AzureVmOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    internal const string ExitMarker = "__ANDO_EXIT_CODE=";

    /// <summary>
    /// Runs a shell script on a Linux VM with `az vm run-command invoke` and
    /// fails the step when the script exits non-zero. The returned output's
    /// Value is the script's stdout (without ANDO's exit-code marker).
    /// Usage: Azure.Vm.RunCommand("rg-eu", "docs-vm", "/usr/local/sbin/update-worker pkg.tar.gz");
    /// </summary>
    /// <param name="resourceGroup">Resource group of the VM.</param>
    /// <param name="vmName">VM name.</param>
    /// <param name="script">Shell script run as root on the VM.</param>
    /// <param name="timeout">Step timeout (default 30 minutes).</param>
    public StepOutput RunCommand(string resourceGroup, string vmName, string script, TimeSpan? timeout = null)
    {
        var output = new StepOutput("Azure.Vm.RunCommand");
        var timeoutMs = (int)(timeout ?? TimeSpan.FromMinutes(30)).TotalMilliseconds;

        Registry.Register("Azure.Vm.RunCommand", async () =>
        {
            var result = await ExecutorFactory().ExecuteAsync("az",
            [
                "vm", "run-command", "invoke",
                "--resource-group", resourceGroup,
                "--name", vmName,
                "--command-id", "RunShellScript",
                "--scripts", WrapScript(script),
                "--output", "json"
            ], new CommandOptions { SuppressOutput = true, TimeoutMs = timeoutMs });

            if (!result.Success)
            {
                Logger.Error($"    az vm run-command failed: {result.Error ?? result.Output}");
                output.Set(result.Output, result.ExitCode);
                return false;
            }

            var (stdout, stderr, exitCode) = ParseResult(result.Output ?? "");
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Logger.Info(line);
            }
            foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                Logger.Warning(line);
            }

            output.Set(stdout, exitCode ?? -1);

            if (exitCode == null)
            {
                Logger.Error("    The script's exit code is missing from the run-command output.");
                return false;
            }
            if (exitCode != 0)
            {
                Logger.Error($"    The script on {vmName} exited with {exitCode}.");
                return false;
            }
            return true;
        }, $"{resourceGroup}/{vmName}");

        return output;
    }

    // Runs the script in a subshell so `exit` inside it still reaches the marker,
    // and starts the marker on a new line even when stdout has no trailing newline.
    internal static string WrapScript(string script) =>
        $"(\n{script}\n)\n__ando_rc=$?\necho\necho \"{ExitMarker}$__ando_rc\"";

    // Parses run-command JSON: value[0].message is
    // "Enable succeeded: \n[stdout]\n...\n[stderr]\n...".
    internal static (string Stdout, string Stderr, int? ExitCode) ParseResult(string json)
    {
        var message = "";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("value", out var value) &&
                value.ValueKind == JsonValueKind.Array)
            {
                message = string.Join("\n", value.EnumerateArray()
                    .Select(v => v.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .Where(m => m != null));
            }
        }
        catch (JsonException)
        {
            message = json;
        }

        var stdout = message;
        var stderr = "";
        var stdoutStart = message.IndexOf("[stdout]", StringComparison.Ordinal);
        var stderrStart = message.IndexOf("[stderr]", StringComparison.Ordinal);
        if (stdoutStart >= 0)
        {
            var end = stderrStart > stdoutStart ? stderrStart : message.Length;
            stdout = message[(stdoutStart + "[stdout]".Length)..end];
        }
        if (stderrStart >= 0)
        {
            stderr = message[(stderrStart + "[stderr]".Length)..].Trim();
        }

        int? exitCode = null;
        var lines = stdout.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        for (var i = lines.Count - 1; i >= 0; i--)
        {
            if (lines[i].StartsWith(ExitMarker, StringComparison.Ordinal))
            {
                if (int.TryParse(lines[i][ExitMarker.Length..], out var code))
                {
                    exitCode = code;
                }
                lines.RemoveAt(i);
                break;
            }
        }

        return (string.Join("\n", lines).Trim(), stderr, exitCode);
    }
}
