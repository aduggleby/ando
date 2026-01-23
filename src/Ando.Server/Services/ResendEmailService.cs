// =============================================================================
// ResendEmailService.cs
//
// Summary: Email service implementation using Resend API.
//
// Sends transactional emails via the Resend API (https://resend.com).
// Inherits Razor view rendering from BaseEmailService.
//
// Design Decisions:
// - Uses HttpClient for API calls (configured in DI)
// - API key from configuration determines if emails are sent
// - Gracefully skips sending if not configured
// =============================================================================

using System.Net.Http.Headers;
using System.Text.Json;
using Ando.Server.Configuration;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// Email service implementation using Resend API.
/// </summary>
public class ResendEmailService : BaseEmailService
{
    private readonly HttpClient _httpClient;

    public ResendEmailService(
        IHttpClientFactory httpClientFactory,
        IOptions<EmailSettings> settings,
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        ILogger<ResendEmailService> logger)
        : base(settings, viewEngine, tempDataProvider, serviceProvider, logger)
    {
        _httpClient = httpClientFactory.CreateClient("Resend");
    }

    /// <inheritdoc />
    public override async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var apiKey = Settings.Resend.ApiKey;

        if (string.IsNullOrEmpty(apiKey))
        {
            Logger.LogWarning("Resend API key not configured, skipping email to {To}", to);
            return;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var body = new
            {
                from = GetFromString(),
                to = new[] { to },
                subject,
                html = htmlBody
            };

            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                Logger.LogError("Failed to send email via Resend: {Status} - {Error}",
                    response.StatusCode, error);
            }
            else
            {
                Logger.LogInformation("Sent email via Resend to {To}: {Subject}", to, subject);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending email via Resend to {To}", to);
        }
    }
}
