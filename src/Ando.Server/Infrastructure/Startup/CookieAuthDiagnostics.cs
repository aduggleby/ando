// =============================================================================
// CookieAuthDiagnostics.cs
//
// Summary: Centralized cookie authentication event handlers and diagnostics.
//
// These handlers preserve existing auth behavior while keeping Program.cs
// focused on startup orchestration.
// =============================================================================

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Infrastructure.Startup;

internal static class CookieAuthDiagnostics
{
    internal static CookieAuthenticationEvents CreateEvents() => new()
    {
        // Log cookie details and then delegate to Identity's SecurityStampValidator.
        // Previously this handler ONLY logged and returned Task.CompletedTask, which
        // completely disabled security stamp validation. Now we call the validator
        // so that password changes etc. properly invalidate old sessions, while the
        // ValidationInterval (24h) prevents overly aggressive re-checks.
        OnValidatePrincipal = async context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Auth.Cookie");

            var userName = context.Principal?.Identity?.Name ?? "(unknown)";
            var isAuthenticated = context.Principal?.Identity?.IsAuthenticated == true;

            logger.LogDebug(
                "Cookie validating: path={Path} user={User} auth={IsAuthenticated} issuedUtc={IssuedUtc} expiresUtc={ExpiresUtc} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto} hasCookieHeader={HasCookieHeader} cookieHeaderLength={CookieHeaderLength}",
                context.HttpContext.Request.Path,
                userName,
                isAuthenticated,
                context.Properties.IssuedUtc,
                context.Properties.ExpiresUtc,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                context.HttpContext.Request.Headers["X-Forwarded-Proto"].ToString(),
                context.HttpContext.Request.Headers.Cookie.Count > 0,
                context.HttpContext.Request.Headers.Cookie.ToString().Length);

            // Run Identity's security stamp validation (respects ValidationInterval).
            await SecurityStampValidator.ValidatePrincipalAsync(context);

            // If the validator rejected the principal, log it prominently.
            if (context.Principal == null)
            {
                logger.LogWarning(
                    "Security stamp validation rejected cookie for user={User}. "
                    + "This means the user's security stamp changed (password change, "
                    + "security update, etc.) since the cookie was issued.",
                    userName);
            }
        },
        OnSigningIn = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Auth.Cookie");

            logger.LogInformation(
                "Signing in: path={Path} user={User} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto}",
                context.HttpContext.Request.Path,
                context.Principal?.Identity?.Name ?? "(unknown)",
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                context.HttpContext.Request.Headers["X-Forwarded-Proto"].ToString());

            return Task.CompletedTask;
        },
        OnSignedIn = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Auth.Cookie");

            logger.LogInformation(
                "Signed in: user={User} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto}",
                context.Principal?.Identity?.Name ?? "(unknown)",
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                context.HttpContext.Request.Headers["X-Forwarded-Proto"].ToString());

            return Task.CompletedTask;
        },
        OnSigningOut = context =>
        {
            var logger = context.HttpContext.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("Auth.Cookie");

            logger.LogInformation(
                "Signing out: user={User} path={Path} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto}",
                context.HttpContext.User?.Identity?.Name ?? "(unknown)",
                context.HttpContext.Request.Path,
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                context.HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                context.HttpContext.Request.Headers["X-Forwarded-Proto"].ToString());

            return Task.CompletedTask;
        },

        // Return 401/403 for API requests instead of redirecting to login.
        OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        },
        OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = 403;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        }
    };
}
