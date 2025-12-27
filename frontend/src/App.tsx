import { useEffect, useMemo, useState, type FormEvent } from "react";
import { GoogleLogin, type CredentialResponse } from "@react-oauth/google";
import { PublicClientApplication } from "@azure/msal-browser";
import { Line } from "react-chartjs-2";
import { Chart, LineElement, PointElement, LinearScale, TimeScale, CategoryScale, Tooltip, Legend } from "chart.js";
import "./App.css";

Chart.register(LineElement, PointElement, LinearScale, TimeScale, CategoryScale, Tooltip, Legend);
type User = {
  id: string;
  email: string;
  fullName?: string | null;
  googleLinked: boolean;
  microsoftLinked: boolean;
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

const API_BASE = import.meta.env.VITE_API_BASE_URL || "http://localhost:4000";
const microsoftClientId = import.meta.env.VITE_MICROSOFT_CLIENT_ID as string | undefined;
const microsoftTenantId = import.meta.env.VITE_MICROSOFT_TENANT_ID as string | undefined;
const isMicrosoftConfigured = Boolean(microsoftClientId && microsoftTenantId);
const msalApp = isMicrosoftConfigured
  ? new PublicClientApplication({
      auth: {
        clientId: microsoftClientId!,
        authority: `https://login.microsoftonline.com/${microsoftTenantId}`,
      },
      cache: {
        cacheLocation: "sessionStorage",
        storeAuthStateInCookie: false,
      },
    })
  : null;

async function fetchJSON<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await fetch(`${API_BASE}${path}`, {
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers || {}),
    },
    ...init,
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(text || res.statusText);
  }
  return res.json() as Promise<T>;
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
  const [patientForm, setPatientForm] = useState({ fullName: "", dob: "", sex: "unknown" });
  const [mappingForm, setMappingForm] = useState({ pattern: "", shortCode: "" });
  const [status, setStatus] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [trendCode, setTrendCode] = useState("");
  const [trendData, setTrendData] = useState<TrendPoint[]>([]);
  const [anomalies, setAnomalies] = useState<Anomaly[]>([]);

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
      try {
        const data = await fetchJSON<{ user: User }>("/me");
        setUser(data.user);
        await Promise.all([loadPatients(), loadNeedsReview()]);
      } catch {
        // not signed in yet
      }
    })();
  }, []);

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

  const loadReports = async (patientId: string) => {
    const data = await fetchJSON<{ reports: Report[] }>(`/patients/${patientId}/reports`);
    setReports(data.reports);
  };

  const loadResults = async (patientId: string) => {
    const data = await fetchJSON<{ results: ResultRow[] }>(`/patients/${patientId}/results`);
    setResults(data.results);
  };

  const handleGoogle = async (cred: CredentialResponse) => {
    if (!cred.credential) {
      setStatus("Missing Google credential");
      return;
    }
    setBusy(true);
    try {
      const resp = await fetchJSON<{ user: User }>("/auth/google", {
        method: "POST",
        body: JSON.stringify({ credential: cred.credential }),
      });
      setUser(resp.user);
      setStatus(null);
      await loadPatients();
    } catch (err) {
      setStatus(err instanceof Error ? err.message : "Login failed");
    } finally {
      setBusy(false);
    }
  };

  const handleGoogleLink = async (cred: CredentialResponse) => {
    if (!cred.credential) {
      setStatus("Missing Google credential");
      return;
    }
    setBusy(true);
    try {
      const resp = await fetchJSON<{ user: User }>("/auth/google/link", {
        method: "POST",
        body: JSON.stringify({ credential: cred.credential }),
      });
      setUser(resp.user);
      setStatus("Google account linked");
    } catch (err) {
      setStatus(err instanceof Error ? err.message : "Linking failed");
    } finally {
      setBusy(false);
    }
  };

  const handleMicrosoftLogin = async (mode: "login" | "link") => {
    if (!msalApp) {
      setStatus("Microsoft sign-in is not configured");
      return;
    }
    setBusy(true);
    try {
      const result = await msalApp.loginPopup({
        scopes: ["openid", "profile", "email"],
        prompt: mode === "link" ? "select_account" : undefined,
      });
      const resp = await fetchJSON<{ user: User }>(mode === "link" ? "/auth/microsoft/link" : "/auth/microsoft", {
        method: "POST",
        body: JSON.stringify({ credential: result.idToken }),
      });
      setUser(resp.user);
      setStatus(mode === "link" ? "Microsoft account linked" : null);
      if (mode === "login") {
        await loadPatients();
      }
    } catch (err) {
      setStatus(err instanceof Error ? err.message : "Microsoft sign-in failed");
    } finally {
      setBusy(false);
    }
  };

  const createPatient = async (e: FormEvent) => {
    e.preventDefault();
    if (!patientForm.fullName.trim()) {
      setStatus("Patient name is required");
      return;
    }
    setBusy(true);
    try {
      await fetchJSON<{ patient: Patient }>("/patients", {
        method: "POST",
        body: JSON.stringify(patientForm),
      });
      setPatientForm({ fullName: "", dob: "", sex: "unknown" });
      await loadPatients();
      await loadNeedsReview();
    } catch (err) {
      setStatus(err instanceof Error ? err.message : "Could not create patient");
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
      setStatus(err instanceof Error ? err.message : "Upload failed");
    } finally {
      setBusy(false);
      if (fileInput) {
        fileInput.value = "";
      }
    }
  };

  const logout = async () => {
    await fetch(`${API_BASE}/auth/logout`, { method: "POST", credentials: "include" });
    setUser(null);
    setPatients([]);
    setReports([]);
    setSelectedPatientId("");
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
      setStatus(err instanceof Error ? err.message : "Failed to save mapping");
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
      setStatus(err instanceof Error ? err.message : "Failed to load trend");
    }
  };

  const loadAnomalies = async (patientId: string) => {
    try {
      const data = await fetchJSON<{ anomalies: Anomaly[] }>(`/patients/${patientId}/integrity/anomalies`);
      setAnomalies(data.anomalies);
    } catch (err) {
      setStatus(err instanceof Error ? err.message : "Failed to load anomalies");
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
      setStatus(err instanceof Error ? err.message : "Failed to save mapping entry");
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
    return "PathoLog";
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
          <h2>Sign in with Google</h2>
          <GoogleLogin onSuccess={handleGoogle} onError={() => setStatus("Google login failed")} useOneTap />
          {isMicrosoftConfigured ? (
            <button type="button" className="ghost" onClick={() => handleMicrosoftLogin("login")} disabled={busy}>
              Sign in with Microsoft
            </button>
          ) : null}
          {status ? <p className="status">{status}</p> : null}
        </section>
      ) : (
        <>
          {!user.googleLinked || !user.microsoftLinked ? (
            <section className="card">
              <h2>Link accounts</h2>
              <div className="stack">
                {!user.googleLinked ? (
                  <GoogleLogin onSuccess={handleGoogleLink} onError={() => setStatus("Google link failed")} />
                ) : null}
                {!user.microsoftLinked && isMicrosoftConfigured ? (
                  <button type="button" className="ghost" onClick={() => handleMicrosoftLogin("link")} disabled={busy}>
                    Link Microsoft account
                  </button>
                ) : null}
              </div>
            </section>
          ) : null}
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
                    <option value="other">Other</option>
                    <option value="unknown">Prefer not to say</option>
                  </select>
                </label>
                <button type="submit" disabled={busy}>
                  Add patient
                </button>
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
        </>
      )}
    </div>
  );
}
