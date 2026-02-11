// =============================================================================
// BaseEmailService.cs
//
// Summary: Abstract base class for email service implementations.
//
// Provides shared functionality for all email providers:
// - Razor view rendering for email templates
// - Build failure email composition
// - Common logging patterns
//
// Design Decisions:
// - Abstract class rather than interface to share Razor rendering logic
// - Each provider implements only the SendEmailAsync method
// - View rendering is provider-agnostic
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Email;
using Ando.Server.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// Base class for email service implementations providing shared Razor rendering.
/// </summary>
public abstract class BaseEmailService : IEmailService
{
    protected readonly EmailSettings Settings;
    protected readonly ILogger Logger;
    private readonly IUrlService _urlService;
    private readonly AndoDbContext _db;

    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;

    protected BaseEmailService(
        IOptions<EmailSettings> settings,
        IUrlService urlService,
        AndoDbContext db,
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        Settings = settings.Value;
        _urlService = urlService;
        _db = db;
        _viewEngine = viewEngine;
        _tempDataProvider = tempDataProvider;
        _serviceProvider = serviceProvider;
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract Task SendEmailAsync(string to, string subject, string htmlBody);

    /// <inheritdoc />
    public async Task SendBuildFailedEmailAsync(Build build, string recipientEmail)
    {
        var recentLogLines = await GetRecentLogLinesAsync(build.Id, 50);

        var viewModel = new BuildFailedEmailViewModel
        {
            BuildId = build.Id,
            ProjectName = build.Project.RepoFullName,
            Branch = build.Branch,
            CommitSha = build.ShortCommitSha,
            CommitMessage = build.CommitMessage ?? "No message",
            CommitAuthor = build.CommitAuthor ?? "Unknown",
            Trigger = build.Trigger.ToString(),
            Status = build.Status.ToString(),
            DurationText = build.Duration.HasValue
                ? $"{build.Duration.Value.TotalSeconds:F0}s"
                : null,
            StepsSummary = $"{build.StepsCompleted}/{build.StepsTotal} passed, {build.StepsFailed} failed",
            ErrorMessage = build.ErrorMessage,
            BuildUrl = _urlService.BuildUrl($"/builds/{build.Id}"),
            FailedAt = build.FinishedAt ?? DateTime.UtcNow,
            RecentLogLines = recentLogLines
        };

        var htmlBody = await RenderViewAsync("Email/BuildFailed", viewModel);

        await SendEmailAsync(
            recipientEmail,
            $"Build Failed: {build.Project.RepoFullName} ({build.Branch})",
            htmlBody);
    }

    private async Task<List<string>> GetRecentLogLinesAsync(int buildId, int maxLines)
    {
        // Pull a recent window of log entries, then slice to the last N physical lines.
        var recentEntries = await _db.BuildLogEntries
            .AsNoTracking()
            .Where(l => l.BuildId == buildId)
            .OrderByDescending(l => l.Sequence)
            .Take(200)
            .Select(l => l.Message)
            .ToListAsync();

        if (recentEntries.Count == 0)
        {
            return [];
        }

        recentEntries.Reverse();

        var allLines = new List<string>();
        foreach (var message in recentEntries)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                continue;
            }

            foreach (var line in message.Split('\n'))
            {
                var normalized = line.TrimEnd('\r');
                allLines.Add(string.IsNullOrWhiteSpace(normalized) ? " " : normalized);
            }
        }

        if (allLines.Count <= maxLines)
        {
            return allLines;
        }

        return allLines.TakeLast(maxLines).ToList();
    }

    /// <summary>
    /// Gets the formatted "from" string for email headers.
    /// </summary>
    protected string GetFromString() => $"{Settings.FromName} <{Settings.FromAddress}>";

    /// <summary>
    /// Renders a Razor view to HTML string.
    /// </summary>
    protected async Task<string> RenderViewAsync<TModel>(string viewName, TModel model)
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
            throw new InvalidOperationException(
                $"View '{viewName}' not found. Searched: {string.Join(", ", viewResult.SearchedLocations)}");
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
