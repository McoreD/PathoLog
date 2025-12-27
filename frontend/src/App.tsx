import { useEffect, useMemo, useState, type FormEvent } from "react";
import { GoogleLogin, type CredentialResponse } from "@react-oauth/google";
import { Line } from "react-chartjs-2";
import { Chart, LineElement, PointElement, LinearScale, TimeScale, CategoryScale, Tooltip, Legend } from "chart.js";
import "./App.css";

Chart.register(LineElement, PointElement, LinearScale, TimeScale, CategoryScale, Tooltip, Legend);
type User = { id: string; email: string; fullName?: string | null };
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

const API_BASE = import.meta.env.VITE_API_BASE_URL || "http://localhost:4000";

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
  const [patientForm, setPatientForm] = useState({ fullName: "", dob: "", sex: "unknown" });
  const [mappingForm, setMappingForm] = useState({ pattern: "", shortCode: "" });
  const [status, setStatus] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [trendCode, setTrendCode] = useState("");
  const [trendData, setTrendData] = useState<TrendPoint[]>([]);

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
    } else {
      setReports([]);
      setResults([]);
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
          {status ? <p className="status">{status}</p> : null}
        </section>
      ) : (
        <>
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
                  </tr>
                </thead>
                <tbody>
                  {reports.map((r) => (
                    <tr key={r.id}>
                      <td>{r.sourceFile?.originalFilename || "PDF"}</td>
                      <td>{r.parsingStatus}</td>
                      <td>{formatDate(r.createdAtUtc)}</td>
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
                  {results.map((r) => (
                    <tr key={r.id}>
                      <td>
                        <div>{r.analyteNameOriginal}</div>
                        <div className="muted small">{r.resultType}</div>
                      </td>
                      <td>
                        {r.valueNumeric ?? r.valueText ?? ""}
                        {r.unitOriginal ? ` ${r.unitOriginal}` : ""}
                      </td>
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
              {trendData.length ? (
                <TrendChart points={trendData} analyte={trendCode} />
              ) : (
                <p className="muted">Enter a short code to view trend.</p>
              )}
            </div>
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
