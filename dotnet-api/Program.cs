using Microsoft.AspNetCore.Http.Json;
using N8nAiLeadOps.DemoApi.Infrastructure;
using N8nAiLeadOps.DemoApi.Models;
using N8nAiLeadOps.DemoApi.Services;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
var options = AppOptions.Load(builder.Configuration, builder.Environment.ContentRootPath);

builder.WebHost.UseUrls($"http://0.0.0.0:{options.Port}");
builder.Services.Configure<JsonOptions>(jsonOptions => AppJson.Configure(jsonOptions.SerializerOptions));
builder.Services.AddSingleton(options);
builder.Services.AddHttpClient();
builder.Services.AddSingleton<JsonFileStore>();
builder.Services.AddSingleton<LeadPipelineService>();
builder.Services.AddSingleton<CrmRepository>();
builder.Services.AddSingleton<ApprovalRepository>();
builder.Services.AddSingleton<AuditService>();
builder.Services.AddSingleton<NotificationService>();
builder.Services.AddSingleton<LlmService>();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception exception)
    {
        var auditService = context.RequestServices.GetRequiredService<AuditService>();
        var runId = context.Request.Headers["x-run-id"].FirstOrDefault();
        var errorEvent = new ErrorEvent
        {
            ErrorId = $"err_{Guid.NewGuid()}",
            RunId = string.IsNullOrWhiteSpace(runId) ? $"run_{Guid.NewGuid()}" : runId,
            Timestamp = SystemClock.UtcNow(),
            Stage = context.Request.Path,
            Message = exception.Message,
            Details = new Dictionary<string, object?>
            {
                ["method"] = context.Request.Method
            }
        };

        try
        {
            await auditService.WriteErrorEventAsync(errorEvent);
        }
        catch
        {
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            Ok = false,
            Error = errorEvent.Message,
            ErrorId = errorEvent.ErrorId
        }, AppJson.Default);
    }
});

app.MapGet("/health", () => Results.Ok(new
{
    Ok = true,
    Service = "dotnet-lead-ops-api",
    LlmMode = options.OpenAiMode,
    SlackMode = options.SlackMode,
    GmailMode = options.GmailMode,
    ApprovalMode = options.HumanApprovalMode
}));

app.MapPost("/internal/normalize", async (HttpRequest request, LeadPipelineService pipeline) =>
{
    var body = await request.ReadFromJsonAsync<JsonObject>(AppJson.Default) ?? new JsonObject();
    var source = body["source"].GetTrimmedString();
    var payload = body["payload"] is JsonObject payloadObject ? payloadObject.DeepCloneObject() : new JsonObject();
    if (payload.Count == 0)
    {
        payload = body;
    }

    var normalized = pipeline.NormalizeInboundLead(payload, source);
    return Results.Ok(new
    {
        Ok = true,
        Data = normalized
    });
});

app.MapPost("/internal/idempotency/check", async (IdempotencyCheckRequest body, AuditService auditService) =>
{
    var key = string.IsNullOrWhiteSpace(body.Key)
        ? Hashing.CreateDeterministicHash(body.Lead ?? new JsonObject())
        : body.Key.Trim();

    var record = await auditService.RegisterSubmissionKeyAsync(key);
    return Results.Ok(new
    {
        Ok = true,
        Data = new
        {
            record.Key,
            record.FirstSeenAt,
            record.LastSeenAt,
            record.SeenCount,
            DuplicateSubmission = record.SeenCount > 1
        }
    });
});

app.MapPost("/internal/llm/extract", async (LlmExtractRequest body, LlmService llmService) =>
{
    var result = await llmService.ExtractLeadAsync(body.Lead);
    return Results.Ok(new
    {
        Ok = true,
        result.Data,
        result.Mode,
        result.FallbackUsed,
        result.Provider,
        result.Error
    });
});

app.MapPost("/internal/llm/score", async (LlmScoreRequest body, LlmService llmService) =>
{
    var result = await llmService.ScoreLeadAsync(body.Lead, body.Extraction);
    return Results.Ok(new
    {
        Ok = true,
        result.Data,
        result.Mode,
        result.FallbackUsed,
        result.Provider,
        result.Error
    });
});

