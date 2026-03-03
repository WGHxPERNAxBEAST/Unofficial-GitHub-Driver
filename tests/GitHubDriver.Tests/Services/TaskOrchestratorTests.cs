using GitHubDriver.Core.Configuration;
using GitHubDriver.Core.Models;
using GitHubDriver.Core.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace GitHubDriver.Tests.Services;

/// <summary>
/// Unit tests for <see cref="TaskOrchestrator"/>.
/// </summary>
public sealed class TaskOrchestratorTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static IOptions<GitHubDriverOptions> DefaultOptions(
        Action<GitHubDriverOptions>? configure = null)
    {
        var opts = new GitHubDriverOptions
        {
            GitHubToken = "test-token",
            RequireCiPass = false,   // disable CI wait by default in unit tests
            MaxReviewRetries = 1,
            MinReviewConfidence = 0.8
        };
        configure?.Invoke(opts);
        return Options.Create(opts);
    }

    private static TaskRequest BuildRequest(string description = "Add unit tests") =>
        new()
        {
            Owner = "test-owner",
            Repository = "test-repo",
            TargetBranch = "main",
            Description = description
        };

    private static IReadOnlyList<SubTask> SingleSubTask() =>
    [
        new SubTask { Index = 0, Title = "Write tests", Description = "Add xUnit tests." }
    ];

    private static CodeReviewResult ApprovedReview() =>
        new()
        {
            IsApproved = true,
            Summary = "Looks good!",
            ConfidenceScore = 0.95
        };

    // ── Happy-path test ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_HappyPath_ReturnsSuccess()
    {
        // Arrange
        var gitHub = new Mock<IGitHubService>(MockBehavior.Strict);
        var copilot = new Mock<ICopilotService>(MockBehavior.Strict);

        var request = BuildRequest();
        var workingBranch = $"copilot/task-{request.Id[..8]}";
        const int prNumber = 42;
        const string mergeSha = "abc1234";

        gitHub.Setup(g => g.CreateBranchAsync(
                request.Owner, request.Repository, workingBranch, request.TargetBranch, default))
            .ReturnsAsync("base-sha");

        copilot.Setup(c => c.DecomposeTaskAsync(request.Description, request.AdditionalContext, default))
            .ReturnsAsync(SingleSubTask());

        copilot.Setup(c => c.GenerateImplementationAsync(
                It.IsAny<SubTask>(), request.AdditionalContext, default))
            .ReturnsAsync("Implementation details.");

        gitHub.Setup(g => g.CreatePullRequestAsync(
                request.Owner, request.Repository,
                It.IsAny<string>(), It.IsAny<string>(),
                workingBranch, request.TargetBranch, default))
            .ReturnsAsync(prNumber);

        gitHub.Setup(g => g.GetPullRequestDiffAsync(
                request.Owner, request.Repository, prNumber, default))
            .ReturnsAsync("diff content");

        copilot.Setup(c => c.ReviewCodeAsync("diff content", request.Description, default))
            .ReturnsAsync(ApprovedReview());

        gitHub.Setup(g => g.AddPullRequestCommentAsync(
                request.Owner, request.Repository, prNumber, It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        copilot.Setup(c => c.GenerateCommitMessageAsync(
                request.Description, It.IsAny<IReadOnlyList<SubTask>>(), default))
            .ReturnsAsync("feat: add unit tests");

        gitHub.Setup(g => g.SquashMergePullRequestAsync(
                request.Owner, request.Repository, prNumber,
                It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(mergeSha);

        var orchestrator = new TaskOrchestrator(
            gitHub.Object, copilot.Object, DefaultOptions(), NullLogger<TaskOrchestrator>.Instance);

        // Act
        var result = await orchestrator.ExecuteAsync(request);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(request.Id, result.TaskId);
        Assert.Equal(mergeSha, result.MergeCommitSha);
        Assert.Equal(prNumber, result.PullRequestNumber);
        Assert.Equal(workingBranch, result.WorkingBranch);
    }

    // ── Status tracking tests ─────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_StoresStatusAfterCompletion()
    {
        // Arrange
        var gitHub = new Mock<IGitHubService>(MockBehavior.Strict);
        var copilot = new Mock<ICopilotService>(MockBehavior.Strict);
        var request = BuildRequest();
        var workingBranch = $"copilot/task-{request.Id[..8]}";
        const int prNumber = 7;

        gitHub.Setup(g => g.CreateBranchAsync(
                request.Owner, request.Repository, workingBranch, request.TargetBranch, default))
            .ReturnsAsync("sha");

        copilot.Setup(c => c.DecomposeTaskAsync(request.Description, null, default))
            .ReturnsAsync(SingleSubTask());

        copilot.Setup(c => c.GenerateImplementationAsync(It.IsAny<SubTask>(), null, default))
            .ReturnsAsync("impl");

        gitHub.Setup(g => g.CreatePullRequestAsync(
                request.Owner, request.Repository,
                It.IsAny<string>(), It.IsAny<string>(),
                workingBranch, request.TargetBranch, default))
            .ReturnsAsync(prNumber);

        gitHub.Setup(g => g.GetPullRequestDiffAsync(
                request.Owner, request.Repository, prNumber, default))
            .ReturnsAsync("diff");

        copilot.Setup(c => c.ReviewCodeAsync("diff", request.Description, default))
            .ReturnsAsync(ApprovedReview());

        gitHub.Setup(g => g.AddPullRequestCommentAsync(
                request.Owner, request.Repository, prNumber, It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        copilot.Setup(c => c.GenerateCommitMessageAsync(
                request.Description, It.IsAny<IReadOnlyList<SubTask>>(), default))
            .ReturnsAsync("feat: done");

        gitHub.Setup(g => g.SquashMergePullRequestAsync(
                request.Owner, request.Repository, prNumber,
                It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync("deadbeef");

        var orchestrator = new TaskOrchestrator(
            gitHub.Object, copilot.Object, DefaultOptions(), NullLogger<TaskOrchestrator>.Instance);

        // Act
        await orchestrator.ExecuteAsync(request);

        // Assert
        var status = orchestrator.GetStatus(request.Id);
        Assert.NotNull(status);
        Assert.Equal(WorkflowPhase.Completed, status.Phase);
        Assert.Equal(workingBranch, status.WorkingBranch);
        Assert.Equal(prNumber, status.PullRequestNumber);
    }

    [Fact]
    public void GetStatus_UnknownId_ReturnsNull()
    {
        var orchestrator = new TaskOrchestrator(
            Mock.Of<IGitHubService>(),
            Mock.Of<ICopilotService>(),
            DefaultOptions(),
            NullLogger<TaskOrchestrator>.Instance);

        var result = orchestrator.GetStatus("nonexistent-id");

        Assert.Null(result);
    }

    [Fact]
    public void GetAllStatuses_Initially_ReturnsEmpty()
    {
        var orchestrator = new TaskOrchestrator(
            Mock.Of<IGitHubService>(),
            Mock.Of<ICopilotService>(),
            DefaultOptions(),
            NullLogger<TaskOrchestrator>.Instance);

        Assert.Empty(orchestrator.GetAllStatuses());
    }

    // ── Failure propagation tests ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenCreateBranchFails_ReturnsFailed()
    {
        var gitHub = new Mock<IGitHubService>();
        gitHub.Setup(g => g.CreateBranchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), default))
            .ThrowsAsync(new HttpRequestException("Network error"));

        var orchestrator = new TaskOrchestrator(
            gitHub.Object,
            Mock.Of<ICopilotService>(),
            DefaultOptions(),
            NullLogger<TaskOrchestrator>.Instance);

        var result = await orchestrator.ExecuteAsync(BuildRequest());

        Assert.False(result.IsSuccess);
        Assert.Contains("Network error", result.Message);
        Assert.Equal(WorkflowPhase.Failed, result.FinalStatus!.Phase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenReviewRejectsPr_ReturnsFailed()
    {
        var gitHub = new Mock<IGitHubService>(MockBehavior.Strict);
        var copilot = new Mock<ICopilotService>(MockBehavior.Strict);
        var request = BuildRequest();
        var workingBranch = $"copilot/task-{request.Id[..8]}";
        const int prNumber = 99;

        gitHub.Setup(g => g.CreateBranchAsync(
                request.Owner, request.Repository, workingBranch, request.TargetBranch, default))
            .ReturnsAsync("sha");

        copilot.Setup(c => c.DecomposeTaskAsync(request.Description, null, default))
            .ReturnsAsync(SingleSubTask());

        copilot.Setup(c => c.GenerateImplementationAsync(It.IsAny<SubTask>(), null, default))
            .ReturnsAsync("impl");

        gitHub.Setup(g => g.CreatePullRequestAsync(
                request.Owner, request.Repository,
                It.IsAny<string>(), It.IsAny<string>(),
                workingBranch, request.TargetBranch, default))
            .ReturnsAsync(prNumber);

        gitHub.Setup(g => g.GetPullRequestDiffAsync(
                request.Owner, request.Repository, prNumber, default))
            .ReturnsAsync("diff");

        copilot.Setup(c => c.ReviewCodeAsync("diff", request.Description, default))
            .ReturnsAsync(new CodeReviewResult
            {
                IsApproved = false,
                Summary = "Tests are missing.",
                ConfidenceScore = 0.4
            });

        gitHub.Setup(g => g.AddPullRequestCommentAsync(
                request.Owner, request.Repository, prNumber, It.IsAny<string>(), default))
            .Returns(Task.CompletedTask);

        var orchestrator = new TaskOrchestrator(
            gitHub.Object, copilot.Object,
            DefaultOptions(o => o.MaxReviewRetries = 1),
            NullLogger<TaskOrchestrator>.Instance);

        var result = await orchestrator.ExecuteAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal(WorkflowPhase.Failed, result.FinalStatus!.Phase);
    }

    // ── Cancellation test ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_WhenCancelled_ReturnsCancelled()
    {
        var gitHub = new Mock<IGitHubService>();
        using var cts = new CancellationTokenSource();

        gitHub.Setup(g => g.CreateBranchAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async (string _, string _, string _, string _, CancellationToken ct) =>
            {
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return "sha";
            });

        var orchestrator = new TaskOrchestrator(
            gitHub.Object,
            Mock.Of<ICopilotService>(),
            DefaultOptions(),
            NullLogger<TaskOrchestrator>.Instance);

        var result = await orchestrator.ExecuteAsync(BuildRequest(), cts.Token);

        Assert.False(result.IsSuccess);
        Assert.Contains("cancel", result.Message, StringComparison.OrdinalIgnoreCase);
    }
}
