// =============================================================================
// DatabaseSeeding.cs
//
// Summary: Contains database role and development data seeding routines used
// during application startup.
//
// Startup seeding is separated from Program.cs to keep startup orchestration
// focused on wiring while preserving existing seed behavior.
// =============================================================================

using Ando.Server.Data;
using Ando.Server.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Infrastructure.Startup;

internal static class DatabaseSeeding
{
    /// <summary>
    /// Seeds the required application roles.
    /// </summary>
    internal static async Task SeedRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        foreach (var roleName in UserRoles.AllRoles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var role = new ApplicationRole
                {
                    Name = roleName,
                    Description = roleName == UserRoles.Admin
                        ? "Global administrator with full system access"
                        : "Standard user with access to their own projects"
                };

                await roleManager.CreateAsync(role);
            }
        }
    }

    /// <summary>
    /// Seeds a local development user and demo project for quick UI testing.
    /// </summary>
    internal static async Task SeedDevelopmentDataAsync(
        AndoDbContext db,
        UserManager<ApplicationUser> userManager)
    {
        const string devEmail = "dev@ando.local";
        const string devPassword = "DevPassword123";
        const string devProjectRepo = "github/ando";

        var user = await userManager.FindByEmailAsync(devEmail);
        if (user == null)
        {
            user = new ApplicationUser
            {
                UserName = devEmail,
                Email = devEmail,
                DisplayName = "Development User",
                EmailConfirmed = true,
                EmailVerified = true,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow,
                GitHubId = 9913377,
                GitHubLogin = "github",
                GitHubConnectedAt = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(user, devPassword);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed development user: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
            }

            await userManager.AddToRoleAsync(user, UserRoles.User);
        }
        else if (!await userManager.IsInRoleAsync(user, UserRoles.User))
        {
            await userManager.AddToRoleAsync(user, UserRoles.User);
        }

        var hasProject = await db.Projects.AnyAsync(p => p.OwnerId == user.Id && p.RepoFullName == devProjectRepo);
        if (!hasProject)
        {
            db.Projects.Add(new Project
            {
                OwnerId = user.Id,
                GitHubRepoId = 9913377001,
                RepoFullName = devProjectRepo,
                RepoUrl = "https://github.com/github/ando",
                DefaultBranch = "main",
                BranchFilter = "main,master",
                InstallationId = 1,
                TimeoutMinutes = 15,
                NotifyOnFailure = false,
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();
        }
    }
}
