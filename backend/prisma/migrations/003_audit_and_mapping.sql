-- Add mapping confirmation audit fields on Result
ALTER TABLE "Result" ADD COLUMN "mappingConfirmedByUserId" TEXT;
ALTER TABLE "Result" ADD COLUMN "mappingConfirmedAtUtc" TIMESTAMP(3);
ALTER TABLE "Result" ADD CONSTRAINT "Result_mappingConfirmedByUserId_fkey" FOREIGN KEY ("mappingConfirmedByUserId") REFERENCES "User"("id") ON DELETE SET NULL ON UPDATE CASCADE;

-- AuditLog table
CREATE TABLE "AuditLog" (
    "id" TEXT PRIMARY KEY DEFAULT gen_random_uuid(),
    "entityType" TEXT NOT NULL,
    "entityId" TEXT NOT NULL,
    "action" TEXT NOT NULL,
    "userId" TEXT,
    "payload" JSONB,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT NOW(),
    CONSTRAINT "AuditLog_userId_fkey" FOREIGN KEY ("userId") REFERENCES "User"("id") ON DELETE SET NULL ON UPDATE CASCADE
);

CREATE INDEX "AuditLog_entityType_entityId_idx" ON "AuditLog"("entityType", "entityId");
