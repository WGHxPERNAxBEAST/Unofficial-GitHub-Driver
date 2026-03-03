using GitHubDriver.Core.Configuration;
using GitHubDriver.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GitHubDriver.Core;

/// <summary>
/// Extension methods for registering GitHubDriver services with the
/// <see cref="IServiceCollection"/> dependency-injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all core GitHubDriver services to the service collection, binding options from the
    /// <see cref="GitHubDriverOptions.SectionName"/> configuration section.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configureOptions">
    /// An optional delegate used to configure <see cref="GitHubDriverOptions"/> after they have
    /// been bound from configuration.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddGitHubDriver(
        this IServiceCollection services,
        Action<GitHubDriverOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ── Options ──────────────────────────────────────────────────────────
        var optionsBuilder = services
            .AddOptions<GitHubDriverOptions>()
            .BindConfiguration(GitHubDriverOptions.SectionName);

        if (configureOptions is not null)
            optionsBuilder.PostConfigure(configureOptions);

        // ── HTTP client for Copilot API ──────────────────────────────────────
        services.AddHttpClient<ICopilotService, CopilotService>();

        // ── GitHub API service ───────────────────────────────────────────────
        services.TryAddTransient<IGitHubService, GitHubService>();

        // ── Orchestrator ─────────────────────────────────────────────────────
        services.TryAddSingleton<ITaskOrchestrator, TaskOrchestrator>();

        return services;
    }
}
