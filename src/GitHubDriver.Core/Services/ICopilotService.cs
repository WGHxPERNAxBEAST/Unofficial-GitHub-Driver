using GitHubDriver.Core.Models;

namespace GitHubDriver.Core.Services;

/// <summary>
/// Provides Copilot-powered operations used during task decomposition and code review.
/// Implementations call the GitHub Copilot Chat / Completions API.
/// </summary>
public interface ICopilotService
{
    /// <summary>
    /// Decomposes a high-level task description into a list of ordered, atomic
    /// <see cref="SubTask"/> instances that Copilot can execute sequentially.
    /// </summary>
    /// <param name="taskDescription">The human-written task description.</param>
    /// <param name="repositoryContext">
    /// Optional context about the target repository (e.g. language, framework, conventions)
    /// that helps Copilot produce better subtasks.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An ordered list of subtasks derived from the task description.</returns>
    Task<IReadOnlyList<SubTask>> DecomposeTaskAsync(
        string taskDescription,
        string? repositoryContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs an automated code review of a pull request diff and returns a structured
    /// <see cref="CodeReviewResult"/>.
    /// </summary>
    /// <param name="diff">The unified diff of the pull request (as returned by the GitHub API).</param>
    /// <param name="taskDescription">The original task description used as acceptance criteria.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="CodeReviewResult"/> indicating approval or failure with comments.</returns>
    Task<CodeReviewResult> ReviewCodeAsync(
        string diff,
        string taskDescription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates implementation suggestions for a specific <see cref="SubTask"/> given the
    /// current state of the repository.
    /// </summary>
    /// <param name="subTask">The subtask to implement.</param>
    /// <param name="repositoryContext">
    /// Context about the target repository to guide the implementation.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A free-form implementation suggestion that can be applied to the repository.</returns>
    Task<string> GenerateImplementationAsync(
        SubTask subTask,
        string? repositoryContext = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a concise commit message suitable for a squash-merge that summarises
    /// all work done for the original task.
    /// </summary>
    /// <param name="taskDescription">The original task description.</param>
    /// <param name="subTasks">The list of subtasks that were completed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A commit message string (title + optional body).</returns>
    Task<string> GenerateCommitMessageAsync(
        string taskDescription,
        IReadOnlyList<SubTask> subTasks,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes CI test-failure logs and produces a <see cref="FixPlan"/> describing the
    /// file changes required to make the tests pass.
    /// </summary>
    /// <param name="ciLogs">The raw CI / test-runner output containing failure details.</param>
    /// <param name="taskDescription">The original task description used as context.</param>
    /// <param name="diff">
    /// The current pull-request diff so Copilot can see the code that is failing.
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A <see cref="FixPlan"/> containing a summary and the file changes to commit.
    /// </returns>
    Task<FixPlan> AnalyzeTestFailuresAsync(
        string ciLogs,
        string taskDescription,
        string diff,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a <see cref="FixPlan"/> that addresses all blocking issues raised by a
    /// previous code review so that the pull request can be approved on the next pass.
    /// </summary>
    /// <param name="review">The <see cref="CodeReviewResult"/> containing the issues to fix.</param>
    /// <param name="diff">The current pull-request diff that was reviewed.</param>
    /// <param name="taskDescription">The original task description used as acceptance criteria.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A <see cref="FixPlan"/> containing a summary and the file changes to commit.
    /// </returns>
    Task<FixPlan> GenerateFixForReviewFeedbackAsync(
        CodeReviewResult review,
        string diff,
        string taskDescription,
        CancellationToken cancellationToken = default);
}
