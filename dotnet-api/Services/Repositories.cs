using N8nAiLeadOps.DemoApi.Infrastructure;
using N8nAiLeadOps.DemoApi.Models;

namespace N8nAiLeadOps.DemoApi.Services;

public sealed class CrmRepository
{
    private static readonly IReadOnlyDictionary<RecommendedRoute, string> OwnerByRoute = new Dictionary<RecommendedRoute, string>
    {
        [RecommendedRoute.HotLeadSales] = "sales-desk",
        [RecommendedRoute.NurtureQueue] = "lifecycle-ops",
        [RecommendedRoute.ManualReview] = "triage-desk",
        [RecommendedRoute.RejectedOrSpam] = "suppression-queue"
    };

    private static readonly IReadOnlyDictionary<RecommendedRoute, LeadStatus> StatusByRoute = new Dictionary<RecommendedRoute, LeadStatus>
    {
        [RecommendedRoute.HotLeadSales] = LeadStatus.Qualified,
        [RecommendedRoute.NurtureQueue] = LeadStatus.Nurture,
        [RecommendedRoute.ManualReview] = LeadStatus.ManualReview,
        [RecommendedRoute.RejectedOrSpam] = LeadStatus.Rejected
    };

    private readonly JsonFileStore _store;
    private readonly string _filePath;

    public CrmRepository(AppOptions options, JsonFileStore store)
    {
        _store = store;
        _filePath = Path.Combine(options.DataRoot, "crm", "leads.json");
    }

    public async Task<IReadOnlyList<LeadRecord>> ListAsync(string? email = null, string? phone = null)
    {
        var leads = await _store.ReadAsync(_filePath, new List<LeadRecord>());
        return leads.Where(lead =>
            (!string.IsNullOrWhiteSpace(email) && string.Equals(lead.Email, email, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(phone) && string.Equals(lead.Phone, phone, StringComparison.Ordinal)) ||
            (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone)))
            .ToList();
    }

    public async Task<LeadRecord?> GetByIdAsync(string id)
    {
        var leads = await _store.ReadAsync(_filePath, new List<LeadRecord>());
        return leads.FirstOrDefault(lead => lead.Id == id);
    }

    public async Task<CrmUpsertResult> UpsertAsync(CrmUpsertPayload payload)
    {
        var leads = await _store.ReadAsync(_filePath, new List<LeadRecord>());
        var index = leads.FindIndex(lead =>
            (!string.IsNullOrWhiteSpace(payload.Extraction.Email) && string.Equals(lead.Email, payload.Extraction.Email, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(payload.Extraction.Phone) && string.Equals(lead.Phone, payload.Extraction.Phone, StringComparison.Ordinal)));

        var now = SystemClock.UtcNow();
        var existing = index >= 0 ? leads[index] : null;

        var notes = new List<string>();
        if (existing is not null)
        {
            notes.AddRange(existing.Notes);
        }

        if (payload.Evaluation.DuplicateSubmission)
        {
            notes.Add("Duplicate submission key detected");
        }

        if (payload.Evaluation.DuplicateMatchCount > 0)
        {
            notes.Add("Duplicate email or phone match detected");
        }

        notes.AddRange(payload.Evaluation.RiskFlags);

        var record = new LeadRecord
        {
            Id = existing?.Id ?? $"lead_{Guid.NewGuid()}",
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
            Status = payload.Evaluation.QualificationStatus == QualificationStatus.Duplicate
                ? LeadStatus.Duplicate
                : StatusByRoute[payload.Evaluation.FinalRoute],
            Owner = OwnerByRoute[payload.Evaluation.FinalRoute],
            LeadScore = payload.Evaluation.LeadScore,
            Route = payload.Evaluation.FinalRoute,
            Notes = notes
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            SourcePayloadSnapshot = payload.Lead.RawPayload.DeepCloneObject(),
            FullName = payload.Extraction.FullName,
            Company = payload.Extraction.Company,
            Email = payload.Extraction.Email,
            Phone = payload.Extraction.Phone,
            Source = payload.Extraction.Source,
            ServiceInterest = payload.Extraction.ServiceInterest,
            EstimatedBudget = payload.Extraction.EstimatedBudget,
            Urgency = payload.Extraction.Urgency,
            Geography = payload.Extraction.Geography,
            FreeTextSummary = payload.Extraction.FreeTextSummary,
            Sentiment = payload.Extraction.Sentiment,
            Intent = payload.Extraction.Intent,
            Confidence = payload.Evaluation.Confidence,
            RiskFlags = payload.Evaluation.RiskFlags.ToList(),
            QualificationStatus = payload.Evaluation.QualificationStatus
        };

        if (index >= 0)
        {
            leads[index] = record;
        }
        else
        {
            leads.Add(record);
        }

        await _store.WriteAsync(_filePath, leads);

        return new CrmUpsertResult
        {
            Record = record,
            WasExisting = existing is not null
        };
    }

    public async Task<LeadRecord?> UpdateStatusAsync(string id, LeadStatus status, string? note = null)
    {
        var leads = await _store.ReadAsync(_filePath, new List<LeadRecord>());
        var index = leads.FindIndex(lead => lead.Id == id);
        if (index < 0)
        {
            return null;
        }

        var record = leads[index];
        var updated = new LeadRecord
        {
            Id = record.Id,
            CreatedAt = record.CreatedAt,
            UpdatedAt = SystemClock.UtcNow(),
            Status = status,
            Owner = record.Owner,
            LeadScore = record.LeadScore,
            Route = record.Route,
            Notes = string.IsNullOrWhiteSpace(note)
                ? record.Notes
                : record.Notes.Concat([note]).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            SourcePayloadSnapshot = record.SourcePayloadSnapshot.DeepCloneObject(),
            FullName = record.FullName,
            Company = record.Company,
            Email = record.Email,
            Phone = record.Phone,
            Source = record.Source,
            ServiceInterest = record.ServiceInterest,
            EstimatedBudget = record.EstimatedBudget,
            Urgency = record.Urgency,
            Geography = record.Geography,
            FreeTextSummary = record.FreeTextSummary,
            Sentiment = record.Sentiment,
            Intent = record.Intent,
            Confidence = record.Confidence,
            RiskFlags = record.RiskFlags.ToList(),
            QualificationStatus = record.QualificationStatus
        };

        leads[index] = updated;
        await _store.WriteAsync(_filePath, leads);
        return updated;
    }
}

public sealed class ApprovalRepository
{
    private readonly JsonFileStore _store;
    private readonly string _filePath;

