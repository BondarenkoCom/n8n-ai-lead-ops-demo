using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using N8nAiLeadOps.DemoApi.Infrastructure;
using N8nAiLeadOps.DemoApi.Models;

namespace N8nAiLeadOps.DemoApi.Services;

public sealed partial class LeadPipelineService
{
    private static readonly IReadOnlyDictionary<LeadSourceType, string> SourceLabels = new Dictionary<LeadSourceType, string>
    {
        [LeadSourceType.WebsiteForm] = "Website form",
        [LeadSourceType.EmailInquiry] = "Email inquiry",
        [LeadSourceType.ChatSimulation] = "Chat simulation",
        [LeadSourceType.ApprovalCallback] = "Approval callback"
    };

    public NormalizedLeadInput NormalizeInboundLead(JsonObject? payload, string? sourceHint)
    {
        var record = payload.DeepCloneObject();
        var from = record["from"] as JsonObject;
        var contact = record["contact"] as JsonObject;
        var metadata = record["metadata"] as JsonObject;
        var source = DetectSource(record, sourceHint);

        return new NormalizedLeadInput
        {
            ReceivedAt = SystemClock.UtcNow(),
            Source = source,
            SourceLabel = SourceLabels[source],
            FullName = PickFirstString(record["full_name"], record["name"], from?["name"], contact?["name"]),
            Company = PickFirstString(record["company"], record["organization"], contact?["company"]),
            Email = PickFirstString(record["email"], from?["email"], contact?["email"]),
            Phone = PickFirstString(record["phone"], contact?["phone"], record["mobile"]),
            ServiceInterest = PickFirstString(record["service_interest"], record["service"], record["topic"]),
            EstimatedBudget = record["estimated_budget"].GetFlexibleInt() ?? record["budget"].GetFlexibleInt() ?? metadata?["budget"].GetFlexibleInt(),
            Urgency = ParseUrgency(PickFirstString(record["urgency"], metadata?["urgency"])),
            Geography = PickFirstString(record["geography"], record["location"], record["country"], contact?["location"]),
            FreeTextSummary = BuildSummary(
                PickFirstString(record["message"], record["body"], record["notes"], record["inquiry"]),
                PickFirstString(record["subject"], metadata?["campaign"], record["service"], record["topic"])),
            RawPayload = record
        };
    }

    public LeadExtractionResult BuildMockExtraction(NormalizedLeadInput lead)
    {
        var text = lead.FreeTextSummary;
        return new LeadExtractionResult
        {
            FullName = lead.FullName,
            Company = lead.Company,
            Email = lead.Email,
            Phone = lead.Phone,
            Source = lead.SourceLabel,
            ServiceInterest = InferServiceInterest(text, lead.ServiceInterest),
            EstimatedBudget = lead.EstimatedBudget ?? InferBudgetFromText(text),
            Urgency = lead.Urgency == UrgencyLevel.Low ? InferUrgencyFromText(text) : lead.Urgency,
            Geography = lead.Geography,
            FreeTextSummary = SummarizeText(text),
            Sentiment = InferSentiment(text),
            Intent = InferIntent(text)
        };
    }

