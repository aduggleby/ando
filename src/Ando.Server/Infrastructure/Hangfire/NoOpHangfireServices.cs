// =============================================================================
// NoOpHangfireServices.cs
//
// Summary: Test/E2E no-op implementations for Hangfire service abstractions.
//
// E2E runs without Hangfire storage/worker infrastructure. These stubs satisfy
// service dependencies while preserving startup behavior in E2E.
// =============================================================================

using Hangfire;
using Hangfire.States;

namespace Ando.Server.Infrastructure.Hangfire;

/// <summary>
/// No-op implementation of <see cref="IBackgroundJobClient"/> for E2E testing.
/// </summary>
internal sealed class NoOpBackgroundJobClient : IBackgroundJobClient
{
    private int _jobCounter;

    public string Create(global::Hangfire.Common.Job job, IState state)
    {
        return $"e2e-job-{Interlocked.Increment(ref _jobCounter)}";
    }

    public bool ChangeState(string jobId, IState state, string expectedState)
    {
        return true;
    }
}

/// <summary>
/// No-op implementation of <see cref="IRecurringJobManager"/> for E2E testing.
/// </summary>
internal sealed class NoOpRecurringJobManager : IRecurringJobManager
{
    public void AddOrUpdate(string recurringJobId, global::Hangfire.Common.Job job, string cronExpression, RecurringJobOptions options)
    {
    }

    public void Trigger(string recurringJobId)
    {
    }

    public void RemoveIfExists(string recurringJobId)
    {
    }
}
