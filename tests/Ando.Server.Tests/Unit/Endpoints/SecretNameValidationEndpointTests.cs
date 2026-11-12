// =============================================================================
// SecretNameValidationEndpointTests.cs
//
// Summary: Unit tests for the secret name policy at the HTTP boundary
// (SetSecretEndpoint and BulkImportSecretsEndpoint).
//
// Secret names must be environment variable names (^[A-Za-z_][A-Za-z0-9_]*$,
// at most 100 characters). FastEndpoints does not evaluate the DTO's
// DataAnnotations, so the endpoints check explicitly; these tests prove an
// invalid name never reaches IProjectService.SetSecretAsync.
//
// Design Decisions:
// - Uses FastEndpoints' Factory.Create with a mocked IProjectService, so no
//   HTTP host, database, or login is needed
// - The route id and the user's NameIdentifier claim are set on the
//   synthetic HttpContext because the endpoints read them directly
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Endpoints.Projects;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace Ando.Server.Tests.Unit.Endpoints;

/// <summary>
/// Tests for secret name validation in <see cref="SetSecretEndpoint"/> and <see cref="BulkImportSecretsEndpoint"/>.
/// </summary>
[Collection(Ando.Server.Tests.TestFixtures.FastEndpointsFactoryCollection.Name)]
public class SecretNameValidationEndpointTests
{
    private const int ProjectId = 7;
    private const int UserId = 3;

    private readonly Mock<IProjectService> _projectService = new();

    public SecretNameValidationEndpointTests()
    {
        Ando.Server.Tests.TestFixtures.FastEndpointsFactoryCollection.ResetResolver();

        _projectService
            .Setup(s => s.GetProjectForUserAsync(ProjectId, UserId))
            .ReturnsAsync(new Project { Id = ProjectId, OwnerId = UserId });
    }

    [Theory]
    [InlineData("MY_SECRET")]
    [InlineData("nuget_key")]
    [InlineData("_PRIVATE")]
    [InlineData("  API_KEY  ")]
    public async Task SetSecret_ValidName_CallsServiceWithTrimmedName(string name)
    {
        var endpoint = Factory.Create<SetSecretEndpoint>(ConfigureContext, _projectService.Object);

        await endpoint.HandleAsync(new SetSecretRequest { Name = name, Value = "value" }, CancellationToken.None);

        endpoint.Response.Success.ShouldBeTrue();
        _projectService.Verify(s => s.SetSecretAsync(ProjectId, name.Trim(), "value"), Times.Once);
    }

    [Theory]
    [InlineData("1KEY")]
    [InlineData("MY-SECRET")]
    [InlineData("MY SECRET")]
    [InlineData("MY.SECRET")]
    [InlineData("ı_KEY")]
    [InlineData("SCHLÜSSEL")]
    public async Task SetSecret_InvalidName_IsRejectedWithoutCallingService(string name)
    {
        var endpoint = Factory.Create<SetSecretEndpoint>(ConfigureContext, _projectService.Object);

        await endpoint.HandleAsync(new SetSecretRequest { Name = name, Value = "value" }, CancellationToken.None);

        endpoint.Response.Success.ShouldBeFalse();
        endpoint.Response.Error.ShouldBe(ProjectSecret.NameRuleDescription);
        _projectService.Verify(s => s.SetSecretAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SetSecret_TooLongName_IsRejectedWithoutCallingService()
    {
        var endpoint = Factory.Create<SetSecretEndpoint>(ConfigureContext, _projectService.Object);
        var name = new string('A', ProjectSecret.MaxNameLength + 1);

        await endpoint.HandleAsync(new SetSecretRequest { Name = name, Value = "value" }, CancellationToken.None);

        endpoint.Response.Success.ShouldBeFalse();
        _projectService.Verify(s => s.SetSecretAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BulkImport_SkipsInvalidAndTooLongNames_AndImportsValidOnes()
    {
        var endpoint = Factory.Create<BulkImportSecretsEndpoint>(ConfigureContext, _projectService.Object);
        var tooLong = new string('A', ProjectSecret.MaxNameLength + 1);
        var content = $"good_key=1\n1BAD=2\n{tooLong}=3\nOTHER-KEY=4\n";

        await endpoint.HandleAsync(new BulkImportSecretsRequest { Content = content }, CancellationToken.None);

        endpoint.Response.Success.ShouldBeFalse();
        endpoint.Response.ImportedCount.ShouldBe(2);
        endpoint.Response.Errors!.Count.ShouldBe(2);
        _projectService.Verify(s => s.SetSecretAsync(ProjectId, "GOOD_KEY", "1"), Times.Once);
        _projectService.Verify(s => s.SetSecretAsync(ProjectId, "OTHER_KEY", "4"), Times.Once);
        _projectService.Verify(s => s.SetSecretAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
    }

    private static void ConfigureContext(DefaultHttpContext context)
    {
        context.Request.RouteValues["id"] = ProjectId.ToString();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test"));
    }
}
