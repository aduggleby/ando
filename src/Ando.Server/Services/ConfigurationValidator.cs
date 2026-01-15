// =============================================================================
// ConfigurationValidator.cs
//
// Summary: Validates required configuration on application startup.
//
// This service checks that all required environment variables and configuration
// settings are properly configured before the application starts accepting
// requests. If configuration is missing, it sets a flag that triggers an
// error page to be shown instead of the normal application.
//
// Design Decisions:
// - Validates early (during startup) to fail fast
// - Collects all errors rather than failing on first error
// - Differentiates between production and development requirements
// - Stores errors in a singleton for access by error middleware
// - Validates Docker is running in rootless mode for security
// =============================================================================

using System.Diagnostics;
using Ando.Server.Configuration;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// Validates required configuration on startup.
/// </summary>
public class ConfigurationValidator
{
    private readonly List<string> _errors = [];
    private readonly IWebHostEnvironment _environment;

    public ConfigurationValidator(
        IConfiguration configuration,
        IOptions<GitHubSettings> gitHubSettings,
        IOptions<EncryptionSettings> encryptionSettings,
        IOptions<TestSettings> testSettings,
        IWebHostEnvironment environment)
    {
        _environment = environment;
        ValidateConfiguration(configuration, gitHubSettings.Value, encryptionSettings.Value, testSettings.Value);
    }

    /// <summary>
    /// Gets whether the configuration is valid.
    /// </summary>
    public bool IsValid => _errors.Count == 0;

    /// <summary>
    /// Gets the list of configuration errors.
    /// </summary>
    public IReadOnlyList<string> Errors => _errors;

    private void ValidateConfiguration(
        IConfiguration configuration,
        GitHubSettings gitHubSettings,
        EncryptionSettings encryptionSettings,
        TestSettings testSettings)
    {
        // Skip validation in Testing environment (uses test doubles)
        if (_environment.IsEnvironment("Testing"))
        {
            // Only validate Test API key in testing environment
            if (string.IsNullOrEmpty(testSettings.ApiKey))
            {
                _errors.Add("Test:ApiKey is required in Testing environment. Set via environment variable: Test__ApiKey");
            }
            return;
        }

        // ---------------------------------------------------------------------
        // Database Connection
        // ---------------------------------------------------------------------
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            _errors.Add("ConnectionStrings:DefaultConnection is required. Set via environment variable: ConnectionStrings__DefaultConnection");
        }

        // ---------------------------------------------------------------------
        // Encryption Key (required for all environments except Development)
        // ---------------------------------------------------------------------
        if (string.IsNullOrEmpty(encryptionSettings.Key))
        {
            if (_environment.IsDevelopment())
            {
                _errors.Add("Encryption:Key is required. Generate with: openssl rand -base64 32. Set via environment variable: Encryption__Key");
            }
            else
            {
                _errors.Add("Encryption:Key is required for production. Generate with: openssl rand -base64 32. Set via environment variable: Encryption__Key");
            }
        }
        else
        {
            // Validate key length
            try
            {
                var keyBytes = Convert.FromBase64String(encryptionSettings.Key);
                if (keyBytes.Length != 32)
                {
                    _errors.Add($"Encryption:Key must be exactly 32 bytes (256 bits). Current key is {keyBytes.Length} bytes.");
                }
            }
            catch (FormatException)
            {
                _errors.Add("Encryption:Key must be a valid base64-encoded string.");
            }
        }

        // ---------------------------------------------------------------------
        // GitHub OAuth (required for production)
        // ---------------------------------------------------------------------
        if (!_environment.IsDevelopment())
        {
            if (string.IsNullOrEmpty(gitHubSettings.ClientId))
            {
                _errors.Add("GitHub:ClientId is required. Set via environment variable: GitHub__ClientId");
            }

            if (string.IsNullOrEmpty(gitHubSettings.ClientSecret))
            {
                _errors.Add("GitHub:ClientSecret is required. Set via environment variable: GitHub__ClientSecret");
            }

            if (string.IsNullOrEmpty(gitHubSettings.WebhookSecret))
            {
                _errors.Add("GitHub:WebhookSecret is required. Set via environment variable: GitHub__WebhookSecret");
            }

            if (string.IsNullOrEmpty(gitHubSettings.AppId))
            {
                _errors.Add("GitHub:AppId is required. Set via environment variable: GitHub__AppId");
            }

            // ---------------------------------------------------------------------
            // Docker Security (required for production)
            // ---------------------------------------------------------------------
            ValidateDockerRootless();
        }
    }

    /// <summary>
    /// Validates that Docker is running in rootless mode.
    /// Running Docker as root is a security risk as it allows container escapes
    /// to gain full root access to the host system.
    /// </summary>
    private void ValidateDockerRootless()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("info");
            startInfo.ArgumentList.Add("--format");
            startInfo.ArgumentList.Add("{{.SecurityOptions}}");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _errors.Add("Docker is not available. Ensure Docker is installed and accessible.");
                return;
            }

            process.WaitForExit(TimeSpan.FromSeconds(10));

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();
                _errors.Add($"Failed to check Docker configuration: {error}");
                return;
            }

            var output = process.StandardOutput.ReadToEnd();

            // Check if rootless mode is enabled by looking for "rootless" in security options
            // Rootless Docker reports "name=rootless" in its security options
            if (!output.Contains("rootless", StringComparison.OrdinalIgnoreCase))
            {
                _errors.Add(
                    "Docker is running as root, which is a security risk. " +
                    "Build containers could escape and gain root access to the host. " +
                    "Please configure Docker to run in rootless mode. " +
                    "See: https://docs.docker.com/engine/security/rootless/");
            }
        }
        catch (Exception ex)
        {
            _errors.Add($"Failed to validate Docker configuration: {ex.Message}");
        }
    }
}

