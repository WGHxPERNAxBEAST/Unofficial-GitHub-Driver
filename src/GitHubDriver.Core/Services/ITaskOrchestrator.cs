using GitHubDriver.Core.Models;

namespace GitHubDriver.Core.Services;

/// <summary>
/// Orchestrates the full automated workflow for a <see cref="TaskRequest"/>:
/// <list type="number">
///   <item><description>Create a dedicated working branch from the target branch.</description></item>
///   <item><description>Use Copilot to decompose the task into subtasks.</description></item>
///   <item><description>Implement each subtask on the working branch.</description></item>
///   <item><description>Wait for CI tests to pass.</description></item>
///   <item><description>Create a pull request and perform an automated code review.</description></item>
///   <item><description>Squash-merge the pull request as a single commit to the target branch.</description></item>
/// </list>
/// </summary>
public interface ITaskOrchestrator
{
    /// <summary>
    /// Executes the full automated workflow for the given <paramref name="request"/> and
    /// returns a <see cref="TaskResult"/> representing the outcome.
    /// </summary>
    /// <param name="request">The task to execute.</param>
    /// <param name="cancellationToken">A cancellation token to abort the workflow.</param>
    /// <returns>
    /// A <see cref="TaskResult"/> whose <see cref="TaskResult.IsSuccess"/> indicates
    /// whether the task completed and was squash-merged successfully.
    /// </returns>
    Task<TaskResult> ExecuteAsync(TaskRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current <see cref="WorkflowStatus"/> for a previously started task.
    /// </summary>
    /// <param name="taskId">
    /// The <see cref="TaskRequest.Id"/> of the task whose status to retrieve.
    /// </param>
    /// <returns>
    /// The current <see cref="WorkflowStatus"/>, or <see langword="null"/> if no task with
    /// the given ID is known.
    /// </returns>
    WorkflowStatus? GetStatus(string taskId);

    /// <summary>
    /// Returns the statuses of all tasks that have been started by this orchestrator instance
    /// since it was created.
    /// </summary>
    /// <returns>A read-only collection of workflow statuses.</returns>
    IReadOnlyCollection<WorkflowStatus> GetAllStatuses();
}
