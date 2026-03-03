namespace GitHubDriver.Core.Configuration;

/// <summary>
/// Configuration options for the GitHub Driver application.
/// Bind this from <c>appsettings.json</c> or environment variables under the
/// <c>GitHubDriver</c> section.
/// </summary>
public sealed class GitHubDriverOptions
{
    /// <summary>
    /// The configuration section name used when binding from <c>IConfiguration</c>.
    /// </summary>
    public const string SectionName = "GitHubDriver";

    /// <summary>
    /// Gets or sets the GitHub Personal Access Token (PAT) or OAuth token used to
    /// authenticate with the GitHub API.
    /// The token requires the following scopes: <c>repo</c>, <c>pull_requests</c>,
    /// <c>copilot</c>.
    /// </summary>
    public required string GitHubToken { get; set; }

    /// <summary>
    /// Gets or sets the name of the GitHub Copilot model to use for task decomposition
    /// and code review. Defaults to <c>"gpt-4o"</c>.
    /// </summary>
    public string CopilotModel { get; set; } = "gpt-4o";

    /// <summary>
    /// Gets or sets the base URL of the GitHub API. Defaults to
    /// <c>"https://api.github.com"</c>. Override for GitHub Enterprise Server.
    /// </summary>
    public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>
    /// Gets or sets the base URL of the GitHub Copilot API endpoint.
    /// Defaults to <c>"https://api.githubcopilot.com"</c>.
    /// </summary>
    public string CopilotApiBaseUrl { get; set; } = "https://api.githubcopilot.com";

    /// <summary>
    /// Gets or sets the prefix used when naming working branches created for automated tasks.
    /// Defaults to <c>"copilot/task-"</c>.
    /// </summary>
    public string BranchPrefix { get; set; } = "copilot/task-";

    /// <summary>
    /// Gets or sets the minimum code-review confidence score (0.0–1.0) required before a
    /// pull request is automatically merged. Defaults to <c>0.85</c>.
    /// </summary>
    public double MinReviewConfidence { get; set; } = 0.85;

    /// <summary>
    /// Gets or sets the maximum number of times the system will attempt to fix review
    /// failures before declaring the task failed. Defaults to <c>3</c>.
    /// </summary>
    public int MaxReviewRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the maximum number of complete fix iterations the orchestrator will
    /// attempt before giving up. Each iteration covers one full pass of CI checks followed
    /// by a code review; if either step finds issues Copilot generates fixes and the loop
    /// repeats. Defaults to <c>5</c>.
    /// </summary>
    public int MaxFixIterations { get; set; } = 5;

    /// <summary>
    /// Gets or sets the maximum number of seconds to wait for a CI run to complete after
    /// pushing to the working branch. Defaults to <c>600</c> (10 minutes).
    /// </summary>
    public int CiTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Gets or sets the polling interval in seconds used while waiting for CI to complete.
    /// Defaults to <c>15</c> seconds.
    /// </summary>
    public int CiPollingIntervalSeconds { get; set; } = 15;

    /// <summary>
    /// Gets or sets a value indicating whether CI checks must pass before a pull request
    /// can be merged. Defaults to <c>true</c>.
    /// </summary>
    public bool RequireCiPass { get; set; } = true;
}
