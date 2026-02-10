using System.Net.Http.Json;
using Ando.Server.Contracts.Auth;
using Ando.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Ando.Server.Tests.Integration;

public class AuthLoginRememberMeTests
{
    [Fact]
    public async Task Post_Login_With_RememberMe_True_Sets_Persistent_Auth_Cookie()
    {
        using var factory = new AndoWebApplicationFactory();

        // Seed a user.
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = "test@example.com",
                Email = "test@example.com",
                CreatedAt = DateTime.UtcNow
            };

            var create = await userManager.CreateAsync(user, "Password123");
            create.Succeeded.ShouldBeTrue(create.Errors.FirstOrDefault()?.Description);
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "test@example.com",
            Password = "Password123",
            RememberMe = true
        });

        resp.IsSuccessStatusCode.ShouldBeTrue();

        resp.Headers.TryGetValues("Set-Cookie", out var setCookies).ShouldBeTrue();
        var authCookie = setCookies!.FirstOrDefault(c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        authCookie.ShouldNotBeNull();

        // A persistent Identity cookie should include an Expires attribute.
        authCookie!.ToLowerInvariant().ShouldContain("expires=");
    }

    [Fact]
    public async Task Post_Login_With_RememberMe_False_Sets_Session_Auth_Cookie()
    {
        using var factory = new AndoWebApplicationFactory();

        // Seed a user.
        using (var scope = factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                UserName = "test2@example.com",
                Email = "test2@example.com",
                CreatedAt = DateTime.UtcNow
            };

            var create = await userManager.CreateAsync(user, "Password123");
            create.Succeeded.ShouldBeTrue(create.Errors.FirstOrDefault()?.Description);
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var resp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Email = "test2@example.com",
            Password = "Password123",
            RememberMe = false
        });

        resp.IsSuccessStatusCode.ShouldBeTrue();

        resp.Headers.TryGetValues("Set-Cookie", out var setCookies).ShouldBeTrue();
        var authCookie = setCookies!.FirstOrDefault(c => c.StartsWith(".AspNetCore.Identity.Application=", StringComparison.Ordinal));
        authCookie.ShouldNotBeNull();

        // Session cookies should not set Expires.
        authCookie!.ToLowerInvariant().ShouldNotContain("expires=");
    }
}
