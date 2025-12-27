import { NextFunction, Request, Response } from "express";
import { OAuth2Client } from "google-auth-library";
import { SignJWT, jwtVerify } from "jose";
import { env } from "./env.js";
import { User } from "@prisma/client";
import { logger } from "./logger.js";
import { prisma } from "./db.js";

export const SESSION_COOKIE = "patholog_session";

const client = new OAuth2Client(env.GOOGLE_CLIENT_ID);
const jwtSecret = new TextEncoder().encode(env.AUTH_SECRET);

export type AuthedRequest = Request & { user?: User };

export async function verifyGoogleCredential(credential: string) {
  const ticket = await client.verifyIdToken({
    idToken: credential,
    audience: env.GOOGLE_CLIENT_ID,
  });
  const payload = ticket.getPayload();
  if (!payload?.email || !payload.sub) {
    throw new Error("Google token missing required claims");
  }
  return {
    email: payload.email,
    sub: payload.sub,
    fullName: payload.name ?? payload.email.split("@")[0],
  };
}

export async function createSessionToken(user: User) {
  return new SignJWT({ email: user.email })
    .setProtectedHeader({ alg: "HS256" })
    .setSubject(user.id)
    .setIssuedAt()
    .setExpirationTime("7d")
    .sign(jwtSecret);
}

export async function parseSessionToken(token: string) {
  try {
    const { payload } = await jwtVerify(token, jwtSecret);
    return { userId: payload.sub as string, email: payload.email as string };
  } catch (err) {
    logger.warn({ err }, "Invalid session token");
    return null;
  }
}

export async function authMiddleware(
  req: AuthedRequest,
  res: Response,
  next: NextFunction,
) {
  const token = req.cookies?.[SESSION_COOKIE];
  if (!token) {
    return res.status(401).json({ error: "Unauthorized" });
  }
  const parsed = await parseSessionToken(token);
  if (!parsed) {
    return res.status(401).json({ error: "Invalid session" });
  }
  const user = await prisma.user.findUnique({ where: { id: parsed.userId } });
  if (!user) {
    return res.status(401).json({ error: "User not found" });
  }
  req.user = user;
  next();
}

export function sessionCookieOptions() {
  return {
    httpOnly: true,
    sameSite: "lax" as const,
    secure: env.NODE_ENV === "production",
    maxAge: 7 * 24 * 60 * 60 * 1000,
  };
}