/// <summary>
/// Middleware that shows configuration error page when configuration is invalid.
/// </summary>
public class ConfigurationErrorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ConfigurationValidator _validator;

    public ConfigurationErrorMiddleware(RequestDelegate next, ConfigurationValidator validator)
    {
        _next = next;
        _validator = validator;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Allow health check to pass through even with config errors
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        // If configuration is invalid, show error page
        if (!_validator.IsValid)
        {
            context.Response.StatusCode = 503;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync(GenerateErrorPage(_validator.Errors));
            return;
        }

        await _next(context);
    }

    private static string GenerateErrorPage(IReadOnlyList<string> errors)
    {
        var errorListHtml = string.Join("\n", errors.Select(e =>
            $"<li>{System.Net.WebUtility.HtmlEncode(e)}</li>"));

        return
$@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <title>Configuration Error - Ando Server</title>
    <style>
        * {{ box-sizing: border-box; margin: 0; padding: 0; }}
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: #0f172a;
            color: #e2e8f0;
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 20px;
        }}
        .container {{
            max-width: 700px;
            background: #1e293b;
            border-radius: 12px;
            padding: 32px;
            box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.5);
        }}
        h1 {{
            color: #f87171;
            font-size: 1.5rem;
            margin-bottom: 16px;
        }}
        p {{
            color: #94a3b8;
            margin-bottom: 24px;
            line-height: 1.6;
        }}
        h2 {{
            font-size: 1rem;
            color: #e2e8f0;
            margin-bottom: 12px;
        }}
        ul {{
            background: #0f172a;
            border-radius: 8px;
            padding: 16px 16px 16px 36px;
            margin-bottom: 24px;
        }}
        li {{
            color: #fbbf24;
            margin-bottom: 8px;
            line-height: 1.5;
            font-family: monospace;
            font-size: 0.875rem;
        }}
        li:last-child {{ margin-bottom: 0; }}
        .help {{
            background: #1e3a5f;
            border-left: 4px solid #38bdf8;
            padding: 16px;
            border-radius: 0 8px 8px 0;
        }}
        .help h3 {{
            color: #38bdf8;
            font-size: 0.875rem;
            margin-bottom: 8px;
        }}
        .help code {{
            background: #0f172a;
            padding: 2px 6px;
            border-radius: 4px;
            font-size: 0.85em;
        }}
        .help p {{
            margin-bottom: 8px;
            font-size: 0.875rem;
        }}
        .help p:last-child {{ margin-bottom: 0; }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>Configuration Error</h1>
        <p>
            Ando Server cannot start because required configuration is missing.
            Please set the following environment variables and restart the application.
        </p>
        <h2>Missing Configuration</h2>
        <ul>
            {errorListHtml}
        </ul>
        <div class=""help"">
            <h3>How to fix</h3>
            <p>Set environment variables in your <code>.env</code> file or Docker configuration:</p>
            <p>Generate encryption key: <code>openssl rand -base64 32</code></p>
            <p>After setting variables, restart the container: <code>docker compose restart</code></p>
        </div>
    </div>
</body>
</html>";
    }
}

/// <summary>
/// Extension methods for configuration validation.
/// </summary>
public static class ConfigurationValidatorExtensions
{
    /// <summary>
    /// Adds configuration validation services.
    /// </summary>
    public static IServiceCollection AddConfigurationValidation(this IServiceCollection services)
    {
        services.AddSingleton<ConfigurationValidator>();
        return services;
    }

    /// <summary>
    /// Uses configuration error middleware to show error page when config is invalid.
    /// </summary>
    public static IApplicationBuilder UseConfigurationValidation(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ConfigurationErrorMiddleware>();
    }
}
