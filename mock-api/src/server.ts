import cors from "cors";
import express, { NextFunction, Request, Response } from "express";
import { randomUUID } from "node:crypto";
import { env } from "../../src/config/env.js";
import {
  ApprovalPayload,
  AuditEvent,
  BookingHandoffPayload,
  EmailDraft,
  ErrorEvent,
  LeadExtractionResult,
  LeadRecord,
  LeadScoringResult,
  NormalizedLeadInput,
  SlackNotificationPayload
} from "../../src/domain/lead.js";
import { createDeterministicHash } from "../../src/lib/hash.js";
import { ApprovalRepository } from "../../src/services/approval-repository.js";
import { AuditService } from "../../src/services/audit-service.js";
import { CrmRepository } from "../../src/services/crm-repository.js";
import { LlmService } from "../../src/services/llm-service.js";
import { NotificationService } from "../../src/services/notification-service.js";
import { buildReplyDraft, evaluateBusinessRules, normalizeInboundLead } from "../../src/services/pipeline-service.js";

const app = express();

app.use(cors());
app.use(express.json({ limit: "1mb" }));

const crmRepository = new CrmRepository(env.dataRoot);
const approvalRepository = new ApprovalRepository(env.dataRoot);
const auditService = new AuditService(env.dataRoot);
const notificationService = new NotificationService(env.dataRoot, {
  slackWebhookUrl: env.slackWebhookUrl,
  slackMode: env.slackMode,
  gmailMode: env.gmailMode
});
const llmService = new LlmService();

const asString = (value: unknown): string | null => {
  return typeof value === "string" && value.trim().length > 0 ? value.trim() : null;
};

const asBoolean = (value: unknown): boolean => {
  return value === true || value === "true";
};

const asNumber = (value: unknown, fallback = 0): number => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const buildSlackPayload = (lead: LeadRecord, kind: SlackNotificationPayload["kind"]): SlackNotificationPayload => {
  return {
    kind,
    lead_id: lead.id,
    route: lead.route,
    full_name: lead.full_name,
    company: lead.company,
    service_interest: lead.service_interest,
    lead_score: lead.lead_score,
    urgency: lead.urgency,
    summary: lead.free_text_summary
  };
};

const buildBookingPayload = (lead: LeadRecord): BookingHandoffPayload => {
  return {
    lead_id: lead.id,
    requested_slot: null,
    route: lead.route,
    owner: lead.owner,
    notes: `Route ${lead.route} generated a booking handoff request.`
  };
};

const buildApprovalPayload = (lead: LeadRecord, emailDraft: EmailDraft | null): ApprovalPayload => {
  return {
    lead_id: lead.id,
    route: lead.route,
    email_draft: emailDraft,
    booking_payload: lead.route === "hot_lead_sales" ? buildBookingPayload(lead) : null,
    reason:
      lead.route === "hot_lead_sales"
        ? "Qualified hot lead requires approval before outbound follow-up and booking handoff."
        : "Manual review route requires approval before any outbound follow-up."
  };
};

const asyncHandler =
  (handler: (request: Request, response: Response) => Promise<void>) =>
  async (request: Request, response: Response, next: NextFunction): Promise<void> => {
    try {
      await handler(request, response);
    } catch (error) {
      next(error);
    }
  };

app.get(
  "/health",
  asyncHandler(async (_request, response) => {
    response.json({
      ok: true,
      service: "mock-crm-api",
      llm_mode: env.openAiMode,
      slack_mode: env.slackMode,
      gmail_mode: env.gmailMode,
      approval_mode: env.humanApprovalMode
    });
  })
);

app.post(
  "/internal/normalize",
  asyncHandler(async (request, response) => {
    const body = request.body as { payload?: unknown; source?: string };
    const normalized = normalizeInboundLead(body.payload ?? body, body.source);
    response.json({
      ok: true,
      data: normalized
    });
  })
);

app.post(
  "/internal/idempotency/check",
  asyncHandler(async (request, response) => {
    const body = request.body as { key?: unknown; lead?: unknown };
    const key =
      asString(body.key) ??
      createDeterministicHash(body.lead ?? {
        payload: request.body
      });
    const record = await auditService.registerSubmissionKey(key);
    response.json({
      ok: true,
      data: {
        ...record,
        duplicate_submission: record.seen_count > 1
      }
    });
  })
);

app.post(
  "/internal/llm/extract",
  asyncHandler(async (request, response) => {
    const lead = request.body.lead as NormalizedLeadInput;
    const result = await llmService.extractLead(lead);
    response.json({
      ok: true,
      ...result
    });
  })
);

app.post(
  "/internal/llm/score",
  asyncHandler(async (request, response) => {
    const lead = request.body.lead as NormalizedLeadInput;
    const extraction = request.body.extraction as LeadExtractionResult;
    const result = await llmService.scoreLead(lead, extraction);
    response.json({
      ok: true,
      ...result
    });
  })
);

app.post(
  "/internal/rules/evaluate",
  asyncHandler(async (request, response) => {
    const evaluation = evaluateBusinessRules({
      lead: request.body.lead as NormalizedLeadInput,
      extraction: request.body.extraction as LeadExtractionResult,
      scoring: request.body.scoring as LeadScoringResult,
      duplicateMatchCount: asNumber(request.body.duplicate_match_count),
      duplicateSubmission: asBoolean(request.body.duplicate_submission),
      budgetMinQualified: asNumber(request.body.budget_min_qualified, env.budgetMinQualified),
      humanApprovalMode: asString(request.body.human_approval_mode) ?? env.humanApprovalMode
    });

    response.json({
      ok: true,
      data: evaluation
    });
  })
);

