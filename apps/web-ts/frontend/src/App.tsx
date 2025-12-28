import { useEffect, useMemo, useState, type FormEvent } from "react";
import { Line } from "react-chartjs-2";
import { Chart, LineElement, PointElement, LinearScale, TimeScale, CategoryScale, Tooltip, Legend } from "chart.js";
import "./App.css";

Chart.register(LineElement, PointElement, LinearScale, TimeScale, CategoryScale, Tooltip, Legend);
type User = {
  id: string;
  email: string;
  fullName?: string | null;
  googleLinked?: boolean;
  microsoftLinked?: boolean;
};
type Patient = { id: string; fullName: string };
type Report = {
  id: string;
  parsingStatus: string;
  createdAtUtc: string;
  sourceFile?: { originalFilename: string };
};
type ReviewReport = Report & { patient?: { fullName: string } };
type ResultRow = {
  id: string;
  analyteNameOriginal: string;
  analyteShortCode: string | null;
  valueNumeric?: number | null;
  valueText?: string | null;
  unitOriginal?: string | null;
  unitNormalised?: string | null;
  reportedDatetimeLocal?: string | null;
  resultType: string;
};
type TrendPoint = {
  id: string;
  reportedDatetimeLocal: string | null;
  collectedDatetimeLocal: string | null;
  valueNumeric: number | null;
  valueText: string | null;
  unitOriginal: string | null;
  unitNormalised: string | null;
  flagSeverity: string | null;
  extractionConfidence: string | null;
  refLow: number | null;
  refHigh: number | null;
};
type Anomaly = { analyte_short_code: string; type: string; detail?: any };
type AiSettings = {
  activeProvider: string | null;
  providers: { provider: string; hasKey: boolean }[];
};

const API_BASE = import.meta.env.VITE_API_BASE_URL || "/api";
const authReturnUrl = typeof window === "undefined" ? "/" : window.location.href;
const profileReturnUrl =
  typeof window === "undefined" ? "/" : `${window.location.origin}${window.location.pathname}#profile`;
const loginMicrosoftUrl = `/.auth/login/aad?post_login_redirect_uri=${encodeURIComponent(authReturnUrl)}`;
const loginGoogleUrl = `/.auth/login/google?post_login_redirect_uri=${encodeURIComponent(authReturnUrl)}`;
const linkMicrosoftUrl = `/.auth/login/aad?post_login_redirect_uri=${encodeURIComponent(profileReturnUrl)}`;
const linkGoogleUrl = `/.auth/login/google?post_login_redirect_uri=${encodeURIComponent(profileReturnUrl)}`;
const logoutUrl = `/.auth/logout?post_logout_redirect_uri=${encodeURIComponent(authReturnUrl)}`;

async function fetchJSON<T>(path: string, init?: RequestInit): Promise<T> {
  const url = `${API_BASE}${path}`;
  const res = await fetch(url, {
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers || {}),
    },
    ...init,
  });
  if (!res.ok) {
    const text = await res.text();
    const requestId = res.headers.get("x-ms-request-id") || res.headers.get("x-ms-client-request-id");
    const message = text.trim() ? text : "(empty response body)";
    const suffix = requestId ? ` request-id=${requestId}` : "";
    throw new Error(`${res.status} ${message} (${url})${suffix}`);
  }
  const text = await res.text();
  if (!text.trim()) {
    return {} as T;
  }
  try {
    return JSON.parse(text) as T;
  } catch {
    const preview = text.length > 200 ? `${text.slice(0, 200)}...` : text;
    throw new Error(`Invalid JSON response: ${preview}`);
  }
}

function formatDate(input: string | Date) {
  const date = typeof input === "string" ? new Date(input) : input;
  return date.toLocaleString();
}

function chooseValue(r: ResultRow, preferNormalised: boolean) {
  const value = r.valueNumeric ?? r.valueText ?? "";
  const unit = preferNormalised ? r.unitNormalised ?? r.unitOriginal ?? undefined : r.unitOriginal ?? r.unitNormalised ?? undefined;
  return `${value}${unit ? ` ${unit}` : ""}`;
}

