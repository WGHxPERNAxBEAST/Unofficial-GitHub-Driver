using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitHubDriver.Core.Configuration;
using GitHubDriver.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubDriver.Core.Services;

/// <summary>
/// Implements <see cref="ICopilotService"/> by calling the GitHub Copilot Chat API
/// (OpenAI-compatible completions endpoint) to decompose tasks and review code.
/// </summary>
public sealed class CopilotService : ICopilotService
{
    private readonly HttpClient _httpClient;
    private readonly GitHubDriverOptions _options;
    private readonly ILogger<CopilotService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Initializes a new instance of <see cref="CopilotService"/>.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> used for API calls.</param>
    /// <param name="options">The driver configuration options.</param>
    /// <param name="logger">The logger.</param>
    public CopilotService(
        HttpClient httpClient,
        IOptions<GitHubDriverOptions> options,
        ILogger<CopilotService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_options.CopilotApiBaseUrl.TrimEnd('/') + "/");
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", _options.GitHubToken);
        _httpClient.DefaultRequestHeaders.Add("Copilot-Integration-Id", "github-driver");
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SubTask>> DecomposeTaskAsync(
        string taskDescription,
        string? repositoryContext = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Decomposing task: {Task}", taskDescription);

        var systemPrompt =
            "You are an expert software engineering assistant. " +
            "Your job is to break down a high-level coding task into ordered, atomic subtasks. " +
            "Each subtask must be small enough to implement in a single logical change. " +
            "Return ONLY a JSON array of objects, each with 'title', 'description', and " +
            "'affectedFiles' (array of strings) fields. Do not include any prose outside the JSON.";

        var userPrompt = repositoryContext is null
            ? $"Task: {taskDescription}"
            : $"Repository context:\n{repositoryContext}\n\nTask: {taskDescription}";

        var content = await CallChatCompletionsAsync(systemPrompt, userPrompt, cancellationToken)
            .ConfigureAwait(false);

        return ParseSubTasks(content);
    }

    /// <inheritdoc/>
    public async Task<CodeReviewResult> ReviewCodeAsync(
        string diff,
        string taskDescription,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Reviewing code for task: {Task}", taskDescription);

        var systemPrompt =
            "You are a senior software engineer performing a code review. " +
            "Review the provided diff against the task requirements and produce a JSON object with: " +
            "'isApproved' (boolean), 'summary' (string), 'confidenceScore' (float 0-1), and " +
            "'comments' (array of objects each with 'filePath', 'lineNumber' (nullable int), " +
            "'severity' ('Info'|'Warning'|'Error'), and 'message'). " +
            "Approve only when all acceptance criteria are met, the code is correct, safe, and well-tested. " +
            "Return ONLY the JSON object, no surrounding prose.";

        var userPrompt =
            $"Task requirements:\n{taskDescription}\n\n" +
            $"Pull request diff:\n```diff\n{diff}\n```";

        var content = await CallChatCompletionsAsync(systemPrompt, userPrompt, cancellationToken)
            .ConfigureAwait(false);

        return ParseCodeReview(content);
    }

