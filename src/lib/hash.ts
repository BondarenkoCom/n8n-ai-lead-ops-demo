import { createHash } from "node:crypto";

const stableValue = (value: unknown): string => {
  if (value === null || value === undefined) {
    return "null";
  }

  if (Array.isArray(value)) {
    return `[${value.map((entry) => stableValue(entry)).join(",")}]`;
  }

  if (typeof value === "object") {
    const record = value as Record<string, unknown>;
    return `{${Object.keys(record)
      .sort()
      .map((key) => `${key}:${stableValue(record[key])}`)
      .join(",")}}`;
  }

  return JSON.stringify(value);
};

export const createDeterministicHash = (value: unknown): string => {
  return createHash("sha256").update(stableValue(value)).digest("hex");
};
