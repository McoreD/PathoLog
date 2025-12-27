# Parsed Output JSON Contract (enforced in backend)

Endpoint: `POST /reports/:reportId/parsed` expects JSON matching this structure (validated with Zod).

```jsonc
{
  "schema_version": "1.0",
  "parsing_version": "0.0.1",
  "report_type": "single_panel_table",
  "results": [
    {
      "analyte_name_original": "TSH",
      "analyte_short_code": "TSH",
      "analyte_code_standard_system": "loinc",
      "analyte_code_standard_value": "11580-8",
      "result_type": "numeric",
      "value_numeric": 2.1,
      "value_text": null,
      "unit_original": "mIU/L",
      "unit_normalised": "mIU/L",
      "censored": false,
      "censor_operator": "none",
      "flag_abnormal": false,
      "flag_severity": "normal",
      "ref_low": 0.4,
      "ref_high": 4.0,
      "ref_text": null,
      "reference_range_context": "adult",
      "collection_context": "fasting",
      "specimen": "Serum",
      "organism_name": null,
      "detection_status": "unknown",
      "comment_text": null,
      "comment_scope": "analyte",
      "calculation_name": null,
      "collected_datetime_local": "2024-12-01T08:00:00",
      "reported_datetime_local": "2024-12-01T14:00:00",
      "lab_number": "ABC123",
      "source_anchor": "page1-row3",
      "extraction_confidence": "medium",
      "reference_ranges": [
        {
          "ref_low": 0.4,
          "ref_high": 4.0,
          "ref_text": null,
          "reference_range_context": "adult",
          "collection_context": "fasting"
        }
      ]
    }
  ],
  "comments": [
    { "scope": "global", "text": "Fasting specimen" }
  ]
}
```

Enum fields and constraints are enforced; missing or invalid fields return HTTP 400. Normalisation (short code generation, mapping lookup, unit normalisation) runs server-side after validation.
