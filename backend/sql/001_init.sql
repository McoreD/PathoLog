create extension if not exists "uuid-ossp";

create table if not exists users(
  id uuid primary key,
  email text unique not null,
  full_name text,
  google_sub text unique,
  microsoft_sub text unique,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists patients(
  id uuid primary key,
  owner_user_id uuid not null references users(id) on delete cascade,
  full_name text not null,
  dob date,
  sex text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists idx_patients_owner on patients(owner_user_id);

create table if not exists source_files(
  id uuid primary key,
  filename text not null,
  content_type text,
  size_bytes integer,
  bytes bytea,
  created_at timestamptz not null default now()
);

create table if not exists reports(
  id uuid primary key,
  patient_id uuid not null references patients(id) on delete cascade,
  source_file_id uuid references source_files(id) on delete set null,
  parsing_status text not null default 'pending',
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists idx_reports_patient on reports(patient_id);

create table if not exists results(
  id uuid primary key,
  report_id uuid not null references reports(id) on delete cascade,
  patient_id uuid not null references patients(id) on delete cascade,
  analyte_name_original text not null,
  analyte_short_code text,
  result_type text not null,
  value_numeric numeric,
  value_text text,
  unit_original text,
  unit_normalised text,
  reported_datetime timestamptz,
  extraction_confidence text,
  flag_severity text,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);
create index if not exists idx_results_patient on results(patient_id);
create index if not exists idx_results_short_code on results(analyte_short_code);

create table if not exists mapping_dictionary(
  id uuid primary key,
  owner_user_id uuid not null references users(id) on delete cascade,
  analyte_name_pattern text not null,
  analyte_short_code text not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique(owner_user_id, analyte_name_pattern)
);

create table if not exists ai_settings(
  id uuid primary key,
  user_id uuid not null references users(id) on delete cascade,
  provider text not null,
  api_key text not null,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique(user_id, provider)
);

create table if not exists audit_logs(
  id uuid primary key,
  entity_type text not null,
  entity_id text not null,
  action text not null,
  user_id uuid,
  payload jsonb,
  created_at timestamptz not null default now()
);
