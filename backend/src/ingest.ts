import { prisma } from "./db.js";
import { parsedPayloadSchema, ParsedPayload } from "./schemas/parsedPayload.js";
import { normalizeResults } from "./normalizer.js";
import { logger } from "./logger.js";
import { UserInputError } from "./utils/errors.js";
import { logAudit } from "./audit.js";

export async function ingestParsedReport(opts: { reportId: string; userId: string; payload: unknown }) {
  const parsed = parsedPayloadSchema.safeParse(opts.payload);
  if (!parsed.success) {
    throw new UserInputError("Invalid parsed payload");
  }
  const payload: ParsedPayload = parsed.data;

  const report = await prisma.report.findFirst({
    where: { id: opts.reportId, patient: { ownerUserId: opts.userId } },
    include: { patient: true },
  });
  if (!report) {
    throw new UserInputError("Report not found", 404);
  }

  const normalizedResults = await normalizeResults({
    patient: report.patient,
    userId: opts.userId,
    results: payload.results,
  });

  await prisma.$transaction(async (tx) => {
    await tx.referenceRange.deleteMany({
      where: { result: { reportId: report.id } },
    });
    await tx.result.deleteMany({
      where: { reportId: report.id },
    });

    for (const r of normalizedResults) {
      const analyteShortCode =
        (r as any).analyteShortCode ||
        (r as any).analyte_short_code ||
        (r as any).analyte_shortCode ||
        "";
      const result = await tx.result.create({
        data: {
          reportId: report.id,
          patientId: report.patientId,
          subpanelId: null,
          analyteNameOriginal: r.analyte_name_original,
          analyteShortCode,
          analyteCodeStandardSystem: (r.analyte_code_standard_system as any) ?? "unknown",
          analyteCodeStandardValue: r.analyte_code_standard_value ?? null,
          mappingMethod: r.mappingMethod ?? "generated",
          mappingConfidence: r.mappingConfidence ?? "medium",
          mappingDictionaryId: r.mappingDictionaryId ?? null,
          resultType: r.resultType,
          valueNumeric: r.value_numeric ?? null,
          valueText: r.value_text ?? null,
          unitOriginal: r.unit_original ?? null,
          unitNormalised: r.unit_normalised ?? r.unitNormalised ?? null,
          censored: r.censored ?? false,
          censorOperator: r.censor_operator ?? "none",
          flagAbnormal: r.flag_abnormal ?? null,
          flagSeverity: (r.flag_severity as any) ?? null,
          refLow: r.ref_low ?? null,
          refHigh: r.ref_high ?? null,
          refText: r.ref_text ?? null,
          referenceRangeContext: r.reference_range_context ?? null,
          collectionContext: r.collection_context ?? null,
          specimen: r.specimen ?? null,
          organismName: r.organism_name ?? null,
          detectionStatus: (r.detection_status as any) ?? null,
          commentText: r.comment_text ?? null,
          commentScope: (r.comment_scope as any) ?? null,
          calculationName: r.calculation_name ?? null,
          collectedDatetimeLocal: r.collected_datetime_local ? new Date(r.collected_datetime_local) : null,
          reportedDatetimeLocal: r.reported_datetime_local ? new Date(r.reported_datetime_local) : null,
          labNumber: r.lab_number ?? null,
          sourceAnchor: r.source_anchor ?? null,
          extractionConfidence: (r.extraction_confidence as any) ?? null,
        },
      });

      const ranges = r.reference_ranges ?? [];
      for (const rr of ranges) {
        await tx.referenceRange.create({
          data: {
            resultId: result.id,
            refLow: rr.ref_low ?? null,
            refHigh: rr.ref_high ?? null,
            refText: rr.ref_text ?? null,
            referenceRangeContext: rr.reference_range_context ?? null,
            collectionContext: rr.collection_context ?? null,
          },
        });
      }
    }

    await tx.report.update({
      where: { id: report.id },
      data: {
        parsingStatus: "completed",
        parsedPayloadJson: payload,
        parsingVersion: payload.parsing_version ?? report.parsingVersion,
      },
    });
  });

  await logAudit({
    entityType: "report",
    entityId: report.id,
    action: "parsed_ingested",
    userId: opts.userId,
    payload: { parsing_version: payload.parsing_version, result_count: payload.results.length },
  });

  logger.info({ reportId: report.id }, "Parsed payload ingested");
}
