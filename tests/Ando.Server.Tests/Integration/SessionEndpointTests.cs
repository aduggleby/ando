using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Ando.Server.Tests.Integration;

public class SessionEndpointTests
{
    [Fact]
    public async Task Post_Session_Redirects_To_Project_Create()
    {
        using var factory = new AndoWebApplicationFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var resp = await client.PostAsync("/session?installation_id=123", new StringContent(""));

        Assert.True(
            resp.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.RedirectMethod or HttpStatusCode.RedirectKeepVerb,
            $"Expected redirect, got {(int)resp.StatusCode} {resp.StatusCode}");

        Assert.Equal("/projects/create?installation_id=123", resp.Headers.Location?.ToString());
    }
}
