CREATE TABLE "UserAiSetting" (
    "id" TEXT NOT NULL,
    "userId" TEXT NOT NULL,
    "provider" TEXT NOT NULL,
    "apiKey" TEXT NOT NULL,
    "createdAtUtc" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updatedAtUtc" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "UserAiSetting_pkey" PRIMARY KEY ("id")
);

CREATE UNIQUE INDEX "UserAiSetting_userId_provider_key" ON "UserAiSetting"("userId", "provider");

ALTER TABLE "UserAiSetting" ADD CONSTRAINT "UserAiSetting_userId_fkey"
  FOREIGN KEY ("userId") REFERENCES "User"("id") ON DELETE CASCADE ON UPDATE CASCADE;
