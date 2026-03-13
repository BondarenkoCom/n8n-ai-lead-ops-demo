import path from "node:path";
import { randomUUID } from "node:crypto";
import { CrmUpsertPayload, LeadRecord, LeadStatus, RecommendedRoute } from "../domain/lead.js";
import { readJsonFile, writeJsonFile } from "../lib/json-file-store.js";

const ownerByRoute: Record<RecommendedRoute, string> = {
  hot_lead_sales: "sales-desk",
  nurture_queue: "lifecycle-ops",
  manual_review: "triage-desk",
  rejected_or_spam: "suppression-queue"
};

const statusByRoute: Record<RecommendedRoute, LeadStatus> = {
  hot_lead_sales: "qualified",
  nurture_queue: "nurture",
  manual_review: "manual_review",
  rejected_or_spam: "rejected"
};

export class CrmRepository {
  private readonly filePath: string;

  constructor(dataRoot: string) {
    this.filePath = path.join(dataRoot, "crm", "leads.json");
  }

  async list(filters?: { email?: string | null; phone?: string | null }): Promise<LeadRecord[]> {
    const leads = await readJsonFile<LeadRecord[]>(this.filePath, []);
    return leads.filter((lead) => {
      if (filters?.email && lead.email?.toLowerCase() === filters.email.toLowerCase()) {
        return true;
      }
      if (filters?.phone && lead.phone === filters.phone) {
        return true;
      }
      if (!filters?.email && !filters?.phone) {
        return true;
      }
      return false;
    });
  }

  async getById(id: string): Promise<LeadRecord | null> {
    const leads = await readJsonFile<LeadRecord[]>(this.filePath, []);
    return leads.find((lead) => lead.id === id) ?? null;
  }

  async upsert(payload: CrmUpsertPayload): Promise<{ record: LeadRecord; was_existing: boolean }> {
    const leads = await readJsonFile<LeadRecord[]>(this.filePath, []);
    const index = leads.findIndex(
      (lead) =>
        (payload.extraction.email && lead.email?.toLowerCase() === payload.extraction.email.toLowerCase()) ||
        (payload.extraction.phone && lead.phone === payload.extraction.phone)
    );

    const now = new Date().toISOString();
    const existing = index >= 0 ? leads[index] : null;

    const record: LeadRecord = {
      id: existing?.id ?? `lead_${randomUUID()}`,
      created_at: existing?.created_at ?? now,
      updated_at: now,
      status:
        payload.evaluation.qualification_status === "duplicate"
          ? "duplicate"
          : statusByRoute[payload.evaluation.final_route],
      owner: ownerByRoute[payload.evaluation.final_route],
      lead_score: payload.evaluation.lead_score,
      route: payload.evaluation.final_route,
      notes: Array.from(
        new Set(
          [
            ...(existing?.notes ?? []),
            payload.evaluation.duplicate_submission ? "Duplicate submission key detected" : null,
            payload.evaluation.duplicate_match_count > 0 ? "Duplicate email or phone match detected" : null,
            ...payload.evaluation.risk_flags
          ].filter((value): value is string => Boolean(value))
        )
      ),
      source_payload_snapshot: payload.lead.raw_payload,
      full_name: payload.extraction.full_name,
      company: payload.extraction.company,
      email: payload.extraction.email,
      phone: payload.extraction.phone,
      source: payload.extraction.source,
      service_interest: payload.extraction.service_interest,
      estimated_budget: payload.extraction.estimated_budget,
      urgency: payload.extraction.urgency,
      geography: payload.extraction.geography,
      free_text_summary: payload.extraction.free_text_summary,
      sentiment: payload.extraction.sentiment,
      intent: payload.extraction.intent,
      confidence: payload.evaluation.confidence,
      risk_flags: payload.evaluation.risk_flags,
      qualification_status: payload.evaluation.qualification_status
    };

    if (index >= 0) {
      leads[index] = record;
    } else {
      leads.push(record);
    }

    await writeJsonFile(this.filePath, leads);

    return {
      record,
      was_existing: Boolean(existing)
    };
  }

  async updateStatus(id: string, status: LeadStatus, note?: string): Promise<LeadRecord | null> {
    const leads = await readJsonFile<LeadRecord[]>(this.filePath, []);
    const index = leads.findIndex((lead) => lead.id === id);
    if (index < 0) {
      return null;
    }

    const record = leads[index];
    const updated: LeadRecord = {
      ...record,
      status,
      updated_at: new Date().toISOString(),
      notes: note ? Array.from(new Set([...record.notes, note])) : record.notes
    };

    leads[index] = updated;
    await writeJsonFile(this.filePath, leads);
    return updated;
  }
}