app.MapPost("/internal/rules/evaluate", (RulesEvaluateRequest body, LeadPipelineService pipeline) =>
{
    var evaluation = pipeline.EvaluateBusinessRules(new RuleEvaluationInput
    {
        Lead = body.Lead,
        Extraction = body.Extraction,
        Scoring = body.Scoring,
        DuplicateMatchCount = body.DuplicateMatchCount,
        DuplicateSubmission = body.DuplicateSubmission,
        BudgetMinQualified = body.BudgetMinQualified ?? options.BudgetMinQualified,
        HumanApprovalMode = string.IsNullOrWhiteSpace(body.HumanApprovalMode) ? options.HumanApprovalMode : body.HumanApprovalMode
    });

    return Results.Ok(new
    {
        Ok = true,
        Data = evaluation
    });
});

app.MapPost("/internal/slack/notify", async (SlackNotificationRequest body, CrmRepository crmRepository, NotificationService notificationService) =>
{
    if (string.IsNullOrWhiteSpace(body.LeadId))
    {
        return Results.BadRequest(new
        {
            Ok = false,
            Error = "lead_id is required"
        });
    }

    var lead = await crmRepository.GetByIdAsync(body.LeadId);
    if (lead is null)
    {
        return Results.NotFound(new
        {
            Ok = false,
            Error = "lead not found"
        });
    }

    var result = await notificationService.SendSlackNotificationAsync(BuildSlackPayload(lead, body.Kind));
    return Results.Ok(new
    {
        Ok = true,
        Data = result
    });
});

app.MapPost("/internal/email/draft", async (EmailDraftRequest body, CrmRepository crmRepository, NotificationService notificationService, LeadPipelineService pipeline) =>
{
    if (string.IsNullOrWhiteSpace(body.LeadId))
    {
        return Results.BadRequest(new
        {
            Ok = false,
            Error = "lead_id is required"
        });
    }

    var lead = await crmRepository.GetByIdAsync(body.LeadId);
    if (lead is null)
    {
        return Results.NotFound(new
        {
            Ok = false,
            Error = "lead not found"
        });
    }

    var draft = body.Draft ?? pipeline.BuildReplyDraft(lead);
    var delivery = await notificationService.SaveEmailDraftAsync(draft);
    return Results.Ok(new
    {
        Ok = true,
        Data = draft,
        Delivery = delivery
    });
});

app.MapPost("/internal/audit/log", async (HttpRequest request, AuditService auditService) =>
{
    var body = await request.ReadFromJsonAsync<JsonObject>(AppJson.Default) ?? new JsonObject();
    var auditNode = body["event"] ?? body;
    var auditEvent = auditNode.DeserializeNode<AuditEvent>() ?? new AuditEvent();
    await auditService.WriteAuditEventAsync(auditEvent);
    return Results.Ok(new
    {
        Ok = true
    });
});

app.MapPost("/internal/errors/log", async (HttpRequest request, AuditService auditService) =>
{
    var body = await request.ReadFromJsonAsync<JsonObject>(AppJson.Default) ?? new JsonObject();
    var errorNode = body["event"] ?? body;
    var errorEvent = errorNode.DeserializeNode<ErrorEvent>() ?? new ErrorEvent();
    await auditService.WriteErrorEventAsync(errorEvent);
    return Results.Ok(new
    {
        Ok = true
    });
});

app.MapPost("/internal/approvals/request", async (ApprovalRequestCommand body, CrmRepository crmRepository, ApprovalRepository approvalRepository, LeadPipelineService pipeline) =>
{
    if (string.IsNullOrWhiteSpace(body.LeadId))
    {
        return Results.BadRequest(new
        {
            Ok = false,
            Error = "lead_id is required"
        });
    }

    var lead = await crmRepository.GetByIdAsync(body.LeadId);
    if (lead is null)
    {
        return Results.NotFound(new
        {
            Ok = false,
            Error = "lead not found"
        });
    }

    var draft = body.EmailDraft ?? pipeline.BuildReplyDraft(lead);
    var approval = await approvalRepository.CreateAsync(BuildApprovalPayload(lead, draft));
    return Results.Ok(new
    {
        Ok = true,
        Data = new
        {
            approval.ApprovalId,
            approval.CreatedAt,
            approval.UpdatedAt,
            approval.Status,
            approval.DecisionAt,
            approval.Route,
            approval.LeadId,
            approval.Reason,
            approval.EmailDraft,
            approval.BookingPayload,
            CallbackUrl = options.ApprovalCallbackUrl
        }
    });
});

