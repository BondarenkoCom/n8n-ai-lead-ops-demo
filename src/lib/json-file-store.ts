import { mkdir, readFile, writeFile, appendFile } from "node:fs/promises";
import path from "node:path";

const ensureParentDirectory = async (filePath: string): Promise<void> => {
  await mkdir(path.dirname(filePath), { recursive: true });
};

export const ensureJsonFile = async <T>(filePath: string, fallback: T): Promise<void> => {
  try {
    await readFile(filePath, "utf8");
  } catch {
    await ensureParentDirectory(filePath);
    await writeFile(filePath, JSON.stringify(fallback, null, 2), "utf8");
  }
};

export const readJsonFile = async <T>(filePath: string, fallback: T): Promise<T> => {
  await ensureJsonFile(filePath, fallback);
  try {
    const content = await readFile(filePath, "utf8");
    return JSON.parse(content) as T;
  } catch {
    return fallback;
  }
};

export const writeJsonFile = async <T>(filePath: string, data: T): Promise<void> => {
  await ensureParentDirectory(filePath);
  await writeFile(filePath, JSON.stringify(data, null, 2), "utf8");
};

export const appendJsonLine = async (filePath: string, data: unknown): Promise<void> => {
  await ensureParentDirectory(filePath);
  await appendFile(filePath, `${JSON.stringify(data)}\n`, "utf8");
};

const escapeCsvValue = (value: unknown): string => {
  const text = value === null || value === undefined ? "" : String(value);
  if (text.includes(",") || text.includes("\"") || text.includes("\n")) {
    return `"${text.replace(/"/g, "\"\"")}"`;
  }
  return text;
};

export const appendCsvRow = async (
  filePath: string,
  headers: string[],
  row: Record<string, unknown>
): Promise<void> => {
  await ensureParentDirectory(filePath);

  let needsHeader = false;
  try {
    const content = await readFile(filePath, "utf8");
    needsHeader = content.trim().length === 0;
  } catch {
    needsHeader = true;
  }

  const lines: string[] = [];
  if (needsHeader) {
    lines.push(headers.join(","));
  }

  lines.push(headers.map((header) => escapeCsvValue(row[header])).join(","));
  await appendFile(filePath, `${lines.join("\n")}\n`, "utf8");
};
