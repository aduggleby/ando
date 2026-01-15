// =============================================================================
// AdminController.cs
//
// Summary: Admin-only controller for system administration and user management.
//
// This controller provides dashboard statistics, user management (list, view,
// lock/unlock, change roles, delete), project overview, and user impersonation
// for support purposes. All actions require the Admin role.
//
// Design Decisions:
// - All actions protected by [Authorize(Roles = "Admin")] policy
// - Pagination for user and project lists (page size of 20)
// - Cannot delete yourself or change your own role
// - Cannot impersonate other admins
// - Audit logging for sensitive operations
// =============================================================================

using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Controllers;

/// <summary>
/// Controller for admin-only system administration.
/// </summary>
[Authorize(Roles = UserRoles.Admin)]
[Route("admin")]
public class AdminController : Controller
{
    private const int PageSize = 20;

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AndoDbContext _db;
    private readonly IImpersonationService _impersonationService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        UserManager<ApplicationUser> userManager,
        AndoDbContext db,
        IImpersonationService impersonationService,
        ILogger<AdminController> logger)
    {
        _userManager = userManager;
        _db = db;
        _impersonationService = impersonationService;
        _logger = logger;
    }

    // =========================================================================
    // Dashboard
    // =========================================================================

    /// <summary>
    /// Admin dashboard with system statistics.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var now = DateTime.UtcNow;
        var yesterday = now.AddDays(-1);

        var totalUsers = await _userManager.Users.CountAsync();
        var verifiedUsers = await _userManager.Users.CountAsync(u => u.EmailVerified);
        var adminUsers = (await _userManager.GetUsersInRoleAsync(UserRoles.Admin)).Count;

        var totalProjects = await _db.Projects.CountAsync();
        var totalBuilds = await _db.Builds.CountAsync();
        var recentBuilds = await _db.Builds.CountAsync(b => b.QueuedAt >= yesterday);

        var recentUsersList = await _userManager.Users
            .OrderByDescending(u => u.CreatedAt)
            .Take(5)
            .Select(u => new RecentUserViewModel
            {
                Id = u.Id,
                Email = u.Email ?? "",
                DisplayName = u.DisplayName ?? u.Email ?? "User",
                CreatedAt = u.CreatedAt,
                EmailVerified = u.EmailVerified
            })
            .ToListAsync();

        var recentBuildsList = await _db.Builds
            .Include(b => b.Project)
            .Where(b => b.QueuedAt >= yesterday)
            .OrderByDescending(b => b.QueuedAt)
            .Take(10)
            .Select(b => new RecentBuildViewModel
            {
                Id = b.Id,
                ProjectName = b.Project.RepoFullName,
                Branch = b.Branch,
                Status = b.Status.ToString(),
                CreatedAt = b.QueuedAt
            })
            .ToListAsync();

        var model = new AdminDashboardViewModel
        {
            TotalUsers = totalUsers,
            VerifiedUsers = verifiedUsers,
            UnverifiedUsers = totalUsers - verifiedUsers,
            AdminUsers = adminUsers,
            TotalProjects = totalProjects,
            TotalBuilds = totalBuilds,
            RecentBuilds = recentBuilds,
            RecentUsers = recentUsersList,
            RecentBuilds24h = recentBuildsList
        };

        return View(model);
    }

    // =========================================================================
    // User Management
    // =========================================================================

    /// <summary>
    /// List all users with search and filtering.
    /// </summary>
    [HttpGet("users")]
    public async Task<IActionResult> Users(string? search, string? role, int page = 1)
    {
        var query = _userManager.Users.AsQueryable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(search)) ||
                (u.DisplayName != null && u.DisplayName.ToLower().Contains(search)));
        }

        // Get all users to check roles (Identity doesn't support role filtering in query)
        var totalUsers = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalUsers / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(u => new
            {
                u.Id,
                Email = u.Email ?? "",
                DisplayName = u.DisplayName ?? u.Email ?? "User",
                u.EmailVerified,
                u.LockoutEnd,
                u.CreatedAt,
                u.LastLoginAt,
                HasGitHubConnection = u.GitHubId != null,
                ProjectCount = u.Projects.Count
            })
            .ToListAsync();

        // Check roles for each user
        var userItems = new List<UserListItemViewModel>();
        foreach (var u in users)
        {
            var user = await _userManager.FindByIdAsync(u.Id.ToString());
            var isAdmin = user != null && await _userManager.IsInRoleAsync(user, UserRoles.Admin);

            // Apply role filter
            if (!string.IsNullOrWhiteSpace(role))
            {
                if (role == UserRoles.Admin && !isAdmin) continue;
                if (role == UserRoles.User && isAdmin) continue;
            }

            userItems.Add(new UserListItemViewModel
            {
                Id = u.Id,
                Email = u.Email,
                DisplayName = u.DisplayName,
                EmailVerified = u.EmailVerified,
                IsAdmin = isAdmin,
                IsLockedOut = u.LockoutEnd.HasValue && u.LockoutEnd > DateTimeOffset.UtcNow,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                HasGitHubConnection = u.HasGitHubConnection,
                ProjectCount = u.ProjectCount
            });
        }

        var model = new UserListViewModel
        {
            Users = userItems,
            CurrentPage = page,
            TotalPages = totalPages,
            TotalUsers = totalUsers,
            PageSize = PageSize,
            SearchQuery = search,
            RoleFilter = role
        };

        return View(model);
    }

    /// <summary>
    /// View user details.
    /// </summary>
    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> UserDetails(int id)
    {
        var user = await _userManager.Users
            .Include(u => u.Projects)
            .ThenInclude(p => p.Builds)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound();
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, UserRoles.Admin);

        var model = new UserDetailsViewModel
        {
            Id = user.Id,
            Email = user.Email ?? "",
            DisplayName = user.DisplayName,
            AvatarUrl = user.AvatarUrl,
            EmailVerified = user.EmailVerified,
            EmailVerificationSentAt = user.EmailVerificationSentAt,
            IsAdmin = isAdmin,
            IsLockedOut = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            LockoutEnd = user.LockoutEnd,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            HasGitHubConnection = user.HasGitHubConnection,
            GitHubLogin = user.GitHubLogin,
            GitHubConnectedAt = user.GitHubConnectedAt,
            Projects = user.Projects.Select(p => new UserProjectViewModel
            {
                Id = p.Id,
                Name = p.RepoFullName,
                Description = null,
                CreatedAt = p.CreatedAt,
                BuildCount = p.Builds.Count
            }).ToList(),
            TotalBuilds = user.Projects.Sum(p => p.Builds.Count)
        };

        return View(model);
    }

    /// <summary>
    /// Show role change form.
    /// </summary>
    [HttpGet("users/{id:int}/role")]
    public async Task<IActionResult> ChangeRole(int id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var currentUserId = int.Parse(_userManager.GetUserId(User)!);
        if (id == currentUserId)
        {
            TempData["Error"] = "You cannot change your own role.";
            return RedirectToAction(nameof(UserDetails), new { id });
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, UserRoles.Admin);

        var model = new ChangeUserRoleViewModel
        {
            UserId = user.Id,
            Email = user.Email ?? "",
            CurrentRole = isAdmin ? UserRoles.Admin : UserRoles.User,
            NewRole = isAdmin ? UserRoles.Admin : UserRoles.User,
            AvailableRoles = [UserRoles.User, UserRoles.Admin]
        };

        return View(model);
    }

    /// <summary>
    /// Process role change.
    /// </summary>
    [HttpPost("users/{id:int}/role")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeRole(int id, ChangeUserRoleViewModel model)
    {
        if (!ModelState.IsValid)
        {
            model.AvailableRoles = [UserRoles.User, UserRoles.Admin];
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var currentUserId = int.Parse(_userManager.GetUserId(User)!);
        if (id == currentUserId)
        {
            TempData["Error"] = "You cannot change your own role.";
            return RedirectToAction(nameof(UserDetails), new { id });
        }

        var isCurrentlyAdmin = await _userManager.IsInRoleAsync(user, UserRoles.Admin);
        var wantsAdmin = model.NewRole == UserRoles.Admin;

        if (isCurrentlyAdmin && !wantsAdmin)
        {
            // Demote from admin
            await _userManager.RemoveFromRoleAsync(user, UserRoles.Admin);
            await _userManager.AddToRoleAsync(user, UserRoles.User);
            _logger.LogInformation("User {UserId} demoted from Admin to User by {AdminId}", id, currentUserId);
        }
        else if (!isCurrentlyAdmin && wantsAdmin)
        {
            // Promote to admin
            await _userManager.RemoveFromRoleAsync(user, UserRoles.User);
            await _userManager.AddToRoleAsync(user, UserRoles.Admin);
            _logger.LogInformation("User {UserId} promoted to Admin by {AdminId}", id, currentUserId);
        }

        TempData["Success"] = $"Role for {user.Email} updated to {model.NewRole}.";
        return RedirectToAction(nameof(UserDetails), new { id });
    }

    /// <summary>
    /// Show lock/unlock form.
    /// </summary>
    [HttpGet("users/{id:int}/lock")]
    public async Task<IActionResult> LockUser(int id)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var currentUserId = int.Parse(_userManager.GetUserId(User)!);
        if (id == currentUserId)
        {
            TempData["Error"] = "You cannot lock your own account.";
            return RedirectToAction(nameof(UserDetails), new { id });
        }

        var model = new LockUserViewModel
        {
            UserId = user.Id,
            Email = user.Email ?? "",
            IsCurrentlyLocked = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            CurrentLockoutEnd = user.LockoutEnd,
            LockDays = 7
        };

        return View(model);
    }

    /// <summary>
    /// Process lock/unlock.
    /// </summary>
    [HttpPost("users/{id:int}/lock")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LockUser(int id, LockUserViewModel model, string action)
    {
        var user = await _userManager.FindByIdAsync(id.ToString());
        if (user == null)
        {
            return NotFound();
        }

        var currentUserId = int.Parse(_userManager.GetUserId(User)!);
        if (id == currentUserId)
        {
            TempData["Error"] = "You cannot lock your own account.";
            return RedirectToAction(nameof(UserDetails), new { id });
        }

        if (action == "unlock")
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
            _logger.LogInformation("User {UserId} unlocked by admin {AdminId}", id, currentUserId);
            TempData["Success"] = $"Account for {user.Email} has been unlocked.";
        }
        else if (action == "lock")
        {
            if (!model.LockDays.HasValue || model.LockDays < 1)
            {
                ModelState.AddModelError(nameof(model.LockDays), "Please specify lock duration.");
                model.Email = user.Email ?? "";
                model.IsCurrentlyLocked = user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow;
                return View(model);
            }

            var lockoutEnd = DateTimeOffset.UtcNow.AddDays(model.LockDays.Value);
            await _userManager.SetLockoutEndDateAsync(user, lockoutEnd);
            _logger.LogInformation("User {UserId} locked for {Days} days by admin {AdminId}", id, model.LockDays, currentUserId);
            TempData["Success"] = $"Account for {user.Email} has been locked for {model.LockDays} days.";
        }

        return RedirectToAction(nameof(UserDetails), new { id });
    }

    /// <summary>
    /// Show delete confirmation form.
    /// </summary>
    [HttpGet("users/{id:int}/delete")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var user = await _userManager.Users
            .Include(u => u.Projects)
            .ThenInclude(p => p.Builds)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound();
        }

        var currentUserId = int.Parse(_userManager.GetUserId(User)!);
        if (id == currentUserId)
        {
            TempData["Error"] = "You cannot delete your own account.";
            return RedirectToAction(nameof(UserDetails), new { id });
        }

        var model = new DeleteUserViewModel
        {
            UserId = user.Id,
            Email = user.Email ?? "",
            DisplayName = user.DisplayName ?? user.Email!.Split('@')[0],
            ProjectCount = user.Projects.Count,
            BuildCount = user.Projects.Sum(p => p.Builds.Count)
        };

        return View(model);
    }

    /// <summary>
    /// Process user deletion.
    /// </summary>
    [HttpPost("users/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(int id, DeleteUserViewModel model)
    {
        var user = await _userManager.Users
            .Include(u => u.Projects)
            .ThenInclude(p => p.Builds)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound();
        }

        var currentUserId = int.Parse(_userManager.GetUserId(User)!);
        if (id == currentUserId)
        {
            TempData["Error"] = "You cannot delete your own account.";
            return RedirectToAction(nameof(UserDetails), new { id });
        }

        // Verify email confirmation
        if (model.ConfirmEmail != user.Email)
        {
            ModelState.AddModelError(nameof(model.ConfirmEmail), "Email address does not match.");
            model.Email = user.Email ?? "";
            model.DisplayName = user.DisplayName ?? user.Email!.Split('@')[0];
            model.ProjectCount = user.Projects.Count;
            model.BuildCount = user.Projects.Sum(p => p.Builds.Count);
            return View(model);
        }

        // Delete user's projects and builds first (cascade might handle this, but explicit is safer)
        foreach (var project in user.Projects.ToList())
        {
            _db.Builds.RemoveRange(project.Builds);
            _db.Projects.Remove(project);
        }

        await _db.SaveChangesAsync();

        // Delete the user
        var result = await _userManager.DeleteAsync(user);
        if (result.Succeeded)
        {
            _logger.LogInformation("User {UserId} ({Email}) deleted by admin {AdminId}", id, user.Email, currentUserId);
            TempData["Success"] = $"User {user.Email} and all their data has been deleted.";
            return RedirectToAction(nameof(Users));
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("", error.Description);
        }

        model.Email = user.Email ?? "";
        model.DisplayName = user.DisplayName ?? user.Email!.Split('@')[0];
        return View(model);
    }

    // =========================================================================
    // Impersonation
    // =========================================================================

    /// <summary>
    /// Start impersonating a user.
    /// </summary>
    [HttpPost("users/{id:int}/impersonate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Impersonate(int id)
    {
        var targetUser = await _userManager.FindByIdAsync(id.ToString());
        if (targetUser == null)
        {
            return NotFound();
        }

        // Cannot impersonate admins
        if (await _userManager.IsInRoleAsync(targetUser, UserRoles.Admin))
        {
            TempData["Error"] = "Cannot impersonate administrator accounts.";
            return RedirectToAction(nameof(UserDetails), new { id });
        }

        var currentUserId = int.Parse(_userManager.GetUserId(User)!);
        var success = await _impersonationService.StartImpersonationAsync(currentUserId, id);

        if (success)
        {
            TempData["Success"] = $"You are now impersonating {targetUser.Email}.";
            return Redirect("/");
        }

        TempData["Error"] = "Failed to start impersonation.";
        return RedirectToAction(nameof(UserDetails), new { id });
    }

    /// <summary>
    /// Stop impersonating and return to admin account.
    /// </summary>
    [HttpPost("stop-impersonation")]
    [AllowAnonymous] // Allow this even when impersonating (user won't have admin role)
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StopImpersonation()
    {
        if (!_impersonationService.IsImpersonating)
        {
            return Redirect("/");
        }

        await _impersonationService.StopImpersonationAsync();
        TempData["Success"] = "Impersonation ended. You are now logged in as yourself.";
        return RedirectToAction(nameof(Index));
    }

    // =========================================================================
    // Projects Overview
    // =========================================================================

    /// <summary>
    /// List all projects in the system.
    /// </summary>
    [HttpGet("projects")]
    public async Task<IActionResult> Projects(string? search, int page = 1)
    {
        var query = _db.Projects
            .Include(p => p.Owner)
            .Include(p => p.Builds)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.ToLower();
            query = query.Where(p =>
                p.RepoFullName.ToLower().Contains(search) ||
                (p.Owner.Email != null && p.Owner.Email.ToLower().Contains(search)));
        }

        var totalProjects = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalProjects / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var projects = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new AdminProjectItemViewModel
            {
                Id = p.Id,
                Name = p.RepoFullName,
                Description = null,
                OwnerEmail = p.Owner.Email ?? "",
                OwnerDisplayName = p.Owner.DisplayName ?? p.Owner.Email ?? "User",
                OwnerId = p.Owner.Id,
                CreatedAt = p.CreatedAt,
                BuildCount = p.Builds.Count,
                LastBuildAt = p.Builds.OrderByDescending(b => b.QueuedAt).Select(b => (DateTime?)b.QueuedAt).FirstOrDefault()
            })
            .ToListAsync();

        var model = new AdminProjectListViewModel
        {
            Projects = projects,
            CurrentPage = page,
            TotalPages = totalPages,
            TotalProjects = totalProjects,
            PageSize = PageSize,
            SearchQuery = search
        };

        return View(model);
    }
}
