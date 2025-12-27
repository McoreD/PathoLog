import { prisma } from "./db.js";
import { User, Patient } from "@prisma/client";

export async function ensureFamilyMembership(user: User) {
  const family = await prisma.familyAccount.findFirst({
    where: { ownerUserId: user.id },
  });
  if (!family) return null;
  const member = await prisma.familyMember.findFirst({
    where: { familyAccountId: family.id, userId: user.id },
  });
  if (!member) {
    await prisma.familyMember.create({
      data: {
        familyAccountId: family.id,
        userId: user.id,
        role: "owner",
      },
    });
  }
  return family;
}

export async function assertPatientAccess(user: User, patientId: string): Promise<Patient> {
  const patient = await prisma.patient.findFirst({
    where: { id: patientId },
  });
  if (!patient) {
    throw new Error("Patient not found");
  }
  if (patient.ownerUserId === user.id) return patient;
  if (patient.familyAccountId) {
    const membership = await prisma.familyMember.findFirst({
      where: { familyAccountId: patient.familyAccountId, userId: user.id },
    });
    if (membership) return patient;
  }
  throw new Error("Forbidden");
}

export async function assertReportAccess(user: User, reportId: string) {
  const report = await prisma.report.findFirst({
    where: { id: reportId },
    include: { patient: true },
  });
  if (!report) throw new Error("Report not found");
  await assertPatientAccess(user, report.patientId);
  return report;
}
