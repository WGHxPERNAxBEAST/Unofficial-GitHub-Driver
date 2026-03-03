namespace GitHubDriver.Core.Models;

/// <summary>
/// Represents the final outcome of executing a <see cref="TaskRequest"/> through the
/// automated workflow.
/// </summary>
public sealed class TaskResult
{
    /// <summary>
    /// Gets or sets the ID of the original <see cref="TaskRequest"/>.
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// Gets or sets a value indicating whether the task was completed successfully.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Gets or sets the SHA of the squash-merge commit on the target branch, if the task
    /// completed successfully.
    /// </summary>
    public string? MergeCommitSha { get; init; }

    /// <summary>
    /// Gets or sets the GitHub pull-request number that was used to review and merge the changes.
    /// </summary>
    public int? PullRequestNumber { get; init; }

    /// <summary>
    /// Gets or sets the name of the working branch that was created for this task.
    /// </summary>
    public string? WorkingBranch { get; init; }

    /// <summary>
    /// Gets or sets a human-readable summary of the outcome.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Gets or sets the optional error that caused the workflow to fail.
    /// </summary>
    public Exception? Error { get; init; }

    /// <summary>
    /// Gets or sets the final <see cref="WorkflowStatus"/> at the time the result was produced.
    /// </summary>
    public WorkflowStatus? FinalStatus { get; init; }

    /// <summary>
    /// Creates a successful <see cref="TaskResult"/>.
    /// </summary>
    /// <param name="taskId">The ID of the completed task.</param>
    /// <param name="mergeCommitSha">The SHA of the squash-merge commit.</param>
    /// <param name="pullRequestNumber">The pull-request number used for the merge.</param>
    /// <param name="workingBranch">The working branch that was merged.</param>
    /// <param name="status">The final workflow status.</param>
    /// <returns>A successful <see cref="TaskResult"/> instance.</returns>
    public static TaskResult Success(
        string taskId,
        string mergeCommitSha,
        int pullRequestNumber,
        string workingBranch,
        WorkflowStatus? status = null) =>
        new()
        {
            TaskId = taskId,
            IsSuccess = true,
            MergeCommitSha = mergeCommitSha,
            PullRequestNumber = pullRequestNumber,
            WorkingBranch = workingBranch,
            Message = $"Task completed. Squash-merged as commit {mergeCommitSha}.",
            FinalStatus = status
        };

    /// <summary>
    /// Creates a failed <see cref="TaskResult"/>.
    /// </summary>
    /// <param name="taskId">The ID of the failed task.</param>
    /// <param name="message">A human-readable description of why the task failed.</param>
    /// <param name="error">The exception that caused the failure, if any.</param>
    /// <param name="status">The final workflow status at the point of failure.</param>
    /// <returns>A failed <see cref="TaskResult"/> instance.</returns>
    public static TaskResult Failure(
        string taskId,
        string message,
        Exception? error = null,
        WorkflowStatus? status = null) =>
        new()
        {
            TaskId = taskId,
            IsSuccess = false,
            Message = message,
            Error = error,
            FinalStatus = status
        };

    /// <inheritdoc/>
    public override string ToString() =>
        IsSuccess
            ? $"✅ Task {TaskId} succeeded – commit {MergeCommitSha}"
            : $"❌ Task {TaskId} failed – {Message}";
}
