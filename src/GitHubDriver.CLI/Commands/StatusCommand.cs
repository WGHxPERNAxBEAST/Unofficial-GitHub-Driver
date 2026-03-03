using System.CommandLine;
using GitHubDriver.Core.Models;
using GitHubDriver.Core.Services;

namespace GitHubDriver.CLI.Commands;

/// <summary>
/// CLI command that displays the current status of a running or completed task.
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// githubdriver status [--task-id &lt;id&gt;]
/// </code>
/// Omitting <c>--task-id</c> lists the status of all known tasks.
/// </remarks>
public static class StatusCommand
{
    /// <summary>
    /// Creates and returns the <c>status</c> <see cref="Command"/> ready to be added to
    /// the root command.
    /// </summary>
    /// <param name="orchestrator">The task orchestrator whose state to query.</param>
    /// <returns>The configured <see cref="Command"/>.</returns>
    public static Command Create(ITaskOrchestrator orchestrator)
    {
        ArgumentNullException.ThrowIfNull(orchestrator);

        var taskIdOption = new Option<string?>(
            name: "--task-id",
            description: "The ID of a specific task to inspect. Omit to list all tasks.");

        var command = new Command("status", "Show the status of one or all automated tasks.")
        {
            taskIdOption
        };

        command.SetHandler((taskId) =>
        {
            if (taskId is not null)
            {
                var status = orchestrator.GetStatus(taskId);
                if (status is null)
                {
                    Console.WriteLine($"❓ Task '{taskId}' not found.");
                    Environment.Exit(1);
                    return;
                }

                PrintStatus(status);
            }
            else
            {
                var all = orchestrator.GetAllStatuses();
                if (all.Count == 0)
                {
                    Console.WriteLine("No tasks have been started in this session.");
                    return;
                }

                foreach (var s in all)
                {
                    PrintStatus(s);
                    Console.WriteLine();
                }
            }
        },
        taskIdOption);

        return command;
    }

    /// <summary>
    /// Writes a formatted summary of a <see cref="WorkflowStatus"/> to standard output.
    /// </summary>
    private static void PrintStatus(WorkflowStatus status)
    {
        var phaseIcon = status.Phase switch
        {
            WorkflowPhase.Completed => "✅",
            WorkflowPhase.Failed    => "❌",
            _                       => "⏳"
        };

        Console.WriteLine($"{phaseIcon} Task: {status.TaskId}");
        Console.WriteLine($"   Phase  : {status.Phase}");
        Console.WriteLine($"   Updated: {status.UpdatedAt:O}");

        if (status.WorkingBranch is not null)
            Console.WriteLine($"   Branch : {status.WorkingBranch}");

        if (status.PullRequestNumber.HasValue)
            Console.WriteLine($"   PR     : #{status.PullRequestNumber}");

        if (status.Message is not null)
            Console.WriteLine($"   Message: {status.Message}");

        if (status.SubTasks.Count > 0)
        {
            Console.WriteLine("   Subtasks:");
            foreach (var s in status.SubTasks)
            {
                var icon = s.Status switch
                {
                    SubTaskStatus.Completed  => "✅",
                    SubTaskStatus.Failed     => "❌",
                    SubTaskStatus.InProgress => "⏳",
                    _                        => "⬜"
                };
                Console.WriteLine($"     {icon} [{s.Index}] {s.Title}");
            }
        }
    }
}
