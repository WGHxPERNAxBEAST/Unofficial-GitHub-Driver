using GitHubDriver.Core.Configuration;
using GitHubDriver.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octokit;

namespace GitHubDriver.Core.Services;

/// <summary>
/// Implements <see cref="IGitHubService"/> using the <see href="https://github.com/octokit/octokit.net">Octokit.NET</see>
/// client library to call the GitHub REST API.
/// </summary>
public sealed class GitHubService : IGitHubService
{
    private readonly IGitHubClient _client;
    private readonly GitHubDriverOptions _options;
    private readonly ILogger<GitHubService> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="GitHubService"/>.
    /// </summary>
    /// <param name="options">The driver configuration options.</param>
    /// <param name="logger">The logger.</param>
    public GitHubService(IOptions<GitHubDriverOptions> options, ILogger<GitHubService> logger)
    {
        _options = options.Value;
        _logger = logger;

        var credentials = new Credentials(_options.GitHubToken);
        _client = new GitHubClient(new ProductHeaderValue("GitHubDriver", "1.0"))
        {
            Credentials = credentials
        };
    }

    /// <summary>
    /// Initializes a new instance of <see cref="GitHubService"/> with a pre-configured
    /// Octokit client (useful for testing).
    /// </summary>
    /// <param name="client">A pre-configured <see cref="IGitHubClient"/>.</param>
    /// <param name="options">The driver configuration options.</param>
    /// <param name="logger">The logger.</param>
    internal GitHubService(IGitHubClient client, IOptions<GitHubDriverOptions> options, ILogger<GitHubService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<string> GetDefaultBranchAsync(
        string owner, string repo, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting default branch for {Owner}/{Repo}", owner, repo);
        var repository = await _client.Repository.Get(owner, repo).ConfigureAwait(false);
        return repository.DefaultBranch;
    }

    /// <inheritdoc/>
    public async Task<string> CreateBranchAsync(
        string owner,
        string repo,
        string newBranchName,
        string baseBranchName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating branch '{NewBranch}' from '{BaseBranch}' in {Owner}/{Repo}",
            newBranchName, baseBranchName, owner, repo);

        // Resolve the SHA of the base branch tip
        var baseBranchRef = await _client.Git.Reference
            .Get(owner, repo, $"refs/heads/{baseBranchName}")
            .ConfigureAwait(false);

        var sha = baseBranchRef.Object.Sha;
        var newRef = new NewReference($"refs/heads/{newBranchName}", sha);
        await _client.Git.Reference.Create(owner, repo, newRef).ConfigureAwait(false);

        _logger.LogInformation(
            "Branch '{NewBranch}' created at {Sha}", newBranchName, sha);

        return sha;
    }

    /// <inheritdoc/>
    public async Task DeleteBranchAsync(
        string owner,
        string repo,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting branch '{Branch}' in {Owner}/{Repo}", branchName, owner, repo);
        await _client.Git.Reference
            .Delete(owner, repo, $"refs/heads/{branchName}")
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int> CreatePullRequestAsync(
        string owner,
        string repo,
        string title,
        string body,
        string headBranch,
        string baseBranch,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Creating pull request '{Title}' ({Head} → {Base}) in {Owner}/{Repo}",
            title, headBranch, baseBranch, owner, repo);

        var newPr = new NewPullRequest(title, headBranch, baseBranch)
        {
            Body = body
        };
        var pr = await _client.PullRequest.Create(owner, repo, newPr).ConfigureAwait(false);

        _logger.LogInformation("Pull request #{Number} created", pr.Number);
        return pr.Number;
    }

    /// <inheritdoc/>
    public async Task<string> SquashMergePullRequestAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string commitTitle,
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Squash-merging PR #{Number} in {Owner}/{Repo}", pullRequestNumber, owner, repo);

        var merge = new MergePullRequest
        {
            CommitTitle = commitTitle,
            CommitMessage = commitMessage,
            MergeMethod = PullRequestMergeMethod.Squash
        };

        var result = await _client.PullRequest
            .Merge(owner, repo, pullRequestNumber, merge)
            .ConfigureAwait(false);

        if (!result.Merged)
            throw new InvalidOperationException(
                $"Failed to merge PR #{pullRequestNumber}: {result.Message}");

        _logger.LogInformation(
            "PR #{Number} squash-merged as commit {Sha}", pullRequestNumber, result.Sha);

        return result.Sha;
    }

    /// <inheritdoc/>
    public async Task<CheckStatus> GetBranchCheckStatusAsync(
        string owner,
        string repo,
        string branchName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting CI check status for branch '{Branch}' in {Owner}/{Repo}", branchName, owner, repo);

        var branch = await _client.Repository.Branch.Get(owner, repo, branchName).ConfigureAwait(false);
        var sha = branch.Commit.Sha;

        var combinedStatus = await _client.Repository.Status
            .GetCombined(owner, repo, sha)
            .ConfigureAwait(false);

        return combinedStatus.State.Value switch
        {
            CommitState.Success => CheckStatus.Success,
            CommitState.Pending => CheckStatus.Pending,
            CommitState.Failure => CheckStatus.Failure,
            CommitState.Error => CheckStatus.Failure,
            _ => CheckStatus.Unknown
        };
    }

    /// <inheritdoc/>
    public async Task<string> GetPullRequestDiffAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Fetching diff for PR #{Number} in {Owner}/{Repo}", pullRequestNumber, owner, repo);

        var files = await _client.PullRequest.Files(owner, repo, pullRequestNumber).ConfigureAwait(false);

        var sb = new System.Text.StringBuilder();
        foreach (var file in files)
        {
            sb.AppendLine($"--- a/{file.FileName}");
            sb.AppendLine($"+++ b/{file.FileName}");
            if (file.Patch is not null)
                sb.AppendLine(file.Patch);
        }

        return sb.ToString();
    }

    /// <inheritdoc/>
    public async Task AddPullRequestCommentAsync(
        string owner,
        string repo,
        int pullRequestNumber,
        string body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Adding comment to PR #{Number} in {Owner}/{Repo}", pullRequestNumber, owner, repo);

        await _client.Issue.Comment
            .Create(owner, repo, pullRequestNumber, body)
            .ConfigureAwait(false);
    }
}
