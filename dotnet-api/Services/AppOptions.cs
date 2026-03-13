using Microsoft.Extensions.Configuration;

namespace N8nAiLeadOps.DemoApi.Services;

public sealed class AppOptions
{
    public int Port { get; init; }
    public string DataRoot { get; init; } = string.Empty;
    public string WebhookBaseUrl { get; init; } = string.Empty;
    public string OpenAiApiKey { get; init; } = string.Empty;
    public string OpenAiModel { get; init; } = string.Empty;
    public string OpenAiBaseUrl { get; init; } = string.Empty;
    public string OpenAiMode { get; init; } = string.Empty;
    public string MockCrmBaseUrl { get; init; } = string.Empty;
    public string SlackWebhookUrl { get; init; } = string.Empty;
    public string SlackMode { get; init; } = string.Empty;
    public string GmailMode { get; init; } = string.Empty;
    public string AuditMode { get; init; } = string.Empty;
    public string HumanApprovalMode { get; init; } = string.Empty;
    public string ApprovalCallbackUrl { get; init; } = string.Empty;
    public int BudgetMinQualified { get; init; }
    public string TimeZone { get; init; } = string.Empty;

    public static AppOptions Load(IConfiguration configuration, string contentRootPath)
    {
        var dataRoot = configuration["DATA_ROOT"];
        var resolvedDataRoot = string.IsNullOrWhiteSpace(dataRoot)
            ? Path.GetFullPath(Path.Combine(contentRootPath, "data"))
            : Path.IsPathRooted(dataRoot)
                ? dataRoot
                : Path.GetFullPath(Path.Combine(contentRootPath, dataRoot));

        return new AppOptions
        {
            Port = AsInt(configuration["PORT"], 3001),
            DataRoot = resolvedDataRoot,
            WebhookBaseUrl = AsString(configuration["WEBHOOK_BASE_URL"], "http://localhost:5678"),
            OpenAiApiKey = configuration["OPENAI_API_KEY"]?.Trim() ?? string.Empty,
            OpenAiModel = AsString(configuration["OPENAI_MODEL"], "gpt-4o-mini"),
            OpenAiBaseUrl = AsString(configuration["OPENAI_BASE_URL"], "https://api.openai.com/v1"),
            OpenAiMode = AsString(configuration["OPENAI_MODE"], "mock"),
            MockCrmBaseUrl = AsString(configuration["MOCK_CRM_BASE_URL"], "http://localhost:3001"),
            SlackWebhookUrl = configuration["SLACK_WEBHOOK_URL"]?.Trim() ?? string.Empty,
            SlackMode = AsString(configuration["SLACK_MODE"], "mock"),
            GmailMode = AsString(configuration["GMAIL_MODE"], "mock"),
            AuditMode = AsString(configuration["AUDIT_MODE"], "file"),
            HumanApprovalMode = AsString(configuration["HUMAN_APPROVAL_MODE"], "conditional"),
            ApprovalCallbackUrl = AsString(configuration["APPROVAL_CALLBACK_URL"], "http://localhost:5678/webhook/lead-approval-decision"),
            BudgetMinQualified = AsInt(configuration["BUDGET_MIN_QUALIFIED"], 3000),
            TimeZone = AsString(configuration["TZ"], "UTC")
        };
    }

    private static int AsInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static string AsString(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
