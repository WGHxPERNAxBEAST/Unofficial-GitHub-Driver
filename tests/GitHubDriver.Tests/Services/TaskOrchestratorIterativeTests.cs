using GitHubDriver.Core.Configuration;
using GitHubDriver.Core.Models;
using GitHubDriver.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace GitHubDriver.Tests.Services;

/// <summary>
/// Tests for the iterative CI → fix → review → fix loop introduced in
/// <see cref="TaskOrchestrator"/>.
/// </summary>
public sealed class TaskOrchestratorIterativeTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IOptions<GitHubDriverOptions> Options(Action<GitHubDriverOptions>? configure = null)
    {
        var opts = new GitHubDriverOptions
        {
            GitHubToken      = "token",
            RequireCiPass    = true,
            MaxFixIterations = 5,
            MaxReviewRetries = 3,
            MinReviewConfidence = 0.8
        };
        configure?.Invoke(opts);
        return Microsoft.Extensions.Options.Options.Create(opts);
    }

    private static TaskRequest Request(string description = "Add feature X") => new()
    {
        Owner = "owner", Repository = "repo", TargetBranch = "main", Description = description
    };

    private static IReadOnlyList<SubTask> OneSubTask() =>
    [
        new SubTask { Index = 0, Title = "Do it", Description = "Do the thing." }
    ];

    private static FixPlan EmptyFix()  => new() { Summary = "No changes needed." };
    private static FixPlan OneFix()    => new()
    {
        Summary = "Fixed it.",
        FileChanges = [new FileChange { Path = "src/Foo.cs", Content = "fixed", Reason = "bug" }]
    };

    private static CodeReviewResult Approved()  => new() { IsApproved = true,  Summary = "LGTM",          ConfidenceScore = 0.95 };
    private static CodeReviewResult Rejected()  => new() { IsApproved = false, Summary = "Missing tests.", ConfidenceScore = 0.40 };

    // ── Setup shared mocks that are the same for all iterative-loop tests ────

    private static (Mock<IGitHubService> gh, Mock<ICopilotService> cp, TaskRequest req, string branch, int prNum)
        BuildMocks()
    {
        var req    = Request();
        var branch = $"copilot/task-{req.Id[..8]}";
        const int prNum = 77;

        var gh = new Mock<IGitHubService>(MockBehavior.Strict);
        var cp = new Mock<ICopilotService>(MockBehavior.Strict);

        gh.Setup(g => g.CreateBranchAsync(
                req.Owner, req.Repository, branch, req.TargetBranch, default))
            .ReturnsAsync("sha");

        cp.Setup(c => c.DecomposeTaskAsync(req.Description, req.AdditionalContext, default))
            .ReturnsAsync(OneSubTask());

        cp.Setup(c => c.GenerateImplementationAsync(It.IsAny<SubTask>(), req.AdditionalContext, default))
            .ReturnsAsync("impl");

        gh.Setup(g => g.CreatePullRequestAsync(
                req.Owner, req.Repository,
                It.IsAny<string>(), It.IsAny<string>(),
                branch, req.TargetBranch, default))
            .ReturnsAsync(prNum);

        gh.Setup(g => g.AddPullRequestCommentAsync(
                req.Owner, req.Repository, prNum, It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        cp.Setup(c => c.GenerateCommitMessageAsync(
                req.Description, It.IsAny<IReadOnlyList<SubTask>>(), default))
            .ReturnsAsync("feat: done");

        gh.Setup(g => g.SquashMergePullRequestAsync(
                req.Owner, req.Repository, prNum,
                It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync("cafebabe");

        return (gh, cp, req, branch, prNum);
    }

    // ── CI fails once, then passes; review approves immediately ─────────────

    [Fact]
    public async Task IterativeLoop_CiFailsOnceThenPasses_AppliesFixAndSucceeds()
    {
        var (gh, cp, req, branch, prNum) = BuildMocks();

        // CI: fails on iteration 1, passes on iteration 2.
        var ciCallCount = 0;
        gh.Setup(g => g.GetBranchCheckStatusAsync(req.Owner, req.Repository, branch, default))
            .ReturnsAsync(() => ++ciCallCount == 1 ? CheckStatus.Failure : CheckStatus.Success);

        gh.Setup(g => g.GetCheckRunLogsAsync(req.Owner, req.Repository, branch, default))
            .ReturnsAsync("FAILED: NullReferenceException in FooTest");

        gh.Setup(g => g.GetPullRequestDiffAsync(req.Owner, req.Repository, prNum, default))
            .ReturnsAsync("diff content");

        cp.Setup(c => c.AnalyzeTestFailuresAsync(
                "FAILED: NullReferenceException in FooTest", req.Description, "diff content", default))
            .ReturnsAsync(OneFix());

        gh.Setup(g => g.ApplyFileChangesAsync(
                req.Owner, req.Repository, branch, It.IsAny<IReadOnlyList<FileChange>>(),
                It.Is<string>(m => m.Contains("test failures")), default))
            .Returns(Task.CompletedTask);

        cp.Setup(c => c.ReviewCodeAsync("diff content", req.Description, default))
            .ReturnsAsync(Approved());

        var orchestrator = new TaskOrchestrator(gh.Object, cp.Object, Options(), NullLogger<TaskOrchestrator>.Instance);
        var result = await orchestrator.ExecuteAsync(req);

        Assert.True(result.IsSuccess);
        Assert.Equal("cafebabe", result.MergeCommitSha);

        // Fix was committed once.
        gh.Verify(g => g.ApplyFileChangesAsync(
            req.Owner, req.Repository, branch, It.IsAny<IReadOnlyList<FileChange>>(),
            It.Is<string>(m => m.Contains("test failures")), default), Times.Once);
    }

    // ── CI passes but review rejects once; fixes applied, then approved ──────

    [Fact]
    public async Task IterativeLoop_ReviewRejectsOnceThenApproves_AppliesFixAndSucceeds()
    {
        var (gh, cp, req, branch, prNum) = BuildMocks();

        gh.Setup(g => g.GetBranchCheckStatusAsync(req.Owner, req.Repository, branch, default))
            .ReturnsAsync(CheckStatus.Success);

        gh.Setup(g => g.GetPullRequestDiffAsync(req.Owner, req.Repository, prNum, default))
            .ReturnsAsync("diff");

        // Review: rejects on iteration 1, approves on iteration 2.
        var reviewCallCount = 0;
        cp.Setup(c => c.ReviewCodeAsync("diff", req.Description, default))
            .ReturnsAsync(() => ++reviewCallCount == 1 ? Rejected() : Approved());

        cp.Setup(c => c.GenerateFixForReviewFeedbackAsync(
                It.IsAny<CodeReviewResult>(), "diff", req.Description, default))
            .ReturnsAsync(OneFix());

        gh.Setup(g => g.ApplyFileChangesAsync(
                req.Owner, req.Repository, branch, It.IsAny<IReadOnlyList<FileChange>>(),
                It.Is<string>(m => m.Contains("review feedback")), default))
            .Returns(Task.CompletedTask);

        var orchestrator = new TaskOrchestrator(gh.Object, cp.Object, Options(), NullLogger<TaskOrchestrator>.Instance);
        var result = await orchestrator.ExecuteAsync(req);

        Assert.True(result.IsSuccess);

        // Verify review was called twice (once rejected, once approved).
        cp.Verify(c => c.ReviewCodeAsync("diff", req.Description, default), Times.Exactly(2));

        // Verify fix was committed once.
        gh.Verify(g => g.ApplyFileChangesAsync(
            req.Owner, req.Repository, branch, It.IsAny<IReadOnlyList<FileChange>>(),
            It.Is<string>(m => m.Contains("review feedback")), default), Times.Once);
    }

    // ── CI fails, fix applied, CI passes, review rejects, fix applied, review approves ──

    [Fact]
    public async Task IterativeLoop_CiFailThenReviewReject_BothFixed_Succeeds()
    {
        var (gh, cp, req, branch, prNum) = BuildMocks();

        // CI: fails once (iteration 1), then passes (iterations 2+).
        var ciCallCount = 0;
        gh.Setup(g => g.GetBranchCheckStatusAsync(req.Owner, req.Repository, branch, default))
            .ReturnsAsync(() => ++ciCallCount == 1 ? CheckStatus.Failure : CheckStatus.Success);

        gh.Setup(g => g.GetCheckRunLogsAsync(req.Owner, req.Repository, branch, default))
            .ReturnsAsync("test failure log");

        gh.Setup(g => g.GetPullRequestDiffAsync(req.Owner, req.Repository, prNum, default))
            .ReturnsAsync("diff");

        cp.Setup(c => c.AnalyzeTestFailuresAsync("test failure log", req.Description, "diff", default))
            .ReturnsAsync(OneFix());

        gh.Setup(g => g.ApplyFileChangesAsync(
                req.Owner, req.Repository, branch, It.IsAny<IReadOnlyList<FileChange>>(),
                It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        // Review: rejects once (iteration 2), then approves (iteration 3).
        var reviewCallCount = 0;
        cp.Setup(c => c.ReviewCodeAsync("diff", req.Description, default))
            .ReturnsAsync(() => ++reviewCallCount == 1 ? Rejected() : Approved());

        cp.Setup(c => c.GenerateFixForReviewFeedbackAsync(
                It.IsAny<CodeReviewResult>(), "diff", req.Description, default))
            .ReturnsAsync(OneFix());

        var orchestrator = new TaskOrchestrator(gh.Object, cp.Object, Options(), NullLogger<TaskOrchestrator>.Instance);
        var result = await orchestrator.ExecuteAsync(req);

        Assert.True(result.IsSuccess);

        // Total file-change commits: 1 (CI fix) + 1 (review fix) = 2.
        gh.Verify(g => g.ApplyFileChangesAsync(
            req.Owner, req.Repository, branch, It.IsAny<IReadOnlyList<FileChange>>(),
            It.IsAny<string>(), default), Times.Exactly(2));
    }

    // ── Max iterations exceeded ───────────────────────────────────────────────

    [Fact]
    public async Task IterativeLoop_AlwaysRejects_FailsAfterMaxIterations()
    {
        var (gh, cp, req, branch, prNum) = BuildMocks();

        gh.Setup(g => g.GetBranchCheckStatusAsync(req.Owner, req.Repository, branch, default))
            .ReturnsAsync(CheckStatus.Success);

        gh.Setup(g => g.GetPullRequestDiffAsync(req.Owner, req.Repository, prNum, default))
            .ReturnsAsync("diff");

        // Review always rejects.
        cp.Setup(c => c.ReviewCodeAsync("diff", req.Description, default))
            .ReturnsAsync(Rejected());

        cp.Setup(c => c.GenerateFixForReviewFeedbackAsync(
                It.IsAny<CodeReviewResult>(), "diff", req.Description, default))
            .ReturnsAsync(OneFix());

        gh.Setup(g => g.ApplyFileChangesAsync(
                req.Owner, req.Repository, branch, It.IsAny<IReadOnlyList<FileChange>>(),
                It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        const int maxIter = 3;
        var orchestrator = new TaskOrchestrator(
            gh.Object, cp.Object,
            Options(o => o.MaxFixIterations = maxIter),
            NullLogger<TaskOrchestrator>.Instance);

        var result = await orchestrator.ExecuteAsync(req);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkflowPhase.Failed, result.FinalStatus!.Phase);

        // Review was called exactly MaxFixIterations times.
        cp.Verify(c => c.ReviewCodeAsync("diff", req.Description, default), Times.Exactly(maxIter));
    }

    // ── Status reflects FixIteration counter ─────────────────────────────────

    [Fact]
    public async Task IterativeLoop_FixIterationCounterIncrements()
    {
        var (gh, cp, req, branch, prNum) = BuildMocks();

        // CI always passes; review rejects once then approves.
        gh.Setup(g => g.GetBranchCheckStatusAsync(req.Owner, req.Repository, branch, default))
            .ReturnsAsync(CheckStatus.Success);

        gh.Setup(g => g.GetPullRequestDiffAsync(req.Owner, req.Repository, prNum, default))
            .ReturnsAsync("diff");

        var reviewCallCount = 0;
        cp.Setup(c => c.ReviewCodeAsync("diff", req.Description, default))
            .ReturnsAsync(() => ++reviewCallCount == 1 ? Rejected() : Approved());

        cp.Setup(c => c.GenerateFixForReviewFeedbackAsync(
                It.IsAny<CodeReviewResult>(), "diff", req.Description, default))
            .ReturnsAsync(EmptyFix());

        var orchestrator = new TaskOrchestrator(gh.Object, cp.Object, Options(), NullLogger<TaskOrchestrator>.Instance);
        await orchestrator.ExecuteAsync(req);

        var status = orchestrator.GetStatus(req.Id);
        Assert.NotNull(status);
        // Should have reached at least iteration 2 (rejected once, approved on second pass).
        Assert.True(status.FixIteration >= 2,
            $"Expected FixIteration >= 2 but got {status.FixIteration}");
    }

    // ── WorkflowPhase transitions through fix phases ──────────────────────────

    [Fact]
    public async Task IterativeLoop_CiFailure_PhaseIsFixingTestFailuresDuringFix()
    {
        var (gh, cp, req, branch, prNum) = BuildMocks();

        var phasesDuringFix = new List<WorkflowPhase>();

        // CI: fails on first call, passes on second.
        var ciCallCount = 0;
        gh.Setup(g => g.GetBranchCheckStatusAsync(req.Owner, req.Repository, branch, default))
            .ReturnsAsync(() => ++ciCallCount == 1 ? CheckStatus.Failure : CheckStatus.Success);

        gh.Setup(g => g.GetCheckRunLogsAsync(req.Owner, req.Repository, branch, default))
            .ReturnsAsync("logs");

        gh.Setup(g => g.GetPullRequestDiffAsync(req.Owner, req.Repository, prNum, default))
            .ReturnsAsync("diff");

        cp.Setup(c => c.AnalyzeTestFailuresAsync("logs", req.Description, "diff", default))
            .ReturnsAsync(EmptyFix());

        cp.Setup(c => c.ReviewCodeAsync("diff", req.Description, default))
            .ReturnsAsync(Approved());

        var orchestrator = new TaskOrchestrator(gh.Object, cp.Object, Options(), NullLogger<TaskOrchestrator>.Instance);
        await orchestrator.ExecuteAsync(req);

        // Final status should be Completed (CI fixed, review approved).
        var status = orchestrator.GetStatus(req.Id);
        Assert.Equal(WorkflowPhase.Completed, status!.Phase);
    }
}
