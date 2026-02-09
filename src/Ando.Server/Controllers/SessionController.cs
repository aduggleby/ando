// =============================================================================
// SessionController.cs
//
// Summary: Compatibility endpoint for GitHub App "setup URL" /session.
//
// Some GitHub App configurations redirect users to /session after installation.
// Historically, Ando's actual callback handler is /projects/github-callback.
//
// To avoid hard failures (e.g., 405 Method Not Allowed) and to support existing
// GitHub App configurations, we accept GET/POST on /session and redirect to the
// canonical callback endpoint, preserving query parameters when present.
// =============================================================================

using Microsoft.AspNetCore.Mvc;

namespace Ando.Server.Controllers;

/// <summary>
/// Compatibility endpoint for GitHub App setup URL.
/// </summary>
[Microsoft.AspNetCore.Authorization.AllowAnonymous]
public class SessionController : Controller
{
    [HttpGet("/session")]
    public IActionResult Get()
    {
        // Preserve any GitHub parameters such as ?installation_id=...&setup_action=...
        var qs = HttpContext.Request.QueryString.Value ?? "";
        return Redirect("/projects/github-callback" + qs);
    }

    [HttpPost("/session")]
    public IActionResult Post()
    {
        // Some clients may POST here; keep behavior consistent by redirecting to
        // the canonical callback endpoint.
        var qs = HttpContext.Request.QueryString.Value ?? "";
        return Redirect("/projects/github-callback" + qs);
    }
}
