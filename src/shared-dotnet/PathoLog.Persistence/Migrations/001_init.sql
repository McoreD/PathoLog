PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS patient (
    id TEXT PRIMARY KEY,
    external_id TEXT,
    full_name TEXT NOT NULL,
    date_of_birth TEXT,
    sex_at_birth TEXT,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS source_file (
    id TEXT PRIMARY KEY,
    original_file_name TEXT NOT NULL,
    hash_sha256 TEXT NOT NULL,
    stored_path TEXT NOT NULL,
    size_bytes INTEGER NOT NULL,
    imported_utc TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS report (
    id TEXT PRIMARY KEY,
    patient_id TEXT NOT NULL,
    source_file_id TEXT,
    report_date TEXT,
    laboratory_name TEXT,
    panel_name TEXT,
    specimen_description TEXT,
    created_utc TEXT NOT NULL,
    updated_utc TEXT NOT NULL,
    FOREIGN KEY (patient_id) REFERENCES patient(id),
    FOREIGN KEY (source_file_id) REFERENCES source_file(id)
);

CREATE TABLE IF NOT EXISTS subpanel (
    id TEXT PRIMARY KEY,
    report_id TEXT NOT NULL,
    name TEXT NOT NULL,
    FOREIGN KEY (report_id) REFERENCES report(id)
);

CREATE TABLE IF NOT EXISTS result (
    id TEXT PRIMARY KEY,
    report_id TEXT NOT NULL,
    subpanel_id TEXT,
    analyte_name TEXT NOT NULL,
    analyte_short_code TEXT NOT NULL,
    value_type TEXT NOT NULL,
    value_number REAL,
    value_text TEXT,
    unit TEXT,
    flag TEXT,
    source_anchor TEXT,
    extraction_confidence REAL,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (report_id) REFERENCES report(id),
    FOREIGN KEY (subpanel_id) REFERENCES subpanel(id)
);

CREATE TABLE IF NOT EXISTS reference_range (
    id TEXT PRIMARY KEY,
    result_id TEXT NOT NULL,
    low REAL,
    high REAL,
    unit TEXT,
    text TEXT,
    FOREIGN KEY (result_id) REFERENCES result(id)
);

CREATE TABLE IF NOT EXISTS comment (
    id TEXT PRIMARY KEY,
    report_id TEXT NOT NULL,
    category TEXT NOT NULL,
    text TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (report_id) REFERENCES report(id)
);

CREATE TABLE IF NOT EXISTS administrative_event (
    id TEXT PRIMARY KEY,
    patient_id TEXT NOT NULL,
    report_id TEXT,
    event_type TEXT NOT NULL,
    description TEXT NOT NULL,
    event_date_utc TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    FOREIGN KEY (patient_id) REFERENCES patient(id),
    FOREIGN KEY (report_id) REFERENCES report(id)
);

CREATE TABLE IF NOT EXISTS mapping_dictionary (
    id TEXT PRIMARY KEY,
    source_name TEXT NOT NULL,
    analyte_short_code TEXT NOT NULL,
    mapping_method TEXT NOT NULL,
    mapping_confidence REAL,
    last_confirmed_utc TEXT
);

CREATE TABLE IF NOT EXISTS review_task (
    id TEXT PRIMARY KEY,
    report_id TEXT NOT NULL,
    field_path TEXT NOT NULL,
    reason TEXT NOT NULL,
    status TEXT NOT NULL,
    created_utc TEXT NOT NULL,
    resolved_utc TEXT,
    FOREIGN KEY (report_id) REFERENCES report(id)
);

CREATE INDEX IF NOT EXISTS idx_report_patient_id ON report(patient_id);
CREATE INDEX IF NOT EXISTS idx_result_report_id ON result(report_id);
CREATE UNIQUE INDEX IF NOT EXISTS idx_mapping_dictionary_source_name ON mapping_dictionary(source_name);
