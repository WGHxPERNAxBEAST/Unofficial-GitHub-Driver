namespace GitHubDriver.Core.Models;

/// <summary>
/// Represents an atomic unit of work derived from a <see cref="TaskRequest"/> by the Copilot
/// task-decomposition step.
/// </summary>
public sealed class SubTask
{
    /// <summary>
    /// Gets or sets the sequential index of this subtask within the parent task (zero-based).
    /// </summary>
    public int Index { get; init; }

    /// <summary>
    /// Gets or sets a short, human-readable title for this subtask.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Gets or sets the detailed description of what needs to be done for this subtask.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Gets or sets the files expected to be created or modified by this subtask (may be empty
    /// when not known upfront).
    /// </summary>
    public IReadOnlyList<string> AffectedFiles { get; init; } = [];

    /// <summary>
    /// Gets or sets the current execution status of this subtask.
    /// </summary>
    public SubTaskStatus Status { get; set; } = SubTaskStatus.Pending;

    /// <summary>
    /// Gets or sets the optional output or notes produced during execution of this subtask.
    /// </summary>
    public string? Output { get; set; }

    /// <inheritdoc/>
    public override string ToString() => $"[{Index}] {Title} ({Status})";
}

/// <summary>
/// Describes the lifecycle state of a <see cref="SubTask"/>.
/// </summary>
public enum SubTaskStatus
{
    /// <summary>The subtask has not been started yet.</summary>
    Pending,

    /// <summary>The subtask is currently being executed.</summary>
    InProgress,

    /// <summary>The subtask completed successfully.</summary>
    Completed,

    /// <summary>The subtask failed and may require intervention.</summary>
    Failed
}
