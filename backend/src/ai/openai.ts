type StructuredRequest = {
  apiKey: string;
  model: string;
  systemInstruction?: string | null;
  prompt: string;
  responseSchema: Record<string, unknown>;
};

export type StructuredResult =
  | { ok: true; raw: string; content: Record<string, unknown> }
  | { ok: false; raw: string; error: string };

export async function requestStructuredCompletion(request: StructuredRequest): Promise<StructuredResult> {
  const payload = {
    model: request.model,
    messages: request.systemInstruction
      ? [
          { role: "system", content: request.systemInstruction },
          { role: "user", content: request.prompt },
        ]
      : [{ role: "user", content: request.prompt }],
    response_format: {
      type: "json_schema",
      json_schema: {
        name: "patholog_structured",
        strict: true,
        schema: request.responseSchema,
      },
    },
  };

  const res = await fetch("https://api.openai.com/v1/chat/completions", {
    method: "POST",
    headers: {
      Authorization: `Bearer ${request.apiKey}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(payload),
  });
  const raw = await res.text();
  if (!res.ok) {
    return { ok: false, raw, error: `OpenAI error ${res.status}: ${res.statusText}` };
  }

  try {
    const data = JSON.parse(raw) as any;
    const content = data?.choices?.[0]?.message?.content;
    if (!content) {
      return { ok: false, raw, error: "OpenAI response missing content" };
    }
    try {
      const parsed = JSON.parse(content) as Record<string, unknown>;
      return { ok: true, raw, content: parsed };
    } catch {
      return { ok: false, raw, error: "OpenAI content was not valid JSON" };
    }
  } catch {
    return { ok: false, raw, error: "Failed to parse OpenAI response" };
  }
}