app.MapGet("/internal/approvals/{id}", async (string id, ApprovalRepository approvalRepository) =>
{
    var approval = await approvalRepository.GetAsync(id);
    return approval is null
        ? Results.NotFound(new
        {
            Ok = false,
            Error = "approval not found"
        })
        : Results.Ok(new
        {
            Ok = true,
            Data = approval
        });
});

app.MapPost("/internal/approvals/resolve", async (ApprovalResolveRequest body, ApprovalRepository approvalRepository) =>
{
    if (string.IsNullOrWhiteSpace(body.ApprovalId) || (body.Decision != "approved" && body.Decision != "rejected"))
    {
        return Results.BadRequest(new
        {
            Ok = false,
            Error = "approval_id and decision are required"
        });
    }

    var approval = await approvalRepository.ResolveAsync(body.ApprovalId, body.Decision);
    return approval is null
        ? Results.NotFound(new
        {
            Ok = false,
            Error = "approval not found"
        })
        : Results.Ok(new
        {
            Ok = true,
            Data = approval
        });
});

app.MapGet("/leads", async (string? email, string? phone, CrmRepository crmRepository) =>
{
    var leads = await crmRepository.ListAsync(email, phone);
    return Results.Ok(new
    {
        Ok = true,
        Data = leads
    });
});

app.MapGet("/leads/{id}", async (string id, CrmRepository crmRepository) =>
{
    var lead = await crmRepository.GetByIdAsync(id);
    return lead is null
        ? Results.NotFound(new
        {
            Ok = false,
            Error = "lead not found"
        })
        : Results.Ok(new
        {
            Ok = true,
            Data = lead
        });
});

app.MapPost("/leads/upsert", async (CrmUpsertPayload body, CrmRepository crmRepository) =>
{
    var result = await crmRepository.UpsertAsync(body);
    return Results.Ok(new
    {
        Ok = true,
        Data = result
    });
});

app.MapPost("/booking/handoff", async (BookingHandoffPayload body, NotificationService notificationService, CrmRepository crmRepository) =>
{
    await notificationService.RecordBookingHandoffAsync(body);
    var updatedLead = await crmRepository.UpdateStatusAsync(body.LeadId, LeadStatus.HandoffRequested, "Booking handoff recorded");
    return Results.Ok(new
    {
        Ok = true,
        Data = new
        {
            HandoffId = $"handoff_{Guid.NewGuid()}",
            Handoff = body,
            Lead = updatedLead
        }
    });
});

var startupAudit = new AuditEvent
{
    EventId = $"audit_{Guid.NewGuid()}",
    RunId = $"run_{Guid.NewGuid()}",
    EventType = "service_started",
    Timestamp = SystemClock.UtcNow(),
    LeadId = null,
    Route = null,
    Status = "ready",
    Details = new Dictionary<string, object?>
    {
        ["port"] = options.Port,
        ["data_root"] = options.DataRoot
    }
};

await app.Services.GetRequiredService<AuditService>().WriteAuditEventAsync(startupAudit);
await app.RunAsync();

static SlackNotificationPayload BuildSlackPayload(LeadRecord lead, SlackNotificationKind kind)
{
    return new SlackNotificationPayload
    {
        Kind = kind,
        LeadId = lead.Id,
        Route = lead.Route,
        FullName = lead.FullName,
        Company = lead.Company,
        ServiceInterest = lead.ServiceInterest,
        LeadScore = lead.LeadScore,
        Urgency = lead.Urgency,
        Summary = lead.FreeTextSummary
    };
}

static BookingHandoffPayload BuildBookingPayload(LeadRecord lead)
{
    return new BookingHandoffPayload
    {
        LeadId = lead.Id,
        RequestedSlot = null,
        Route = lead.Route,
        Owner = lead.Owner,
        Notes = $"Route {lead.Route} generated a booking handoff request."
    };
}

static ApprovalPayload BuildApprovalPayload(LeadRecord lead, EmailDraft? emailDraft)
{
    return new ApprovalPayload
    {
        LeadId = lead.Id,
        Route = lead.Route,
        EmailDraft = emailDraft,
        BookingPayload = lead.Route == RecommendedRoute.HotLeadSales ? BuildBookingPayload(lead) : null,
        Reason = lead.Route == RecommendedRoute.HotLeadSales
            ? "Qualified hot lead requires approval before outbound follow-up and booking handoff."
            : "Manual review route requires approval before any outbound follow-up."
    };
}
