using System.CommandLine;
using GitHubDriver.Core.Models;
using GitHubDriver.Core.Services;
using Microsoft.Extensions.Logging;

namespace GitHubDriver.CLI.Commands;

/// <summary>
/// CLI command that assigns a high-level coding task to the GitHubDriver orchestrator,
/// waits for it to complete, and prints the outcome.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// githubdriver assign-task \
///   --owner &lt;owner&gt; \
///   --repo &lt;repo&gt; \
///   --branch &lt;target-branch&gt; \
///   --description "Add user authentication" \
///   [--context "ASP.NET Core 8, uses JWT"]
/// </code>
/// </remarks>
public static class AssignTaskCommand
{
    /// <summary>
    /// Creates and returns the <c>assign-task</c> <see cref="Command"/> ready to be added to
    /// the root command.
    /// </summary>
    /// <param name="orchestrator">The task orchestrator to use.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The configured <see cref="Command"/>.</returns>
    public static Command Create(ITaskOrchestrator orchestrator, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(logger);

        var ownerOption = new Option<string>(
            name: "--owner",
            description: "The GitHub repository owner (user or organization).")
        { IsRequired = true };

        var repoOption = new Option<string>(
            name: "--repo",
            description: "The GitHub repository name.")
        { IsRequired = true };

        var branchOption = new Option<string>(
            name: "--branch",
            description: "The target branch to which the completed task will be squash-merged.",
            getDefaultValue: () => "main");

        var descriptionOption = new Option<string>(
            name: "--description",
            description: "A high-level description of the coding task to complete.")
        { IsRequired = true };

        var contextOption = new Option<string?>(
            name: "--context",
            description: "Optional additional context (e.g. framework, conventions, acceptance criteria).");

        var command = new Command("assign-task", "Assign a coding task to be completed automatically.")
        {
            ownerOption,
            repoOption,
            branchOption,
            descriptionOption,
            contextOption
        };

        command.SetHandler(async (owner, repo, branch, description, context) =>
        {
            var request = new TaskRequest
            {
                Owner = owner,
                Repository = repo,
                TargetBranch = branch,
                Description = description,
                AdditionalContext = context
            };

            Console.WriteLine($"🚀 Assigning task [{request.Id}]…");
            Console.WriteLine($"   Repository : {owner}/{repo}");
            Console.WriteLine($"   Branch     : {branch}");
            Console.WriteLine($"   Description: {description}");
            Console.WriteLine();

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("⚠️  Cancellation requested…");
                cts.Cancel();
            };

            var result = await orchestrator.ExecuteAsync(request, cts.Token);

            Console.WriteLine();
            Console.WriteLine(result.ToString());

            if (!result.IsSuccess)
            {
                logger.LogError(result.Error, "Task failed: {Message}", result.Message);
                Environment.Exit(1);
            }
        },
        ownerOption, repoOption, branchOption, descriptionOption, contextOption);

        return command;
    }
}
