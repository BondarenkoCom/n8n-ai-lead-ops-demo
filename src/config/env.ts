import path from "node:path";
import dotenv from "dotenv";

dotenv.config();

const asNumber = (value: string | undefined, fallback: number): number => {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
};

const asString = (value: string | undefined, fallback: string): string => {
  return value && value.trim().length > 0 ? value.trim() : fallback;
};

export const env = {
  port: asNumber(process.env.PORT, 3001),
  dataRoot: path.resolve(process.cwd(), process.env.DATA_ROOT ?? "./data"),
  webhookBaseUrl: asString(process.env.WEBHOOK_BASE_URL, "http://localhost:5678"),
  openAiApiKey: process.env.OPENAI_API_KEY?.trim() ?? "",
  openAiModel: asString(process.env.OPENAI_MODEL, "gpt-4o-mini"),
  openAiBaseUrl: asString(process.env.OPENAI_BASE_URL, "https://api.openai.com/v1"),
  openAiMode: asString(process.env.OPENAI_MODE, "mock"),
  mockCrmBaseUrl: asString(process.env.MOCK_CRM_BASE_URL, "http://localhost:3001"),
  slackWebhookUrl: process.env.SLACK_WEBHOOK_URL?.trim() ?? "",
  slackMode: asString(process.env.SLACK_MODE, "mock"),
  gmailMode: asString(process.env.GMAIL_MODE, "mock"),
  auditMode: asString(process.env.AUDIT_MODE, "file"),
  humanApprovalMode: asString(process.env.HUMAN_APPROVAL_MODE, "conditional"),
  approvalCallbackUrl: asString(
    process.env.APPROVAL_CALLBACK_URL,
    "http://localhost:5678/webhook/lead-approval-decision"
  ),
  budgetMinQualified: asNumber(process.env.BUDGET_MIN_QUALIFIED, 3000),
  timeZone: asString(process.env.TZ, "UTC")
};
