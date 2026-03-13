import { randomUUID } from "node:crypto";
import {
  EmailDraft,
  LeadExtractionResult,
  LeadRecord,
  LeadScoringResult,
  LeadSourceType,
  NormalizedLeadInput,
  RuleEvaluationResult
} from "../domain/lead.js";

const asRecord = (value: unknown): Record<string, unknown> => {
  if (value && typeof value === "object" && !Array.isArray(value)) {
    return value as Record<string, unknown>;
  }
  return {};
};

const asString = (value: unknown): string | null => {
  if (typeof value !== "string") {
    return null;
  }
  const normalized = value.trim();
  return normalized.length > 0 ? normalized : null;
};

const asNumber = (value: unknown): number | null => {
  if (typeof value === "number" && Number.isFinite(value)) {
    return value;
  }
  if (typeof value === "string") {
    const digits = value.replace(/[^0-9.]/g, "");
    const parsed = Number(digits);
    return Number.isFinite(parsed) ? parsed : null;
  }
  return null;
};

const pickFirstString = (values: unknown[]): string | null => {
  for (const value of values) {
    const normalized = asString(value);
    if (normalized) {
      return normalized;
    }
  }
  return null;
};

const detectSource = (payload: Record<string, unknown>, sourceHint?: string): LeadSourceType => {
  if (sourceHint === "email_inquiry" || "from" in payload || "subject" in payload) {
    return "email_inquiry";
  }
  if (sourceHint === "chat_simulation" || "channel" in payload || "contact" in payload) {
    return "chat_simulation";
  }
  if (sourceHint === "approval_callback") {
    return "approval_callback";
  }
  return "website_form";
};

const toSourceLabel = (source: LeadSourceType): string => {
  const labels: Record<LeadSourceType, string> = {
    website_form: "Website form",
    email_inquiry: "Email inquiry",
    chat_simulation: "Chat simulation",
    approval_callback: "Approval callback"
  };
  return labels[source];
};

const buildSummary = (values: Array<string | null>): string => {
  const summary = values.filter((value): value is string => Boolean(value)).join(" | ").trim();
  return summary.length > 0 ? summary : "Inbound lead received without descriptive text.";
};

export const normalizeInboundLead = (payload: unknown, sourceHint?: string): NormalizedLeadInput => {
  const record = asRecord(payload);
  const from = asRecord(record.from);
  const contact = asRecord(record.contact);
  const metadata = asRecord(record.metadata);
  const source = detectSource(record, sourceHint);

  const freeTextSummary = buildSummary([
    pickFirstString([record.message, record.body, record.notes, record.inquiry]),
    pickFirstString([record.subject, metadata.campaign, record.service, record.topic])
  ]);

  return {
    received_at: new Date().toISOString(),
    source,
    source_label: toSourceLabel(source),
    full_name: pickFirstString([record.full_name, record.name, from.name, contact.name]),
    company: pickFirstString([record.company, record.organization, contact.company]),
    email: pickFirstString([record.email, from.email, contact.email]),
    phone: pickFirstString([record.phone, contact.phone, record.mobile]),
    service_interest: pickFirstString([record.service_interest, record.service, record.topic]),
    estimated_budget: asNumber(record.estimated_budget ?? record.budget ?? metadata.budget),
    urgency:
      (pickFirstString([record.urgency, metadata.urgency])?.toLowerCase() as
        | "low"
        | "medium"
        | "high"
        | undefined) ?? "low",
    geography: pickFirstString([record.geography, record.location, record.country, contact.location]),
    free_text_summary: freeTextSummary,
    raw_payload: record
  };
};

const isSpam = (extraction: LeadExtractionResult): boolean => {
  const value = extraction.free_text_summary.toLowerCase();
  return (
    extraction.intent === "spam" ||
    /(casino|buy email list|guest post|backlink|forex|seo agency package)/.test(value)
  );
};

