import { prisma } from "../db.js";
import { logger } from "../logger.js";
import { requestStructuredCompletion } from "./openai.js";

const DEFAULT_OPENAI_MODEL = "gpt-4o-mini";

const PARSED_PAYLOAD_SCHEMA = {
  type: "object",
  additionalProperties: false,
  properties: {
    schema_version: { type: "string" },
    parsing_version: { type: "string" },
    report_type: {
      type: "string",
      enum: ["single_panel_table", "multi_panel", "cumulative_table", "narrative_only"],
    },
    results: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        properties: {
          analyte_name_original: { type: "string" },
          analyte_short_code: { type: "string" },
          analyte_code_standard_system: { type: "string", enum: ["loinc", "custom", "unknown"] },
          analyte_code_standard_value: { type: "string" },
          result_type: {
            type: "string",
            enum: ["numeric", "qualitative", "semi_quantitative", "micro_target", "panel_summary", "admin_event"],
          },
          value_numeric: { type: ["number", "null"] },
          value_text: { type: ["string", "null"] },
          unit_original: { type: ["string", "null"] },
          unit_normalised: { type: ["string", "null"] },
          censored: { type: "boolean" },
          censor_operator: { type: "string", enum: ["lt", "gt", "le", "ge", "eq", "none"] },
          flag_abnormal: { type: ["boolean", "null"] },
          flag_severity: { type: "string", enum: ["normal", "borderline", "high", "low", "critical", "unknown"] },
          ref_low: { type: ["number", "null"] },
          ref_high: { type: ["number", "null"] },
          ref_text: { type: ["string", "null"] },
          reference_range_context: { type: ["string", "null"] },
          collection_context: { type: ["string", "null"] },
          specimen: { type: ["string", "null"] },
          organism_name: { type: ["string", "null"] },
          detection_status: { type: "string", enum: ["detected", "not_detected", "equivocal", "unknown"] },
          comment_text: { type: ["string", "null"] },
          comment_scope: { type: "string", enum: ["analyte", "panel", "global"] },
          calculation_name: { type: ["string", "null"] },
          collected_datetime_local: { type: ["string", "null"] },
          reported_datetime_local: { type: ["string", "null"] },
          lab_number: { type: ["string", "null"] },
          source_anchor: { type: ["string", "null"] },
          extraction_confidence: { type: "string", enum: ["high", "medium", "low"] },
          reference_ranges: {
            type: "array",
            items: {
              type: "object",
              additionalProperties: false,
              properties: {
                ref_low: { type: ["number", "null"] },
                ref_high: { type: ["number", "null"] },
                ref_text: { type: ["string", "null"] },
                reference_range_context: { type: ["string", "null"] },
                collection_context: { type: ["string", "null"] },
              },
            },
          },
        },
        required: ["analyte_name_original", "result_type"],
      },
    },
    comments: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        properties: {
          scope: { type: "string", enum: ["analyte", "panel", "global"] },
          text: { type: "string" },
        },
        required: ["scope", "text"],
      },
    },
  },
  required: ["results"],
};

const SHORT_CODE_SCHEMA = {
  type: "object",
  additionalProperties: false,
  properties: {
    mappings: {
      type: "array",
      items: {
        type: "object",
        additionalProperties: false,
        properties: {
          analyte_name_original: { type: "string" },
          analyte_short_code: { type: "string" },
        },
        required: ["analyte_name_original", "analyte_short_code"],
      },
    },
  },
  required: ["mappings"],
};

async function getUserAiKey(userId: string, provider: string) {
  return prisma.userAiSetting.findUnique({
    where: {
      userId_provider: {
        userId,
        provider,
      },
    },
  });
}

export async function extractParsedPayload(userId: string, rawText: string) {
  const settings = await getUserAiKey(userId, "openai");
  if (!settings) return null;

  const prompt = [
    "Extract structured pathology report data from the text below.",
    "Return JSON that matches the provided schema exactly.",
    "If a field is missing, omit it or set it to null.",
    "",
    rawText.slice(0, 120000),
  ].join("\n");

  const result = await requestStructuredCompletion({
    apiKey: settings.apiKey,
    model: DEFAULT_OPENAI_MODEL,
    systemInstruction: "You are a medical data extraction assistant.",
    prompt,
    responseSchema: PARSED_PAYLOAD_SCHEMA,
  });

  if (!result.ok) {
    logger.warn({ error: result.error }, "AI parse failed");
    return null;
  }
  return result.content;
}

export async function suggestShortCodes(userId: string, analytes: string[]) {
  const settings = await getUserAiKey(userId, "openai");
  if (!settings || !analytes.length) return new Map<string, string>();

  const prompt = [
    "Generate short analyte codes (2-8 chars, uppercase) for the given analyte names.",
    "Return JSON with mappings for each analyte name.",
    "",
    JSON.stringify(analytes),
  ].join("\n");

  const result = await requestStructuredCompletion({
    apiKey: settings.apiKey,
    model: DEFAULT_OPENAI_MODEL,
    systemInstruction: "You generate short, consistent lab analyte codes.",
    prompt,
    responseSchema: SHORT_CODE_SCHEMA,
  });

  if (!result.ok) {
    logger.warn({ error: result.error }, "AI short code lookup failed");
    return new Map<string, string>();
  }

  const mappings = (result.content.mappings as Array<{ analyte_name_original: string; analyte_short_code: string }>) || [];
  const map = new Map<string, string>();
  for (const item of mappings) {
    if (item?.analyte_name_original && item?.analyte_short_code) {
      map.set(item.analyte_name_original.toLowerCase(), item.analyte_short_code);
    }
  }
  return map;
}
