// =============================================================================
// StepModelTests.cs
//
// Summary: Tests for the step model: Ando.Block / OnFailure / Finally / Defer /
// When / Require / Confirm / Report / CommandTimeout and how WorkflowRunner
// executes blocks, finally scopes and cancellation.
// =============================================================================

using Ando.Operations;
using Ando.Profiles;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Steps;

[Trait("Category", "Unit")]
public class StepModelTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();
    private readonly BuildOptions _options = new();
    private readonly List<string> _log = [];

    private AndoOperations CreateAndo() =>
        new(_registry, _logger, p => p, _options, new ArtifactOperations(_logger), new ProfileRegistry());

    // Registers a step that records its name and returns the given result.
    private void Step(string name, bool succeeds = true) =>
        _registry.Register(name, () =>
        {
            _log.Add(name);
            return Task.FromResult(succeeds);
        });

    private Task<WorkflowResult> RunAsync(CancellationToken token = default) =>
        new WorkflowRunner(_registry, _logger).RunAsync(_options, null, token);

    // ---- Blocks -------------------------------------------------------------

    [Fact]
    public async Task Block_RunsBodyStepsInOrder()
    {
        var ando = CreateAndo();
        ando.Block("b", () => { Step("a"); Step("b"); Step("c"); });

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _log.ShouldBe(["a", "b", "c"]);
    }

    [Fact]
    public async Task Block_OnFailure_RunsHandlerThenFinally_ThenBuildFails()
    {
        var ando = CreateAndo();
        ando.Block("b", () => { Step("a"); Step("boom", false); Step("never"); })
            .OnFailure(() => Step("onFailure"))
            .Finally(() => Step("finally"));
        Step("after");

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        _log.ShouldBe(["a", "boom", "onFailure", "finally"]);
    }

    [Fact]
    public async Task Block_OnFailure_DoesNotRunWhenBlockSucceeds()
    {
        var ando = CreateAndo();
        ando.Block("b", () => Step("a"))
            .OnFailure(() => Step("onFailure"))
            .Finally(() => Step("finally"));

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _log.ShouldBe(["a", "finally"]);
    }

    [Fact]
    public async Task Block_FailingFinallyStep_FailsBuild()
    {
        var ando = CreateAndo();
        ando.Block("b", () => Step("a")).Finally(() => Step("fin", false));

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Block_MultipleOnFailureHandlers_RunInAddedOrder()
    {
        var ando = CreateAndo();
        ando.Block("b", () => Step("boom", false))
            .OnFailure(() => Step("h1"))
            .OnFailure(() => Step("h2"));

        await RunAsync();

        _log.ShouldBe(["boom", "h1", "h2"]);
    }

    [Fact]
    public async Task NestedBlocks_OnFailure_RunsInnerHandlersBeforeOuter()
    {
        var ando = CreateAndo();
        ando.Block("outer", () =>
            {
                Step("outer-1");
                ando.Block("inner", () => Step("boom", false))
                    .OnFailure(() => Step("inner-onFailure"))
                    .Finally(() => Step("inner-finally"));
                Step("outer-never");
            })
            .OnFailure(() => Step("outer-onFailure"))
            .Finally(() => Step("outer-finally"));

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        _log.ShouldBe(["outer-1", "boom", "inner-onFailure", "inner-finally", "outer-onFailure", "outer-finally"]);
    }

    [Fact]
    public async Task Block_FinallyInsideBodyViaAndoFinally_RunsAtEndOfBlock()
    {
        var ando = CreateAndo();
        ando.Block("b", () =>
        {
            ando.Finally(() => Step("block-finally"));
            Step("a");
        });
        Step("after");

        await RunAsync();

        _log.ShouldBe(["a", "block-finally", "after"]);
    }

    // ---- Finally ------------------------------------------------------------

    [Fact]
    public async Task Finally_RunsLastRegisteredFirst()
    {
        var ando = CreateAndo();
        ando.Finally(() => Step("f1"));
        ando.Finally(() => Step("f2"));
        ando.Finally(() => Step("f3"));
        Step("main");

        await RunAsync();

        _log.ShouldBe(["main", "f3", "f2", "f1"]);
    }

    [Fact]
    public async Task Finally_EachEntryRunsEvenWhenAnEarlierOneFailed()
    {
        var ando = CreateAndo();
        ando.Finally(() => Step("f1"));
        ando.Finally(() => Step("f2", false));
        ando.Finally(() => Step("f3"));

        var result = await RunAsync();

        _log.ShouldBe(["f3", "f2", "f1"]);
        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Finally_FailingStepInsideOneLambda_DoesNotStopRemainingSteps()
    {
        var ando = CreateAndo();
        ando.Finally(() =>
        {
            Step("x");
            Step("y", false);
            Step("z");
        });

        var result = await RunAsync();

        _log.ShouldBe(["x", "y", "z"]);
        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Finally_TopLevel_RunsAfterSuccess()
    {
        var ando = CreateAndo();
        ando.Finally(() => Step("fin"));
        Step("main");

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _log.ShouldBe(["main", "fin"]);
    }

    [Fact]
    public async Task Finally_TopLevel_RunsAfterFailure()
    {
        var ando = CreateAndo();
        ando.Finally(() => Step("fin"));
        Step("main", false);
        Step("never");

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        _log.ShouldBe(["main", "fin"]);
    }

    // ---- Cancellation -------------------------------------------------------

    [Fact]
    public async Task Cancellation_PreCancelled_SkipsNormalStepsButRunsFinally()
    {
        var ando = CreateAndo();
        Step("main");
        ando.Finally(() => Step("fin"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await RunAsync(cts.Token);

        _log.ShouldBe(["fin"]);
        result.WasCancelled.ShouldBeTrue();
        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancellation_MidRun_SkipsRemainingStepsButRunsOnFailureAndFinally()
    {
        var ando = CreateAndo();
        using var cts = new CancellationTokenSource();
        ando.Block("b", () =>
            {
                _registry.Register("cancel", () =>
                {
                    _log.Add("cancel");
                    cts.Cancel();
                    return Task.FromResult(true);
                });
                Step("skipped");
            })
            .OnFailure(() => Step("onFailure"))
            .Finally(() => Step("finally"));
        Step("skipped-too");
        ando.Finally(() => Step("top-finally"));

        var result = await RunAsync(cts.Token);

        _log.ShouldBe(["cancel", "onFailure", "finally", "top-finally"]);
        result.WasCancelled.ShouldBeTrue();
        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancelled_IsTrueInOnFailureAfterCtrlC_FalseAfterPlainFailure()
    {
        var ando = CreateAndo();
        ando.Cancelled.ShouldBeFalse(); // evaluation time
        using var cts = new CancellationTokenSource();
        var seen = new List<bool>();
        ando.Block("cancelled", () =>
            {
                _registry.Register("cancel", () =>
                {
                    cts.Cancel();
                    return Task.FromResult(true);
                });
            })
            .OnFailure(() => ando.When(() => !ando.Cancelled, () => Step("swap"), () => Step("skip-swap")))
            .Finally(() => ando.Defer(() => seen.Add(ando.Cancelled)));

        var result = await RunAsync(cts.Token);

        result.WasCancelled.ShouldBeTrue();
        _log.ShouldBe(["skip-swap"]);
        seen.ShouldBe([true]);
        ando.Cancelled.ShouldBeFalse(); // outside the run
    }

    [Fact]
    public async Task Cancelled_IsFalseInOnFailureAfterPlainFailure()
    {
        var ando = CreateAndo();
        ando.Block("failing", () => Step("boom", false))
            .OnFailure(() => ando.When(() => !ando.Cancelled, () => Step("swap"), () => Step("skip-swap")));

        var result = await RunAsync();

        result.WasCancelled.ShouldBeFalse();
        _log.ShouldBe(["boom", "swap"]);
    }

    [Fact]
    public async Task SnapshotAborted_ContainsFinishedAndRunningSteps()
    {
        var runner = new WorkflowRunner(_registry, _logger);
        Step("done");
        _registry.Register("running", () =>
        {
            var snapshot = runner.SnapshotAborted();
            snapshot.Aborted.ShouldBeTrue();
            snapshot.StepResults.Select(r => (r.StepName, r.Success, r.ErrorMessage))
                .ShouldBe([("done", true, null), ("running", false, "aborted")]);
            return Task.FromResult(true);
        });

        var result = await runner.RunAsync(_options);

        result.Success.ShouldBeTrue();
        result.Aborted.ShouldBeFalse();
    }

    [Fact]
    public async Task Cancellation_StepThrowingOperationCanceled_RunsFinally()
    {
        var ando = CreateAndo();
        using var cts = new CancellationTokenSource();
        _registry.Register("cancelled", () =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        });
        ando.Finally(() => Step("fin"));

        var result = await RunAsync(cts.Token);

        result.WasCancelled.ShouldBeTrue();
        _log.ShouldBe(["fin"]);
    }

    [Fact]
    public async Task StepThrowingOperationCanceled_WithoutUserCancel_IsOrdinaryFailure()
    {
        var ando = CreateAndo();
        _registry.Register("timeout", () => throw new OperationCanceledException());
        ando.Finally(() => Step("fin"));

        var result = await RunAsync();

        result.WasCancelled.ShouldBeFalse();
        result.Success.ShouldBeFalse();
        _log.ShouldBe(["fin"]);
    }

    // ---- Defer / When -------------------------------------------------------

    [Fact]
    public async Task Defer_RunsAtExecutionTimeAndSeesEarlierStepValues()
    {
        var ando = CreateAndo();
        var value = "unset";
        var bodyRanDuringEvaluation = false;
        _registry.Register("set", () =>
        {
            value = "from-step";
            return Task.FromResult(true);
        });
        ando.Defer(() =>
        {
            bodyRanDuringEvaluation = true;
            Step($"deferred:{value}");
        });

        bodyRanDuringEvaluation.ShouldBeFalse();
        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _log.ShouldBe(["deferred:from-step"]);
    }

    [Fact]
    public async Task Defer_BodyThrowing_FailsBuildAndRecordsStep()
    {
        var ando = CreateAndo();
        ando.Defer(() => throw new InvalidOperationException("nope"));
        Step("never");

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        _log.ShouldBeEmpty();
        result.StepResults.ShouldContain(r => r.StepName == "Ando.Defer" && !r.Success && r.ErrorMessage == "nope");
    }

    [Fact]
    public async Task Defer_BoolSetAtRuntime_DrivesWhenInsideFinally()
    {
        var ando = CreateAndo();
        var added = false;
        ando.Defer(() => added = true);
        ando.Finally(() => ando.When(() => added, () => Step("remove"), () => Step("nothing-to-remove")));

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _log.ShouldBe(["remove"]);
    }

    [Fact]
    public async Task Defer_BoolNotSet_WhenInsideFinallyTakesOtherwise()
    {
        var ando = CreateAndo();
        var added = false;
        ando.Finally(() => ando.When(() => added, () => Step("remove"), () => Step("nothing-to-remove")));

        await RunAsync();

        _log.ShouldBe(["nothing-to-remove"]);
    }

    [Fact]
    public async Task When_ConditionTrue_RunsThenOnly()
    {
        var ando = CreateAndo();
        ando.When(() => true, () => Step("then"), () => Step("otherwise"));

        await RunAsync();

        _log.ShouldBe(["then"]);
    }

    [Fact]
    public async Task When_ConditionFalse_RunsOtherwise()
    {
        var ando = CreateAndo();
        ando.When(() => false, () => Step("then"), () => Step("otherwise"));

        await RunAsync();

        _log.ShouldBe(["otherwise"]);
    }

    [Fact]
    public async Task When_ConditionFalseWithoutOtherwise_RunsNothingAndSucceeds()
    {
        var ando = CreateAndo();
        ando.When(() => false, () => Step("then"));

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _log.ShouldBeEmpty();
    }

    [Fact]
    public async Task When_ConditionIsEvaluatedAtExecutionTime()
    {
        var ando = CreateAndo();
        var flag = false;
        ando.When(() => flag, () => Step("then"), () => Step("otherwise"));
        flag = true; // changed after registration, before the run

        await RunAsync();

        _log.ShouldBe(["then"]);
    }

    // ---- Require / Confirm --------------------------------------------------

    [Fact]
    public async Task Require_ConditionTrue_Passes()
    {
        var ando = CreateAndo();
        ando.Require(() => true, "must hold");

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Require_ConditionFalse_FailsBuildAndLogsMessage()
    {
        var ando = CreateAndo();
        ando.Require(() => false, "must hold");
        Step("never");

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain("must hold");
        _log.ShouldBeEmpty();
    }

    [Fact]
    public async Task Confirm_WithAssumeYes_Passes()
    {
        var ando = CreateAndo();
        _options.Session.AssumeYes = true;
        ando.Confirm("Swap into production?");

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _logger.InfoMessages.ShouldContain(m => m.Contains("Swap into production?") && m.Contains("--yes"));
    }

    [SkippableFact]
    public async Task Confirm_WithoutYesAndRedirectedInput_Fails()
    {
        Skip.IfNot(Console.IsInputRedirected, "This test needs redirected stdin (it is not under this runner).");
        var ando = CreateAndo();
        ando.Confirm("Swap into production?");
        Step("never");

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("--yes"));
        _log.ShouldBeEmpty();
    }

    // ---- Report -------------------------------------------------------------

    [Fact]
    public async Task Report_AddsToSessionAtExecutionTime()
    {
        var ando = CreateAndo();
        var output = new StepOutput("x");
        _registry.Register("produce", () =>
        {
            output.Set("  healthy \n");
            return Task.FromResult(true);
        });
        ando.Report("static", "value");
        ando.Report("output", output);
        ando.Report("lazy", () => "computed");

        _options.Session.Reports.ShouldBeEmpty();
        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _options.Session.Reports.Select(r => (r.Key, r.Value)).ToList().ShouldBe(
            [("static", "value"), ("output", "healthy"), ("lazy", "computed")]);
    }

    [Fact]
    public async Task Report_StepOutputNotSet_FailsTheStep()
    {
        var ando = CreateAndo();
        ando.Report("output", new StepOutput("never-ran"));

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        _options.Session.Reports.ShouldBeEmpty();
    }

    // ---- CommandTimeout -----------------------------------------------------

    [Fact]
    public async Task CommandTimeout_AppliesToLaterRegisteredCommandStepsOnly()
    {
        var ando = CreateAndo();
        var shell = new ShellCommandOperations(_registry, _logger, () => _executor);

        shell.Run("echo", "before");
        ando.CommandTimeout(TimeSpan.FromMinutes(15));
        shell.Run("echo", "after");
        shell.Run("echo", "own").WithTimeout(TimeSpan.FromSeconds(7));

        await RunAsync();

        _executor.ExecutedCommands[0].Options!.TimeoutMs.ShouldBe(Ando.Execution.CommandOptions.DefaultTimeoutMs);
        _executor.ExecutedCommands[1].Options!.TimeoutMs.ShouldBe(15 * 60 * 1000);
        _executor.ExecutedCommands[2].Options!.TimeoutMs.ShouldBe(7000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void CommandTimeout_NonPositive_Throws(int seconds)
    {
        var ando = CreateAndo();

        Should.Throw<ArgumentOutOfRangeException>(() => ando.CommandTimeout(TimeSpan.FromSeconds(seconds)));
    }

    // ---- Registry / results -------------------------------------------------

    [Fact]
    public void AllSteps_IncludesStepsInsideBlocksAndFinallyScopes()
    {
        var ando = CreateAndo();
        Step("top");
        ando.Block("b", () => Step("body"))
            .OnFailure(() => Step("onFailure"))
            .Finally(() => Step("blockFinally"));
        ando.Finally(() => Step("topFinally"));

        var names = _registry.AllSteps.Select(s => s.Name).ToList();

        names.ShouldContain("top");
        names.ShouldContain("body");
        names.ShouldContain("blockFinally");
        names.ShouldContain("topFinally");
        names.ShouldContain("Ando.Block");
        names.ShouldContain("Ando.Finally");
        _registry.Steps.Select(s => s.Name).ShouldBe(["top", "Ando.Block"]);
    }

    [Fact]
    public void AllSteps_IncludesOnFailureSteps()
    {
        var ando = CreateAndo();
        ando.Block("b", () => Step("body")).OnFailure(() => Step("onFailure"));

        _registry.AllSteps.Select(s => s.Name).ShouldContain("onFailure");
    }

    [Fact]
    public async Task StepResults_IncludeNestedStepsWithDepthAndCleanupFlags()
    {
        var ando = CreateAndo();
        Step("top");
        ando.Block("b", () => { Step("body"); Step("boom", false); })
            .OnFailure(() => Step("onFailure"))
            .Finally(() => Step("blockFinally"));

        var result = await RunAsync();

        StepResult R(string name) => result.StepResults.Single(r => r.StepName == name);
        R("top").Depth.ShouldBe(0);
        R("top").Cleanup.ShouldBeFalse();
        R("body").Depth.ShouldBeGreaterThan(0);
        R("body").Cleanup.ShouldBeFalse();
        R("boom").Success.ShouldBeFalse();
        R("onFailure").Depth.ShouldBeGreaterThan(0);
        R("onFailure").Cleanup.ShouldBeTrue();
        R("blockFinally").Depth.ShouldBeGreaterThan(0);
        R("blockFinally").Cleanup.ShouldBeTrue();
        result.StepResults.Select(r => r.StepName).ToList()
            .ShouldBe(["top", "body", "boom", "onFailure", "blockFinally"]);
    }

    [Fact]
    public async Task Clear_ResetsRootFinallyAndDefaultTimeout()
    {
        var ando = CreateAndo();
        ando.Finally(() => Step("fin"));
        ando.CommandTimeout(TimeSpan.FromMinutes(1));

        _registry.Clear();

        _registry.Root.Finally.ShouldBeEmpty();
        _registry.DefaultCommandTimeoutMs.ShouldBeNull();
        (await RunAsync()).StepsRun.ShouldBe(0);
    }
}
