namespace GitHubDriver.Core.Models;

/// <summary>
/// Represents the current phase and progress of the automated workflow for a <see cref="TaskRequest"/>.
/// </summary>
public sealed class WorkflowStatus
{
    /// <summary>
    /// Gets or sets the ID of the associated <see cref="TaskRequest"/>.
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// Gets or sets the current phase of the workflow.
    /// </summary>
    public WorkflowPhase Phase { get; set; } = WorkflowPhase.NotStarted;

    /// <summary>
    /// Gets or sets the name of the working branch created for this task (e.g.
    /// <c>copilot/task-&lt;id&gt;</c>).
    /// </summary>
    public string? WorkingBranch { get; set; }

    /// <summary>
    /// Gets or sets the GitHub pull-request number created for this task, if any.
    /// </summary>
    public int? PullRequestNumber { get; set; }

    /// <summary>
    /// Gets or sets the list of subtasks derived from the original task description.
    /// </summary>
    public IList<SubTask> SubTasks { get; set; } = [];

    /// <summary>
    /// Gets or sets the current fix-iteration number (1-based). Zero means no fix iteration
    /// has started yet. Each iteration represents one complete pass of: CI → fix (if needed) →
    /// code review → fix (if needed).
    /// </summary>
    public int FixIteration { get; set; }

    /// <summary>
    /// Gets or sets a human-readable summary of the current state, including any error messages.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp of the last status update.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets a value indicating whether the workflow has reached a terminal state
    /// (either <see cref="WorkflowPhase.Completed"/> or <see cref="WorkflowPhase.Failed"/>).
    /// </summary>
    public bool IsTerminal =>
        Phase is WorkflowPhase.Completed or WorkflowPhase.Failed;

    /// <inheritdoc/>
    public override string ToString() =>
        $"Task {TaskId}: {Phase}" + (Message is not null ? $" – {Message}" : string.Empty);
}

/// <summary>
/// Enumerates the sequential phases of the automated task workflow.
/// </summary>
public enum WorkflowPhase
{
    /// <summary>The workflow has not yet started.</summary>
    NotStarted,

    /// <summary>The working branch is being created from the target branch.</summary>
    CreatingBranch,

    /// <summary>Copilot is decomposing the high-level task into subtasks.</summary>
    DecomposingTask,

    /// <summary>Subtasks are being implemented by Copilot on the working branch.</summary>
    ImplementingChanges,

    /// <summary>The build and automated tests are being executed against the working branch.</summary>
    RunningTests,

    /// <summary>A pull request is being created from the working branch to the target branch.</summary>
    CreatingPullRequest,

    /// <summary>Copilot is performing a code review of the pull request.</summary>
    ReviewingCode,

    /// <summary>
    /// CI tests failed; Copilot is analyzing the failure logs and generating fixes
    /// to commit to the working branch before re-running tests.
    /// </summary>
    FixingTestFailures,

    /// <summary>
    /// The code review rejected the pull request; Copilot is addressing the review comments
    /// and committing fixes to the working branch before re-running tests and re-reviewing.
    /// </summary>
    AddressingReviewFeedback,

    /// <summary>The pull request is being squash-merged into the target branch.</summary>
    MergingChanges,

    /// <summary>The workflow completed successfully; the target branch has exactly one new commit.</summary>
    Completed,

    /// <summary>The workflow encountered an unrecoverable error.</summary>
    Failed
}
