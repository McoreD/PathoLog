type GeminiRequest = {
  apiKey: string;
  model: string;
  systemInstruction?: string | null;
  prompt: string;
  responseSchema: Record<string, unknown>;
};

export type GeminiResult =
  | { ok: true; raw: string; content: Record<string, unknown> }
  | { ok: false; raw: string; error: string };

export async function requestGeminiCompletion(request: GeminiRequest): Promise<GeminiResult> {
  const endpoint = `https://generativelanguage.googleapis.com/v1beta/models/${request.model}:generateContent?key=${request.apiKey}`;
  const prompt = request.systemInstruction
    ? `${request.systemInstruction}\n\n${request.prompt}`
    : request.prompt;
  const payload = {
    contents: [
      {
        parts: [{ text: prompt }],
      },
    ],
    generationConfig: {
      responseMimeType: "application/json",
      responseSchema: request.responseSchema,
    },
  };

  const res = await fetch(endpoint, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  const raw = await res.text();
  if (!res.ok) {
    return { ok: false, raw, error: `Gemini error ${res.status}: ${res.statusText}` };
  }

  try {
    const data = JSON.parse(raw) as any;
    const text = data?.candidates?.[0]?.content?.parts?.[0]?.text;
    if (!text) {
      return { ok: false, raw, error: "Gemini response missing content" };
    }
    try {
      const parsed = JSON.parse(text) as Record<string, unknown>;
      return { ok: true, raw, content: parsed };
    } catch {
      return { ok: false, raw, error: "Gemini content was not valid JSON" };
    }
  } catch {
    return { ok: false, raw, error: "Failed to parse Gemini response" };
  }
}
