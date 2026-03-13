using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using N8nAiLeadOps.DemoApi.Infrastructure;
using N8nAiLeadOps.DemoApi.Models;

namespace N8nAiLeadOps.DemoApi.Services;

public sealed class NotificationService
{
    private readonly JsonFileStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppOptions _options;
    private readonly string _slackFilePath;
    private readonly string _emailFilePath;
    private readonly string _bookingFilePath;

    public NotificationService(AppOptions options, JsonFileStore store, IHttpClientFactory httpClientFactory)
    {
        _options = options;
        _store = store;
        _httpClientFactory = httpClientFactory;
        _slackFilePath = Path.Combine(options.DataRoot, "notifications", "slack-messages.jsonl");
        _emailFilePath = Path.Combine(options.DataRoot, "email", "drafts.jsonl");
        _bookingFilePath = Path.Combine(options.DataRoot, "bookings", "handoffs.jsonl");
    }

    public async Task<SlackDeliveryResult> SendSlackNotificationAsync(SlackNotificationPayload payload)
    {
        await _store.AppendJsonLineAsync(_slackFilePath, payload);

        if (_options.SlackMode != "mock" && !string.IsNullOrWhiteSpace(_options.SlackWebhookUrl))
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.PostAsJsonAsync(_options.SlackWebhookUrl, new
            {
                Text = $"[{payload.Kind}] {payload.Company ?? payload.FullName ?? payload.LeadId} | score {payload.LeadScore} | {payload.Summary}"
            }, AppJson.Default);

            return new SlackDeliveryResult
            {
                Delivered = response.IsSuccessStatusCode,
                Mode = "live"
            };
        }

        return new SlackDeliveryResult
        {
            Delivered = true,
            Mode = "mock"
        };
    }

    public async Task<EmailDraftDeliveryResult> SaveEmailDraftAsync(EmailDraft draft)
    {
        await _store.AppendJsonLineAsync(_emailFilePath, draft);
        return new EmailDraftDeliveryResult
        {
            Stored = true,
            Mode = _options.GmailMode
        };
    }

    public async Task<BookingHandoffStoreResult> RecordBookingHandoffAsync(BookingHandoffPayload payload)
    {
        await _store.AppendJsonLineAsync(_bookingFilePath, payload);
        return new BookingHandoffStoreResult
        {
            Stored = true
        };
    }
}

public sealed class LlmService
{
    private readonly AppOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LeadPipelineService _pipeline;

    public LlmService(AppOptions options, IHttpClientFactory httpClientFactory, LeadPipelineService pipeline)
    {
        _options = options;
        _httpClientFactory = httpClientFactory;
        _pipeline = pipeline;
    }

    public async Task<LlmStepResult<LeadExtractionResult>> ExtractLeadAsync(NormalizedLeadInput lead)
    {
        var fallback = _pipeline.BuildMockExtraction(lead);
        if (_options.OpenAiMode == "mock" || string.IsNullOrWhiteSpace(_options.OpenAiApiKey))
        {
            return new LlmStepResult<LeadExtractionResult>
            {
                Data = fallback,
                Mode = "mock",
                FallbackUsed = false,
                Provider = "mock-heuristics",
                Error = null
            };
        }

        try
        {
            var payload = await CallChatCompletionAsync(
                "Extract structured lead attributes and return JSON only with the requested fields.",
                new JsonObject
                {
                    ["task"] = "extract_lead_attributes",
                    ["lead"] = JsonSerializer.SerializeToNode(lead, AppJson.Default)
                });

            return new LlmStepResult<LeadExtractionResult>
            {
                Data = ToExtraction(fallback, payload),
                Mode = "live",
                FallbackUsed = false,
                Provider = _options.OpenAiModel,
                Error = null
            };
        }
        catch (Exception exception)
        {
            return new LlmStepResult<LeadExtractionResult>
            {
                Data = fallback,
                Mode = "fallback",
                FallbackUsed = true,
                Provider = "mock-heuristics",
                Error = exception.Message
            };
        }
    }

    public async Task<LlmStepResult<LeadScoringResult>> ScoreLeadAsync(NormalizedLeadInput lead, LeadExtractionResult extraction)
    {
        var fallback = _pipeline.BuildMockScoring(lead, extraction);
        if (_options.OpenAiMode == "mock" || string.IsNullOrWhiteSpace(_options.OpenAiApiKey))
        {
            return new LlmStepResult<LeadScoringResult>
            {
                Data = fallback,
                Mode = "mock",
                FallbackUsed = false,
                Provider = "mock-heuristics",
                Error = null
            };
        }

        try
        {
            var payload = await CallChatCompletionAsync(
                "Score the lead and return JSON only with lead_score, qualification_status, recommended_route, confidence, and risk_flags.",
                new JsonObject
                {
                    ["task"] = "score_lead",
                    ["lead"] = JsonSerializer.SerializeToNode(lead, AppJson.Default),
                    ["extraction"] = JsonSerializer.SerializeToNode(extraction, AppJson.Default)
                });

            return new LlmStepResult<LeadScoringResult>
            {
                Data = ToScoring(fallback, payload),
                Mode = "live",
                FallbackUsed = false,
                Provider = _options.OpenAiModel,
                Error = null
            };
        }
        catch (Exception exception)
        {
            return new LlmStepResult<LeadScoringResult>
            {
                Data = fallback,
                Mode = "fallback",
                FallbackUsed = true,
                Provider = "mock-heuristics",
                Error = exception.Message
            };
        }
    }