    public LeadScoringResult BuildMockScoring(NormalizedLeadInput lead, LeadExtractionResult extraction)
    {
        var score = 35;
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (extraction.Intent)
        {
            case IntentLabel.Purchase:
                score += 26;
                break;
            case IntentLabel.Research:
                score += 12;
                break;
            case IntentLabel.Support:
                score += 4;
                break;
            case IntentLabel.Spam:
                score -= 55;
                flags.Add("spam_keywords");
                break;
            default:
                flags.Add("intent_unclear");
                break;
        }

        if (extraction.EstimatedBudget.HasValue)
        {
            if (extraction.EstimatedBudget >= 15000)
            {
                score += 20;
            }
            else if (extraction.EstimatedBudget >= 5000)
            {
                score += 12;
            }
            else if (extraction.EstimatedBudget < 1500)
            {
                score -= 18;
                flags.Add("budget_low");
            }
        }
        else
        {
            flags.Add("budget_missing");
        }

        score += extraction.Urgency switch
        {
            UrgencyLevel.High => 10,
            UrgencyLevel.Medium => 5,
            _ => 0
        };

        if (!string.IsNullOrWhiteSpace(extraction.Company))
        {
            score += 5;
        }
        else
        {
            flags.Add("company_missing");
        }

        if (!string.IsNullOrWhiteSpace(extraction.Email) || !string.IsNullOrWhiteSpace(extraction.Phone))
        {
            score += 6;
        }
        else
        {
            score -= 18;
            flags.Add("contact_missing");
        }

        if (extraction.Sentiment == SentimentLabel.Positive)
        {
            score += 5;
        }

        if (lead.Source == LeadSourceType.WebsiteForm)
        {
            score += 3;
        }

        score = Math.Clamp(score, 0, 100);

        var completenessParts = new[]
        {
            extraction.FullName,
            extraction.Company,
            extraction.Email,
            extraction.Phone,
            extraction.ServiceInterest,
            extraction.Geography
        }.Count(value => !string.IsNullOrWhiteSpace(value));

        var confidence = 0.42 + completenessParts * 0.07;
        if (extraction.Intent == IntentLabel.Purchase)
        {
            confidence += 0.12;
        }

        if (extraction.Intent == IntentLabel.Unknown)
        {
            confidence -= 0.08;
        }

        if (flags.Contains("contact_missing"))
        {
            confidence -= 0.12;
        }

        confidence = Math.Clamp(Math.Round(confidence, 2), 0.3, 0.97);

        var qualificationStatus = extraction.Intent == IntentLabel.Spam || score < 20
            ? QualificationStatus.Rejected
            : score >= 75 && confidence >= 0.65
                ? QualificationStatus.Qualified
                : score >= 40 && confidence >= 0.58
                    ? QualificationStatus.Nurture
                    : QualificationStatus.ManualReview;

        return new LeadScoringResult
        {
            LeadScore = score,
            QualificationStatus = qualificationStatus,
            RecommendedRoute = InferRoute(score, confidence, qualificationStatus),
            Confidence = confidence,
            RiskFlags = flags.ToList()
        };
    }

    public RuleEvaluationResult EvaluateBusinessRules(RuleEvaluationInput input)
    {
        var riskFlags = new HashSet<string>(input.Scoring.RiskFlags, StringComparer.OrdinalIgnoreCase);
        var leadScore = input.Scoring.LeadScore;
        var qualificationStatus = input.Scoring.QualificationStatus;
        var finalRoute = input.Scoring.RecommendedRoute;
        var confidence = input.Scoring.Confidence;

        if (IsSpam(input.Extraction))
        {
            finalRoute = RecommendedRoute.RejectedOrSpam;
            qualificationStatus = QualificationStatus.Rejected;
            leadScore = Math.Min(leadScore, 10);
            riskFlags.Add("spam_filtered");
        }

        if (string.IsNullOrWhiteSpace(input.Extraction.Email) && string.IsNullOrWhiteSpace(input.Extraction.Phone))
        {
            finalRoute = RecommendedRoute.ManualReview;
            qualificationStatus = QualificationStatus.ManualReview;
            confidence = Math.Min(confidence, 0.52);
            riskFlags.Add("missing_required_contact");
        }

        if (string.IsNullOrWhiteSpace(input.Extraction.ServiceInterest))
        {
            finalRoute = RecommendedRoute.ManualReview;
            qualificationStatus = QualificationStatus.ManualReview;
            confidence = Math.Min(confidence, 0.58);
            riskFlags.Add("service_interest_missing");
        }

        if (input.Extraction.EstimatedBudget.HasValue && input.Extraction.EstimatedBudget < input.BudgetMinQualified && finalRoute != RecommendedRoute.RejectedOrSpam)
        {
            finalRoute = RecommendedRoute.NurtureQueue;
            if (qualificationStatus != QualificationStatus.Rejected)
            {
                qualificationStatus = QualificationStatus.Nurture;
            }

            riskFlags.Add("budget_below_threshold");
        }

        if (input.Extraction.Urgency == UrgencyLevel.High && input.Extraction.EstimatedBudget.HasValue && input.Extraction.EstimatedBudget >= input.BudgetMinQualified && finalRoute != RecommendedRoute.RejectedOrSpam)
        {
            finalRoute = RecommendedRoute.HotLeadSales;
            qualificationStatus = QualificationStatus.Qualified;
            riskFlags.Add("urgency_escalation");
        }

        if (input.DuplicateMatchCount > 0)
        {
            finalRoute = RecommendedRoute.ManualReview;
            qualificationStatus = QualificationStatus.Duplicate;
            confidence = Math.Min(confidence, 0.64);
            riskFlags.Add("duplicate_contact_match");
        }

        if (input.DuplicateSubmission)
        {
            finalRoute = RecommendedRoute.ManualReview;
            qualificationStatus = QualificationStatus.Duplicate;
            confidence = Math.Min(confidence, 0.6);
            riskFlags.Add("duplicate_submission_key");
        }

        var requiresHumanApproval = input.HumanApprovalMode == "always" ||
                                    (input.HumanApprovalMode == "conditional" &&
                                     (finalRoute == RecommendedRoute.HotLeadSales || finalRoute == RecommendedRoute.ManualReview));

        return new RuleEvaluationResult
        {
            LeadScore = Math.Clamp(leadScore, 0, 100),
            QualificationStatus = qualificationStatus,
            FinalRoute = finalRoute,
            Confidence = Math.Round(confidence, 2),
            RiskFlags = riskFlags.ToList(),
            DuplicateMatchCount = input.DuplicateMatchCount,
            DuplicateSubmission = input.DuplicateSubmission,
            RequiresHumanApproval = requiresHumanApproval,
            ShouldNotifySlack = finalRoute is RecommendedRoute.HotLeadSales or RecommendedRoute.ManualReview,
            ShouldCreateReplyDraft = finalRoute != RecommendedRoute.RejectedOrSpam,
            ShouldHandoffBooking = finalRoute == RecommendedRoute.HotLeadSales && qualificationStatus == QualificationStatus.Qualified
        };
    }

