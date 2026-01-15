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
/// Base class for email service implementations providing shared Razor rendering.
/// </summary>
public abstract class BaseEmailService : IEmailService
{
    protected readonly EmailSettings Settings;
    protected readonly ILogger Logger;

    private readonly IRazorViewEngine _viewEngine;
    private readonly ITempDataProvider _tempDataProvider;
    private readonly IServiceProvider _serviceProvider;

    protected BaseEmailService(
        IOptions<EmailSettings> settings,
        IRazorViewEngine viewEngine,
        ITempDataProvider tempDataProvider,
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        Settings = settings.Value;
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
