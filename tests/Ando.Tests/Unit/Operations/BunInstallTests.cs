// =============================================================================
// BunInstallTests.cs
//
// Summary: Tests for Bun.Install, with and without a SHA-256 checksum.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class BunInstallTests
{
    private const string Sha = "CF0ED0A920799D576FFDE4E0CAE66D732BF23C2530407F26F59C7831DFFE1F0E";
    private readonly StepRegistry _registry = new();
    private readonly MockExecutor _executor = new();

    private BunOperations CreateBun() => new(_registry, new TestLogger(), () => _executor);

    private async Task<string> ScriptAsync(Action<BunOperations> install)
    {
        install(CreateBun());
        await _registry.Steps[0].Execute();
        var command = _executor.LastCommand!;
        command.Command.ShouldBe("bash");
        return command.Args[1];
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("1")]
    [InlineData("1.2")]
    [InlineData("1.2.23; rm -rf /")]
    public void Install_WithSha256_RequiresExactVersion(string version)
    {
        Should.Throw<ArgumentException>(() => CreateBun().Install(version, Sha));
        _registry.Steps.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zz0ed0a920799d576ffde4e0cae66d732bf23c2530407f26f59c7831dffe1f0e")]
    [InlineData("cf0ed0a920799d576ffde4e0cae66d732bf23c2530407f26f59c7831dffe1f0e; id")]
    public void Install_WithBadSha256_Throws(string sha)
    {
        Should.Throw<ArgumentException>(() => CreateBun().Install("1.2.23", sha));
    }

    [Fact]
    public async Task Install_WithSha256_DownloadsVerifiesAndInstallsReleaseZip()
    {
        var script = await ScriptAsync(b => b.Install("v1.2.23", Sha));

        script.ShouldStartWith("set -eu;");
        script.ShouldContain("https://github.com/oven-sh/bun/releases/download/bun-v1.2.23/bun-linux-$arch.zip");
        script.ShouldContain("x86_64) arch=x64");
        script.ShouldContain("aarch64|arm64) arch=aarch64");
        script.ShouldContain($"echo \"{Sha.ToLowerInvariant()}  $tmp/bun.zip\" | sha256sum -c -");
        script.ShouldContain("SHA-256 mismatch");
        script.ShouldContain("apt-get install -y -qq unzip");
        script.ShouldContain("/usr/local/bin/bun; chmod +x /usr/local/bin/bun");
        script.ShouldContain("[ \"$(bun --version)\" = \"1.2.23\" ]");
        script.ShouldNotContain("bun.sh/install");
        _registry.Steps[0].Name.ShouldBe("Bun.Install");
    }

    [Fact]
    public async Task Install_WithoutSha256_UsesOfficialInstaller()
    {
        var script = await ScriptAsync(b => b.Install("1.2.23"));

        script.ShouldContain("curl -fsSL https://bun.sh/install");
        script.ShouldContain("bun-v1.2.23");
        script.ShouldNotContain("sha256sum");
    }

    [Fact]
    public async Task Install_Latest_UsesInstallerWithoutTag()
    {
        var script = await ScriptAsync(b => b.Install());

        script.ShouldContain("command -v bun >/dev/null; then");
        script.ShouldContain("/tmp/ando-bun-install.sh ;");
    }
}
