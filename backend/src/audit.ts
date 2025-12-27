import { prisma } from "./db.js";
import { logger } from "./logger.js";

type AuditPayload = Record<string, any> | undefined;

export async function logAudit(entry: {
  entityType: string;
  entityId: string;
  action: string;
  userId?: string;
  payload?: AuditPayload;
}) {
  try {
    await prisma.auditLog.create({
      data: {
        entityType: entry.entityType,
        entityId: entry.entityId,
        action: entry.action,
        userId: entry.userId,
        payload: entry.payload,
      },
    });
  } catch (err) {
    logger.warn({ err }, "Failed to write audit log");
  }
}
