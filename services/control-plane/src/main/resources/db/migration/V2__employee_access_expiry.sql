alter table company_harness.employees
  add column if not exists access_expires_at timestamptz;

create index if not exists employees_access_expiry_idx
  on company_harness.employees(access_expires_at)
  where status = 'active' and access_expires_at is not null;

comment on column company_harness.employees.access_expires_at is
  'Null means access remains valid until an administrator disables the employee.';
