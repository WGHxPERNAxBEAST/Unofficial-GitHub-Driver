using System.Collections.Concurrent;
using GitHubDriver.Core.Configuration;
using GitHubDriver.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubDriver.Core.Services;

/// <summary>
/// Implements <see cref="ITaskOrchestrator"/> to drive the full automated task workflow:
/// branch creation → Copilot decomposition → implementation → CI wait → PR creation →
/// code review → squash-merge.
/// </summary>
public sealed class TaskOrchestrator : ITaskOrchestrator
{
    private readonly IGitHubService _gitHub;
    private readonly ICopilotService _copilot;
    private readonly GitHubDriverOptions _options;
    private readonly ILogger<TaskOrchestrator> _logger;

    /// <summary>Thread-safe store of in-progress and completed workflow statuses keyed by task ID.</summary>
    private readonly ConcurrentDictionary<string, WorkflowStatus> _statuses = new();

    /// <summary>
    /// Initializes a new instance of <see cref="TaskOrchestrator"/>.
    /// </summary>
    /// <param name="gitHub">The GitHub API service.</param>
    /// <param name="copilot">The Copilot service.</param>
    /// <param name="options">The driver configuration options.</param>
    /// <param name="logger">The logger.</param>
    public TaskOrchestrator(
        IGitHubService gitHub,
        ICopilotService copilot,
        IOptions<GitHubDriverOptions> options,
        ILogger<TaskOrchestrator> logger)
    {
        _gitHub = gitHub;
        _copilot = copilot;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public WorkflowStatus? GetStatus(string taskId) =>
        _statuses.TryGetValue(taskId, out var status) ? status : null;

    /// <inheritdoc/>
    public IReadOnlyCollection<WorkflowStatus> GetAllStatuses() =>
        _statuses.Values.ToList();

    /// <inheritdoc/>
    public async Task<TaskResult> ExecuteAsync(
        TaskRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var status = new WorkflowStatus { TaskId = request.Id };
        _statuses[request.Id] = status;

        _logger.LogInformation("Starting workflow for task {Id}: {Description}", request.Id, request.Description);

        try
        {
            // ── Phase 1: Create the working branch ───────────────────────────
            Advance(status, WorkflowPhase.CreatingBranch, "Creating working branch.");

            var workingBranch = $"{_options.BranchPrefix}{request.Id[..8]}";
            status.WorkingBranch = workingBranch;

            await _gitHub.CreateBranchAsync(
                request.Owner, request.Repository, workingBranch, request.TargetBranch, cancellationToken)
                .ConfigureAwait(false);

            // ── Phase 2: Decompose the task ──────────────────────────────────
            Advance(status, WorkflowPhase.DecomposingTask, "Decomposing task into subtasks.");

            var subTasks = await _copilot.DecomposeTaskAsync(
                request.Description, request.AdditionalContext, cancellationToken)
                .ConfigureAwait(false);

            status.SubTasks = [..subTasks];

            _logger.LogInformation(
                "Task decomposed into {Count} subtask(s).", subTasks.Count);

            // ── Phase 3: Implement the subtasks ──────────────────────────────
            Advance(status, WorkflowPhase.ImplementingChanges, $"Implementing {subTasks.Count} subtask(s).");

            foreach (var subTask in status.SubTasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteSubTaskAsync(request, subTask, cancellationToken).ConfigureAwait(false);
            }

            // ── Phase 4: Wait for CI ──────────────────────────────────────────
            if (_options.RequireCiPass)
            {
                Advance(status, WorkflowPhase.RunningTests, "Waiting for CI checks to complete.");
                await WaitForCiAsync(request.Owner, request.Repository, workingBranch, cancellationToken)
                    .ConfigureAwait(false);
            }

            // ── Phase 5: Create pull request ──────────────────────────────────
            Advance(status, WorkflowPhase.CreatingPullRequest, "Creating pull request.");

            var prTitle = $"[Automated] {TrimToLength(request.Description, 80)}";
            var prBody = BuildPullRequestBody(request, status.SubTasks);

            var prNumber = await _gitHub.CreatePullRequestAsync(
                request.Owner, request.Repository,
                prTitle, prBody,
                workingBranch, request.TargetBranch,
                cancellationToken)
                .ConfigureAwait(false);

            status.PullRequestNumber = prNumber;

            // ── Phase 6: Code review (with retry) ─────────────────────────────
            Advance(status, WorkflowPhase.ReviewingCode, $"Reviewing PR #{prNumber}.");

            await ReviewWithRetriesAsync(request, prNumber, status, cancellationToken)
                .ConfigureAwait(false);

            // ── Phase 7: Squash-merge ──────────────────────────────────────────
            Advance(status, WorkflowPhase.MergingChanges, $"Squash-merging PR #{prNumber}.");

            var commitMessage = await _copilot.GenerateCommitMessageAsync(
                request.Description, [..status.SubTasks], cancellationToken)
                .ConfigureAwait(false);

            var mergeCommitSha = await _gitHub.SquashMergePullRequestAsync(
                request.Owner, request.Repository,
                prNumber,
                prTitle,
                commitMessage,
                cancellationToken)
                .ConfigureAwait(false);

            // ── Done ───────────────────────────────────────────────────────────
            Advance(status, WorkflowPhase.Completed,
                $"Task completed successfully. Commit: {mergeCommitSha}");

            _logger.LogInformation(
                "Task {Id} completed successfully. Squash-merge commit: {Sha}",
                request.Id, mergeCommitSha);

            return TaskResult.Success(
                request.Id, mergeCommitSha, prNumber, workingBranch, status);
        }
        catch (OperationCanceledException)
        {
            Advance(status, WorkflowPhase.Failed, "Workflow was cancelled.");
            _logger.LogWarning("Task {Id} was cancelled.", request.Id);
            return TaskResult.Failure(request.Id, "Workflow was cancelled.", status: status);
        }
        catch (Exception ex)
        {
            Advance(status, WorkflowPhase.Failed, $"Workflow failed: {ex.Message}");
            _logger.LogError(ex, "Task {Id} failed in phase {Phase}.", request.Id, status.Phase);
            return TaskResult.Failure(request.Id, ex.Message, ex, status);
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single subtask: calls Copilot for implementation guidance and marks the
    /// subtask as completed (or failed).
    /// </summary>
    private async Task ExecuteSubTaskAsync(
        TaskRequest request,
        SubTask subTask,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Executing subtask {Index}: {Title}", subTask.Index, subTask.Title);

        subTask.Status = SubTaskStatus.InProgress;

        try
        {
            var implementation = await _copilot.GenerateImplementationAsync(
                subTask, request.AdditionalContext, cancellationToken)
                .ConfigureAwait(false);

            subTask.Output = implementation;
            subTask.Status = SubTaskStatus.Completed;

            _logger.LogDebug(
                "Subtask {Index} implementation generated ({Length} chars).",
                subTask.Index, implementation.Length);
        }
        catch (Exception ex)
        {
            subTask.Status = SubTaskStatus.Failed;
            subTask.Output = ex.Message;
            _logger.LogError(ex, "Subtask {Index} failed.", subTask.Index);
            throw;
        }
    }

    /// <summary>
    /// Polls the GitHub CI check status for the working branch until it either passes,
    /// fails, or the configured timeout expires.
    /// </summary>
    private async Task WaitForCiAsync(
        string owner,
        string repo,
        string branchName,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(_options.CiTimeoutSeconds);
        var interval = TimeSpan.FromSeconds(_options.CiPollingIntervalSeconds);

        _logger.LogInformation("Waiting up to {Timeout}s for CI on '{Branch}'.", _options.CiTimeoutSeconds, branchName);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var checkStatus = await _gitHub
                .GetBranchCheckStatusAsync(owner, repo, branchName, cancellationToken)
                .ConfigureAwait(false);

            switch (checkStatus)
            {
                case CheckStatus.Success:
                    _logger.LogInformation("CI checks passed on '{Branch}'.", branchName);
                    return;

                case CheckStatus.Failure:
                    throw new InvalidOperationException(
                        $"CI checks failed on branch '{branchName}'. Halting workflow.");

                case CheckStatus.Unknown:
                    // No checks configured; treat as passing.
                    _logger.LogWarning(
                        "No CI checks found on '{Branch}'. Continuing without CI validation.", branchName);
                    return;

                default:
                    _logger.LogDebug("CI still pending on '{Branch}'. Waiting {Interval}s…", branchName, interval.TotalSeconds);
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        throw new TimeoutException(
            $"CI checks on '{branchName}' did not complete within {_options.CiTimeoutSeconds} seconds.");
    }

    /// <summary>
    /// Runs an automated code review loop, retrying up to <see cref="GitHubDriverOptions.MaxReviewRetries"/>
    /// times if the initial review does not approve the pull request.
    /// </summary>
    private async Task ReviewWithRetriesAsync(
        TaskRequest request,
        int prNumber,
        WorkflowStatus status,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= _options.MaxReviewRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var diff = await _gitHub
                .GetPullRequestDiffAsync(request.Owner, request.Repository, prNumber, cancellationToken)
                .ConfigureAwait(false);

            var review = await _copilot
                .ReviewCodeAsync(diff, request.Description, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Review attempt {Attempt}/{Max}: {Result}", attempt, _options.MaxReviewRetries, review);

            // Post the review summary as a PR comment so humans can see it.
            var commentBody = BuildReviewComment(review, attempt);
            await _gitHub
                .AddPullRequestCommentAsync(
                    request.Owner, request.Repository, prNumber, commentBody, cancellationToken)
                .ConfigureAwait(false);

            if (review.IsApproved && review.ConfidenceScore >= _options.MinReviewConfidence)
            {
                _logger.LogInformation(
                    "PR #{Number} approved on attempt {Attempt} (confidence {Score:P0}).",
                    prNumber, attempt, review.ConfidenceScore);
                return;
            }

            if (attempt == _options.MaxReviewRetries)
            {
                throw new InvalidOperationException(
                    $"Code review rejected PR #{prNumber} after {_options.MaxReviewRetries} attempt(s). " +
                    $"Last summary: {review.Summary}");
            }

            // Surface review issues as subtask feedback so the next iteration can address them.
            var issuesSummary = string.Join("\n", review.Comments.Select(c => c.ToString()));
            _logger.LogWarning(
                "Review rejected on attempt {Attempt}. Issues:\n{Issues}", attempt, issuesSummary);
        }
    }

    /// <summary>
    /// Advances the workflow to the next phase and updates the status timestamp.
    /// </summary>
    private static void Advance(WorkflowStatus status, WorkflowPhase phase, string message)
    {
        status.Phase = phase;
        status.Message = message;
        status.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Builds the Markdown body for the automated pull request.
    /// </summary>
    private static string BuildPullRequestBody(TaskRequest request, IList<SubTask> subTasks)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 🤖 Automated Task");
        sb.AppendLine();
        sb.AppendLine($"**Task:** {request.Description}");
        sb.AppendLine();
        sb.AppendLine("### Subtasks Completed");
        foreach (var s in subTasks)
            sb.AppendLine($"- [x] {s.Title}");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine("*This pull request was created and reviewed automatically by GitHubDriver.*");
        return sb.ToString();
    }

    /// <summary>
    /// Formats a Copilot code-review result as a Markdown comment suitable for posting to a PR.
    /// </summary>
    private static string BuildReviewComment(CodeReviewResult review, int attempt)
    {
        var icon = review.IsApproved ? "✅" : "❌";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## {icon} Automated Code Review (attempt {attempt})");
        sb.AppendLine();
        sb.AppendLine(review.Summary);
        sb.AppendLine();
        sb.AppendLine($"**Confidence:** {review.ConfidenceScore:P0}");

        if (review.Comments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Issues Found");
            foreach (var c in review.Comments)
                sb.AppendLine($"- **[{c.Severity}]** `{c.FilePath}`" +
                    (c.LineNumber.HasValue ? $" line {c.LineNumber}" : string.Empty) +
                    $": {c.Message}");
        }

        return sb.ToString();
    }

    /// <summary>Trims a string to at most <paramref name="maxLength"/> characters.</summary>
    private static string TrimToLength(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength] + "…";
}
