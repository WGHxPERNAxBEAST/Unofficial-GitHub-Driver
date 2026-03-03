using System.CommandLine;
using GitHubDriver.CLI.Commands;
using GitHubDriver.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// ── Build configuration ──────────────────────────────────────────────────────
// Configuration is loaded from (in priority order):
//   1. appsettings.json  (shipped with the tool)
//   2. appsettings.{DOTNET_ENVIRONMENT}.json  (optional environment override)
//   3. Environment variables  (GITHUBDRIVER__GITHUBTOKEN, etc.)
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json",
        optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// ── Build the DI container ───────────────────────────────────────────────────
var services = new ServiceCollection();

services.AddSingleton<IConfiguration>(configuration);

services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.SetMinimumLevel(
        configuration["Logging:LogLevel:Default"] is string level
            ? Enum.Parse<LogLevel>(level)
            : LogLevel.Information);
});

services.AddGitHubDriver();

var provider = services.BuildServiceProvider();

// ── Wire up the CLI ──────────────────────────────────────────────────────────
var orchestrator = provider.GetRequiredService<GitHubDriver.Core.Services.ITaskOrchestrator>();
var logger       = provider.GetRequiredService<ILogger<Program>>();

var rootCommand = new RootCommand(
    "GitHubDriver – Un-Official GitHub Copilot Manager that automates coding tasks in your repositories.")
{
    AssignTaskCommand.Create(orchestrator, logger),
    StatusCommand.Create(orchestrator)
};

rootCommand.Name = "githubdriver";

return await rootCommand.InvokeAsync(args);

// Required for top-level statements partial class name
internal partial class Program { }
