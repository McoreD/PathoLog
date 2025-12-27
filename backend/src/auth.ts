import { NextFunction, Request, Response } from "express";
import { User } from "@prisma/client";
import { prisma } from "./db.js";
import { ensureFamilyAccountForUser } from "./family.js";
import { ensureFamilyMembership } from "./access.js";

export type AuthedRequest = Request & { user?: User };

type AuthPrincipal = {
  provider: string;
  userId: string;
  email: string;
  fullName: string;
};

function allowAnonymousAuth() {
  return process.env.ALLOW_ANONYMOUS_AUTH === "true";
}

function findClaim(claims: Array<{ typ: string; val: string }> | undefined, type: string) {
  return claims?.find((claim) => claim.typ?.toLowerCase() === type.toLowerCase())?.val ?? null;
}

function resolveEmail(principal: any) {
  const claims = Array.isArray(principal?.claims) ? principal.claims : [];
  const claim =
    findClaim(claims, "preferred_username") ||
    findClaim(claims, "email") ||
    findClaim(claims, "upn");
  if (claim && claim.includes("@")) return claim;
  const details = principal?.userDetails;
  return typeof details === "string" && details.includes("@") ? details : null;
}

function resolveDisplayName(principal: any, email: string) {
  const claims = Array.isArray(principal?.claims) ? principal.claims : [];
  const claim = findClaim(claims, "name") || findClaim(claims, "given_name");
  if (claim && claim.trim()) return claim;
  return email;
}

function getPrincipal(req: Request): AuthPrincipal | null {
  const encoded = req.headers["x-ms-client-principal"];
  if (!encoded || Array.isArray(encoded)) return null;
  try {
    const json = Buffer.from(encoded, "base64").toString("utf-8");
    const principal = JSON.parse(json);
    const roles = Array.isArray(principal?.userRoles) ? principal.userRoles : [];
    if (!roles.includes("authenticated")) return null;
    if (!principal?.identityProvider || !principal?.userId) return null;
    const email = resolveEmail(principal);
    if (!email) return null;
    const fullName = resolveDisplayName(principal, email);
    return {
      provider: principal.identityProvider,
      userId: principal.userId,
      email,
      fullName,
    };
  } catch {
    return null;
  }
}

async function upsertUser(principal: AuthPrincipal) {
  const provider = principal.provider.toLowerCase();
  const data: Record<string, string | null> = {
    fullName: principal.fullName,
  };
  if (provider === "google") {
    data.googleSub = principal.userId;
  } else if (provider === "aad") {
    data.microsoftSub = principal.userId;
  }
  const user = await prisma.user.upsert({
    where: { email: principal.email },
    update: data,
    create: {
      email: principal.email,
      fullName: principal.fullName,
      googleSub: data.googleSub ?? null,
      microsoftSub: data.microsoftSub ?? null,
    },
  });
  await ensureFamilyAccountForUser(user);
  await ensureFamilyMembership(user);
  return user;
}

export async function authMiddleware(
  req: AuthedRequest,
  res: Response,
  next: NextFunction,
) {
  const principal = getPrincipal(req);
  if (!principal) {
    if (allowAnonymousAuth()) {
      const user = await upsertUser({
        provider: "local",
        userId: "local",
        email: "local@patholog.dev",
        fullName: "Local User",
      });
      req.user = user;
      return next();
    }
    return res.status(401).json({ error: "Authentication required" });
  }
  const user = await upsertUser(principal);
  req.user = user;
  next();
}

export async function getAuthUser(req: Request): Promise<User | null> {
  const principal = getPrincipal(req);
  if (!principal) {
    if (allowAnonymousAuth()) {
      return upsertUser({
        provider: "local",
        userId: "local",
        email: "local@patholog.dev",
        fullName: "Local User",
      });
    }
    return null;
  }
  return upsertUser(principal);
}
