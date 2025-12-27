-- CreateEnum
CREATE TYPE "Sex" AS ENUM ('female', 'male', 'other', 'unknown');

-- CreateEnum
CREATE TYPE "ParsingStatus" AS ENUM ('pending', 'completed', 'needs_review', 'failed');

-- CreateEnum
CREATE TYPE "ExtractionConfidence" AS ENUM ('high', 'medium', 'low');

-- CreateEnum
CREATE TYPE "MappingMethod" AS ENUM ('dictionary', 'generated', 'user_confirmed');

-- CreateEnum
CREATE TYPE "MappingConfidence" AS ENUM ('high', 'medium', 'low');

-- CreateEnum
CREATE TYPE "ResultType" AS ENUM ('numeric', 'qualitative', 'semi_quantitative', 'micro_target', 'panel_summary', 'admin_event');

-- CreateEnum
CREATE TYPE "SourceFileProvider" AS ENUM ('azure_blob', 's3', 'local');

-- CreateEnum
CREATE TYPE "StandardSystem" AS ENUM ('loinc', 'custom', 'unknown');

-- CreateEnum
CREATE TYPE "DetectionStatus" AS ENUM ('detected', 'not_detected', 'equivocal', 'unknown');

-- CreateEnum
CREATE TYPE "FlagSeverity" AS ENUM ('normal', 'borderline', 'high', 'low', 'critical', 'unknown');

-- CreateEnum
CREATE TYPE "CommentScope" AS ENUM ('analyte', 'panel', 'global');

-- CreateEnum
CREATE TYPE "AdministrativeEventType" AS ENUM ('test_not_performed', 'specimen_comment', 'rejection', 'cancellation');

-- CreateEnum
CREATE TYPE "AdministrativeStatus" AS ENUM ('not_performed', 'rejected', 'cancelled', 'unknown');

-- CreateEnum
CREATE TYPE "TaskType" AS ENUM ('missing_short_code', 'low_confidence', 'unit_mismatch', 'range_context', 'unknown');

-- CreateEnum
CREATE TYPE "TaskStatus" AS ENUM ('open', 'resolved', 'dismissed');

-- CreateEnum
CREATE TYPE "PanelInterpretationFlag" AS ENUM ('normal', 'abnormal', 'borderline', 'unknown');

-- CreateEnum
CREATE TYPE "ValidationStatus" AS ENUM ('validated', 'not_fully_validated', 'unknown');

