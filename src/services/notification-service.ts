import path from "node:path";
import {
  BookingHandoffPayload,
  EmailDraft,
  SlackNotificationPayload
} from "../domain/lead.js";
import { appendJsonLine } from "../lib/json-file-store.js";

export class NotificationService {
  private readonly slackFilePath: string;
  private readonly emailFilePath: string;
  private readonly bookingFilePath: string;
  private readonly slackWebhookUrl: string;
  private readonly slackMode: string;
  private readonly gmailMode: string;

  constructor(dataRoot: string, options: { slackWebhookUrl: string; slackMode: string; gmailMode: string }) {
    this.slackFilePath = path.join(dataRoot, "notifications", "slack-messages.jsonl");
    this.emailFilePath = path.join(dataRoot, "email", "drafts.jsonl");
    this.bookingFilePath = path.join(dataRoot, "bookings", "handoffs.jsonl");
    this.slackWebhookUrl = options.slackWebhookUrl;
    this.slackMode = options.slackMode;
    this.gmailMode = options.gmailMode;
  }

  async sendSlackNotification(payload: SlackNotificationPayload): Promise<{ delivered: boolean; mode: string }> {
    await appendJsonLine(this.slackFilePath, payload);

    if (this.slackMode !== "mock" && this.slackWebhookUrl) {
      const response = await fetch(this.slackWebhookUrl, {
        method: "POST",
        headers: {
          "Content-Type": "application/json"
        },
        body: JSON.stringify({
          text: `[${payload.kind}] ${payload.company ?? payload.full_name ?? payload.lead_id} | score ${payload.lead_score} | ${payload.summary}`
        })
      });

      return {
        delivered: response.ok,
        mode: "live"
      };
    }

    return {
      delivered: true,
      mode: "mock"
    };
  }

  async saveEmailDraft(draft: EmailDraft): Promise<{ stored: boolean; mode: string }> {
    await appendJsonLine(this.emailFilePath, draft);
    return {
      stored: true,
      mode: this.gmailMode
    };
  }

  async recordBookingHandoff(payload: BookingHandoffPayload): Promise<{ stored: boolean }> {
    await appendJsonLine(this.bookingFilePath, payload);
    return {
      stored: true
    };
  }
}
