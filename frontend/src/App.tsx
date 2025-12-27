import { useEffect, useMemo, useState, type FormEvent } from "react";
import { GoogleLogin, type CredentialResponse } from "@react-oauth/google";
import "./App.css";

type User = { id: string; email: string; fullName?: string | null };
type Patient = { id: string; fullName: string };
type Report = {
  id: string;
  parsingStatus: string;
  createdAtUtc: string;
  sourceFile?: { originalFilename: string };
};
type ReviewReport = Report & { patient?: { fullName: string } };

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

export default function App() {
  const [user, setUser] = useState<User | null>(null);
  const [patients, setPatients] = useState<Patient[]>([]);
  const [selectedPatientId, setSelectedPatientId] = useState<string>("");
  const [reports, setReports] = useState<Report[]>([]);
  const [needsReview, setNeedsReview] = useState<ReviewReport[]>([]);
  const [patientForm, setPatientForm] = useState({ fullName: "", dob: "", sex: "unknown" });
  const [status, setStatus] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

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
    } else {
      setReports([]);
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
        </>
      )}
    </div>
  );
}
