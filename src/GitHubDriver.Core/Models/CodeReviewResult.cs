namespace GitHubDriver.Core.Models;

/// <summary>
/// Represents the outcome of a Copilot-powered code review on a pull request.
/// </summary>
public sealed class CodeReviewResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the code review approved the changes.
    /// </summary>
    public bool IsApproved { get; init; }

    /// <summary>
    /// Gets or sets the overall summary produced by the code review.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// Gets or sets the list of individual review comments, if any issues were found.
    /// </summary>
    public IReadOnlyList<ReviewComment> Comments { get; init; } = [];

    /// <summary>
    /// Gets or sets the confidence score (0.0–1.0) of the review, where 1.0 represents
    /// complete confidence that the changes are correct and production-ready.
    /// </summary>
    public double ConfidenceScore { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        IsApproved
            ? $"✅ Review approved (confidence {ConfidenceScore:P0}): {Summary}"
            : $"❌ Review rejected: {Summary}";
}

/// <summary>
/// Represents a single comment produced during a code review.
/// </summary>
public sealed class ReviewComment
{
    /// <summary>
    /// Gets or sets the file path this comment refers to.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Gets or sets the line number within the file (1-based), if applicable.
    /// </summary>
    public int? LineNumber { get; init; }

    /// <summary>
    /// Gets or sets the severity of this comment.
    /// </summary>
    public ReviewCommentSeverity Severity { get; init; }

    /// <summary>
    /// Gets or sets the review comment text.
    /// </summary>
    public required string Message { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{Severity}] {FilePath}" + (LineNumber.HasValue ? $":{LineNumber}" : string.Empty) + $": {Message}";
}

/// <summary>
/// Describes the severity level of a <see cref="ReviewComment"/>.
/// </summary>
public enum ReviewCommentSeverity
{
    /// <summary>Informational note; no action required.</summary>
    Info,

    /// <summary>A suggestion that should be considered but is not blocking.</summary>
    Warning,

    /// <summary>A critical issue that must be resolved before merging.</summary>
    Error
}
