namespace GitHubDriver.Core.Models;

/// <summary>
/// Represents a single file create-or-update operation to be committed to a branch
/// as part of an automated fix pass.
/// </summary>
public sealed class FileChange
{
    /// <summary>
    /// Gets or sets the repository-relative path of the file (e.g. <c>src/Foo.cs</c>).
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// Gets or sets the full UTF-8 text content to write to the file.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// Gets or sets an optional per-file note describing why this change is being made.
    /// Used for logging; not written to the repository.
    /// </summary>
    public string? Reason { get; init; }

    /// <inheritdoc/>
    public override string ToString() =>
        Reason is null ? Path : $"{Path} ({Reason})";
}