function findClaim(principal: any, type: string) {
  const claims = Array.isArray(principal?.claims) ? principal.claims : [];
  const match = claims.find((claim: any) => claim?.typ?.toLowerCase() === type.toLowerCase());
  return match?.val ?? null;
}

function resolveEmail(principal: any) {
  const claim = findClaim(principal, "preferred_username") || findClaim(principal, "email") || findClaim(principal, "upn");
  if (claim && claim.includes("@")) {
    return claim;
  }
  const details = principal?.userDetails;
  return typeof details === "string" && details.includes("@") ? details : null;
}

function resolveDisplayName(principal: any, email: string) {
  const claim = findClaim(principal, "name") || findClaim(principal, "given_name");
  if (claim && String(claim).trim()) {
    return String(claim);
  }
  return email;
}

function TrendChart({ points, analyte }: { points: TrendPoint[]; analyte: string }) {
  const labels = points.map((p) => p.reportedDatetimeLocal || p.collectedDatetimeLocal || "");
  const data = {
    labels,
    datasets: [
      {
        label: analyte || "Value",
        data: points.map((p) => p.valueNumeric ?? null),
        borderColor: "#0f766e",
        backgroundColor: "rgba(15,118,110,0.2)",
        tension: 0.2,
        spanGaps: true,
      },
      {
        label: "Ref Low",
        data: points.map((p) => p.refLow ?? null),
        borderColor: "#d97706",
        borderDash: [6, 6],
        pointRadius: 0,
        spanGaps: true,
      },
      {
        label: "Ref High",
        data: points.map((p) => p.refHigh ?? null),
        borderColor: "#d97706",
        borderDash: [6, 6],
        pointRadius: 0,
        spanGaps: true,
      },
    ],
  };
  return <Line data={data} />;
}
export default function App() {
  const [user, setUser] = useState<User | null>(null);
  const [patients, setPatients] = useState<Patient[]>([]);
  const [selectedPatientId, setSelectedPatientId] = useState<string>("");
  const [reports, setReports] = useState<Report[]>([]);
  const [needsReview, setNeedsReview] = useState<ReviewReport[]>([]);
  const [results, setResults] = useState<ResultRow[]>([]);
  const [filter, setFilter] = useState("");
  const [preferNormalised, setPreferNormalised] = useState(true);
  const [patientForm, setPatientForm] = useState({ fullName: "", dob: "", sex: "female" });
  const [mappingForm, setMappingForm] = useState({ pattern: "", shortCode: "" });
  const [status, setStatus] = useState<string | null>(null);
  const [debugInfo, setDebugInfo] = useState<string | null>(null);
  const [debugError, setDebugError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [profileName, setProfileName] = useState("");
  const [profileStatus, setProfileStatus] = useState<string | null>(null);
  const [savingProfile, setSavingProfile] = useState(false);
  const [trendCode, setTrendCode] = useState("");
  const [trendData, setTrendData] = useState<TrendPoint[]>([]);
  const [anomalies, setAnomalies] = useState<Anomaly[]>([]);
  const [aiSettings, setAiSettings] = useState<AiSettings | null>(null);
  const [aiProvider, setAiProvider] = useState("openai");
  const [aiKey, setAiKey] = useState("");
  const [savingAi, setSavingAi] = useState(false);
  const [diagnostics, setDiagnostics] = useState<Record<string, unknown> | null>(null);
  const [loadingDiagnostics, setLoadingDiagnostics] = useState(false);

  const filteredResults = results.filter((r) => {
    if (!filter.trim()) return true;
    const f = filter.toLowerCase();
    return (
      r.analyteNameOriginal.toLowerCase().includes(f) ||
      (r.analyteShortCode ?? "").toLowerCase().includes(f)
    );
  });

  useEffect(() => {
    (async () => {
      const authUser = await loadAuthPrincipal();
      if (authUser) {
        setUser(authUser);
        const profile = await loadUserProfile();
        if (profile) {
          setUser((prev) => ({ ...prev, ...profile }));
        }
        await loadAiSettings().catch(() => null);
        await Promise.all([loadPatients(), loadNeedsReview()]);
      } else {
        setUser(null);
      }
    })();
  }, []);

  useEffect(() => {
    setProfileName(user?.fullName ?? "");
  }, [user]);

  useEffect(() => {
    if (selectedPatientId) {
      loadReports(selectedPatientId);
      loadResults(selectedPatientId);
      loadAnomalies(selectedPatientId);
    } else {
      setReports([]);
      setResults([]);
      setAnomalies([]);
    }
  }, [selectedPatientId]);

  const recordError = (action: string, err: unknown, fallback: string) => {
    const message = err instanceof Error ? err.message : fallback;
    setStatus(fallback);
    setDebugError(`${action}: ${message}`);
  };

  const loadPatients = async () => {
    const data = await fetchJSON<{ patients: Patient[] }>("/patients");
    setPatients(data.patients);
    if (!selectedPatientId && data.patients.length) {
      setSelectedPatientId(data.patients[0].id);
    }
  };

  const loadNeedsReview = async () => {
    const data = await fetchJSON<{ reports: ReviewReport[] }>("/reports/needs-review");
    setNeedsReview(data.reports);
  };

  const loadAiSettings = async () => {
    try {
      const data = await fetchJSON<AiSettings>("/ai/settings");
      setAiSettings(data);
      if (data.activeProvider) {
        setAiProvider(data.activeProvider);
      }
    } catch (err) {
      recordError("Load AI settings", err, "Failed to load AI settings");
    }
  };

  const loadReports = async (patientId: string) => {
    const data = await fetchJSON<{ reports: Report[] }>(`/patients/${patientId}/reports`);
    setReports(data.reports);
  };

  const loadResults = async (patientId: string) => {
    const data = await fetchJSON<{ results: ResultRow[] }>(`/patients/${patientId}/results`);
    setResults(data.results);
  };

  const loadAuthPrincipal = async (): Promise<User | null> => {
    try {
      const res = await fetch("/.auth/me", { credentials: "include" });
      if (!res.ok) return null;
      const data = (await res.json()) as { clientPrincipal?: any };
      const principal = data.clientPrincipal;
      if (!principal?.userRoles?.includes("authenticated")) return null;
      const email = resolveEmail(principal);
      if (!email) return null;
      const fullName = resolveDisplayName(principal, email);
      return { id: principal.userId, email, fullName };
    } catch {
      return null;
    }
  };

  const loadUserProfile = async (): Promise<User | null> => {
    try {
      const data = await fetchJSON<{ user: User }>("/me");
      return data.user;
    } catch {
      return null;
    }
  };

  const createPatient = async (e: FormEvent) => {
    e.preventDefault();
    if (!patientForm.fullName.trim()) {
      setStatus("Patient name is required");
      return;
    }
    setBusy(true);
    setDebugInfo(`POST ${API_BASE}/patients`);
    try {
      await fetchJSON<{ patient: Patient }>("/patients", {
        method: "POST",
        body: JSON.stringify(patientForm),
      });
      setPatientForm({ fullName: "", dob: "", sex: "female" });
      await loadPatients();
      await loadNeedsReview();
      setDebugInfo("Create patient succeeded");
      setDebugError(null);
    } catch (err) {
      recordError("Create patient", err, "Could not create patient");
    } finally {
      setBusy(false);
    }
  };

  const uploadReport = async (e: React.FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!selectedPatientId) {
      setStatus("Select a patient first");
      return;
    }
    const fileInput = (e.currentTarget.elements.namedItem("file") as HTMLInputElement) || null;
    const file = fileInput?.files?.[0];
    if (!file) {
      setStatus("Choose a PDF to upload");
      return;
    }
    setBusy(true);
    try {
      const formData = new FormData();
      formData.append("file", file);
      const res = await fetch(`${API_BASE}/patients/${selectedPatientId}/reports`, {
        method: "POST",
        body: formData,
        credentials: "include",
      });
      if (!res.ok) {
        const txt = await res.text();
        throw new Error(txt || "Upload failed");
      }
      setStatus("Upload saved");
      await loadReports(selectedPatientId);
      await loadResults(selectedPatientId);
      await loadNeedsReview();
    } catch (err) {
      recordError("Upload report", err, "Upload failed");
    } finally {
      setBusy(false);
      if (fileInput) {
        fileInput.value = "";
      }
    }
  };

  const logout = async () => {
    window.location.href = logoutUrl;
  };

  const saveProfile = async (e: FormEvent) => {
    e.preventDefault();
    setSavingProfile(true);
    setProfileStatus(null);
    try {
      const data = await fetchJSON<{ user: User }>("/me", {
        method: "PATCH",
        body: JSON.stringify({ fullName: profileName }),
      });
      setUser(data.user);
      setProfileStatus("Profile updated");
    } catch (err) {
      setProfileStatus(err instanceof Error ? err.message : "Failed to update profile");
    } finally {
      setSavingProfile(false);
    }
  };

  const saveAiKey = async (e: FormEvent) => {
    e.preventDefault();
    setSavingAi(true);
    try {
      const payload = { apiKey: aiKey.trim(), provider: aiProvider };
      const data = await fetchJSON<{ provider: string; hasKey: boolean }>("/ai/settings", {
        method: "POST",
        body: JSON.stringify(payload),
      });
      setAiSettings((prev) => {
        if (!prev) {
          return { activeProvider: payload.provider, providers: [{ provider: payload.provider, hasKey: data.hasKey }] };
        }
        const updated = prev.providers.filter((p) => p.provider !== payload.provider);
        updated.unshift({ provider: payload.provider, hasKey: data.hasKey });
        return { activeProvider: payload.provider, providers: updated };
      });
      setAiKey("");
      setStatus("AI key saved");
      setDebugError(null);
    } catch (err) {
      recordError("Save AI key", err, "Failed to save AI key");
    } finally {
      setSavingAi(false);
    }
  };

  const runDiagnostics = async () => {
    setLoadingDiagnostics(true);
    try {
      const data = await fetchJSON<Record<string, unknown>>("/diagnostics/db");
      setDiagnostics(data);
      setDebugError(null);
    } catch (err) {
      recordError("Run diagnostics", err, "Failed to run diagnostics");
    } finally {
      setLoadingDiagnostics(false);
    }
  };

  const saveShortCode = async (resultId: string, code: string) => {
    if (!code.trim()) return;
    try {
      await fetchJSON(`/results/${resultId}/confirm-mapping`, {
        method: "PATCH",
        body: JSON.stringify({ analyte_short_code: code }),
      });
      setStatus("Mapping saved");
      if (selectedPatientId) {
        await loadResults(selectedPatientId);
      }
    } catch (err) {
      recordError("Save short code", err, "Failed to save mapping");
    }
  };

  const loadTrend = async (analyteShortCode: string) => {
    if (!selectedPatientId || !analyteShortCode) return;
    try {
      const data = await fetchJSON<{ series: TrendPoint[] }>(
        `/patients/${selectedPatientId}/trend?analyte_short_code=${encodeURIComponent(analyteShortCode)}`,
      );
      setTrendData(data.series);
      setTrendCode(analyteShortCode);
    } catch (err) {
      recordError("Load trend", err, "Failed to load trend");
    }
  };

  const loadAnomalies = async (patientId: string) => {
    try {
      const data = await fetchJSON<{ anomalies: Anomaly[] }>(`/patients/${patientId}/integrity/anomalies`);
      setAnomalies(data.anomalies);
    } catch (err) {
      recordError("Load anomalies", err, "Failed to load anomalies");
    }
  };

  const saveMappingDictionary = async (e: FormEvent) => {
    e.preventDefault();
    if (!mappingForm.pattern.trim() || !mappingForm.shortCode.trim()) {
      setStatus("Pattern and short code are required");
      return;
    }
    try {
      await fetchJSON("/mapping-dictionary", {
        method: "POST",
        body: JSON.stringify({
          analyte_name_pattern: mappingForm.pattern,
          analyte_short_code: mappingForm.shortCode,
        }),
      });
      setStatus("Mapping dictionary updated");
      setMappingForm({ pattern: "", shortCode: "" });
    } catch (err) {
      recordError("Save mapping entry", err, "Failed to save mapping entry");
    }
  };

  const exportPatient = (analyte?: string) => {
    if (!selectedPatientId) return;
    const url = `${API_BASE}/patients/${selectedPatientId}/results/export${analyte ? `?analyte_short_code=${encodeURIComponent(analyte)}` : ""}`;
    window.open(url, "_blank");
  };

  const exportReport = (reportId: string) => {
    const url = `${API_BASE}/reports/${reportId}/export`;
    window.open(url, "_blank");
  };

  const headerText = useMemo(() => {
    if (user) {
      return `Welcome, ${user.fullName || user.email}`;
    }
    return "PathoLog (web-ts)";
  }, [user]);

  return (
    <div className="app-shell">
      <header>
        <div>
          <h1>{headerText}</h1>
          <p className="subtitle">Structured pathology storage and trend foundations</p>
        </div>
        <div>
          {user ? (
            <button className="ghost" onClick={logout}>
              Sign out
            </button>
          ) : null}
        </div>
      </header>

      {!user ? (
        <section className="card">
          <h2>Sign in</h2>
          <div className="stack">
            <a className="ghost" href={loginMicrosoftUrl}>
              Sign in with Microsoft
            </a>
            <a className="ghost" href={loginGoogleUrl}>
              Sign in with Google
            </a>
          </div>
          {status ? <p className="status">{status}</p> : null}
        </section>
      ) : (
        <>
          {debugError ? <div className="card"><div className="status">Error: {debugError}</div></div> : null}
          <section className="card" id="profile">
            <h2>Profile</h2>
            <div className="grid">
              <form className="stack" onSubmit={saveProfile}>
                <label>
                  Display name
                  <input
                    type="text"
                    value={profileName}
                    onChange={(e) => setProfileName(e.target.value)}
                    placeholder={user?.email ?? "Display name"}
                  />
                </label>
                <button type="submit" disabled={savingProfile}>
                  {savingProfile ? "Saving..." : "Update display name"}
                </button>
                {profileStatus ? <p className="status">{profileStatus}</p> : null}
                <p className="muted small">Shown in the app header.</p>
              </form>
              <div className="stack">
                <div>
                  <h3>Linked accounts</h3>
                  <p className="muted small">
                    Link providers by signing in with the same email address on each provider.
                  </p>
                </div>
                <div className="list">
                  <div className="list-item">
                    <div className="row">
                      <div>
                        <strong>Microsoft</strong>
                        <div className="muted small">{user?.microsoftLinked ? "Linked" : "Not linked"}</div>
                      </div>
                      {user?.microsoftLinked ? (
                        <span className="muted small">Linked</span>
                      ) : (
                        <a className="ghost" href={linkMicrosoftUrl}>
                          Link Microsoft
                        </a>
                      )}
                    </div>
                  </div>
                  <div className="list-item">
                    <div className="row">
                      <div>
                        <strong>Google</strong>
                        <div className="muted small">{user?.googleLinked ? "Linked" : "Not linked"}</div>
                      </div>
                      {user?.googleLinked ? (
                        <span className="muted small">Linked</span>
                      ) : (
                        <a className="ghost" href={linkGoogleUrl}>
                          Link Google
                        </a>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </section>
          <section className="grid">
            <div className="card">
              <h2>Patients</h2>
              <form className="stack" onSubmit={createPatient}>
                <label>
                  Full name
                  <input
                    type="text"
                    value={patientForm.fullName}
                    onChange={(e) => setPatientForm({ ...patientForm, fullName: e.target.value })}
                    placeholder="Jane Doe"
                    required
                  />
                </label>
                <label>
                  DOB (optional)
                  <input
                    type="date"
                    value={patientForm.dob}
                    onChange={(e) => setPatientForm({ ...patientForm, dob: e.target.value })}
                  />
                </label>
                <label>
                  Sex
                  <select
                    value={patientForm.sex}
                    onChange={(e) => setPatientForm({ ...patientForm, sex: e.target.value })}
                  >
                    <option value="female">Female</option>
                    <option value="male">Male</option>
                  </select>
                </label>
                <button type="submit" disabled={busy}>
                  Add patient
                </button>
                {debugInfo ? <div className="muted small">Debug: {debugInfo}</div> : null}
                {debugError ? <div className="status">Error: {debugError}</div> : null}
              </form>

              <div className="list">
                {patients.map((p) => (
                  <button
                    key={p.id}
                    className={selectedPatientId === p.id ? "list-item active" : "list-item"}
                    onClick={() => setSelectedPatientId(p.id)}
                  >
                    {p.fullName}
                  </button>
                ))}
                {!patients.length ? <p className="muted">No patients yet</p> : null}
              </div>
            </div>

            <div className="card">
              <h2>Upload report</h2>
              <form className="stack" onSubmit={uploadReport}>
                <label>
                  Patient
                  <select
                    value={selectedPatientId}
                    onChange={(e) => setSelectedPatientId(e.target.value)}
                    required
                  >
                    <option value="">Select...</option>
                    {patients.map((p) => (
                      <option key={p.id} value={p.id}>
                        {p.fullName}
                      </option>
                    ))}
                  </select>
                </label>
                <input type="file" name="file" accept="application/pdf" />
                <button type="submit" disabled={busy}>
                  Upload PDF
                </button>
              </form>
              {status ? <p className="status">{status}</p> : null}
            </div>
          </section>

          <section className="card">
            <h2>Reports</h2>
            {!reports.length ? (
              <p className="muted">Uploads will appear here per patient.</p>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>File</th>
                    <th>Status</th>
                    <th>Uploaded</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {reports.map((r) => (
                    <tr key={r.id}>
                      <td>{r.sourceFile?.originalFilename || "PDF"}</td>
                      <td>{r.parsingStatus}</td>
                      <td>{formatDate(r.createdAtUtc)}</td>
                      <td>
                        <button className="ghost" type="button" onClick={() => exportReport(r.id)}>
                          Export CSV
                        </button>
                        <a className="ghost" href={`${API_BASE}/reports/${r.id}/file`} target="_blank" rel="noreferrer">
                          View PDF
                        </a>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          <section className="card">
            <h2>Needs review</h2>
            {!needsReview.length ? (
              <p className="muted">No low-confidence extractions yet.</p>
            ) : (
              <ul className="list">
                {needsReview.map((r) => (
                  <li key={r.id} className="list-item">
                    <div className="row">
                      <div>
                        <div className="muted small">{r.patient?.fullName ?? "Unknown patient"}</div>
                        <strong>{r.sourceFile?.originalFilename ?? "Report"}</strong>
                      </div>
                      <div>{r.parsingStatus}</div>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="card">
            <h2>Results</h2>
            <div className="filters">
              <label className="inline">
                Search
                <input
                  type="text"
                  value={filter}
                  onChange={(e) => setFilter(e.target.value)}
                  placeholder="Analyte name or code"
                />
              </label>
              <label className="inline">
                Prefer normalised unit
                <input type="checkbox" checked={preferNormalised} onChange={(e) => setPreferNormalised(e.target.checked)} />
              </label>
              <div className="actions">
                <button type="button" className="ghost" onClick={() => exportPatient()}>
                  Export patient CSV
                </button>
                {trendCode ? (
                  <button type="button" className="ghost" onClick={() => exportPatient(trendCode)}>
                    Export {trendCode}
                  </button>
                ) : null}
              </div>
            </div>
            {!results.length ? (
              <p className="muted">No results ingested yet for this patient.</p>
            ) : (
              <table>
                <thead>
                  <tr>
                    <th>Analyte</th>
                    <th>Value</th>
                    <th>Short code</th>
                    <th>Reported</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredResults.map((r) => (
                    <tr key={r.id}>
                      <td>
                        <div>{r.analyteNameOriginal}</div>
                        <div className="muted small">{r.resultType}</div>
                      </td>
                      <td className={`flag-${(r as any).flagSeverity || "normal"}`}>{chooseValue(r, preferNormalised)}</td>
                      <td>
                        <input
                          type="text"
                          defaultValue={r.analyteShortCode ?? ""}
                          maxLength={8}
                          onBlur={(e) => saveShortCode(r.id, e.target.value)}
                        />
                      </td>
                      <td>{r.reportedDatetimeLocal ? formatDate(r.reportedDatetimeLocal) : ""}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          <section className="card">
            <h2>Trend</h2>
            <div className="stack">
              <label>
                Analyte short code
                <input
                  type="text"
                  value={trendCode}
                  onChange={(e) => setTrendCode(e.target.value.toUpperCase())}
                  onBlur={(e) => loadTrend(e.target.value)}
                  placeholder="e.g. TSH"
                />
              </label>
              {trendData.length >= 1 ? (
                <div className="compare">
                  {(() => {
                    const last = trendData[trendData.length - 1];
                    const prev = trendData.length > 1 ? trendData[trendData.length - 2] : null;
                    const base = trendData[0];
                    const val = last.valueNumeric ?? null;
                    const prevVal = prev?.valueNumeric ?? null;
                    const baseVal = base?.valueNumeric ?? null;
                    const deltaPrev = prevVal !== null && val !== null ? val - prevVal : null;
                    const deltaBase = baseVal !== null && val !== null ? val - baseVal : null;
                    return (
                      <>
                        <div>
                          <div className="muted small">Last</div>
                          <strong>{val ?? last.valueText ?? "-"}</strong>
                        </div>
                        <div>
                          <div className="muted small">vs prev</div>
                          <strong>{deltaPrev !== null ? (deltaPrev > 0 ? `+${deltaPrev}` : deltaPrev) : "-"}</strong>
                        </div>
                        <div>
                          <div className="muted small">vs baseline</div>
                          <strong>{deltaBase !== null ? (deltaBase > 0 ? `+${deltaBase}` : deltaBase) : "-"}</strong>
                        </div>
                      </>
                    );
                  })()}
                </div>
              ) : null}
              {trendData.length ? (
                <TrendChart points={trendData} analyte={trendCode} />
              ) : (
                <p className="muted">Enter a short code to view trend.</p>
              )}
            </div>
          </section>

          <section className="card">
            <h2>Integrity flags</h2>
            {!anomalies.length ? (
              <p className="muted">No anomalies detected.</p>
            ) : (
              <ul className="list">
                {anomalies.map((a, idx) => (
                  <li key={idx} className="list-item">
                    <strong>{a.analyte_short_code}</strong> — {a.type}
                    {a.detail ? <div className="muted small">{JSON.stringify(a.detail)}</div> : null}
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section className="card">
            <h2>Add mapping dictionary entry</h2>
            <form className="stack" onSubmit={saveMappingDictionary}>
              <label>
                Analyte name pattern
                <input
                  type="text"
                  value={mappingForm.pattern}
                  onChange={(e) => setMappingForm({ ...mappingForm, pattern: e.target.value })}
                />
              </label>
              <label>
                Preferred short code
                <input
                  type="text"
                  value={mappingForm.shortCode}
                  onChange={(e) => setMappingForm({ ...mappingForm, shortCode: e.target.value })}
                  maxLength={8}
                />
              </label>
              <button type="submit">Save mapping</button>
            </form>
          </section>

          <section className="card">
            <h2>AI settings</h2>
            <form className="stack" onSubmit={saveAiKey}>
              <label>
                Provider
                <select value={aiProvider} onChange={(e) => setAiProvider(e.target.value)}>
                  <option value="openai">OpenAI</option>
                  <option value="gemini">Gemini</option>
                </select>
              </label>
              <label>
                API key
                <input
                  type="password"
                  value={aiKey}
                  onChange={(e) => setAiKey(e.target.value)}
                  placeholder={
                    aiSettings?.providers?.find((p) => p.provider === aiProvider)?.hasKey
                      ? "Key saved (enter to replace)"
                      : "sk-..."
                  }
                />
              </label>
              <button type="submit" disabled={savingAi}>
                {aiSettings?.providers?.find((p) => p.provider === aiProvider)?.hasKey ? "Update key" : "Save key"}
              </button>
              {aiSettings?.providers?.find((p) => p.provider === aiProvider)?.hasKey ? (
                <div className="muted small">Key is stored securely on the server.</div>
              ) : null}
            </form>
          </section>

          <section className="card">
            <h2>Troubleshooting</h2>
            <p className="muted">
              Query database metadata, counts, and recent rows to validate schema and data flow.
            </p>
            <button type="button" className="ghost" onClick={runDiagnostics} disabled={loadingDiagnostics}>
              {loadingDiagnostics ? "Running diagnostics..." : "Run diagnostics"}
            </button>
            {diagnostics ? (
              <pre className="debug-block">{JSON.stringify(diagnostics, null, 2)}</pre>
            ) : null}
          </section>
        </>
      )}
    </div>
  );
}
