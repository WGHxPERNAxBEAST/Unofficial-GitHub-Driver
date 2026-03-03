namespace GitHubDriver.Core.Models;

/// <summary>
/// A Copilot-generated plan for fixing issues discovered during CI test runs or code reviews.
/// Contains a summary of what is being fixed and a list of file changes to commit.
/// </summary>
public sealed class FixPlan
{
    /// <summary>
    /// Gets or sets a human-readable summary of the fixes being applied.
    /// Used as the Git commit message body for the fix commit.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Gets or sets the ordered list of file changes that implement the fix.
    /// </summary>
    public IReadOnlyList<FileChange> FileChanges { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether this plan contains at least one file change.
    /// </summary>
    public bool HasChanges => FileChanges.Count > 0;

    /// <inheritdoc/>
    public override string ToString() =>
        $"FixPlan ({FileChanges.Count} file(s)): {Summary}";
}