app.post(
  "/internal/slack/notify",
  asyncHandler(async (request, response) => {
    const leadId = asString(request.body.lead_id);
    if (!leadId) {
      response.status(400).json({ ok: false, error: "lead_id is required" });
      return;
    }

    const lead = await crmRepository.getById(leadId);
    if (!lead) {
      response.status(404).json({ ok: false, error: "lead not found" });
      return;
    }

    const kind = (request.body.kind as SlackNotificationPayload["kind"]) ?? "manual_review";
    const result = await notificationService.sendSlackNotification(buildSlackPayload(lead, kind));
    response.json({
      ok: true,
      data: result
    });
  })
);

app.post(
  "/internal/email/draft",
  asyncHandler(async (request, response) => {
    const leadId = asString(request.body.lead_id);
    if (!leadId) {
      response.status(400).json({ ok: false, error: "lead_id is required" });
      return;
    }

    const lead = await crmRepository.getById(leadId);
    if (!lead) {
      response.status(404).json({ ok: false, error: "lead not found" });
      return;
    }

    const providedDraft = request.body.draft as EmailDraft | undefined;
    const draft = providedDraft ?? buildReplyDraft(lead);
    const delivery = await notificationService.saveEmailDraft(draft);
    response.json({
      ok: true,
      data: draft,
      delivery
    });
  })
);

app.post(
  "/internal/audit/log",
  asyncHandler(async (request, response) => {
    const event = (request.body.event ?? request.body) as AuditEvent;
    await auditService.writeAuditEvent(event);
    response.json({
      ok: true
    });
  })
);

app.post(
  "/internal/errors/log",
  asyncHandler(async (request, response) => {
    const event = (request.body.event ?? request.body) as ErrorEvent;
    await auditService.writeErrorEvent(event);
    response.json({
      ok: true
    });
  })
);

app.post(
  "/internal/approvals/request",
  asyncHandler(async (request, response) => {
    const leadId = asString(request.body.lead_id);
    if (!leadId) {
      response.status(400).json({ ok: false, error: "lead_id is required" });
      return;
    }

    const lead = await crmRepository.getById(leadId);
    if (!lead) {
      response.status(404).json({ ok: false, error: "lead not found" });
      return;
    }

    const draft = request.body.email_draft ? (request.body.email_draft as EmailDraft) : buildReplyDraft(lead);
    const approval = await approvalRepository.create(buildApprovalPayload(lead, draft));
    response.json({
      ok: true,
      data: {
        ...approval,
        callback_url: env.approvalCallbackUrl
      }
    });
  })
);

app.get(
  "/internal/approvals/:id",
  asyncHandler(async (request, response) => {
    const approval = await approvalRepository.get(request.params.id);
    if (!approval) {
      response.status(404).json({ ok: false, error: "approval not found" });
      return;
    }

    response.json({
      ok: true,
      data: approval
    });
  })
);

app.post(
  "/internal/approvals/resolve",
  asyncHandler(async (request, response) => {
    const approvalId = asString(request.body.approval_id);
    const decision = asString(request.body.decision);

    if (!approvalId || (decision !== "approved" && decision !== "rejected")) {
      response.status(400).json({ ok: false, error: "approval_id and decision are required" });
      return;
    }

    const approval = await approvalRepository.resolve(approvalId, decision);
    if (!approval) {
      response.status(404).json({ ok: false, error: "approval not found" });
      return;
    }

    response.json({
      ok: true,
      data: approval
    });
  })
);

app.get(
  "/leads",
  asyncHandler(async (request, response) => {
    const leads = await crmRepository.list({
      email: asString(request.query.email),
      phone: asString(request.query.phone)
    });
    response.json({
      ok: true,
      data: leads
    });
  })
);

app.get(
  "/leads/:id",
  asyncHandler(async (request, response) => {
    const lead = await crmRepository.getById(request.params.id);
    if (!lead) {
      response.status(404).json({ ok: false, error: "lead not found" });
      return;
    }

    response.json({
      ok: true,
      data: lead
    });
  })
);

app.post(
  "/leads/upsert",
  asyncHandler(async (request, response) => {
    const result = await crmRepository.upsert(request.body);
    response.json({
      ok: true,
      data: result
    });
  })
);

app.post(
  "/booking/handoff",
  asyncHandler(async (request, response) => {
    const payload = request.body as BookingHandoffPayload;
    await notificationService.recordBookingHandoff(payload);
    const updatedLead = await crmRepository.updateStatus(
      payload.lead_id,
      "handoff_requested",
      "Booking handoff recorded"
    );

    response.json({
      ok: true,
      data: {
        handoff_id: `handoff_${randomUUID()}`,
        handoff: payload,
        lead: updatedLead
      }
    });
  })
);

app.use((error: unknown, request: Request, response: Response, _next: NextFunction) => {
  const event: ErrorEvent = {
    error_id: `err_${randomUUID()}`,
    run_id: asString(request.headers["x-run-id"]) ?? `run_${randomUUID()}`,
    timestamp: new Date().toISOString(),
    stage: request.path,
    message: error instanceof Error ? error.message : "Unknown server error",
    details: {
      method: request.method
    }
  };

  auditService.writeErrorEvent(event).catch(() => undefined);

  response.status(500).json({
    ok: false,
    error: event.message,
    error_id: event.error_id
  });
});

app.listen(env.port, () => {
  const startupEvent: AuditEvent = {
    event_id: `audit_${randomUUID()}`,
    run_id: `run_${randomUUID()}`,
    event_type: "service_started",
    timestamp: new Date().toISOString(),
    lead_id: null,
    route: null,
    status: "ready",
    details: {
      port: env.port,
      data_root: env.dataRoot
    }
  };

  auditService.writeAuditEvent(startupEvent).catch(() => undefined);
  process.stdout.write(`mock-api listening on ${env.port}\n`);
});