export const evaluateBusinessRules = (input: {
  lead: NormalizedLeadInput;
  extraction: LeadExtractionResult;
  scoring: LeadScoringResult;
  duplicateMatchCount: number;
  duplicateSubmission: boolean;
  budgetMinQualified: number;
  humanApprovalMode: string;
}): RuleEvaluationResult => {
  const riskFlags = new Set<string>(input.scoring.risk_flags);
  let leadScore = input.scoring.lead_score;
  let qualificationStatus = input.scoring.qualification_status;
  let finalRoute = input.scoring.recommended_route;
  let confidence = input.scoring.confidence;

  if (isSpam(input.extraction)) {
    finalRoute = "rejected_or_spam";
    qualificationStatus = "rejected";
    leadScore = Math.min(leadScore, 10);
    riskFlags.add("spam_filtered");
  }

  if (!input.extraction.email && !input.extraction.phone) {
    finalRoute = "manual_review";
    qualificationStatus = "manual_review";
    confidence = Math.min(confidence, 0.52);
    riskFlags.add("missing_required_contact");
  }

  if (!input.extraction.service_interest) {
    finalRoute = "manual_review";
    qualificationStatus = "manual_review";
    confidence = Math.min(confidence, 0.58);
    riskFlags.add("service_interest_missing");
  }

  if (
    input.extraction.estimated_budget !== null &&
    input.extraction.estimated_budget < input.budgetMinQualified &&
    finalRoute !== "rejected_or_spam"
  ) {
    finalRoute = "nurture_queue";
    if (qualificationStatus !== "rejected") {
      qualificationStatus = "nurture";
    }
    riskFlags.add("budget_below_threshold");
  }

  if (
    input.extraction.urgency === "high" &&
    input.extraction.estimated_budget !== null &&
    input.extraction.estimated_budget >= input.budgetMinQualified &&
    finalRoute !== "rejected_or_spam"
  ) {
    finalRoute = "hot_lead_sales";
    qualificationStatus = "qualified";
    riskFlags.add("urgency_escalation");
  }

  if (input.duplicateMatchCount > 0) {
    finalRoute = "manual_review";
    qualificationStatus = "duplicate";
    confidence = Math.min(confidence, 0.64);
    riskFlags.add("duplicate_contact_match");
  }

  if (input.duplicateSubmission) {
    finalRoute = "manual_review";
    qualificationStatus = "duplicate";
    confidence = Math.min(confidence, 0.6);
    riskFlags.add("duplicate_submission_key");
  }

  const requiresHumanApproval =
    input.humanApprovalMode === "always" ||
    (input.humanApprovalMode === "conditional" &&
      (finalRoute === "hot_lead_sales" || finalRoute === "manual_review"));

  return {
    lead_score: Math.max(0, Math.min(100, Math.round(leadScore))),
    qualification_status: qualificationStatus,
    final_route: finalRoute,
    confidence: Number(confidence.toFixed(2)),
    risk_flags: Array.from(riskFlags),
    duplicate_match_count: input.duplicateMatchCount,
    duplicate_submission: input.duplicateSubmission,
    requires_human_approval: requiresHumanApproval,
    should_notify_slack: finalRoute === "hot_lead_sales" || finalRoute === "manual_review",
    should_create_reply_draft: finalRoute !== "rejected_or_spam",
    should_handoff_booking: finalRoute === "hot_lead_sales" && qualificationStatus === "qualified"
  };
};

export const buildReplyDraft = (lead: LeadRecord): EmailDraft => {
  const firstName = lead.full_name?.split(" ")[0] ?? "there";
  const serviceLine = lead.service_interest ?? "your requested service";
  const subject =
    lead.route === "hot_lead_sales"
      ? `Next steps for ${serviceLine}`
      : `Follow-up on your ${serviceLine} inquiry`;

  const body =
    lead.route === "hot_lead_sales"
      ? `Hi ${firstName},\n\nThanks for reaching out about ${serviceLine}. We reviewed your inquiry and can move this into a short discovery call. If the timeline still stands, reply with two preferred time windows and any relevant constraints.\n\nBest,\nBluePeak Ops`
      : `Hi ${firstName},\n\nThanks for sharing the details on ${serviceLine}. We reviewed the request and can outline a practical next step once we confirm scope, timeline, and fit. Reply with any missing context that would help sharpen the estimate.\n\nBest,\nBluePeak Ops`;

  return {
    draft_id: `draft_${randomUUID()}`,
    created_at: new Date().toISOString(),
    to: lead.email,
    subject,
    body,
    mode: "mock",
    lead_id: lead.id
  };
};
