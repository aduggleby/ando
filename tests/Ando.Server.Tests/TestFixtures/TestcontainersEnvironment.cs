// =============================================================================
// TestcontainersEnvironment.cs
//
// Summary: Configures Testcontainers for test processes that run inside a
// container sharing the host's Docker daemon (the ANDO build container).
//
// When `dotnet test` runs inside the ANDO build container (--dind), the Docker
// socket belongs to the host daemon, so every container Testcontainers starts
// lives on the host. Ports it publishes are reachable from the test process only
// through host.docker.internal, never through localhost. Without this setup the
// resource reaper (Ryuk) is started on the host but contacted on localhost, its
// initialization times out, and every container-backed test fails before any
// database is started (ANDO build log, 2026-10-13).
//
// Design Decisions:
// - A module initializer runs before any fixture touches Testcontainers, which
//   reads its settings once; setting them later has no effect.
// - Only applies inside a container (/.dockerenv). Developer machines are unchanged.
// - Explicit environment variables win: TESTCONTAINERS_HOST_OVERRIDE and
//   TESTCONTAINERS_RYUK_DISABLED are honored by Testcontainers itself, so this
//   code only fills in defaults when they are unset.
// - The reaper is disabled inside containers because it is contacted the same way
//   (localhost) and its socket mount assumes the host path. Isolated container mode
//   (the default inside containers, see DatabaseTestContainerMode) stops and removes
//   its own containers, so the reaper is only lost as a safety net after a hard kill.
// - Inside a container the databases are reached DIRECTLY over Docker networking
//   (container IP and container port) rather than through the host's published port:
//   host firewalls (ufw on developer machines, nftables default-drop on the prod1 VMs)
//   block container-to-host traffic, so even host.docker.internal is not reliable.
//   For that the database container must share a network with the test runner:
//   GetRunnerNetworkNameAsync inspects the runner's own container and the fixtures
//   attach their containers to that network (nothing to do on the default bridge).
// =============================================================================

using System.Diagnostics;
using System.Runtime.CompilerServices;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// Applies the Testcontainers settings needed when the tests themselves run in a container
/// that shares the host's Docker daemon.
/// </summary>
public static class TestcontainersEnvironment
{
    /// <summary>
    /// Host name through which containers started on the host daemon are reachable from
    /// inside another container (Docker Desktop and docker run --add-host host-gateway).
    /// </summary>
    public const string HostGatewayName = "host.docker.internal";

    /// <summary>
    /// Returns true when the current process runs inside a container.
    /// </summary>
    /// <returns>Whether /.dockerenv exists.</returns>
    public static bool IsInsideContainer() => File.Exists("/.dockerenv");

    private static readonly Lazy<Task<string?>> RunnerNetwork = new(DiscoverRunnerNetworkAsync);

    /// <summary>
    /// Name of the Docker network the test runner container is attached to, or null when the
    /// tests do not run inside a container or the runner sits on the default bridge (where every
    /// Testcontainers container lands anyway). Fixtures attach their containers to this network
    /// so <see cref="DirectEndpoint"/> is reachable.
    /// </summary>
    /// <returns>The network name or null.</returns>
    public static Task<string?> GetRunnerNetworkNameAsync() => RunnerNetwork.Value;

    /// <summary>
    /// Returns the address to connect to: inside a container the container's own IP and the
    /// container port (direct Docker networking), otherwise the host and the mapped port.
    /// </summary>
    /// <param name="container">The started container.</param>
    /// <param name="containerPort">The service port inside the container.</param>
    /// <returns>Host and port to use in the connection string.</returns>
    public static (string Host, int Port) DirectEndpoint(IContainer container, int containerPort) =>
        IsInsideContainer()
            ? (container.IpAddress, containerPort)
            : (container.Hostname, container.GetMappedPublicPort(containerPort));

    // Inspects the runner's own container (its hostname is the container id) with the docker
    // CLI, which the ANDO build container has (Docker.Install) and shared mode already uses.
    // Any failure means "no network", which keeps the default-bridge behavior rather than
    // failing every fixture at once.
    private static async Task<string?> DiscoverRunnerNetworkAsync()
    {
        if (!IsInsideContainer())
        {
            return null;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            psi.ArgumentList.Add("inspect");
            psi.ArgumentList.Add("-f");
            psi.ArgumentList.Add("{{range $k, $v := .NetworkSettings.Networks}}{{$k}} {{end}}");
            psi.ArgumentList.Add(Environment.MachineName);

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("docker could not be started");
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(error.Trim());
            }

            var networks = output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var name = networks.FirstOrDefault(n => n != "bridge") ?? networks.FirstOrDefault();
            return name is null or "bridge" ? null : name;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TestcontainersEnvironment: could not inspect the runner container ({ex.GetType().Name}: {ex.Message}); assuming the default bridge.");
            return null;
        }
    }

    // Runs once when the test assembly loads, before any fixture constructs a container.
    [ModuleInitializer]
    internal static void Configure()
    {
        if (!IsInsideContainer())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TESTCONTAINERS_HOST_OVERRIDE")))
        {
            TestcontainersSettings.DockerHostOverride = HostGatewayName;
        }

        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TESTCONTAINERS_RYUK_DISABLED")))
        {
            TestcontainersSettings.ResourceReaperEnabled = false;
        }
    }
}
