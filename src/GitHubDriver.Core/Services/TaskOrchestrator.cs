using System.Collections.Concurrent;
using GitHubDriver.Core.Configuration;
using GitHubDriver.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubDriver.Core.Services;

/// <summary>
/// Implements <see cref="ITaskOrchestrator"/> to drive the full automated task workflow:
/// <list type="number">
///   <item><description>Create a dedicated working branch from the target branch.</description></item>
///   <item><description>Use Copilot to decompose the task into subtasks.</description></item>
///   <item><description>Implement each subtask on the working branch.</description></item>
///   <item><description>Create a pull request.</description></item>
///   <item><description>
///     Enter the <b>iterative fix loop</b> (up to <see cref="GitHubDriverOptions.MaxFixIterations"/>
///     iterations):
///     <list type="bullet">
///       <item><description>
///         Wait for CI tests to pass.  If they fail, Copilot analyzes the failure logs, generates
///         a <see cref="FixPlan"/>, commits the fixes to the working branch, and the loop repeats
///         from the CI-wait step.
///       </description></item>
///       <item><description>
///         When CI passes, perform an automated code review.  If the review rejects the PR,
///         Copilot generates a <see cref="FixPlan"/> addressing every review comment, commits the
///         fixes, and the loop repeats from the CI-wait step (ensuring the fixes themselves are
///         also tested before the next review).
///       </description></item>
///     </list>
///   </description></item>
///   <item><description>
///     Once CI passes <em>and</em> the code review approves with sufficient confidence, the pull
///     request is squash-merged as a single, clean commit on the target branch.
///   </description></item>
/// </list>
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

            var workingBranch = $"{_options.BranchPrefix}{request.Id[..Math.Min(request.Id.Length, 8)]}";
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

            _logger.LogInformation("Task decomposed into {Count} subtask(s).", subTasks.Count);

            // ── Phase 3: Implement the subtasks ──────────────────────────────
            Advance(status, WorkflowPhase.ImplementingChanges, $"Implementing {subTasks.Count} subtask(s).");

            foreach (var subTask in status.SubTasks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ExecuteSubTaskAsync(request, subTask, cancellationToken).ConfigureAwait(false);
            }

            // ── Phase 4: Create pull request ──────────────────────────────────
            Advance(status, WorkflowPhase.CreatingPullRequest, "Creating pull request.");

            var prTitle = $"[Automated] {TrimToLength(request.Description, 80)}";
            var prBody  = BuildPullRequestBody(request, status.SubTasks);

            var prNumber = await _gitHub.CreatePullRequestAsync(
                request.Owner, request.Repository,
                prTitle, prBody,
                workingBranch, request.TargetBranch,
                cancellationToken)
                .ConfigureAwait(false);

            status.PullRequestNumber = prNumber;

            // ── Phase 5: Iterative CI → fix → review → fix loop ──────────────
            //
            // Each iteration:
            //   a. Wait for CI.  On failure → Copilot fixes → commit → restart iteration.
            //   b. CI passed → run code review.
            //      On rejection → Copilot fixes → commit → restart iteration (re-runs CI).
            //   c. Review approved → exit loop and proceed to merge.
            //
            await RunIterativeFixLoopAsync(request, status, prNumber, workingBranch, cancellationToken)
                .ConfigureAwait(false);

            // ── Phase 6: Squash-merge ──────────────────────────────────────────
            Advance(status, WorkflowPhase.MergingChanges, $"Squash-merging PR #{prNumber}.");

            var commitMessage = await _copilot.GenerateCommitMessageAsync(
                request.Description, [..status.SubTasks], cancellationToken)
                .ConfigureAwait(false);

            var mergeCommitSha = await _gitHub.SquashMergePullRequestAsync(
                request.Owner, request.Repository,
                prNumber, prTitle, commitMessage,
                cancellationToken)
                .ConfigureAwait(false);

            // ── Done ───────────────────────────────────────────────────────────
            Advance(status, WorkflowPhase.Completed,
                $"Task completed successfully. Commit: {mergeCommitSha}");

            _logger.LogInformation(
                "Task {Id} completed successfully. Squash-merge commit: {Sha}",
                request.Id, mergeCommitSha);

            return TaskResult.Success(request.Id, mergeCommitSha, prNumber, workingBranch, status);
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

    // ── Iterative fix loop ───────────────────────────────────────────────────

    /// <summary>
    /// Runs the CI → fix → review → fix loop until the pull request is approved and all
    /// tests pass, or until <see cref="GitHubDriverOptions.MaxFixIterations"/> is exceeded.
    /// </summary>
    /// <remarks>
    /// Each iteration of the loop is a complete pass:
    /// <list type="number">
    ///   <item>Wait for CI checks to complete.</item>
    ///   <item>
    ///     If CI failed: call <see cref="ICopilotService.AnalyzeTestFailuresAsync"/>, commit
    ///     the fix plan, and <c>continue</c> — which restarts the loop from the CI-wait step,
    ///     ensuring the fixes are themselves tested before review.
    ///   </item>
    ///   <item>If CI passed: perform a code review.</item>
    ///   <item>
    ///     If review rejected: call <see cref="ICopilotService.GenerateFixForReviewFeedbackAsync"/>,
    ///     commit the fix plan, and <c>continue</c> — which restarts the loop so that the
    ///     review fixes are tested before the next review attempt.
    ///   </item>
    ///   <item>If review approved with sufficient confidence: return (success).</item>
    /// </list>
    /// </remarks>
    private async Task RunIterativeFixLoopAsync(
        TaskRequest request,
        WorkflowStatus status,
        int prNumber,
        string workingBranch,
        CancellationToken cancellationToken)
    {
        var maxIterations = _options.MaxFixIterations;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            status.FixIteration = iteration;
            _logger.LogInformation(
                "Fix loop iteration {Iteration}/{Max} for task {Id}.",
                iteration, maxIterations, request.Id);

            // ── a. CI wait ────────────────────────────────────────────────
            if (_options.RequireCiPass)
            {
                Advance(status, WorkflowPhase.RunningTests,
                    $"[Iteration {iteration}] Waiting for CI checks.");

                var ciPassed = await WaitForCiAsync(
                    request.Owner, request.Repository, workingBranch, cancellationToken)
                    .ConfigureAwait(false);

                if (!ciPassed)
                {
                    // ── CI failed → let Copilot fix it ───────────────────
                    Advance(status, WorkflowPhase.FixingTestFailures,
                        $"[Iteration {iteration}] CI failed — analyzing failures and applying fixes.");

                    var ciLogs = await _gitHub.GetCheckRunLogsAsync(
                        request.Owner, request.Repository, workingBranch, cancellationToken)
                        .ConfigureAwait(false);

                    var diff = await _gitHub.GetPullRequestDiffAsync(
                        request.Owner, request.Repository, prNumber, cancellationToken)
                        .ConfigureAwait(false);

                    var fixPlan = await _copilot.AnalyzeTestFailuresAsync(
                        ciLogs, request.Description, diff, cancellationToken)
                        .ConfigureAwait(false);

                    _logger.LogInformation(
                        "CI fix plan (iteration {Iteration}): {Summary}", iteration, fixPlan.Summary);

                    if (fixPlan.HasChanges)
                    {
                        var commitMsg = $"fix: address test failures (iteration {iteration})\n\n{fixPlan.Summary}";
                        await _gitHub.ApplyFileChangesAsync(
                            request.Owner, request.Repository, workingBranch,
                            fixPlan.FileChanges, commitMsg, cancellationToken)
                            .ConfigureAwait(false);

                        await _gitHub.AddPullRequestCommentAsync(
                            request.Owner, request.Repository, prNumber,
                            BuildFixCommitComment("test failures", iteration, fixPlan),
                            cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Copilot produced no file changes for CI failures on iteration {Iteration}.",
                            iteration);
                    }

                    // Restart loop — re-run CI against the fresh fixes.
                    continue;
                }
            }

            // ── b. Code review ────────────────────────────────────────────
            Advance(status, WorkflowPhase.ReviewingCode,
                $"[Iteration {iteration}] Reviewing PR #{prNumber}.");

            var reviewDiff = await _gitHub.GetPullRequestDiffAsync(
                request.Owner, request.Repository, prNumber, cancellationToken)
                .ConfigureAwait(false);

            var review = await _copilot.ReviewCodeAsync(
                reviewDiff, request.Description, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Review result (iteration {Iteration}): {Result}", iteration, review);

            // Post the review as a PR comment so humans can see it.
            await _gitHub.AddPullRequestCommentAsync(
                request.Owner, request.Repository, prNumber,
                BuildReviewComment(review, iteration),
                cancellationToken)
                .ConfigureAwait(false);

            if (review.IsApproved && review.ConfidenceScore >= _options.MinReviewConfidence)
            {
                _logger.LogInformation(
                    "PR #{Number} approved on iteration {Iteration} (confidence {Score:P0}).",
                    prNumber, iteration, review.ConfidenceScore);
                return; // ✅ Done — proceed to squash-merge.
            }

            // ── c. Review rejected → let Copilot address feedback ─────────
            Advance(status, WorkflowPhase.AddressingReviewFeedback,
                $"[Iteration {iteration}] Review rejected — addressing feedback and applying fixes.");

            var reviewFix = await _copilot.GenerateFixForReviewFeedbackAsync(
                review, reviewDiff, request.Description, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Review fix plan (iteration {Iteration}): {Summary}", iteration, reviewFix.Summary);

            if (reviewFix.HasChanges)
            {
                var fixCommitMsg = $"fix: address review feedback (iteration {iteration})\n\n{reviewFix.Summary}";
                await _gitHub.ApplyFileChangesAsync(
                    request.Owner, request.Repository, workingBranch,
                    reviewFix.FileChanges, fixCommitMsg, cancellationToken)
                    .ConfigureAwait(false);

                await _gitHub.AddPullRequestCommentAsync(
                    request.Owner, request.Repository, prNumber,
                    BuildFixCommitComment("review feedback", iteration, reviewFix),
                    cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning(
                    "Copilot produced no file changes for review feedback on iteration {Iteration}.",
                    iteration);
            }

            // Restart loop — fixes will be re-tested (CI) then re-reviewed.
        }

        // All iterations exhausted without approval.
        throw new InvalidOperationException(
            $"Could not achieve an approved, fully-tested implementation after " +
            $"{maxIterations} fix iteration(s). Review the PR for details.");
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Executes a single subtask: calls Copilot for implementation guidance and marks the
    /// subtask as completed (or failed).
    /// </summary>
    private async Task ExecuteSubTaskAsync(
        TaskRequest request,
        SubTask subTask,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executing subtask {Index}: {Title}", subTask.Index, subTask.Title);

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
    /// <returns>
    /// <see langword="true"/> if CI passed (or no checks are configured);
    /// <see langword="false"/> if CI explicitly failed.
    /// </returns>
    private async Task<bool> WaitForCiAsync(
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
                    return true;

                case CheckStatus.Failure:
                    _logger.LogWarning("CI checks failed on '{Branch}'.", branchName);
                    return false;

                case CheckStatus.Unknown:
                    _logger.LogWarning(
                        "No CI checks found on '{Branch}'. Treating as passed.", branchName);
                    return true;

                default:
                    _logger.LogDebug(
                        "CI still pending on '{Branch}'. Waiting {Interval}s…",
                        branchName, interval.TotalSeconds);
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }

        throw new TimeoutException(
            $"CI checks on '{branchName}' did not complete within {_options.CiTimeoutSeconds} seconds.");
    }

    /// <summary>Advances the workflow to the next phase and updates the status timestamp.</summary>
    private static void Advance(WorkflowStatus status, WorkflowPhase phase, string message)
    {
        status.Phase     = phase;
        status.Message   = message;
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
    private static string BuildReviewComment(CodeReviewResult review, int iteration)
    {
        var icon = review.IsApproved ? "✅" : "❌";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## {icon} Automated Code Review (iteration {iteration})");
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

    /// <summary>
    /// Formats a fix-commit notification as a Markdown comment for the PR.
    /// </summary>
    private static string BuildFixCommitComment(string fixType, int iteration, FixPlan plan)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"## 🔧 Automated Fix — {fixType} (iteration {iteration})");
        sb.AppendLine();
        sb.AppendLine(plan.Summary);

        if (plan.FileChanges.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### Files Changed");
            foreach (var f in plan.FileChanges)
                sb.AppendLine($"- `{f.Path}`" + (f.Reason is not null ? $": {f.Reason}" : string.Empty));
        }

        return sb.ToString();
    }

    /// <summary>Trims a string to at most <paramref name="maxLength"/> characters.</summary>
    private static string TrimToLength(string value, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxLength);
        return value.Length <= maxLength ? value : value[..maxLength] + "…";
    }
}
