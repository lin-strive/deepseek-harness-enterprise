#!/bin/sh
set -eu

: "${CONTROL_PLANE_POSTGRES_USER:?CONTROL_PLANE_POSTGRES_USER is required}"
: "${CONTROL_PLANE_POSTGRES_PASSWORD:?CONTROL_PLANE_POSTGRES_PASSWORD is required}"
: "${CONTROL_PLANE_POSTGRES_DB:?CONTROL_PLANE_POSTGRES_DB is required}"

psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=cp_user="$CONTROL_PLANE_POSTGRES_USER" \
  --set=cp_password="$CONTROL_PLANE_POSTGRES_PASSWORD" <<'SQL'
select format('create role %I login password %L', :'cp_user', :'cp_password')
where not exists (select 1 from pg_roles where rolname = :'cp_user') \gexec

select format('alter role %I login password %L', :'cp_user', :'cp_password') \gexec
SQL

psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" \
  --set=cp_user="$CONTROL_PLANE_POSTGRES_USER" \
  --set=cp_database="$CONTROL_PLANE_POSTGRES_DB" <<'SQL'
select format('create database %I owner %I', :'cp_database', :'cp_user')
where not exists (select 1 from pg_database where datname = :'cp_database') \gexec

select format('alter database %I owner to %I', :'cp_database', :'cp_user') \gexec
SQL
