// =============================================================================
// NonInteractiveTests.cs
//
// Summary: Makes every test in this assembly non-interactive.
//
// Design Decisions:
// - EnvironmentHelper.GetRequiredOrPrompt reads the console when stdin is a
//   terminal, so tests that reach it hung when run in a pty. A module
//   initializer turns the interactivity check off for the whole test process.
// - Console.ReadLine reads Console.In, so stdin is replaced by an empty reader (a
//   command that prompts gets end-of-input instead of waiting on the terminal).
//   Console.ReadKey ignores it; the release checklist and the Claude permission
//   prompt check IsInteractive (the .env.ando gitignore and DIND prompts do not).
// - Run locks go to a per-run temp folder (ANDO_LOCK_DIR), never ~/.ando/locks.
// =============================================================================

using System.Runtime.CompilerServices;
using Ando.Utilities;

namespace Ando.Tests.TestFixtures;

internal static class NonInteractiveTests
{
    [ModuleInitializer]
    internal static void Init()
    {
        EnvironmentHelper.IsInteractive = () => false;
        Console.SetIn(TextReader.Null);
        var lockDir = Path.Combine(Path.GetTempPath(), "ando-tests-locks-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable(Ando.Execution.RunLockSet.LockDirEnvVar, lockDir);

        // Remove this run's lock folder when the test process exits.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { if (Directory.Exists(lockDir)) Directory.Delete(lockDir, recursive: true); } catch { }
        };
    }
}