    private async Task<JsonObject> CallChatCompletionAsync(string systemPrompt, JsonObject userPayload)
    {
        var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.OpenAiBaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(new
            {
                Model = _options.OpenAiModel,
                Temperature = 0.1,
                ResponseFormat = new
                {
                    Type = "json_object"
                },
                Messages = new object[]
                {
                    new
                    {
                        Role = "system",
                        Content = systemPrompt
                    },
                    new
                    {
                        Role = "user",
                        Content = userPayload.ToJsonString(AppJson.Default)
                    }
                }
            }, options: AppJson.Default)
        };

        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.OpenAiApiKey);
        var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LLM request failed with status {(int)response.StatusCode}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var responseNode = JsonNode.Parse(content)?.AsObject();
        var choices = responseNode?["choices"] as JsonArray;
        var messageContent = choices?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(messageContent))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(messageContent)?.AsObject() ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static LeadExtractionResult ToExtraction(LeadExtractionResult fallback, JsonObject payload)
    {
        return new LeadExtractionResult
        {
            FullName = payload["full_name"].GetTrimmedString() ?? fallback.FullName,
            Company = payload["company"].GetTrimmedString() ?? fallback.Company,
            Email = payload["email"].GetTrimmedString() ?? fallback.Email,
            Phone = payload["phone"].GetTrimmedString() ?? fallback.Phone,
            Source = payload["source"].GetTrimmedString() ?? fallback.Source,
            ServiceInterest = payload["service_interest"].GetTrimmedString() ?? fallback.ServiceInterest,
            EstimatedBudget = payload["estimated_budget"].GetFlexibleInt() ?? fallback.EstimatedBudget,
            Urgency = ParseUrgency(payload["urgency"].GetTrimmedString()) ?? fallback.Urgency,
            Geography = payload["geography"].GetTrimmedString() ?? fallback.Geography,
            FreeTextSummary = payload["free_text_summary"].GetTrimmedString() ?? fallback.FreeTextSummary,
            Sentiment = ParseSentiment(payload["sentiment"].GetTrimmedString()) ?? fallback.Sentiment,
            Intent = ParseIntent(payload["intent"].GetTrimmedString()) ?? fallback.Intent
        };
    }

    private static LeadScoringResult ToScoring(LeadScoringResult fallback, JsonObject payload)
    {
        var riskFlags = payload["risk_flags"] is JsonArray array
            ? array.Select(node => node.GetTrimmedString()).Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!).ToList()
            : fallback.RiskFlags;

        return new LeadScoringResult
        {
            LeadScore = payload["lead_score"].GetFlexibleInt() ?? fallback.LeadScore,
            QualificationStatus = ParseQualificationStatus(payload["qualification_status"].GetTrimmedString()) ?? fallback.QualificationStatus,
            RecommendedRoute = ParseRecommendedRoute(payload["recommended_route"].GetTrimmedString()) ?? fallback.RecommendedRoute,
            Confidence = payload["confidence"]?.DeserializeNode<double?>() ?? fallback.Confidence,
            RiskFlags = riskFlags
        };
    }

    private static UrgencyLevel? ParseUrgency(string? value)
    {
        return value switch
        {
            "high" => UrgencyLevel.High,
            "medium" => UrgencyLevel.Medium,
            "low" => UrgencyLevel.Low,
            _ => null
        };
    }

    private static SentimentLabel? ParseSentiment(string? value)
    {
        return value switch
        {
            "positive" => SentimentLabel.Positive,
            "negative" => SentimentLabel.Negative,
            "neutral" => SentimentLabel.Neutral,
            _ => null
        };
    }

    private static IntentLabel? ParseIntent(string? value)
    {
        return value switch
        {
            "purchase" => IntentLabel.Purchase,
            "research" => IntentLabel.Research,
            "support" => IntentLabel.Support,
            "spam" => IntentLabel.Spam,
            "unknown" => IntentLabel.Unknown,
            _ => null
        };
    }

    private static QualificationStatus? ParseQualificationStatus(string? value)
    {
        return value switch
        {
            "qualified" => QualificationStatus.Qualified,
            "nurture" => QualificationStatus.Nurture,
            "manual_review" => QualificationStatus.ManualReview,
            "rejected" => QualificationStatus.Rejected,
            "duplicate" => QualificationStatus.Duplicate,
            _ => null
        };
    }

    private static RecommendedRoute? ParseRecommendedRoute(string? value)
    {
        return value switch
        {
            "hot_lead_sales" => RecommendedRoute.HotLeadSales,
            "nurture_queue" => RecommendedRoute.NurtureQueue,
            "manual_review" => RecommendedRoute.ManualReview,
            "rejected_or_spam" => RecommendedRoute.RejectedOrSpam,
            _ => null
        };
    }
}