    public EmailDraft BuildReplyDraft(LeadRecord lead)
    {
        var firstName = lead.FullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
        var serviceLine = lead.ServiceInterest ?? "your requested service";
        var subject = lead.Route == RecommendedRoute.HotLeadSales
            ? $"Next steps for {serviceLine}"
            : $"Follow-up on your {serviceLine} inquiry";

        var body = lead.Route == RecommendedRoute.HotLeadSales
            ? $"Hi {firstName},\n\nThanks for reaching out about {serviceLine}. We reviewed your inquiry and can move this into a short discovery call. If the timeline still stands, reply with two preferred time windows and any relevant constraints.\n\nBest,\nBluePeak Ops"
            : $"Hi {firstName},\n\nThanks for sharing the details on {serviceLine}. We reviewed the request and can outline a practical next step once we confirm scope, timeline, and fit. Reply with any missing context that would help sharpen the estimate.\n\nBest,\nBluePeak Ops";

        return new EmailDraft
        {
            DraftId = $"draft_{Guid.NewGuid()}",
            CreatedAt = SystemClock.UtcNow(),
            To = lead.Email,
            Subject = subject,
            Body = body,
            Mode = "mock",
            LeadId = lead.Id
        };
    }

    private static LeadSourceType DetectSource(JsonObject payload, string? sourceHint)
    {
        if (sourceHint == "email_inquiry" || payload.ContainsKey("from") || payload.ContainsKey("subject"))
        {
            return LeadSourceType.EmailInquiry;
        }

        if (sourceHint == "chat_simulation" || payload.ContainsKey("channel") || payload.ContainsKey("contact"))
        {
            return LeadSourceType.ChatSimulation;
        }

        if (sourceHint == "approval_callback")
        {
            return LeadSourceType.ApprovalCallback;
        }

        return LeadSourceType.WebsiteForm;
    }