    public ApprovalRepository(AppOptions options, JsonFileStore store)
    {
        _store = store;
        _filePath = Path.Combine(options.DataRoot, "approvals", "pending-approvals.json");
    }

    public async Task<ApprovalRequestRecord> CreateAsync(ApprovalPayload payload)
    {
        var approvals = await _store.ReadAsync(_filePath, new List<ApprovalRequestRecord>());
        var now = SystemClock.UtcNow();
        var record = new ApprovalRequestRecord
        {
            ApprovalId = $"approval_{Guid.NewGuid()}",
            CreatedAt = now,
            UpdatedAt = now,
            Status = ApprovalStatus.Pending,
            DecisionAt = null,
            Route = payload.Route,
            LeadId = payload.LeadId,
            Reason = payload.Reason,
            EmailDraft = payload.EmailDraft,
            BookingPayload = payload.BookingPayload
        };

        approvals.Add(record);
        await _store.WriteAsync(_filePath, approvals);
        return record;
    }

    public async Task<ApprovalRequestRecord?> GetAsync(string approvalId)
    {
        var approvals = await _store.ReadAsync(_filePath, new List<ApprovalRequestRecord>());
        return approvals.FirstOrDefault(approval => approval.ApprovalId == approvalId);
    }

    public async Task<ApprovalRequestRecord?> ResolveAsync(string approvalId, string decision)
    {
        var approvals = await _store.ReadAsync(_filePath, new List<ApprovalRequestRecord>());
        var index = approvals.FindIndex(approval => approval.ApprovalId == approvalId);
        if (index < 0)
        {
            return null;
        }

        var now = SystemClock.UtcNow();
        var updated = approvals[index];
        updated.Status = decision == "approved" ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        updated.DecisionAt = now;
        updated.UpdatedAt = now;

        approvals[index] = updated;
        await _store.WriteAsync(_filePath, approvals);
        return updated;
    }
}

public sealed class AuditService
{
    private static readonly string[] AuditHeaders = ["timestamp", "run_id", "event_type", "lead_id", "route", "status"];

    private readonly JsonFileStore _store;
    private readonly string _auditJsonlPath;
    private readonly string _auditCsvPath;
    private readonly string _errorJsonlPath;
    private readonly string _idempotencyPath;

    public AuditService(AppOptions options, JsonFileStore store)
    {
        _store = store;
        _auditJsonlPath = Path.Combine(options.DataRoot, "audit", "audit-log.jsonl");
        _auditCsvPath = Path.Combine(options.DataRoot, "audit", "audit-log.csv");
        _errorJsonlPath = Path.Combine(options.DataRoot, "errors", "error-log.jsonl");
        _idempotencyPath = Path.Combine(options.DataRoot, "idempotency", "keys.json");
    }

    public async Task WriteAuditEventAsync(AuditEvent auditEvent)
    {
        await _store.AppendJsonLineAsync(_auditJsonlPath, auditEvent);
        await _store.AppendCsvRowAsync(_auditCsvPath, AuditHeaders, new Dictionary<string, string?>
        {
            ["timestamp"] = auditEvent.Timestamp,
            ["run_id"] = auditEvent.RunId,
            ["event_type"] = auditEvent.EventType,
            ["lead_id"] = auditEvent.LeadId,
            ["route"] = auditEvent.Route,
            ["status"] = auditEvent.Status
        });
    }

    public Task WriteErrorEventAsync(ErrorEvent errorEvent)
    {
        return _store.AppendJsonLineAsync(_errorJsonlPath, errorEvent);
    }

    public async Task<IdempotencyRecord> RegisterSubmissionKeyAsync(string key)
    {
        var records = await _store.ReadAsync(_idempotencyPath, new List<IdempotencyRecord>());
        var existing = records.FirstOrDefault(record => record.Key == key);
        var now = SystemClock.UtcNow();

        if (existing is not null)
        {
            existing.LastSeenAt = now;
            existing.SeenCount += 1;
            await _store.WriteAsync(_idempotencyPath, records);
            return existing;
        }

        var record = new IdempotencyRecord
        {
            Key = key,
            FirstSeenAt = now,
            LastSeenAt = now,
            SeenCount = 1
        };

        records.Add(record);
        await _store.WriteAsync(_idempotencyPath, records);
        return record;
    }
}
