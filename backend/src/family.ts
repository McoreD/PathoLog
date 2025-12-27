import { prisma } from "./db.js";
import { User } from "@prisma/client";

export async function ensureFamilyAccountForUser(user: User) {
  const existing = await prisma.familyAccount.findFirst({
    where: { ownerUserId: user.id },
  });
  if (existing) return existing;
  const created = await prisma.familyAccount.create({
    data: {
      ownerUserId: user.id,
      name: `${user.email.split("@")[0]} family`,
    },
  });
  return created;
}
