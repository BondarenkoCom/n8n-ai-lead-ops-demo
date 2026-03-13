using System.Text.Json.Nodes;

namespace N8nAiLeadOps.DemoApi.Models;

public enum LeadSourceType
{
    WebsiteForm,
    EmailInquiry,
    ChatSimulation,
    ApprovalCallback
}

public enum UrgencyLevel
{
    Low,
    Medium,
    High
}

public enum SentimentLabel
{
    Positive,
    Neutral,
    Negative
}

public enum IntentLabel
{
    Purchase,
    Research,
    Support,
    Spam,
    Unknown
}

public enum QualificationStatus
{
    Qualified,
    Nurture,
    ManualReview,
    Rejected,
    Duplicate
}

public enum RecommendedRoute
{
    HotLeadSales,
    NurtureQueue,
    ManualReview,
    RejectedOrSpam
}

public enum LeadStatus
{
    New,
    Qualified,
    Nurture,
    ManualReview,
    Rejected,
    Duplicate,
    HandoffRequested
}

public enum ApprovalStatus
{
    Pending,
    Approved,
    Rejected
}

public enum SlackNotificationKind
{
    HotLeadSales,
    ManualReview,
    ApprovalRequired
}

public sealed class NormalizedLeadInput
{
    public string ReceivedAt { get; set; } = string.Empty;
    public LeadSourceType Source { get; set; }
    public string SourceLabel { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? ServiceInterest { get; set; }
    public int? EstimatedBudget { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public string? Geography { get; set; }
    public string FreeTextSummary { get; set; } = string.Empty;
    public JsonObject RawPayload { get; set; } = new();
}

public sealed class LeadExtractionResult
{
    public string? FullName { get; set; }
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? ServiceInterest { get; set; }
    public int? EstimatedBudget { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public string? Geography { get; set; }
    public string FreeTextSummary { get; set; } = string.Empty;
    public SentimentLabel Sentiment { get; set; }
    public IntentLabel Intent { get; set; }
}

public sealed class LeadScoringResult
{
    public int LeadScore { get; set; }
    public QualificationStatus QualificationStatus { get; set; }
    public RecommendedRoute RecommendedRoute { get; set; }
    public double Confidence { get; set; }
    public List<string> RiskFlags { get; set; } = [];
}

public sealed class RuleEvaluationResult
{
    public int LeadScore { get; set; }
    public QualificationStatus QualificationStatus { get; set; }
    public RecommendedRoute FinalRoute { get; set; }
    public double Confidence { get; set; }
    public List<string> RiskFlags { get; set; } = [];
    public int DuplicateMatchCount { get; set; }
    public bool DuplicateSubmission { get; set; }
    public bool RequiresHumanApproval { get; set; }
    public bool ShouldNotifySlack { get; set; }
    public bool ShouldCreateReplyDraft { get; set; }
    public bool ShouldHandoffBooking { get; set; }
}

public sealed class LeadRecord
{
    public string Id { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public LeadStatus Status { get; set; }
    public string Owner { get; set; } = string.Empty;
    public int LeadScore { get; set; }
    public RecommendedRoute Route { get; set; }
    public List<string> Notes { get; set; } = [];
    public JsonObject SourcePayloadSnapshot { get; set; } = new();
    public string? FullName { get; set; }
    public string? Company { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? ServiceInterest { get; set; }
    public int? EstimatedBudget { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public string? Geography { get; set; }
    public string FreeTextSummary { get; set; } = string.Empty;
    public SentimentLabel Sentiment { get; set; }
    public IntentLabel Intent { get; set; }
    public double Confidence { get; set; }
    public List<string> RiskFlags { get; set; } = [];
    public QualificationStatus QualificationStatus { get; set; }
}

public sealed class CrmUpsertPayload
{
    public NormalizedLeadInput Lead { get; set; } = new();
    public LeadExtractionResult Extraction { get; set; } = new();
    public LeadScoringResult Scoring { get; set; } = new();
    public RuleEvaluationResult Evaluation { get; set; } = new();
}

public sealed class CrmUpsertResult
{
    public LeadRecord Record { get; set; } = new();
    public bool WasExisting { get; set; }
}

public sealed class EmailDraft
{
    public string DraftId { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string? To { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Mode { get; set; } = string.Empty;
    public string LeadId { get; set; } = string.Empty;
}

public sealed class BookingHandoffPayload
{
    public string LeadId { get; set; } = string.Empty;
    public string? RequestedSlot { get; set; }
    public RecommendedRoute Route { get; set; }
    public string Owner { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class ApprovalPayload
{
    public string LeadId { get; set; } = string.Empty;
    public RecommendedRoute Route { get; set; }
    public EmailDraft? EmailDraft { get; set; }
    public BookingHandoffPayload? BookingPayload { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed class ApprovalRequestRecord
{
    public string ApprovalId { get; set; } = string.Empty;
    public string CreatedAt { get; set; } = string.Empty;
    public string UpdatedAt { get; set; } = string.Empty;
    public ApprovalStatus Status { get; set; }
    public string? DecisionAt { get; set; }
    public RecommendedRoute Route { get; set; }
    public string LeadId { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public EmailDraft? EmailDraft { get; set; }
    public BookingHandoffPayload? BookingPayload { get; set; }
}

public sealed class AuditEvent
{
    public string EventId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string? LeadId { get; set; }
    public string? Route { get; set; }
    public string Status { get; set; } = string.Empty;
    public Dictionary<string, object?> Details { get; set; } = new();
}

public sealed class ErrorEvent
{
    public string ErrorId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object?> Details { get; set; } = new();
}

public sealed class IdempotencyRecord
{
    public string Key { get; set; } = string.Empty;
    public string FirstSeenAt { get; set; } = string.Empty;
    public string LastSeenAt { get; set; } = string.Empty;
    public int SeenCount { get; set; }
}

public sealed class SlackNotificationPayload
{
    public SlackNotificationKind Kind { get; set; }
    public string LeadId { get; set; } = string.Empty;
    public RecommendedRoute Route { get; set; }
    public string? FullName { get; set; }
    public string? Company { get; set; }
    public string? ServiceInterest { get; set; }
    public int LeadScore { get; set; }
    public UrgencyLevel Urgency { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public sealed class LlmStepResult<T>
{
    public T Data { get; set; } = default!;
    public string Mode { get; set; } = string.Empty;
    public bool FallbackUsed { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class SlackDeliveryResult
{
    public bool Delivered { get; set; }
    public string Mode { get; set; } = string.Empty;
}

public sealed class EmailDraftDeliveryResult
{
    public bool Stored { get; set; }
    public string Mode { get; set; } = string.Empty;
}

public sealed class BookingHandoffStoreResult
{
    public bool Stored { get; set; }
}

public sealed class RuleEvaluationInput
{
    public NormalizedLeadInput Lead { get; set; } = new();
    public LeadExtractionResult Extraction { get; set; } = new();
    public LeadScoringResult Scoring { get; set; } = new();
    public int DuplicateMatchCount { get; set; }
    public bool DuplicateSubmission { get; set; }
    public int BudgetMinQualified { get; set; }
    public string HumanApprovalMode { get; set; } = string.Empty;
}

public sealed class IdempotencyCheckRequest
{
    public string? Key { get; set; }
    public JsonObject? Lead { get; set; }
}

public sealed class LlmExtractRequest
{
    public NormalizedLeadInput Lead { get; set; } = new();
}

public sealed class LlmScoreRequest
{
    public NormalizedLeadInput Lead { get; set; } = new();
    public LeadExtractionResult Extraction { get; set; } = new();
}

public sealed class RulesEvaluateRequest
{
    public NormalizedLeadInput Lead { get; set; } = new();
    public LeadExtractionResult Extraction { get; set; } = new();
    public LeadScoringResult Scoring { get; set; } = new();
    public int DuplicateMatchCount { get; set; }
    public bool DuplicateSubmission { get; set; }
    public int? BudgetMinQualified { get; set; }
    public string? HumanApprovalMode { get; set; }
}

public sealed class SlackNotificationRequest
{
    public string? LeadId { get; set; }
    public SlackNotificationKind Kind { get; set; } = SlackNotificationKind.ManualReview;
}

public sealed class EmailDraftRequest
{
    public string? LeadId { get; set; }
    public EmailDraft? Draft { get; set; }
}

public sealed class ApprovalRequestCommand
{
    public string? LeadId { get; set; }
    public EmailDraft? EmailDraft { get; set; }
}

public sealed class ApprovalResolveRequest
{
    public string? ApprovalId { get; set; }
    public string? Decision { get; set; }
}
