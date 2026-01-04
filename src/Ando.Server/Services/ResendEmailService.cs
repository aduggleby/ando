// =============================================================================
// ResendEmailService.cs
//
// Summary: Email service implementation using Resend API.
//
// Sends transactional emails via the Resend API. Uses Razor views for
// email template rendering.
//
// Design Decisions:
// - Uses Razor views for email templates (same engine as web views)
// - Templates inherit from a base layout for consistent styling
// - API key from configuration
// =============================================================================

using System.Net.Http.Headers;
using System.Text.Json;
using Ando.Server.Configuration;
using Ando.Server.Email;
using Ando.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// Email service implementation using Resend API.
/// </summary>
public class ResendEmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly ResendSettings _settings;
    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ResendEmailService> _logger;

    public ResendEmailService(
        IHttpClientFactory httpClientFactory,
        IOptions<ResendSettings> settings,
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        ILogger<ResendEmailService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Resend");
        _settings = settings.Value;
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendBuildFailedEmailAsync(Build build, string recipientEmail)
    {
        var viewModel = new BuildFailedEmailViewModel
        {
            ProjectName = build.Project.RepoFullName,
            Branch = build.Branch,
            CommitSha = build.ShortCommitSha,
            CommitMessage = build.CommitMessage ?? "No message",
            CommitAuthor = build.CommitAuthor ?? "Unknown",
            ErrorMessage = build.ErrorMessage,
            BuildUrl = $"/builds/{build.Id}", // TODO: Full URL from config
            FailedAt = build.FinishedAt ?? DateTime.UtcNow
        };

        var htmlBody = await RenderViewAsync("Email/BuildFailed", viewModel);

        await SendEmailAsync(
            recipientEmail,
            $"Build Failed: {build.Project.RepoFullName} ({build.Branch})",
            htmlBody);
    }

    /// <summary>
    /// Sends an email via the Resend API.
    /// </summary>
    private async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        if (string.IsNullOrEmpty(_settings.ApiKey))
        {
            _logger.LogWarning("Resend API key not configured, skipping email to {To}", to);
            return;
        }

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);

            var body = new
            {
                from = $"{_settings.FromName} <{_settings.FromAddress}>",
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
                _logger.LogError("Failed to send email: {Status} - {Error}", response.StatusCode, error);
            }
            else
            {
                _logger.LogInformation("Sent email to {To}: {Subject}", to, subject);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email to {To}", to);
        }
    }

    /// <summary>
    /// Renders a Razor view to HTML string.
    /// </summary>
    private async Task<string> RenderViewAsync<TModel>(string viewName, TModel model)
    {
        var httpContext = new DefaultHttpContext { RequestServices = _serviceProvider };
        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());

        using var sw = new StringWriter();

        var viewResult = _viewEngine.FindView(actionContext, viewName, false);

        if (!viewResult.Success)
        {
            // Try as absolute path
            viewResult = _viewEngine.GetView(null, $"~/Views/{viewName}.cshtml", false);
        }

        if (!viewResult.Success)
        {
            throw new InvalidOperationException($"View '{viewName}' not found. Searched: {string.Join(", ", viewResult.SearchedLocations)}");
        }

        var viewDictionary = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model
        };

        var tempData = new TempDataDictionary(httpContext, _tempDataProvider);

        var viewContext = new ViewContext(
            actionContext,
            viewResult.View,
            viewDictionary,
            tempData,
            sw,
            new HtmlHelperOptions());

        await viewResult.View.RenderAsync(viewContext);

        return sw.ToString();
    }
}
