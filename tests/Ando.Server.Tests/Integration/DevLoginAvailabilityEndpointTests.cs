// =============================================================================
// DevLoginAvailabilityEndpointTests.cs
//
// Summary: Integration tests for development-login availability metadata endpoint.
//
// Verifies the endpoint reflects host environment state so the SPA can safely
// decide whether to show the Development Login shortcut.
// =============================================================================

using System.Net.Http.Json;
using Ando.Server.Contracts.Auth;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ando.Server.Tests.Integration;

public class DevLoginAvailabilityEndpointTests
{
    [Fact]
    public async Task Get_DevLoginAvailability_In_Testing_Environment_Returns_False()
    {
        using var factory = new AndoWebApplicationFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/api/auth/dev-login-availability");
        response.IsSuccessStatusCode.ShouldBeTrue();

        var payload = await response.Content.ReadFromJsonAsync<DevLoginAvailabilityResponse>();
        payload.ShouldNotBeNull();
        payload!.IsAvailable.ShouldBeFalse();
    }
}
