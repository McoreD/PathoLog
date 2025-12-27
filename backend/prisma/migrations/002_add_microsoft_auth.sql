ALTER TABLE "User" ADD COLUMN "microsoftSub" TEXT;

CREATE UNIQUE INDEX "User_microsoftSub_key" ON "User"("microsoftSub");
