using GitHubDriver.Core.Models;

namespace GitHubDriver.Tests.Services;

/// <summary>
/// Unit tests for the model classes — verifying default values, factory methods,
/// and utility behaviour.
/// </summary>
public sealed class ModelTests
{
    // ── TaskRequest ────────────────────────────────────────────────────────────

    [Fact]
    public void TaskRequest_DefaultId_IsNotEmpty()
    {
        var req = new TaskRequest
        {
            Owner = "o",
            Repository = "r",
            TargetBranch = "main",
            Description = "d"
        };

        Assert.False(string.IsNullOrEmpty(req.Id));
    }

    [Fact]
    public void TaskRequest_CreatedAt_IsUtcAndRecent()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var req = new TaskRequest
        {
            Owner = "o",
            Repository = "r",
            TargetBranch = "main",
            Description = "d"
        };
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.InRange(req.CreatedAt, before, after);
    }

    [Fact]
    public void TaskRequest_ToString_ContainsKeyFields()
    {
        var req = new TaskRequest
        {
            Owner = "myorg",
            Repository = "myrepo",
            TargetBranch = "develop",
            Description = "Some task"
        };

        var str = req.ToString();
        Assert.Contains("myorg/myrepo", str);
        Assert.Contains("develop", str);
        Assert.Contains("Some task", str);
    }

    // ── TaskResult ─────────────────────────────────────────────────────────────

    [Fact]
    public void TaskResult_Success_HasCorrectProperties()
    {
        var result = TaskResult.Success("task-1", "abc123", 5, "feature-branch");

        Assert.True(result.IsSuccess);
        Assert.Equal("task-1", result.TaskId);
        Assert.Equal("abc123", result.MergeCommitSha);
        Assert.Equal(5, result.PullRequestNumber);
        Assert.Equal("feature-branch", result.WorkingBranch);
        Assert.Contains("abc123", result.Message);
    }

    [Fact]
    public void TaskResult_Failure_HasCorrectProperties()
    {
        var ex = new InvalidOperationException("boom");
        var result = TaskResult.Failure("task-2", "Something went wrong", ex);

        Assert.False(result.IsSuccess);
        Assert.Equal("task-2", result.TaskId);
        Assert.Equal("Something went wrong", result.Message);
        Assert.Same(ex, result.Error);
    }

    [Fact]
    public void TaskResult_ToString_ReflectsOutcome()
    {
        var success = TaskResult.Success("id", "deadbeef", 1, "branch");
        var failure = TaskResult.Failure("id", "oops");

        Assert.StartsWith("✅", success.ToString());
        Assert.StartsWith("❌", failure.ToString());
    }

    // ── WorkflowStatus ─────────────────────────────────────────────────────────

    [Fact]
    public void WorkflowStatus_IsTerminal_TrueForCompletedAndFailed()
    {
        var completed = new WorkflowStatus { TaskId = "t", Phase = WorkflowPhase.Completed };
        var failed    = new WorkflowStatus { TaskId = "t", Phase = WorkflowPhase.Failed    };
        var running   = new WorkflowStatus { TaskId = "t", Phase = WorkflowPhase.ReviewingCode };

        Assert.True(completed.IsTerminal);
        Assert.True(failed.IsTerminal);
        Assert.False(running.IsTerminal);
    }

    [Fact]
    public void WorkflowStatus_InitialPhase_IsNotStarted()
    {
        var status = new WorkflowStatus { TaskId = "t" };
        Assert.Equal(WorkflowPhase.NotStarted, status.Phase);
    }

    // ── SubTask ───────────────────────────────────────────────────────────────

    [Fact]
    public void SubTask_DefaultStatus_IsPending()
    {
        var sub = new SubTask { Title = "T", Description = "D" };
        Assert.Equal(SubTaskStatus.Pending, sub.Status);
    }

    [Fact]
    public void SubTask_AffectedFiles_DefaultsToEmpty()
    {
        var sub = new SubTask { Title = "T", Description = "D" };
        Assert.Empty(sub.AffectedFiles);
    }

    // ── CodeReviewResult ──────────────────────────────────────────────────────

    [Fact]
    public void CodeReviewResult_ToString_ApprovedPrefixedCheckmark()
    {
        var review = new CodeReviewResult
        {
            IsApproved = true,
            Summary = "Great code!",
            ConfidenceScore = 0.95
        };

        Assert.StartsWith("✅", review.ToString());
    }

    [Fact]
    public void CodeReviewResult_ToString_RejectedPrefixedCross()
    {
        var review = new CodeReviewResult
        {
            IsApproved = false,
            Summary = "Missing tests.",
            ConfidenceScore = 0.3
        };

        Assert.StartsWith("❌", review.ToString());
    }

    [Fact]
    public void ReviewComment_ToString_IncludesAllFields()
    {
        var comment = new ReviewComment
        {
            FilePath = "src/Foo.cs",
            LineNumber = 42,
            Severity = ReviewCommentSeverity.Error,
            Message = "Null dereference risk."
        };

        var str = comment.ToString();
        Assert.Contains("src/Foo.cs", str);
        Assert.Contains("42", str);
        Assert.Contains("Error", str);
        Assert.Contains("Null dereference risk.", str);
    }
}
