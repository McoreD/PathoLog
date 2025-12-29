import fs from "fs";
import { fileURLToPath } from "url";
import path from "path";

export type ParsedResult = {
  analyte_name_original: string;
  analyte_short_code: string | null;
  result_type: "numeric" | "qualitative";
  value_numeric?: number | null;
  value_text?: string | null;
  unit_original?: string | null;
  unit_normalised?: string | null;
  reported_datetime_local?: string | null;
  extraction_confidence?: "high" | "medium" | "low" | null;
  source_anchor?: string | null;
};

export type ReviewTask = {
  field_path: string;
  reason: string;
};

export type AiParseOutput = {
  results: ParsedResult[];
  reviewTasks: ReviewTask[];
  reportType: string | null;
};

const OPENAI_MODEL = "gpt-4o";
const GEMINI_MODEL = "gemini-2.5-pro";
const PROMPT_BASE = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../../../..", "src", "shared-prompts", "ai");
const PASS1_TEMPLATE = loadPrompt("pass1.txt");
const PASS2_TEMPLATE = loadPrompt("pass2.txt");
const SCHEMA_TEMPLATE = loadPrompt("schema.json");

function loadPrompt(name: string) {
  const fullPath = path.join(PROMPT_BASE, name);
  try {
    return fs.readFileSync(fullPath, "utf8");
  } catch {
    return "";
  }
}

function buildPass1Prompt(text: string) {
  return PASS1_TEMPLATE.replace("{{TEXT}}", text);
}

function buildPass2Prompt(text: string, pass1Json: string | null) {
  return PASS2_TEMPLATE.replace("{{PASS1_JSON}}", pass1Json ?? "null")
    .replace("{{SCHEMA}}", SCHEMA_TEMPLATE)
    .replace("{{TEXT}}", text);
}

export async function parsePdfWithAi(args: {
  buffer: Buffer;
  preferredProvider?: "openai" | "gemini" | null;
  openAiKey?: string | null;
  geminiKey?: string | null;
}): Promise<AiParseOutput> {
  const text = await extractText(args.buffer);
  const preferred = args.preferredProvider ?? "openai";

  let aiResult: AiParseOutput | null = null;
  if (text) {
    if (preferred === "gemini") {
      aiResult = await extractWithGemini(text, args.geminiKey);
      if (!aiResult) {
        aiResult = await extractWithOpenAi(text, args.openAiKey);
      }
    } else {
      aiResult = await extractWithOpenAi(text, args.openAiKey);
      if (!aiResult) {
        aiResult = await extractWithGemini(text, args.geminiKey);
      }
    }
  }

  return aiResult ?? extractHeuristic(text);
}

async function extractText(buffer: Buffer) {
  try {
    const { PDFParse } = await import("pdf-parse");
    const parser = new PDFParse({ data: buffer });
    const parsed = await parser.getText();
    await parser.destroy();
    return (parsed.text || "").trim();
  } catch {
    return "";
  }
}

async function extractWithOpenAi(text: string, apiKey?: string | null): Promise<AiParseOutput | null> {
  if (!apiKey) return null;

  try {
    const pass1Payload = {
      model: OPENAI_MODEL,
      temperature: 0.1,
      messages: [
        { role: "system", content: "You are parsing an Australian pathology PDF report." },
        {
          role: "user",
          content: buildPass1Prompt(text),
        },
      ],
    };

    const pass1Json = await callOpenAi(apiKey, pass1Payload);
    const reportType = parseReportType(pass1Json);

    const pass2Payload = {
      model: OPENAI_MODEL,
      temperature: 0.1,
      messages: [
        { role: "system", content: "You are extracting structured pathology results from an Australian lab PDF." },
        {
          role: "user",
          content: buildPass2Prompt(text, pass1Json),
        },
      ],
    };

    const pass2Json = await callOpenAi(apiKey, pass2Payload);
    if (!pass2Json) return null;
    return parseAiResult(pass2Json, reportType);
  } catch {
    return null;
  }
}

async function extractWithGemini(text: string, apiKey?: string | null): Promise<AiParseOutput | null> {
  if (!apiKey) return null;

  try {
    const pass1Payload = {
      contents: [
        {
          role: "user",
          parts: [
            {
              text: buildPass1Prompt(text),
            },
          ],
        },
      ],
      generationConfig: { temperature: 0.1 },
    };

    const pass1Json = await callGemini(apiKey, pass1Payload);
    const reportType = parseReportType(pass1Json);

    const pass2Payload = {
      contents: [
        {
          role: "user",
          parts: [
            {
              text: buildPass2Prompt(text, pass1Json),
            },
          ],
        },
      ],
      generationConfig: { temperature: 0.1 },
    };

    const pass2Json = await callGemini(apiKey, pass2Payload);
    if (!pass2Json) return null;
    return parseAiResult(pass2Json, reportType);
  } catch {
    return null;
  }
}

