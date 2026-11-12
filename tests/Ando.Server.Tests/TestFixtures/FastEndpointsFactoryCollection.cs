// =============================================================================
// FastEndpointsFactoryCollection.cs
//
// Summary: xUnit collection for unit tests that build endpoints with
// FastEndpoints' Factory.Create.
//
// FastEndpoints keeps one process-wide service resolver and Factory.Create only
// creates it when none is set. The WebApplicationFactory tests set it to their
// host's provider and dispose that host, so a later Factory.Create fails with
// ObjectDisposedException ("IServiceProvider"). Classes in this collection run
// after the parallel tests, one at a time, and call ResetResolver() before each
// test so Factory.Create builds a fresh resolver.
// =============================================================================

using System.Reflection;
using FastEndpoints;

namespace Ando.Server.Tests.TestFixtures;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FastEndpointsFactoryCollection
{
    public const string Name = "FastEndpoints Factory";

    /// <summary>
    /// Clears FastEndpoints' internal resolver so the next Factory.Create builds a
    /// fresh one instead of reusing a disposed host provider.
    /// </summary>
    public static void ResetResolver()
    {
        // ServiceResolver is internal, so find it next to the public IServiceResolver.
        var resolverType = typeof(IServiceResolver).Assembly.GetType("FastEndpoints.ServiceResolver");
        var field = resolverType?.GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException(
                "FastEndpoints.ServiceResolver._instance not found; update ResetResolver for this FastEndpoints version.");
        field.SetValue(null, null);
    }
}
