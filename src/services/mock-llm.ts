import {
  IntentLabel,
  LeadExtractionResult,
  LeadScoringResult,
  NormalizedLeadInput,
  QualificationStatus,
  RecommendedRoute,
  SentimentLabel,
  UrgencyLevel
} from "../domain/lead.js";

const budgetPattern = /(?:\$|usd|budget|investment|around|approx(?:imately)?)\s*([0-9]{1,3}(?:[,\s][0-9]{3})+|[0-9]{3,6})/i;

const stripBudgetValue = (value: string): number | null => {
  const digits = value.replace(/[^0-9]/g, "");
  if (!digits) {
    return null;
  }

  const parsed = Number(digits);
  return Number.isFinite(parsed) ? parsed : null;
};

export const inferBudgetFromText = (text: string): number | null => {
  const match = text.match(budgetPattern);
  return match?.[1] ? stripBudgetValue(match[1]) : null;
};

export const inferUrgencyFromText = (text: string): UrgencyLevel => {
  const value = text.toLowerCase();
  if (/(asap|urgent|this week|today|immediately)/.test(value)) {
    return "high";
  }
  if (/(next month|soon|timeline|q[1-4]|this month)/.test(value)) {
    return "medium";
  }
  return "low";
};

export const inferSentiment = (text: string): SentimentLabel => {
  const value = text.toLowerCase();
  if (/(frustrated|blocked|painful|urgent|need help)/.test(value)) {
    return "negative";
  }
  if (/(excited|interested|ready|looking forward|great fit)/.test(value)) {
    return "positive";
  }
  return "neutral";
};

export const inferIntent = (text: string): IntentLabel => {
  const value = text.toLowerCase();
  if (/(casino|forex|seo package|buy lists|guest post|backlink)/.test(value)) {
    return "spam";
  }
  if (/(proposal|quote|book|demo|ready to start|need a partner|engage)/.test(value)) {
    return "purchase";
  }
  if (/(exploring|research|considering|curious|evaluate)/.test(value)) {
    return "research";
  }
  if (/(issue|support|problem|bug|outage)/.test(value)) {
    return "support";
  }
  return "unknown";
};

export const inferServiceInterest = (text: string, fallback: string | null): string | null => {
  if (fallback) {
    return fallback;
  }

  const value = text.toLowerCase();
  if (/(lead|qualification|crm|sales ops)/.test(value)) {
    return "Lead qualification automation";
  }
  if (/(ai|llm|assistant|copilot)/.test(value)) {
    return "AI workflow implementation";
  }
  if (/(booking|calendar|appointment)/.test(value)) {
    return "Booking orchestration";
  }
  if (/(integration|api|sync)/.test(value)) {
    return "Systems integration";
  }
  return null;
};

export const summarizeText = (text: string): string => {
  const cleaned = text.replace(/\s+/g, " ").trim();
  if (cleaned.length <= 220) {
    return cleaned;
  }
  return `${cleaned.slice(0, 217)}...`;
};

const inferRoute = (score: number, confidence: number, status: QualificationStatus): RecommendedRoute => {
  if (status === "rejected") {
    return "rejected_or_spam";
  }
  if (status === "duplicate" || confidence < 0.6) {
    return "manual_review";
  }
  if (score >= 75) {
    return "hot_lead_sales";
  }
  if (score >= 40) {
    return "nurture_queue";
  }
  return "manual_review";
};

export const buildMockExtraction = (lead: NormalizedLeadInput): LeadExtractionResult => {
  const text = lead.free_text_summary;
  return {
    full_name: lead.full_name,
    company: lead.company,
    email: lead.email,
    phone: lead.phone,
    source: lead.source_label,
    service_interest: inferServiceInterest(text, lead.service_interest),
    estimated_budget: lead.estimated_budget ?? inferBudgetFromText(text),
    urgency: lead.urgency === "low" ? inferUrgencyFromText(text) : lead.urgency,
    geography: lead.geography,
    free_text_summary: summarizeText(text),
    sentiment: inferSentiment(text),
    intent: inferIntent(text)
  };
};

export const buildMockScoring = (
  lead: NormalizedLeadInput,
  extraction: LeadExtractionResult
): LeadScoringResult => {
  let score = 35;
  const flags = new Set<string>();

  if (extraction.intent === "purchase") {
    score += 26;
  } else if (extraction.intent === "research") {
    score += 12;
  } else if (extraction.intent === "support") {
    score += 4;
  } else if (extraction.intent === "spam") {
    score -= 55;
    flags.add("spam_keywords");
  } else {
    flags.add("intent_unclear");
  }

  if (extraction.estimated_budget !== null) {
    if (extraction.estimated_budget >= 15000) {
      score += 20;
    } else if (extraction.estimated_budget >= 5000) {
      score += 12;
    } else if (extraction.estimated_budget < 1500) {
      score -= 18;
      flags.add("budget_low");
    }
  } else {
    flags.add("budget_missing");
  }

  if (extraction.urgency === "high") {
    score += 10;
  } else if (extraction.urgency === "medium") {
    score += 5;
  }

  if (extraction.company) {
    score += 5;
  } else {
    flags.add("company_missing");
  }

  if (extraction.email || extraction.phone) {
    score += 6;
  } else {
    score -= 18;
    flags.add("contact_missing");
  }

  if (extraction.sentiment === "positive") {
    score += 5;
  }

  if (lead.source === "website_form") {
    score += 3;
  }

  score = Math.max(0, Math.min(100, score));

  const completenessParts = [
    extraction.full_name,
    extraction.company,
    extraction.email,
    extraction.phone,
    extraction.service_interest,
    extraction.geography
  ].filter((value) => Boolean(value)).length;

  let confidence = 0.42 + completenessParts * 0.07;
  if (extraction.intent === "purchase") {
    confidence += 0.12;
  }
  if (extraction.intent === "unknown") {
    confidence -= 0.08;
  }
  if (flags.has("contact_missing")) {
    confidence -= 0.12;
  }
  confidence = Math.max(0.3, Math.min(0.97, Number(confidence.toFixed(2))));

  let qualificationStatus: QualificationStatus;
  if (extraction.intent === "spam" || score < 20) {
    qualificationStatus = "rejected";
  } else if (score >= 75 && confidence >= 0.65) {
    qualificationStatus = "qualified";
  } else if (score >= 40 && confidence >= 0.58) {
    qualificationStatus = "nurture";
  } else {
    qualificationStatus = "manual_review";
  }

  const recommendedRoute = inferRoute(score, confidence, qualificationStatus);

  return {
    lead_score: score,
    qualification_status: qualificationStatus,
    recommended_route: recommendedRoute,
    confidence,
    risk_flags: Array.from(flags)
  };
};
