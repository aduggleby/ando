// =============================================================================
// FileLoggingSettingsTests.cs
//
// Summary: Unit tests for binding the Logging:File section to
// FileLoggingSettings (defaults and environment-variable style keys).
// =============================================================================

using Ando.Server.Configuration;
using Microsoft.Extensions.Configuration;

namespace Ando.Server.Tests.Unit.Configuration;

/// <summary>
/// Tests for <see cref="FileLoggingSettings"/>.
/// </summary>
public class FileLoggingSettingsTests
{
    [Fact]
    public void Bind_EmptySection_UsesDefaults()
    {
        var configuration = new ConfigurationBuilder().Build();

        var settings = configuration.GetSection(FileLoggingSettings.SectionName).Get<FileLoggingSettings>()
            ?? new FileLoggingSettings();

        settings.Enabled.ShouldBeFalse();
        settings.Path.ShouldBe("/var/log/ando");
        settings.RetainedDays.ShouldBe(90);
    }

    [Fact]
    public void Bind_EnvironmentVariableKeys_OverridesDefaults()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Logging__File__Enabled etc. arrive as Logging:File:Enabled.
                ["Logging:File:Enabled"] = "true",
                ["Logging:File:Path"] = "/srv/ando/logs",
                ["Logging:File:RetainedDays"] = "14"
            })
            .Build();

        var settings = configuration.GetSection(FileLoggingSettings.SectionName).Get<FileLoggingSettings>();

        settings.ShouldNotBeNull();
        settings.Enabled.ShouldBeTrue();
        settings.Path.ShouldBe("/srv/ando/logs");
        settings.RetainedDays.ShouldBe(14);
    }
}
