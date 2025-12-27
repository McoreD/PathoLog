import { z } from "zod";

const referenceRangeSchema = z.object({
  ref_low: z.number().nullable().optional(),
  ref_high: z.number().nullable().optional(),
  ref_text: z.string().nullable().optional(),
  reference_range_context: z.string().nullable().optional(),
  collection_context: z.string().nullable().optional(),
});

const resultSchema = z.object({
  analyte_name_original: z.string(),
  analyte_short_code: z.string().min(2).max(8).optional(),
  analyte_code_standard_system: z.enum(["loinc", "custom", "unknown"]).optional(),
  analyte_code_standard_value: z.string().optional(),
  result_type: z.enum([
    "numeric",
    "qualitative",
    "semi_quantitative",
    "micro_target",
    "panel_summary",
    "admin_event",
  ]),
  value_numeric: z.number().nullable().optional(),
  value_text: z.string().nullable().optional(),
  unit_original: z.string().nullable().optional(),
  unit_normalised: z.string().nullable().optional(),
  censored: z.boolean().optional(),
  censor_operator: z.enum(["lt", "gt", "le", "ge", "eq", "none"]).optional(),
  flag_abnormal: z.boolean().nullable().optional(),
  flag_severity: z.enum(["normal", "borderline", "high", "low", "critical", "unknown"]).optional(),
  ref_low: z.number().nullable().optional(),
  ref_high: z.number().nullable().optional(),
  ref_text: z.string().nullable().optional(),
  reference_range_context: z.string().nullable().optional(),
  collection_context: z.string().nullable().optional(),
  specimen: z.string().nullable().optional(),
  organism_name: z.string().nullable().optional(),
  detection_status: z.enum(["detected", "not_detected", "equivocal", "unknown"]).optional(),
  comment_text: z.string().nullable().optional(),
  comment_scope: z.enum(["analyte", "panel", "global"]).optional(),
  calculation_name: z.string().nullable().optional(),
  collected_datetime_local: z.string().nullable().optional(),
  reported_datetime_local: z.string().nullable().optional(),
  lab_number: z.string().nullable().optional(),
  source_anchor: z.string().nullable().optional(),
  extraction_confidence: z.enum(["high", "medium", "low"]).optional(),
  reference_ranges: z.array(referenceRangeSchema).optional(),
});

export const parsedPayloadSchema = z.object({
  schema_version: z.string().default("1.0"),
  parsing_version: z.string().default("0.0.1"),
  report_type: z
    .enum(["single_panel_table", "multi_panel", "cumulative_table", "narrative_only"])
    .optional(),
  results: z.array(resultSchema).default([]),
  comments: z.array(
    z.object({
      scope: z.enum(["analyte", "panel", "global"]),
      text: z.string(),
    }),
  ).optional(),
});

export type ParsedPayload = z.infer<typeof parsedPayloadSchema>;
export type ParsedResult = z.infer<typeof resultSchema>;
