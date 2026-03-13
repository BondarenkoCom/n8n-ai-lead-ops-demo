import path from "node:path";
import { randomUUID } from "node:crypto";
import { ApprovalPayload, ApprovalRequestRecord } from "../domain/lead.js";
import { readJsonFile, writeJsonFile } from "../lib/json-file-store.js";

export class ApprovalRepository {
  private readonly filePath: string;

  constructor(dataRoot: string) {
    this.filePath = path.join(dataRoot, "approvals", "pending-approvals.json");
  }

  async create(payload: ApprovalPayload): Promise<ApprovalRequestRecord> {
    const approvals = await readJsonFile<ApprovalRequestRecord[]>(this.filePath, []);
    const now = new Date().toISOString();
    const record: ApprovalRequestRecord = {
      approval_id: `approval_${randomUUID()}`,
      created_at: now,
      updated_at: now,
      status: "pending",
      decision_at: null,
      route: payload.route,
      lead_id: payload.lead_id,
      reason: payload.reason,
      email_draft: payload.email_draft,
      booking_payload: payload.booking_payload
    };

    approvals.push(record);
    await writeJsonFile(this.filePath, approvals);
    return record;
  }

  async get(approvalId: string): Promise<ApprovalRequestRecord | null> {
    const approvals = await readJsonFile<ApprovalRequestRecord[]>(this.filePath, []);
    return approvals.find((approval) => approval.approval_id === approvalId) ?? null;
  }

  async resolve(approvalId: string, decision: "approved" | "rejected"): Promise<ApprovalRequestRecord | null> {
    const approvals = await readJsonFile<ApprovalRequestRecord[]>(this.filePath, []);
    const index = approvals.findIndex((approval) => approval.approval_id === approvalId);
    if (index < 0) {
      return null;
    }

    const now = new Date().toISOString();
    const updated: ApprovalRequestRecord = {
      ...approvals[index],
      status: decision,
      decision_at: now,
      updated_at: now
    };

    approvals[index] = updated;
    await writeJsonFile(this.filePath, approvals);
    return updated;
  }
}