function extractHeuristic(text: string): AiParseOutput {
  const results: ParsedResult[] = [];
  const reviewTasks: ReviewTask[] = [];
  const lines = text
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l.length > 0);

  for (const line of lines) {
    const match = line.match(/^([A-Za-z][A-Za-z0-9 /%().-]{2,})\s+([<>]?\d+(?:\.\d+)?)(?:\s*([A-Za-zć/%-]+))?/);
    if (!match) continue;
    const [, name, valueRaw, unit] = match;
    const numeric = Number(valueRaw.replace(/[<>]/g, ""));
    results.push({
      analyte_name_original: name.trim(),
      analyte_short_code: toShortCode(name),
      result_type: "numeric",
      value_numeric: Number.isNaN(numeric) ? null : numeric,
      value_text: valueRaw,
      unit_original: unit || null,
      extraction_confidence: "low",
    });
    if (results.length >= 12) break;
  }

  if (results.length === 0) {
    if (!text) {
      results.push({
        analyte_name_original: "Report imported",
        analyte_short_code: "PDF",
        result_type: "qualitative",
        value_text: "Parsed text captured",
        extraction_confidence: "low",
      });
      reviewTasks.push({ field_path: "result:Report imported", reason: "Low confidence extraction" });
    } else {
      reviewTasks.push({ field_path: "results", reason: "No structured results parsed from PDF text" });
    }
  }

  return { results, reviewTasks, reportType: null };
}

function parseAiResult(json: string, reportType: string | null): AiParseOutput {
  const results: ParsedResult[] = [];
  const reviewTasks: ReviewTask[] = [];
  const parsed = JSON.parse(json);

  if (Array.isArray(parsed.results)) {
    for (const r of parsed.results) {
      results.push({
        analyte_name_original: r.analyte_name_original ?? "Analyte",
        analyte_short_code: r.analyte_short_code ?? toShortCode(r.analyte_name_original ?? "Analyte"),
        result_type: r.result_type === "numeric" ? "numeric" : "qualitative",
        value_numeric: typeof r.value_numeric === "number" ? r.value_numeric : null,
        value_text: typeof r.value_text === "string" ? r.value_text : null,
        unit_original: typeof r.unit_original === "string" ? r.unit_original : null,
        unit_normalised: null,
        extraction_confidence: r.extraction_confidence ?? "medium",
        source_anchor: r.source_anchor ?? null,
      });
    }
  }

  if (Array.isArray(parsed.review_tasks)) {
    for (const t of parsed.review_tasks) {
      reviewTasks.push({
        field_path: typeof t.field_path === "string" ? t.field_path : "field",
        reason: typeof t.reason === "string" ? t.reason : "needs review",
      });
    }
  }

  for (const r of results) {
    if (!r.analyte_short_code) {
      r.analyte_short_code = toShortCode(r.analyte_name_original);
    }
  }

  return { results, reviewTasks, reportType };
}

function parseReportType(payload: string | null) {
  if (!payload) return null;
  try {
    const data = JSON.parse(payload);
    if (typeof data.report_type === "string") return data.report_type;
  } catch {
    return null;
  }
  return null;
}

function toShortCode(name: string) {
  const letters = (name.match(/[A-Za-z]/g) || []).join("").toUpperCase();
  if (letters.length >= 2 && letters.length <= 5) return letters;
  if (letters.length > 5) return letters.slice(0, 5);
  const words = name
    .split(/\s+/)
    .filter(Boolean)
    .map((w) => w[0]?.toUpperCase() || "")
    .join("");
  return words || "RS";
}

async function callOpenAi(apiKey: string, payload: object) {
  const res = await fetch("https://api.openai.com/v1/chat/completions", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${apiKey}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });
  if (!res.ok) {
    return null;
  }
  const json = (await res.json()) as any;
  return json?.choices?.[0]?.message?.content ?? null;
}

async function callGemini(apiKey: string, payload: object) {
  const res = await fetch(
    `https://generativelanguage.googleapis.com/v1beta/models/${GEMINI_MODEL}:generateContent?key=${apiKey}`,
    {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    },
  );
  if (!res.ok) {
    return null;
  }
  const json = (await res.json()) as any;
  return json?.candidates?.[0]?.content?.parts?.[0]?.text ?? null;
}
