import { ParsedResult } from "./schemas/parsedPayload.js";
import { MappingDictionary, Patient, ResultType, MappingMethod, MappingConfidence } from "@prisma/client";
import { prisma } from "./db.js";
import { ensureFamilyAccountForUser } from "./family.js";
import { UserInputError } from "./utils/errors.js";
import crypto from "crypto";

const unitMap: Record<string, string> = {
  "g/l": "g/L",
  "mmol/l": "mmol/L",
  "mg/dl": "mg/dL",
  "%": "%",
  "iu/l": "IU/L",
};

function normalizeUnit(unit?: string | null) {
  if (!unit) return null;
  const trimmed = unit.trim();
  const key = trimmed.toLowerCase();
  return unitMap[key] ?? trimmed;
}

function generateShortCode(name: string) {
  const cleaned = name.replace(/[^a-zA-Z0-9 ]/g, " ").trim();
  const words = cleaned.split(/\s+/).filter(Boolean);
  if (!words.length) {
    return `A${crypto.randomInt(100, 999)}`;
  }
  const code = words
    .map((w) => w[0])
    .join("")
    .slice(0, 4)
    .toUpperCase();
  return code.padEnd(2, "X");
}

function matchDictionaryEntry(name: string, entries: MappingDictionary[]) {
  const lower = name.toLowerCase();
  return entries.find((e) => lower === e.analyteNamePattern.toLowerCase());
}

export async function normalizeResults(args: {
  patient: Patient & { ownerUserId: string | null };
  userId: string;
  results: ParsedResult[];
}) {
  const { patient, userId, results } = args;
  const user = await prisma.user.findUnique({ where: { id: userId } });
  if (!user) throw new UserInputError("User not found");
  const family = await ensureFamilyAccountForUser(user);

  // Ensure patient has familyAccountId for mapping reuse
  if (!patient.familyAccountId) {
    await prisma.patient.update({
      where: { id: patient.id },
      data: { familyAccountId: family.id },
    });
    patient.familyAccountId = family.id;
  }

  const dictionary = await prisma.mappingDictionary.findMany({
    where: { familyAccountId: family.id, enabled: true },
  });

  return results.map((r) => {
    const dictMatch = matchDictionaryEntry(r.analyte_name_original, dictionary);
    const unitNormalised = r.unit_normalised ?? normalizeUnit(r.unit_original);
    let mappingMethod: MappingMethod = "generated";
    let mappingConfidence: MappingConfidence = "medium";
    let analyteShortCode = r.analyte_short_code;
    let mappingDictionaryId: string | null = null;

    if (dictMatch) {
      analyteShortCode = dictMatch.analyteShortCode;
      mappingMethod = "dictionary";
      mappingConfidence = "high";
      mappingDictionaryId = dictMatch.id;
    } else if (!analyteShortCode) {
      analyteShortCode = generateShortCode(r.analyte_name_original);
    } else {
      mappingMethod = "user_confirmed";
      mappingConfidence = "high";
    }

    const censored = r.censored ?? false;
    const resultType = r.result_type as ResultType;

    return {
      analyteShortCode,
      mappingMethod,
      mappingConfidence,
      mappingDictionaryId,
      unitNormalised,
      resultType,
       censored,
      ...r,
    };
  });
}
