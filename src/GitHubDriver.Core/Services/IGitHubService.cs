using GitHubDriver.Core.Models;

namespace GitHubDriver.Core.Services;

/// <summary>
/// Provides high-level operations against the GitHub API for repository, branch, and
/// pull-request management.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Returns the default branch name of the specified repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The default branch name (e.g. <c>"main"</c>).</returns>
    Task<string> GetDefaultBranchAsync(string owner, string repo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new branch from the tip of an existing base branch.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="newBranchName">The name for the new branch.</param>
    /// <param name="baseBranchName">The source branch from which to create the new branch.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The SHA of the commit at the tip of the newly created branch.</returns>
    Task<string> CreateBranchAsync(
        string owner,
        string repo,
        string newBranchName,
        string baseBranchName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a branch from the repository.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="branchName">The branch to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DeleteBranchAsync(
        string owner,
        string repo,
        string branchName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a pull request from <paramref name="headBranch"/> targeting
    /// <paramref name="baseBranch"/>.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="title">The pull-request title.</param>
    /// <param name="body">The pull-request description body (supports Markdown).</param>
    /// <param name="headBranch">The head (source) branch.</param>
    /// <param name="baseBranch">The base (target) branch.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of the newly created pull request.</returns>
    Task<int> CreatePullRequestAsync(
        string owner,
        string repo,
        string title,
        string body,
        string headBranch,
        string baseBranch,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Squash-merges the specified pull request into its base branch.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="pullRequestNumber">The pull-request number to merge.</param>
    /// <param name="commitTitle">The title of the resulting squash-merge commit.</param>
    /// <param name="commitMessage">The body of the resulting squash-merge commit message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The SHA of the squash-merge commit.</returns>
    Task<string> SquashMergePullRequestAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string commitTitle,
        string commitMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the combined CI check status for the latest commit on the given branch.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="branchName">The branch whose checks to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The combined check status for the head commit on the branch.</returns>
    Task<CheckStatus> GetBranchCheckStatusAsync(
        string owner,
        string repo,
        string branchName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the diff (patch) for a pull request as a string.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="pullRequestNumber">The pull-request number.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The unified diff string for the pull request.</returns>
    Task<string> GetPullRequestDiffAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Posts a review comment to a pull request.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="pullRequestNumber">The pull-request number.</param>
    /// <param name="body">The comment body.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task AddPullRequestCommentAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the combined log output from all failed check runs on the latest commit
    /// of the specified branch.  Used by the fix loop to give Copilot context about why
    /// tests are failing.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="branchName">The branch whose check-run logs to retrieve.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// A string containing the concatenated log output from all failed check runs, or an
    /// empty string if no failed runs were found.
    /// </returns>
    Task<string> GetCheckRunLogsAsync(
        string owner,
        string repo,
        string branchName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Commits one or more file create-or-update operations to the specified branch in a
    /// single atomic commit.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <param name="branchName">The target branch to commit to.</param>
    /// <param name="changes">
    /// The list of files to create or overwrite.  Each entry supplies the repository-relative
    /// path and the full UTF-8 content of the file.
    /// </param>
    /// <param name="commitMessage">The commit message for this batch of changes.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ApplyFileChangesAsync(
        string owner,
        string repo,
        string branchName,
        IReadOnlyList<FileChange> changes,
        string commitMessage,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents the aggregated CI check result for a commit or branch.
/// </summary>
public enum CheckStatus
{
    /// <summary>All checks passed.</summary>
    Success,

    /// <summary>One or more checks are still running.</summary>
    Pending,

    /// <summary>One or more checks failed.</summary>
    Failure,

    /// <summary>No checks are configured or the status could not be determined.</summary>
    Unknown
}
