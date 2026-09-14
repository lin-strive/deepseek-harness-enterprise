create extension if not exists pgcrypto;
create schema if not exists company_harness;

create table if not exists company_harness.departments (
  id uuid primary key default gen_random_uuid(),
  name text not null unique,
  created_at timestamptz not null default now()
);

create table if not exists company_harness.employees (
  id uuid primary key default gen_random_uuid(),
  employee_number text not null unique,
  display_name text not null,
  department_id uuid not null references company_harness.departments(id),
  status text not null check (status in ('active', 'disabled')),
  monthly_budget_cny numeric(12, 2) not null check (monthly_budget_cny >= 0),
  rpm_limit integer not null check (rpm_limit > 0),
  tpm_limit integer not null check (tpm_limit > 0),
  max_concurrent_requests integer not null check (max_concurrent_requests > 0),
  row_version bigint not null default 1,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create table if not exists company_harness.activation_codes (
  id uuid primary key default gen_random_uuid(),
  employee_id uuid not null references company_harness.employees(id),
  code_hash char(64) not null unique,
  expires_at timestamptz not null,
  redeemed_at timestamptz,
  revoked_at timestamptz,
  created_by uuid,
  created_at timestamptz not null default now(),
  check (redeemed_at is null or redeemed_at >= created_at)
);

create table if not exists company_harness.activation_sessions (
  id uuid primary key default gen_random_uuid(),
  activation_code_id uuid not null references company_harness.activation_codes(id),
  session_hash char(64) not null unique,
  expires_at timestamptz not null,
  consumed_at timestamptz,
  created_at timestamptz not null default now()
);

create table if not exists company_harness.issued_virtual_keys (
  id uuid primary key default gen_random_uuid(),
  employee_id uuid not null references company_harness.employees(id),
  litellm_token_id text not null unique,
  key_alias text not null,
  status text not null check (status in ('active', 'disabled', 'rotated')),
  created_at timestamptz not null default now(),
  disabled_at timestamptz
);

create table if not exists company_harness.release_manifests (
  id uuid primary key default gen_random_uuid(),
  channel text not null check (channel in ('candidate', 'pilot', 'stable', 'rollback')),
  manifest_version text not null,
  manifest_json jsonb not null,
  signature text not null,
  published_at timestamptz not null,
  created_at timestamptz not null default now(),
  unique (channel, manifest_version)
);

create table if not exists company_harness.admin_audit_events (
  id uuid primary key default gen_random_uuid(),
  actor_admin_id uuid not null,
  action text not null,
  resource_type text not null,
  resource_id text not null,
  metadata jsonb not null default '{}'::jsonb,
  occurred_at timestamptz not null default now()
);

create index if not exists activation_codes_employee_id_idx on company_harness.activation_codes(employee_id);
create index if not exists activation_sessions_expires_at_idx on company_harness.activation_sessions(expires_at);
create index if not exists issued_virtual_keys_employee_id_idx on company_harness.issued_virtual_keys(employee_id);
create index if not exists admin_audit_events_occurred_at_idx on company_harness.admin_audit_events(occurred_at desc);

comment on column company_harness.activation_codes.code_hash is 'HMAC-SHA-256; plaintext activation codes are never stored.';
comment on column company_harness.activation_sessions.session_hash is 'SHA-256 of the opaque preview session token.';
comment on table company_harness.issued_virtual_keys is 'Stores LiteLLM token identifiers only; never stores plaintext virtual keys.';
