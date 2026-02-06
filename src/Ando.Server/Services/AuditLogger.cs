// =============================================================================
// AuditLogger.cs
//
// Summary: Audit logging service for security-sensitive operations.
//
// Provides structured audit logging for administrative actions, authentication
// events, and other security-relevant operations. Logs are written to both
// the standard logging infrastructure and can be persisted to database.
//
// Design Decisions:
// - Uses structured logging for easy querying
// - Includes actor, target, action, and metadata
// - Timestamps are UTC
// - Separate log category for audit trail
// =============================================================================

using System.Security.Claims;
using System.Text.Json;

namespace Ando.Server.Services;

/// <summary>
/// Audit event categories for filtering and querying.
/// </summary>
public enum AuditCategory
{
    /// <summary>User authentication events (login, logout, password changes).</summary>
    Authentication,

    /// <summary>User account management (create, delete, role changes).</summary>
    UserManagement,

    /// <summary>Project management (create, delete, settings changes).</summary>
    ProjectManagement,

    /// <summary>Build operations (trigger, cancel).</summary>
    BuildManagement,

    /// <summary>Admin-specific operations (impersonation, system settings).</summary>
    AdminAction,

    /// <summary>Security-related events (failed auth, rate limiting).</summary>
    Security
}

/// <summary>
/// Represents an audit log entry.
/// </summary>
public record AuditEntry
{
    /// <summary>Timestamp of the event (UTC).</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>Category of the audit event.</summary>
    public required AuditCategory Category { get; init; }

    /// <summary>Specific action performed (e.g., "UserDeleted", "RoleChanged").</summary>
    public required string Action { get; init; }

    /// <summary>ID of the user performing the action (null for system events).</summary>
    public int? ActorId { get; init; }

    /// <summary>Email/name of the user performing the action.</summary>
    public string? ActorName { get; init; }

    /// <summary>ID of the affected entity (user, project, build, etc.).</summary>
    public string? TargetId { get; init; }

    /// <summary>Type of the affected entity.</summary>
    public string? TargetType { get; init; }

    /// <summary>Human-readable description of the action.</summary>
    public required string Description { get; init; }

    /// <summary>IP address of the request (if applicable).</summary>
    public string? IpAddress { get; init; }

    /// <summary>Additional metadata as key-value pairs.</summary>
    public Dictionary<string, object>? Metadata { get; init; }

    /// <summary>Whether the action was successful.</summary>
    public bool Success { get; init; } = true;
}

/// <summary>
/// Interface for audit logging service.
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Logs an audit event.
    /// </summary>
    void Log(AuditEntry entry);

    /// <summary>
    /// Logs an audit event with the current HTTP context.
    /// </summary>
    void Log(
        AuditCategory category,
        string action,
        string description,
        ClaimsPrincipal? user = null,
        string? targetId = null,
        string? targetType = null,
        Dictionary<string, object>? metadata = null,
        bool success = true);

    /// <summary>
    /// Logs an admin action with full context.
    /// </summary>
    void LogAdminAction(
        string action,
        string description,
        int adminId,
        string? adminEmail,
        int? targetUserId = null,
        string? targetUserEmail = null,
        Dictionary<string, object>? metadata = null,
        bool success = true);
}

/// <summary>
/// Audit logging service implementation.
/// </summary>
public class AuditLogger : IAuditLogger
{
    private readonly ILogger<AuditLogger> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogger(
        ILogger<AuditLogger> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public void Log(AuditEntry entry)
    {
        // Log to standard logging with structured data
        _logger.LogInformation(
            "AUDIT [{Category}] {Action}: {Description} | Actor: {ActorId} ({ActorName}) | Target: {TargetType}/{TargetId} | IP: {IpAddress} | Success: {Success} | Metadata: {Metadata}",
            entry.Category,
            entry.Action,
            entry.Description,
            entry.ActorId,
            entry.ActorName ?? "System",
            entry.TargetType ?? "N/A",
            entry.TargetId ?? "N/A",
            entry.IpAddress ?? "N/A",
            entry.Success,
            entry.Metadata != null ? JsonSerializer.Serialize(entry.Metadata) : "{}");
    }

    /// <inheritdoc />
    public void Log(
        AuditCategory category,
        string action,
        string description,
        ClaimsPrincipal? user = null,
        string? targetId = null,
        string? targetType = null,
        Dictionary<string, object>? metadata = null,
        bool success = true)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var actorId = user?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var actorName = user?.FindFirst(ClaimTypes.Email)?.Value
            ?? user?.FindFirst(ClaimTypes.Name)?.Value;

        var entry = new AuditEntry
        {
            Category = category,
            Action = action,
            Description = description,
            ActorId = int.TryParse(actorId, out var id) ? id : null,
            ActorName = actorName,
            TargetId = targetId,
            TargetType = targetType,
            IpAddress = GetClientIpAddress(httpContext),
            Metadata = metadata,
            Success = success
        };

        Log(entry);
    }

    /// <inheritdoc />
    public void LogAdminAction(
        string action,
        string description,
        int adminId,
        string? adminEmail,
        int? targetUserId = null,
        string? targetUserEmail = null,
        Dictionary<string, object>? metadata = null,
        bool success = true)
    {
        var httpContext = _httpContextAccessor.HttpContext;

        // Build metadata with target user info if provided
        var fullMetadata = metadata ?? new Dictionary<string, object>();
        if (targetUserEmail != null)
        {
            fullMetadata["targetUserEmail"] = targetUserEmail;
        }

        var entry = new AuditEntry
        {
            Category = AuditCategory.AdminAction,
            Action = action,
            Description = description,
            ActorId = adminId,
            ActorName = adminEmail,
            TargetId = targetUserId?.ToString(),
            TargetType = targetUserId != null ? "User" : null,
            IpAddress = GetClientIpAddress(httpContext),
            Metadata = fullMetadata.Count > 0 ? fullMetadata : null,
            Success = success
        };

        Log(entry);
    }

    /// <summary>
    /// Gets the client IP address from the HTTP context.
    /// Handles X-Forwarded-For header for reverse proxy scenarios.
    /// </summary>
    private static string? GetClientIpAddress(HttpContext? httpContext)
    {
        if (httpContext == null) return null;

        // Check for forwarded header (behind reverse proxy)
        var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain (original client)
            return forwardedFor.Split(',')[0].Trim();
        }

        // Fall back to direct connection IP
        return httpContext.Connection.RemoteIpAddress?.ToString();
    }
}