    /// <inheritdoc/>
    public async Task<string> GenerateImplementationAsync(
        SubTask subTask,
        string? repositoryContext = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating implementation for subtask: {Title}", subTask.Title);

        var systemPrompt =
            "You are an expert software engineer. " +
            "Provide a detailed implementation plan and code changes for the given subtask. " +
            "Format your response as clear, actionable instructions with code snippets where relevant.";

        var context = repositoryContext is null ? string.Empty : $"Repository context:\n{repositoryContext}\n\n";
        var userPrompt =
            $"{context}Subtask: {subTask.Title}\n\n{subTask.Description}" +
            (subTask.AffectedFiles.Count > 0
                ? $"\n\nFiles to modify: {string.Join(", ", subTask.AffectedFiles)}"
                : string.Empty);

        return await CallChatCompletionsAsync(systemPrompt, userPrompt, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> GenerateCommitMessageAsync(
        string taskDescription,
        IReadOnlyList<SubTask> subTasks,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating commit message for task: {Task}", taskDescription);

        var systemPrompt =
            "You are a software engineer writing a Git commit message. " +
            "Create a concise, conventional-commit-style message (type: short description, blank line, " +
            "then a bulleted body summarising what was done). Keep the title under 72 characters.";

        var subtaskSummary = string.Join("\n", subTasks.Select((s, i) => $"  {i + 1}. {s.Title}"));
        var userPrompt =
            $"Original task: {taskDescription}\n\nCompleted subtasks:\n{subtaskSummary}";

        return await CallChatCompletionsAsync(systemPrompt, userPrompt, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<FixPlan> AnalyzeTestFailuresAsync(
        string ciLogs,
        string taskDescription,
        string diff,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Analyzing test failures for task: {Task}", taskDescription);

        var systemPrompt =
            "You are an expert software engineer analyzing CI test failures. " +
            "Given the failing test output and the current code diff, produce a JSON object " +
            "with 'summary' (string describing what is being fixed) and 'fileChanges' (array of " +
            "objects each with 'path' (string), 'content' (full corrected file content as string), " +
            "and 'reason' (short string)). " +
            "Return ONLY the JSON object, no surrounding prose.";

        var userPrompt =
            $"Original task: {taskDescription}\n\n" +
            $"Current diff:\n```diff\n{diff}\n```\n\n" +
            $"CI failure logs:\n```\n{ciLogs}\n```";

        var content = await CallChatCompletionsAsync(systemPrompt, userPrompt, cancellationToken)
            .ConfigureAwait(false);

        return ParseFixPlan(content);
    }

    /// <inheritdoc/>
    public async Task<FixPlan> GenerateFixForReviewFeedbackAsync(
        CodeReviewResult review,
        string diff,
        string taskDescription,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Generating fixes for review feedback on task: {Task}", taskDescription);

        var issueLines = review.Comments.Count > 0
            ? string.Join("\n", review.Comments.Select(c => $"- [{c.Severity}] {c.FilePath}" +
                (c.LineNumber.HasValue ? $":{c.LineNumber}" : string.Empty) + $": {c.Message}"))
            : "(no specific comments – see summary)";

        var systemPrompt =
            "You are a senior software engineer addressing code review feedback. " +
            "Given the review issues and the current code diff, produce a JSON object " +
            "with 'summary' (string describing what is being fixed) and 'fileChanges' (array of " +
            "objects each with 'path' (string), 'content' (full corrected file content as string), " +
            "and 'reason' (short string explaining the fix)). " +
            "Every Error-severity comment MUST be resolved. " +
            "Return ONLY the JSON object, no surrounding prose.";

        var userPrompt =
            $"Original task: {taskDescription}\n\n" +
            $"Current diff:\n```diff\n{diff}\n```\n\n" +
            $"Review summary: {review.Summary}\n\n" +
            $"Review issues:\n{issueLines}";

        var content = await CallChatCompletionsAsync(systemPrompt, userPrompt, cancellationToken)
            .ConfigureAwait(false);

        return ParseFixPlan(content);
    }

    // ── Private helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a single-turn chat completion request to the Copilot API and returns the
    /// assistant's reply text.
    /// </summary>
    private async Task<string> CallChatCompletionsAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken)
    {
        var request = new
        {
            model = _options.CopilotModel,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userPrompt   }
            },
            temperature = 0.2,
            max_tokens = 4096
        };

        _logger.LogDebug("Calling Copilot API at {BaseAddress}", _httpClient.BaseAddress);

        using var response = await _httpClient
            .PostAsJsonAsync("chat/completions", request, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var choicesElement = doc.RootElement.GetProperty("choices");

        if (choicesElement.GetArrayLength() == 0)
            throw new InvalidOperationException("Copilot API returned a response with no choices.");

        var content = choicesElement[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        return content ?? throw new InvalidOperationException("Copilot API returned an empty response.");
    }

    /// <summary>
    /// Parses a <see cref="FixPlan"/> from a JSON Copilot response.
    /// </summary>
    private static FixPlan ParseFixPlan(string json)
    {
        json = StripCodeFences(json);

        var dto = JsonSerializer.Deserialize<FixPlanDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse fix plan from Copilot response.");

        return new FixPlan
        {
            Summary = dto.Summary ?? "No summary provided.",
            FileChanges = (dto.FileChanges ?? [])
                .Select(f => new FileChange
                {
                    Path    = f.Path    ?? "unknown",
                    Content = f.Content ?? string.Empty,
                    Reason  = f.Reason
                })
                .ToList()
        };
    }

    /// <summary>
    /// Parses a JSON array of subtask definitions from a Copilot response string.
    /// </summary>
    private static IReadOnlyList<SubTask> ParseSubTasks(string json)
    {
        // Strip possible markdown code fences that the model might add.
        json = StripCodeFences(json);

        var items = JsonSerializer.Deserialize<List<SubTaskDto>>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse subtask list from Copilot response.");

        return items
            .Select((dto, index) => new SubTask
            {
                Index = index,
                Title = dto.Title ?? $"Subtask {index + 1}",
                Description = dto.Description ?? string.Empty,
                AffectedFiles = dto.AffectedFiles ?? []
            })
            .ToList();
    }

    /// <summary>
    /// Parses a <see cref="CodeReviewResult"/> from a JSON Copilot response.
    /// </summary>
    private static CodeReviewResult ParseCodeReview(string json)
    {
        json = StripCodeFences(json);

        var dto = JsonSerializer.Deserialize<CodeReviewDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to parse code review result from Copilot response.");

        return new CodeReviewResult
        {
            IsApproved = dto.IsApproved,
            Summary = dto.Summary ?? "No summary provided.",
            ConfidenceScore = dto.ConfidenceScore,
            Comments = (dto.Comments ?? [])
                .Select(c => new ReviewComment
                {
                    FilePath = c.FilePath ?? "unknown",
                    LineNumber = c.LineNumber,
                    Severity = Enum.TryParse<ReviewCommentSeverity>(c.Severity, true, out var sev)
                        ? sev
                        : ReviewCommentSeverity.Info,
                    Message = c.Message ?? string.Empty
                })
                .ToList()
        };
    }

    /// <summary>
    /// Removes Markdown code fences (<c>```json … ```</c>) that language models sometimes
    /// wrap their JSON output in.
    /// </summary>
    private static string StripCodeFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');
            if (firstNewLine > 0)
                text = text[(firstNewLine + 1)..];
        }

        if (text.EndsWith("```", StringComparison.Ordinal))
            text = text[..^3].TrimEnd();

        return text;
    }

    // ── DTO records used only for deserialization ────────────────────────────────

    private sealed record SubTaskDto(
        [property: JsonPropertyName("title")]        string? Title,
        [property: JsonPropertyName("description")]  string? Description,
        [property: JsonPropertyName("affectedFiles")] List<string>? AffectedFiles);

    private sealed record CodeReviewDto(
        [property: JsonPropertyName("isApproved")]      bool IsApproved,
        [property: JsonPropertyName("summary")]         string? Summary,
        [property: JsonPropertyName("confidenceScore")] double ConfidenceScore,
        [property: JsonPropertyName("comments")]        List<ReviewCommentDto>? Comments);

    private sealed record ReviewCommentDto(
        [property: JsonPropertyName("filePath")]   string? FilePath,
        [property: JsonPropertyName("lineNumber")] int? LineNumber,
        [property: JsonPropertyName("severity")]   string? Severity,
        [property: JsonPropertyName("message")]    string? Message);

    private sealed record FixPlanDto(
        [property: JsonPropertyName("summary")]     string? Summary,
        [property: JsonPropertyName("fileChanges")] List<FileChangeDto>? FileChanges);

    private sealed record FileChangeDto(
        [property: JsonPropertyName("path")]    string? Path,
        [property: JsonPropertyName("content")] string? Content,
        [property: JsonPropertyName("reason")]  string? Reason);
}
