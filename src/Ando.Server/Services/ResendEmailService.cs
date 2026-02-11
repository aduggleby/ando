// =============================================================================
// ResendEmailService.cs
//
// Summary: Email service implementation using Resend-compatible HTTP API.
//
// Sends transactional emails via the Resend API (https://resend.com) or a
// Resend-compatible provider (e.g. SelfMX).
// Inherits Razor view rendering from BaseEmailService.
//
// Design Decisions:
// - Uses a raw HTTP call to Resend-compatible API so failures can log response body/headers
// - API key from configuration determines if emails are sent
// - Gracefully skips sending if not configured
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Data;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// Email service implementation using Resend-compatible HTTP API.
/// </summary>
public class ResendEmailService : BaseEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ResendEmailService(
        IHttpClientFactory httpClientFactory,
        IOptions<EmailSettings> settings,
        IUrlService urlService,
        AndoDbContext db,
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        ILogger<ResendEmailService> logger)
        : base(settings, urlService, db, viewEngine, tempDataProvider, serviceProvider, logger)
    {
        _httpClientFactory = httpClientFactory;
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
            // Resend-compatible API: POST /emails with JSON body.
            // We intentionally use raw HTTP so we can log response body/headers when a provider
            // returns non-JSON (common misconfiguration during early integration).
            var baseUrl = Settings.Resend.BaseUrl?.TrimEnd('/') ?? "https://api.resend.com";
            var url = $"{baseUrl}/emails";

            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            // Avoid logging HTML body (it contains tokens). Log size/metadata only.
            var payload = new
            {
                from = Settings.FromAddress,
                to = new[] { to },
                subject,
                html = htmlBody
            };
            var json = JsonSerializer.Serialize(payload);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");

            var client = _httpClientFactory.CreateClient("Resend");
            var sw = Stopwatch.StartNew();
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();

            var contentType = res.Content.Headers.ContentType?.ToString() ?? "<none>";
            var responseBody = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode ||
                (!contentType.Contains("application/json", StringComparison.OrdinalIgnoreCase)))
            {
                Logger.LogError(
                    "Resend email send failed. method={Method} url={Url} status={Status} reason={Reason} duration_ms={DurationMs} content_type={ContentType} from={From} to={To} subject={Subject} html_len={HtmlLen} activity_id={ActivityId} response_headers={ResponseHeaders} response_body={ResponseBody}",
                    req.Method.Method,
                    url,
                    (int)res.StatusCode,
                    res.ReasonPhrase ?? "<none>",
                    (long)sw.Elapsed.TotalMilliseconds,
                    contentType,
                    Settings.FromAddress,
                    to,
                    subject,
                    htmlBody.Length,
                    Activity.Current?.Id ?? "<none>",
                    FormatHeaders(res.Headers, res.Content.Headers),
                    TruncateForLog(responseBody, 32_000));

                return;
            }

            Logger.LogInformation(
                "Sent email via Resend-compatible API. url={Url} status={Status} duration_ms={DurationMs} to={To} subject={Subject}",
                url,
                (int)res.StatusCode,
                (long)sw.Elapsed.TotalMilliseconds,
                to,
                subject);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error sending email via Resend to {To}", to);
        }
    }

    private static string TruncateForLog(string s, int maxChars)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "<empty>";
        }

        if (s.Length <= maxChars)
        {
            return s;
        }

        return s[..maxChars] + $"...<truncated chars={s.Length - maxChars}>";
    }

    private static string FormatHeaders(HttpResponseHeaders headers, HttpContentHeaders contentHeaders)
    {
        // Compact single-line format; safe to log.
        var sb = new StringBuilder();
        foreach (var h in headers)
        {
            sb.Append(h.Key).Append('=').Append(string.Join(",", h.Value)).Append("; ");
        }

        foreach (var h in contentHeaders)
        {
            sb.Append(h.Key).Append('=').Append(string.Join(",", h.Value)).Append("; ");
        }

        return sb.Length == 0 ? "<none>" : sb.ToString();
    }
}