-- CreateTable
CREATE TABLE "User" (
    "id" TEXT NOT NULL,
    "email" TEXT NOT NULL,
    "fullName" TEXT,
    "googleSub" TEXT,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAtUtc" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "User_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "FamilyAccount" (
    "id" TEXT NOT NULL,
    "ownerUserId" TEXT NOT NULL,
    "name" TEXT NOT NULL,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAtUtc" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "FamilyAccount_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "FamilyMember" (
    "id" TEXT NOT NULL,
    "familyAccountId" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "role" TEXT NOT NULL,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "FamilyMember_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Patient" (
    "id" TEXT NOT NULL,
    "familyAccountId" TEXT,
    "ownerUserId" TEXT,
    "externalPatientKey" TEXT,
    "fullName" TEXT NOT NULL,
    "dob" TIMESTAMP(3),
    "sex" "Sex",
    "addressText" TEXT,
    "phoneText" TEXT,
    "medicareNumberText" TEXT,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAtUtc" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "Patient_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "SourceFile" (
    "id" TEXT NOT NULL,
    "storageProvider" "SourceFileProvider" NOT NULL,
    "storageBucket" TEXT,
    "storageKey" TEXT NOT NULL,
    "originalFilename" TEXT NOT NULL,
    "contentType" TEXT NOT NULL,
    "sizeBytes" INTEGER NOT NULL,
    "uploadedByUserId" TEXT NOT NULL,
    "uploadedAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "SourceFile_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Report" (
    "id" TEXT NOT NULL,
    "patientId" TEXT NOT NULL,
    "sourceFileId" TEXT NOT NULL,
    "sourcePdfHash" TEXT NOT NULL,
    "providerName" TEXT,
    "providerTradingName" TEXT,
    "providerAbn" TEXT,
    "providerPhone" TEXT,
    "providerWebsite" TEXT,
    "accreditationNpaac" BOOLEAN,
    "accreditationIso15189" BOOLEAN,
    "accreditationNumber" TEXT,
    "apaNumber" TEXT,
    "nataNumbers" TEXT,
    "generatorSystem" TEXT,
    "instrumentReportLevel" TEXT,
    "referrerName" TEXT,
    "referrerRef" TEXT,
    "copyTo" TEXT,
    "requestedDate" TIMESTAMP(3),
    "collectedDatetimeLocal" TIMESTAMP(3),
    "receivedDatetimeLocal" TIMESTAMP(3),
    "reportedDatetimeLocal" TIMESTAMP(3),
    "documentCreatedDatetimeLocal" TIMESTAMP(3),
    "timeZone" TEXT,
    "reportType" TEXT,
    "panelNameOriginal" TEXT,
    "specimenOriginal" TEXT,
    "clinicalNotes" TEXT,
    "globalComments" TEXT,
    "rawText" TEXT,
    "rawTextExtractionMethod" TEXT,
    "pageCount" INTEGER,
    "parsingStatus" "ParsingStatus" NOT NULL DEFAULT 'pending',
    "parsingVersion" TEXT NOT NULL DEFAULT '0.0.1',
    "extractionConfidenceOverall" "ExtractionConfidence",
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAtUtc" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "Report_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Subpanel" (
    "id" TEXT NOT NULL,
    "reportId" TEXT NOT NULL,
    "subpanelNameOriginal" TEXT,
    "specimenOriginal" TEXT,
    "testMethod" TEXT,
    "instrumentSubpanelLevel" TEXT,
    "validationStatus" "ValidationStatus",
    "subpanelReportedDatetime" TIMESTAMP(3),
    "panelInterpretationText" TEXT,
    "panelInterpretationFlag" "PanelInterpretationFlag",
    "sourceAnchor" TEXT,

    CONSTRAINT "Subpanel_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Result" (
    "id" TEXT NOT NULL,
    "reportId" TEXT NOT NULL,
    "subpanelId" TEXT,
    "patientId" TEXT NOT NULL,
    "analyteNameOriginal" TEXT NOT NULL,
    "analyteShortCode" TEXT,
    "analyteCodeStandardSystem" "StandardSystem" NOT NULL DEFAULT 'unknown',
    "analyteCodeStandardValue" TEXT,
    "analyteGroup" TEXT,
    "mappingMethod" "MappingMethod" DEFAULT 'dictionary',
    "mappingConfidence" "MappingConfidence" DEFAULT 'medium',
    "mappingDictionaryId" TEXT,
    "resultType" "ResultType" NOT NULL,
    "valueNumeric" DOUBLE PRECISION,
    "valueText" TEXT,
    "unitOriginal" TEXT,
    "unitNormalised" TEXT,
    "censored" BOOLEAN NOT NULL DEFAULT false,
    "censorOperator" TEXT DEFAULT 'none',
    "flagAbnormal" BOOLEAN,
    "flagSeverity" "FlagSeverity",
    "refLow" DOUBLE PRECISION,
    "refHigh" DOUBLE PRECISION,
    "refText" TEXT,
    "referenceRangeContext" TEXT,
    "collectionContext" TEXT,
    "specimen" TEXT,
    "specimenContainer" TEXT,
    "preservative" TEXT,
    "method" TEXT,
    "organismName" TEXT,
    "targetGroup" TEXT,
    "targetTaxonRank" TEXT,
    "detectionStatus" "DetectionStatus",
    "morphologyComment" TEXT,
    "commentText" TEXT,
    "commentScope" "CommentScope",
    "calculationName" TEXT,
    "calculationApplicabilityRules" TEXT,
    "calculationExcludedReason" TEXT,
    "collectedDatetimeLocal" TIMESTAMP(3),
    "reportedDatetimeLocal" TIMESTAMP(3),
    "labNumber" TEXT,
    "sourceAnchor" TEXT,
    "extractionConfidence" "ExtractionConfidence",
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAtUtc" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "Result_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "ReferenceRange" (
    "id" TEXT NOT NULL,
    "resultId" TEXT NOT NULL,
    "refLow" DOUBLE PRECISION,
    "refHigh" DOUBLE PRECISION,
    "refText" TEXT,
    "referenceRangeContext" TEXT,
    "collectionContext" TEXT,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "ReferenceRange_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "Comment" (
    "id" TEXT NOT NULL,
    "reportId" TEXT NOT NULL,
    "scope" "CommentScope" NOT NULL,
    "text" TEXT NOT NULL,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "Comment_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "AdministrativeEvent" (
    "id" TEXT NOT NULL,
    "reportId" TEXT NOT NULL,
    "patientId" TEXT NOT NULL,
    "eventType" "AdministrativeEventType" NOT NULL,
    "testNameRequested" TEXT,
    "testStatus" "AdministrativeStatus" NOT NULL,
    "notPerformedReason" TEXT,
    "specimenComment" TEXT,
    "sourceAnchor" TEXT,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "AdministrativeEvent_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "MappingDictionary" (
    "id" TEXT NOT NULL,
    "familyAccountId" TEXT NOT NULL,
    "analyteNamePattern" TEXT NOT NULL,
    "analyteShortCode" TEXT NOT NULL,
    "analyteCodeStandardSystem" "StandardSystem" NOT NULL DEFAULT 'custom',
    "analyteCodeStandardValue" TEXT,
    "preferredUnitNormalised" TEXT,
    "enabled" BOOLEAN NOT NULL DEFAULT true,
    "createdByUserId" TEXT NOT NULL,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAtUtc" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "MappingDictionary_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "CumulativeSeries" (
    "id" TEXT NOT NULL,
    "reportId" TEXT NOT NULL,
    "patientId" TEXT NOT NULL,
    "seriesName" TEXT NOT NULL,
    "specimenOriginal" TEXT,
    "sourceAnchor" TEXT,
    "extractionConfidence" "ExtractionConfidence",
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "CumulativeSeries_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "CumulativeColumn" (
    "id" TEXT NOT NULL,
    "cumulativeSeriesId" TEXT NOT NULL,
    "analyteNameOriginal" TEXT NOT NULL,
    "analyteShortCode" TEXT,
    "analyteCodeStandardSystem" "StandardSystem" NOT NULL DEFAULT 'unknown',
    "analyteCodeStandardValue" TEXT,
    "unitOriginal" TEXT,
    "unitNormalised" TEXT,
    "mappingMethod" "MappingMethod" DEFAULT 'dictionary',
    "mappingConfidence" "MappingConfidence" DEFAULT 'medium',
    "resultType" TEXT NOT NULL,
    "mappingDictionaryId" TEXT,

    CONSTRAINT "CumulativeColumn_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "CumulativeRow" (
    "id" TEXT NOT NULL,
    "cumulativeSeriesId" TEXT NOT NULL,
    "collectionDate" TIMESTAMP(3) NOT NULL,
    "labNumber" TEXT,
    "valuesJson" JSONB NOT NULL,

    CONSTRAINT "CumulativeRow_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "ReviewTask" (
    "id" TEXT NOT NULL,
    "reportId" TEXT NOT NULL,
    "patientId" TEXT NOT NULL,
    "taskType" "TaskType" NOT NULL,
    "status" "TaskStatus" NOT NULL DEFAULT 'open',
    "payloadJson" JSONB NOT NULL,
    "resolvedByUserId" TEXT,
    "resolvedAtUtc" TIMESTAMP(3),
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "ReviewTask_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "User_email_key" ON "User"("email");

-- CreateIndex
CREATE UNIQUE INDEX "User_googleSub_key" ON "User"("googleSub");

-- CreateIndex
CREATE UNIQUE INDEX "Report_sourcePdfHash_key" ON "Report"("sourcePdfHash");

-- CreateIndex
CREATE INDEX "Report_patientId_collectedDatetimeLocal_idx" ON "Report"("patientId", "collectedDatetimeLocal");

-- CreateIndex
CREATE UNIQUE INDEX "MappingDictionary_familyAccountId_analyteNamePattern_key" ON "MappingDictionary"("familyAccountId", "analyteNamePattern");

-- CreateIndex
CREATE INDEX "ReviewTask_status_createdAtUtc_idx" ON "ReviewTask"("status", "createdAtUtc");

-- CreateIndex
CREATE INDEX "ReviewTask_patientId_taskType_idx" ON "ReviewTask"("patientId", "taskType");

-- AddForeignKey
ALTER TABLE "FamilyAccount" ADD CONSTRAINT "FamilyAccount_ownerUserId_fkey" FOREIGN KEY ("ownerUserId") REFERENCES "User"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "FamilyMember" ADD CONSTRAINT "FamilyMember_familyAccountId_fkey" FOREIGN KEY ("familyAccountId") REFERENCES "FamilyAccount"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "FamilyMember" ADD CONSTRAINT "FamilyMember_userId_fkey" FOREIGN KEY ("userId") REFERENCES "User"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Patient" ADD CONSTRAINT "Patient_familyAccountId_fkey" FOREIGN KEY ("familyAccountId") REFERENCES "FamilyAccount"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Patient" ADD CONSTRAINT "Patient_ownerUserId_fkey" FOREIGN KEY ("ownerUserId") REFERENCES "User"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "SourceFile" ADD CONSTRAINT "SourceFile_uploadedByUserId_fkey" FOREIGN KEY ("uploadedByUserId") REFERENCES "User"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Report" ADD CONSTRAINT "Report_patientId_fkey" FOREIGN KEY ("patientId") REFERENCES "Patient"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Report" ADD CONSTRAINT "Report_sourceFileId_fkey" FOREIGN KEY ("sourceFileId") REFERENCES "SourceFile"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Subpanel" ADD CONSTRAINT "Subpanel_reportId_fkey" FOREIGN KEY ("reportId") REFERENCES "Report"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Result" ADD CONSTRAINT "Result_reportId_fkey" FOREIGN KEY ("reportId") REFERENCES "Report"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Result" ADD CONSTRAINT "Result_subpanelId_fkey" FOREIGN KEY ("subpanelId") REFERENCES "Subpanel"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Result" ADD CONSTRAINT "Result_patientId_fkey" FOREIGN KEY ("patientId") REFERENCES "Patient"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Result" ADD CONSTRAINT "Result_mappingDictionaryId_fkey" FOREIGN KEY ("mappingDictionaryId") REFERENCES "MappingDictionary"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "ReferenceRange" ADD CONSTRAINT "ReferenceRange_resultId_fkey" FOREIGN KEY ("resultId") REFERENCES "Result"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Comment" ADD CONSTRAINT "Comment_reportId_fkey" FOREIGN KEY ("reportId") REFERENCES "Report"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "AdministrativeEvent" ADD CONSTRAINT "AdministrativeEvent_reportId_fkey" FOREIGN KEY ("reportId") REFERENCES "Report"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "AdministrativeEvent" ADD CONSTRAINT "AdministrativeEvent_patientId_fkey" FOREIGN KEY ("patientId") REFERENCES "Patient"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "MappingDictionary" ADD CONSTRAINT "MappingDictionary_familyAccountId_fkey" FOREIGN KEY ("familyAccountId") REFERENCES "FamilyAccount"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "MappingDictionary" ADD CONSTRAINT "MappingDictionary_createdByUserId_fkey" FOREIGN KEY ("createdByUserId") REFERENCES "User"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "CumulativeSeries" ADD CONSTRAINT "CumulativeSeries_reportId_fkey" FOREIGN KEY ("reportId") REFERENCES "Report"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "CumulativeSeries" ADD CONSTRAINT "CumulativeSeries_patientId_fkey" FOREIGN KEY ("patientId") REFERENCES "Patient"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "CumulativeColumn" ADD CONSTRAINT "CumulativeColumn_cumulativeSeriesId_fkey" FOREIGN KEY ("cumulativeSeriesId") REFERENCES "CumulativeSeries"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "CumulativeColumn" ADD CONSTRAINT "CumulativeColumn_mappingDictionaryId_fkey" FOREIGN KEY ("mappingDictionaryId") REFERENCES "MappingDictionary"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "CumulativeRow" ADD CONSTRAINT "CumulativeRow_cumulativeSeriesId_fkey" FOREIGN KEY ("cumulativeSeriesId") REFERENCES "CumulativeSeries"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "ReviewTask" ADD CONSTRAINT "ReviewTask_reportId_fkey" FOREIGN KEY ("reportId") REFERENCES "Report"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "ReviewTask" ADD CONSTRAINT "ReviewTask_patientId_fkey" FOREIGN KEY ("patientId") REFERENCES "Patient"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "ReviewTask" ADD CONSTRAINT "ReviewTask_resolvedByUserId_fkey" FOREIGN KEY ("resolvedByUserId") REFERENCES "User"("id") ON DELETE SET NULL ON UPDATE CASCADE;

