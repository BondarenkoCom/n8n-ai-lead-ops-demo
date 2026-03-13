export type LeadSourceType = "website_form" | "email_inquiry" | "chat_simulation" | "approval_callback";

export type UrgencyLevel = "low" | "medium" | "high";

export type SentimentLabel = "positive" | "neutral" | "negative";

export type IntentLabel = "purchase" | "research" | "support" | "spam" | "unknown";

export type QualificationStatus = "qualified" | "nurture" | "manual_review" | "rejected" | "duplicate";

export type RecommendedRoute = "hot_lead_sales" | "nurture_queue" | "manual_review" | "rejected_or_spam";

export type LeadStatus = "new" | "qualified" | "nurture" | "manual_review" | "rejected" | "duplicate" | "handoff_requested";

export interface NormalizedLeadInput {
  received_at: string;
  source: LeadSourceType;
  source_label: string;
  full_name: string | null;
  company: string | null;
  email: string | null;
  phone: string | null;
  service_interest: string | null;
  estimated_budget: number | null;
  urgency: UrgencyLevel;
  geography: string | null;
  free_text_summary: string;
  raw_payload: Record<string, unknown>;
}

export interface LeadExtractionResult {
  full_name: string | null;
  company: string | null;
  email: string | null;
  phone: string | null;
  source: string;
  service_interest: string | null;
  estimated_budget: number | null;
  urgency: UrgencyLevel;
  geography: string | null;
  free_text_summary: string;
  sentiment: SentimentLabel;
  intent: IntentLabel;
}

export interface LeadScoringResult {
  lead_score: number;
  qualification_status: QualificationStatus;
  recommended_route: RecommendedRoute;
  confidence: number;
  risk_flags: string[];
}

export interface RuleEvaluationResult {
  lead_score: number;
  qualification_status: QualificationStatus;
  final_route: RecommendedRoute;
  confidence: number;
  risk_flags: string[];
  duplicate_match_count: number;
  duplicate_submission: boolean;
  requires_human_approval: boolean;
  should_notify_slack: boolean;
  should_create_reply_draft: boolean;
  should_handoff_booking: boolean;
}

export interface LeadRecord {
  id: string;
  created_at: string;
  updated_at: string;
  status: LeadStatus;
  owner: string;
  lead_score: number;
  route: RecommendedRoute;
  notes: string[];
  source_payload_snapshot: Record<string, unknown>;
  full_name: string | null;
  company: string | null;
  email: string | null;
  phone: string | null;
  source: string;
  service_interest: string | null;
  estimated_budget: number | null;
  urgency: UrgencyLevel;
  geography: string | null;
  free_text_summary: string;
  sentiment: SentimentLabel;
  intent: IntentLabel;
  confidence: number;
  risk_flags: string[];
  qualification_status: QualificationStatus;
}

export interface CrmUpsertPayload {
  lead: NormalizedLeadInput;
  extraction: LeadExtractionResult;
  scoring: LeadScoringResult;
  evaluation: RuleEvaluationResult;
}

export interface EmailDraft {
  draft_id: string;
  created_at: string;
  to: string | null;
  subject: string;
  body: string;
  mode: string;
  lead_id: string;
}

export interface BookingHandoffPayload {
  lead_id: string;
  requested_slot: string | null;
  route: RecommendedRoute;
  owner: string;
  notes: string;
}

export interface ApprovalPayload {
  lead_id: string;
  route: RecommendedRoute;
  email_draft: EmailDraft | null;
  booking_payload: BookingHandoffPayload | null;
  reason: string;
}

export interface ApprovalRequestRecord {
  approval_id: string;
  created_at: string;
  updated_at: string;
  status: "pending" | "approved" | "rejected";
  decision_at: string | null;
  route: RecommendedRoute;
  lead_id: string;
  reason: string;
  email_draft: EmailDraft | null;
  booking_payload: BookingHandoffPayload | null;
}

export interface AuditEvent {
  event_id: string;
  run_id: string;
  event_type: string;
  timestamp: string;
  lead_id: string | null;
  route: string | null;
  status: string;
  details: Record<string, unknown>;
}

export interface ErrorEvent {
  error_id: string;
  run_id: string;
  timestamp: string;
  stage: string;
  message: string;
  details: Record<string, unknown>;
}

export interface IdempotencyRecord {
  key: string;
  first_seen_at: string;
  last_seen_at: string;
  seen_count: number;
}

export interface SlackNotificationPayload {
  kind: "hot_lead_sales" | "manual_review" | "approval_required";
  lead_id: string;
  route: RecommendedRoute;
  full_name: string | null;
  company: string | null;
  service_interest: string | null;
  lead_score: number;
  urgency: UrgencyLevel;
  summary: string;
}

export interface LlmStepResult<T> {
  data: T;
  mode: "mock" | "live" | "fallback";
  fallback_used: boolean;
  provider: string;
  error: string | null;
}
