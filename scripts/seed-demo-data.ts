import path from "node:path";
import { fileURLToPath } from "node:url";
import { writeFile } from "node:fs/promises";
import { writeJsonFile } from "../src/lib/json-file-store.js";

const currentFile = fileURLToPath(import.meta.url);
const projectRoot = path.resolve(path.dirname(currentFile), "..");

const run = async (): Promise<void> => {
  const seedRecord = {
    id: "lead_seed_northwind",
    created_at: "2026-03-01T09:30:00.000Z",
    updated_at: "2026-03-01T09:30:00.000Z",
    status: "qualified",
    owner: "sales-desk",
    lead_score: 81,
    route: "hot_lead_sales",
    notes: ["Seeded demo lead"],
    source_payload_snapshot: {
      name: "Jordan Ellis",
      company: "Northwind Health Advisory",
      email: "jordan.ellis@northwind-demo.com"
    },
    full_name: "Jordan Ellis",
    company: "Northwind Health Advisory",
    email: "jordan.ellis@northwind-demo.com",
    phone: "+1-312-555-0182",
    source: "Website form",
    service_interest: "Lead qualification automation",
    estimated_budget: 18000,
    urgency: "high",
    geography: "Chicago, US",
    free_text_summary: "Seed contact used for duplicate lead testing.",
    sentiment: "positive",
    intent: "purchase",
    confidence: 0.89,
    risk_flags: [],
    qualification_status: "qualified"
  };

  await writeJsonFile(path.join(projectRoot, "data", "crm", "leads.json"), [seedRecord]);
  await writeJsonFile(path.join(projectRoot, "data", "approvals", "pending-approvals.json"), []);
  await writeJsonFile(path.join(projectRoot, "data", "idempotency", "keys.json"), []);
  await writeJsonFile(path.join(projectRoot, "test-data", "seed", "crm-seed.json"), [seedRecord]);

  await writeFile(path.join(projectRoot, "data", "audit", "audit-log.jsonl"), "", "utf8");
  await writeFile(path.join(projectRoot, "data", "audit", "audit-log.csv"), "", "utf8");
  await writeFile(path.join(projectRoot, "data", "errors", "error-log.jsonl"), "", "utf8");
  await writeFile(path.join(projectRoot, "data", "notifications", "slack-messages.jsonl"), "", "utf8");
  await writeFile(path.join(projectRoot, "data", "email", "drafts.jsonl"), "", "utf8");
  await writeFile(path.join(projectRoot, "data", "bookings", "handoffs.jsonl"), "", "utf8");
};

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
