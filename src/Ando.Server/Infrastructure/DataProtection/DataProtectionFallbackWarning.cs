// =============================================================================
// DataProtectionFallbackWarning.cs
//
// Summary: Lightweight startup diagnostic payload used when key persistence
// cannot be configured in Testing/E2E environments.
//
// This keeps startup diagnostics explicit while avoiding inline support types
// in Program.cs.
// =============================================================================

namespace Ando.Server.Infrastructure.DataProtection;

internal sealed record DataProtectionFallbackWarning(string KeysPath, Exception Exception);
