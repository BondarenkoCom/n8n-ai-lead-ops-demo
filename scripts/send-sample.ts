import path from "node:path";
import { fileURLToPath } from "node:url";
import { readFile } from "node:fs/promises";
import { env } from "../src/config/env.js";

const currentFile = fileURLToPath(import.meta.url);
const projectRoot = path.resolve(path.dirname(currentFile), "..");

const scenario = process.argv[2] ?? "high-intent-b2b-website";
const sourceFlag = process.argv.find((arg) => arg.startsWith("--source=")) ?? "";

const webhookBySource: Record<string, string> = {
  website_form: "lead-intake-website",
  email_inquiry: "lead-intake-email",
  chat_simulation: "lead-intake-chat"
};

const run = async (): Promise<void> => {
  const scenarioPath = path.join(projectRoot, "docs", "sample-payloads", `${scenario}.json`);
  const payload = JSON.parse(await readFile(scenarioPath, "utf8")) as Record<string, unknown>;
  const explicitSource = sourceFlag.replace("--source=", "");
  const source = explicitSource || (payload.source_type as string) || "website_form";
  const webhookName = webhookBySource[source] ?? webhookBySource.website_form;
  const baseUrl = env.webhookBaseUrl.replace(/\/$/, "");
  const response = await fetch(`${baseUrl}/webhook/${webhookName}`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json"
    },
    body: JSON.stringify(payload)
  });

  const text = await response.text();
  process.stdout.write(`${text}\n`);
};

run().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
