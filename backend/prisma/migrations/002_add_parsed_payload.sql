-- Add parsed payload json storage on Report
ALTER TABLE "Report" ADD COLUMN "parsedPayloadJson" JSONB;