    private static string BuildSummary(params string?[] values)
    {
        var summary = string.Join(" | ", values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()));
        return string.IsNullOrWhiteSpace(summary) ? "Inbound lead received without descriptive text." : summary;
    }

    private static string? PickFirstString(params JsonNode?[] values)
    {
        foreach (var value in values)
        {
            var normalized = value.GetTrimmedString();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return null;
    }

    private static UrgencyLevel ParseUrgency(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "high" => UrgencyLevel.High,
            "medium" => UrgencyLevel.Medium,
            _ => UrgencyLevel.Low
        };
    }

    private static bool IsSpam(LeadExtractionResult extraction)
    {
        var value = extraction.FreeTextSummary.ToLowerInvariant();
        return extraction.Intent == IntentLabel.Spam || SpamFilterPattern().IsMatch(value);
    }

    private static int? InferBudgetFromText(string text)
    {
        var match = BudgetPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        var digits = new string(match.Groups[1].Value.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var parsed) ? parsed : null;
    }

    private static UrgencyLevel InferUrgencyFromText(string text)
    {
        var value = text.ToLowerInvariant();
        if (Regex.IsMatch(value, "(asap|urgent|this week|today|immediately)"))
        {
            return UrgencyLevel.High;
        }

        if (Regex.IsMatch(value, "(next month|soon|timeline|q[1-4]|this month)"))
        {
            return UrgencyLevel.Medium;
        }

        return UrgencyLevel.Low;
    }

    private static SentimentLabel InferSentiment(string text)
    {
        var value = text.ToLowerInvariant();
        if (Regex.IsMatch(value, "(frustrated|blocked|painful|urgent|need help)"))
        {
            return SentimentLabel.Negative;
        }

        if (Regex.IsMatch(value, "(excited|interested|ready|looking forward|great fit)"))
        {
            return SentimentLabel.Positive;
        }

        return SentimentLabel.Neutral;
    }

    private static IntentLabel InferIntent(string text)
    {
        var value = text.ToLowerInvariant();
        if (Regex.IsMatch(value, "(casino|forex|seo package|buy lists|guest post|backlink)"))
        {
            return IntentLabel.Spam;
        }

        if (Regex.IsMatch(value, "(proposal|quote|book|demo|ready to start|need a partner|engage)"))
        {
            return IntentLabel.Purchase;
        }

        if (Regex.IsMatch(value, "(exploring|research|considering|curious|evaluate)"))
        {
            return IntentLabel.Research;
        }

        if (Regex.IsMatch(value, "(issue|support|problem|bug|outage)"))
        {
            return IntentLabel.Support;
        }

        return IntentLabel.Unknown;
    }

    private static string? InferServiceInterest(string text, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(fallback))
        {
            return fallback;
        }

        var value = text.ToLowerInvariant();
        if (Regex.IsMatch(value, "(lead|qualification|crm|sales ops)"))
        {
            return "Lead qualification automation";
        }

        if (Regex.IsMatch(value, "(ai|llm|assistant|copilot)"))
        {
            return "AI workflow implementation";
        }

        if (Regex.IsMatch(value, "(booking|calendar|appointment)"))
        {
            return "Booking orchestration";
        }

        if (Regex.IsMatch(value, "(integration|api|sync)"))
        {
            return "Systems integration";
        }

        return null;
    }

    private static string SummarizeText(string text)
    {
        var cleaned = Regex.Replace(text, "\\s+", " ").Trim();
        return cleaned.Length <= 220 ? cleaned : $"{cleaned[..217]}...";
    }

    private static RecommendedRoute InferRoute(int score, double confidence, QualificationStatus status)
    {
        if (status == QualificationStatus.Rejected)
        {
            return RecommendedRoute.RejectedOrSpam;
        }

        if (status == QualificationStatus.Duplicate || confidence < 0.6)
        {
            return RecommendedRoute.ManualReview;
        }

        if (score >= 75)
        {
            return RecommendedRoute.HotLeadSales;
        }

        if (score >= 40)
        {
            return RecommendedRoute.NurtureQueue;
        }

        return RecommendedRoute.ManualReview;
    }

    [GeneratedRegex(@"(?:\$|usd|budget|investment|around|approx(?:imately)?)\s*([0-9]{1,3}(?:[,\s][0-9]{3})+|[0-9]{3,6})", RegexOptions.IgnoreCase)]
    private static partial Regex BudgetPattern();

    [GeneratedRegex("(casino|buy email list|guest post|backlink|forex|seo agency package)", RegexOptions.IgnoreCase)]
    private static partial Regex SpamFilterPattern();
}
