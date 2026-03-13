import { env } from "../config/env.js";
import {
  LeadExtractionResult,
  LeadScoringResult,
  LlmStepResult,
  NormalizedLeadInput
} from "../domain/lead.js";
import { buildMockExtraction, buildMockScoring } from "./mock-llm.js";

const parseJsonMessage = (content: unknown): Record<string, unknown> => {
  if (typeof content !== "string") {
    return {};
  }

  try {
    return JSON.parse(content) as Record<string, unknown>;
  } catch {
    return {};
  }
};

const callChatCompletion = async (
  systemPrompt: string,
  userPayload: Record<string, unknown>
): Promise<Record<string, unknown>> => {
  const response = await fetch(`${env.openAiBaseUrl.replace(/\/$/, "")}/chat/completions`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${env.openAiApiKey}`
    },
    body: JSON.stringify({
      model: env.openAiModel,
      temperature: 0.1,
      response_format: {
        type: "json_object"
      },
      messages: [
        {
          role: "system",
          content: systemPrompt
        },
        {
          role: "user",
          content: JSON.stringify(userPayload)
        }
      ]
    })
  });

  if (!response.ok) {
    throw new Error(`LLM request failed with status ${response.status}`);
  }

  const body = (await response.json()) as {
    choices?: Array<{ message?: { content?: string } }>;
  };

  return parseJsonMessage(body.choices?.[0]?.message?.content);
};

const asUrgency = (value: unknown): "low" | "medium" | "high" => {
  return value === "high" || value === "medium" ? value : "low";
};

const asExtraction = (fallback: LeadExtractionResult, payload: Record<string, unknown>): LeadExtractionResult => {
  return {
    full_name: typeof payload.full_name === "string" ? payload.full_name : fallback.full_name,
    company: typeof payload.company === "string" ? payload.company : fallback.company,
    email: typeof payload.email === "string" ? payload.email : fallback.email,
    phone: typeof payload.phone === "string" ? payload.phone : fallback.phone,
    source: typeof payload.source === "string" ? payload.source : fallback.source,
    service_interest:
      typeof payload.service_interest === "string" ? payload.service_interest : fallback.service_interest,
    estimated_budget:
      typeof payload.estimated_budget === "number" ? payload.estimated_budget : fallback.estimated_budget,
    urgency: asUrgency(payload.urgency),
    geography: typeof payload.geography === "string" ? payload.geography : fallback.geography,
    free_text_summary:
      typeof payload.free_text_summary === "string" ? payload.free_text_summary : fallback.free_text_summary,
    sentiment:
      payload.sentiment === "positive" || payload.sentiment === "negative" ? payload.sentiment : fallback.sentiment,
    intent:
      payload.intent === "purchase" ||
      payload.intent === "research" ||
      payload.intent === "support" ||
      payload.intent === "spam"
        ? payload.intent
        : fallback.intent
  };
};

const asScoring = (fallback: LeadScoringResult, payload: Record<string, unknown>): LeadScoringResult => {
  return {
    lead_score: typeof payload.lead_score === "number" ? payload.lead_score : fallback.lead_score,
    qualification_status:
      payload.qualification_status === "qualified" ||
      payload.qualification_status === "nurture" ||
      payload.qualification_status === "manual_review" ||
      payload.qualification_status === "rejected" ||
      payload.qualification_status === "duplicate"
        ? payload.qualification_status
        : fallback.qualification_status,
    recommended_route:
      payload.recommended_route === "hot_lead_sales" ||
      payload.recommended_route === "nurture_queue" ||
      payload.recommended_route === "manual_review" ||
      payload.recommended_route === "rejected_or_spam"
        ? payload.recommended_route
        : fallback.recommended_route,
    confidence: typeof payload.confidence === "number" ? payload.confidence : fallback.confidence,
    risk_flags: Array.isArray(payload.risk_flags)
      ? payload.risk_flags.filter((value): value is string => typeof value === "string")
      : fallback.risk_flags
  };
};

export class LlmService {
  async extractLead(lead: NormalizedLeadInput): Promise<LlmStepResult<LeadExtractionResult>> {
    const fallback = buildMockExtraction(lead);

    if (env.openAiMode === "mock" || !env.openAiApiKey) {
      return {
        data: fallback,
        mode: "mock",
        fallback_used: false,
        provider: "mock-heuristics",
        error: null
      };
    }

    try {
      const payload = await callChatCompletion(
        "Extract structured lead attributes and return JSON only with the requested fields.",
        {
          task: "extract_lead_attributes",
          lead
        }
      );

      return {
        data: asExtraction(fallback, payload),
        mode: "live",
        fallback_used: false,
        provider: env.openAiModel,
        error: null
      };
    } catch (error) {
      return {
        data: fallback,
        mode: "fallback",
        fallback_used: true,
        provider: "mock-heuristics",
        error: error instanceof Error ? error.message : "Unknown extraction error"
      };
    }
  }

  async scoreLead(
    lead: NormalizedLeadInput,
    extraction: LeadExtractionResult
  ): Promise<LlmStepResult<LeadScoringResult>> {
    const fallback = buildMockScoring(lead, extraction);

    if (env.openAiMode === "mock" || !env.openAiApiKey) {
      return {
        data: fallback,
        mode: "mock",
        fallback_used: false,
        provider: "mock-heuristics",
        error: null
      };
    }

    try {
      const payload = await callChatCompletion(
        "Score the lead and return JSON only with lead_score, qualification_status, recommended_route, confidence, and risk_flags.",
        {
          task: "score_lead",
          lead,
          extraction
        }
      );

      return {
        data: asScoring(fallback, payload),
        mode: "live",
        fallback_used: false,
        provider: env.openAiModel,
        error: null
      };
    } catch (error) {
      return {
        data: fallback,
        mode: "fallback",
        fallback_used: true,
        provider: "mock-heuristics",
        error: error instanceof Error ? error.message : "Unknown scoring error"
      };
    }
  }
}
