import path from "node:path";
import { AuditEvent, ErrorEvent, IdempotencyRecord } from "../domain/lead.js";
import { appendCsvRow, appendJsonLine, readJsonFile, writeJsonFile } from "../lib/json-file-store.js";

export class AuditService {
  private readonly auditJsonlPath: string;
  private readonly auditCsvPath: string;
  private readonly errorJsonlPath: string;
  private readonly idempotencyPath: string;

  constructor(dataRoot: string) {
    this.auditJsonlPath = path.join(dataRoot, "audit", "audit-log.jsonl");
    this.auditCsvPath = path.join(dataRoot, "audit", "audit-log.csv");
    this.errorJsonlPath = path.join(dataRoot, "errors", "error-log.jsonl");
    this.idempotencyPath = path.join(dataRoot, "idempotency", "keys.json");
  }

  async writeAuditEvent(event: AuditEvent): Promise<void> {
    await appendJsonLine(this.auditJsonlPath, event);
    await appendCsvRow(this.auditCsvPath, ["timestamp", "run_id", "event_type", "lead_id", "route", "status"], {
      timestamp: event.timestamp,
      run_id: event.run_id,
      event_type: event.event_type,
      lead_id: event.lead_id,
      route: event.route,
      status: event.status
    });
  }

  async writeErrorEvent(event: ErrorEvent): Promise<void> {
    await appendJsonLine(this.errorJsonlPath, event);
  }

  async registerSubmissionKey(key: string): Promise<IdempotencyRecord> {
    const records = await readJsonFile<IdempotencyRecord[]>(this.idempotencyPath, []);
    const existing = records.find((record) => record.key === key);
    const now = new Date().toISOString();

    if (existing) {
      existing.last_seen_at = now;
      existing.seen_count += 1;
      await writeJsonFile(this.idempotencyPath, records);
      return existing;
    }

    const record: IdempotencyRecord = {
      key,
      first_seen_at: now,
      last_seen_at: now,
      seen_count: 1
    };

    records.push(record);
    await writeJsonFile(this.idempotencyPath, records);
    return record;
  }
}
