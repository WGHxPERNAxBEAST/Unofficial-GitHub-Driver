namespace GitHubDriver.Core.Models;

/// <summary>
/// Represents a high-level coding task assigned by the user to be completed in a specific repository.
/// The orchestrator breaks this into subtasks, completes the work in a dedicated branch,
/// reviews and tests it, then squash-merges it as a single commit to the target branch.
/// </summary>
public sealed class TaskRequest
{
    /// <summary>
    /// Gets or sets the GitHub repository owner (user or organization).
    /// </summary>
    public required string Owner { get; init; }

    /// <summary>
    /// Gets or sets the GitHub repository name.
    /// </summary>
    public required string Repository { get; init; }

    /// <summary>
    /// Gets or sets the target branch to which the completed work will be squash-merged.
    /// Defaults to the repository's default branch (typically "main" or "master").
    /// </summary>
    public required string TargetBranch { get; init; }

    /// <summary>
    /// Gets or sets the high-level description of the task to complete.
    /// This will be passed to Copilot for decomposition into subtasks.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets or sets an optional unique identifier for this task request.
    /// Defaults to a new GUID string.
    /// </summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Gets or sets the UTC timestamp when this task request was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets additional context or constraints for the task (e.g. coding style requirements,
    /// acceptance criteria, or links to related issues).
    /// </summary>
    public string? AdditionalContext { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{Id}] {Owner}/{Repository}@{TargetBranch}: {Description}";
}
