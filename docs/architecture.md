# Architecture

## Trigger sources

The workflow accepts inbound data from three channels:

- website lead form webhook
- email inquiry payload webhook
- chat or WhatsApp-style payload webhook

Each source is tagged before entering a shared normalization stage.

## LLM extraction stage

The extraction step maps raw inbound data into a reusable lead schema:

- `full_name`
- `company`
- `email`
- `phone`
- `source`
- `service_interest`
- `estimated_budget`
- `urgency`
- `geography`
- `free_text_summary`
- `sentiment`
- `intent`

The ASP.NET Core backend exposes an OpenAI-compatible abstraction. When live credentials are absent or a live request fails, the system falls back to deterministic extraction heuristics.

## LLM scoring stage

The scoring stage produces:

- `lead_score`
- `qualification_status`
- `recommended_route`
- `confidence`
- `risk_flags`

The scoring result is treated as advisory and is adjusted by deterministic business rules before routing.

## Rule-based routing

Deterministic rules run after scoring and enforce:

- spam and junk filtering
- missing contact checks
- budget threshold handling
- urgency escalation
- duplicate match handling
- idempotency duplicate handling

Final routes:

- `hot_lead_sales`
- `nurture_queue`
- `manual_review`
- `rejected_or_spam`

## CRM sync

The file-backed CRM API is implemented in the `.NET` service and supports:

- `POST /leads/upsert`
- `GET /leads`
- `GET /leads/:id`
- `POST /booking/handoff`

Upsert keys are email and phone. Records store score, route, owner, notes, and a source payload snapshot for traceability.

## Slack alerting

Slack-style alerts are triggered for:

- hot leads
- manual review items
- approval-required items

When no live webhook is configured, messages are written to `data/notifications/slack-messages.jsonl`.

## Email drafting

The workflow creates a reply draft for qualified or nurture routes. In mock mode, drafts are appended to `data/email/drafts.jsonl`. This simulates a Gmail draft connector without requiring external credentials.

## Audit logging

Every run writes audit records to:

- `data/audit/audit-log.jsonl`
- `data/audit/audit-log.csv`

Errors are captured in `data/errors/error-log.jsonl`.

## Human approval checkpoint

The workflow contains an explicit approval branch before outbound follow-up and booking handoff for configured routes. Approval requests are persisted to `data/approvals/pending-approvals.json`, and the approval callback webhook resolves those requests before continuing execution.

## Failure handling

The demo includes:

- idempotency registration before downstream actions
- structured error logging in the `.NET` API
- workflow-level failure logging path
- deterministic fallback behavior when live LLM calls fail

## Local runtime components

- `n8n`: orchestration engine and webhook entrypoints
- `dotnet-api`: ASP.NET Core service for CRM, approval store, LLM adapter, Slack adapter, mail draft adapter, and audit writer
- `mock-api`: original TypeScript prototype retained as a reference artifact
- `data/`: file-backed demo storage
- `docs/sample-payloads/`: replayable inbound scenarios
- `test-data/demo-responses/`: expected outcome snapshots
