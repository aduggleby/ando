// =============================================================================
// Program.cs
//
// Summary: Application entry point for the ANDO build system CLI.
//
// This file serves as the minimal bootstrap entry point that delegates all
// processing to AndoCli. Using top-level statements for simplicity since
// no initialization logic is needed beyond creating and running the CLI.
//
// Design Decisions:
// - Uses top-level statements (C# 9+) for minimal boilerplate
// - Wraps CLI in 'using' to ensure proper resource cleanup (log file handles)
// - Returns exit code from RunAsync to propagate success/failure to shell
// =============================================================================

using Ando.Cli;

// Create the CLI instance with command-line arguments and ensure cleanup on exit.
// The using statement guarantees Dispose() is called even if an exception occurs,
// which properly flushes and closes the log file.
using var cli = new AndoCli(args);
return await cli.RunAsync();
