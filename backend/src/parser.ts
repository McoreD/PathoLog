import pdfParse from "pdf-parse";
import { prisma } from "./db.js";
import { createStorage } from "./storage.js";
import { logger } from "./logger.js";

const storage = createStorage();

type ParseResult = {
  rawText: string;
  method: "pdf_text" | "ocr_fallback";
  confidence: "high" | "medium" | "low";
};

async function extractPdfText(buffer: Buffer): Promise<ParseResult | null> {
  try {
    const parsed = await pdfParse(buffer);
    const text = (parsed.text || "").trim();
    if (!text) return null;
    return { rawText: text, method: "pdf_text", confidence: "high" };
  } catch (err) {
    logger.warn({ err }, "Failed digital PDF parse, will try OCR fallback");
    return null;
  }
}

async function ocrFallback(_buffer: Buffer): Promise<ParseResult | null> {
  // TODO: integrate OCR (e.g., Azure Computer Vision or Tesseract). For now mark as needs_review.
  return { rawText: "", method: "ocr_fallback", confidence: "low" };
}

export async function parseReport(reportId: string) {
  const report = await prisma.report.findUnique({
    where: { id: reportId },
    include: { sourceFile: true },
  });
  if (!report) {
    logger.warn({ reportId }, "Report not found for parsing");
    return;
  }

  try {
    const buffer = await storage.getFileBuffer(report.sourceFile.storageKey);
    const digital = await extractPdfText(buffer);
    const parsed = digital ?? (await ocrFallback(buffer));

    if (parsed && parsed.rawText) {
      await prisma.report.update({
        where: { id: report.id },
        data: {
          rawText: parsed.rawText,
          rawTextExtractionMethod: parsed.method === "pdf_text" ? "pdf_text" : "ocr_mixed",
          parsingStatus: "completed",
          extractionConfidenceOverall: parsed.confidence,
        },
      });
    } else {
      await prisma.report.update({
        where: { id: report.id },
        data: {
          parsingStatus: "needs_review",
          extractionConfidenceOverall: "low",
        },
      });
    }
  } catch (err) {
    logger.error({ err, reportId }, "Parsing failed");
    await prisma.report.update({
      where: { id: report.id },
      data: {
        parsingStatus: "failed",
        extractionConfidenceOverall: "low",
      },
    });
  }
}
