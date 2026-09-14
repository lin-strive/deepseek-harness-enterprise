#!/usr/bin/env bash
set -euo pipefail

DEPLOY_DIR=${DEPLOY_DIR:-/opt/company-harness/litellm}
ADMIN_BASE=${ADMIN_BASE:-http://127.0.0.1:8766/api/v1/admin}
ACTIVATION_BASE=${ACTIVATION_BASE:-http://127.0.0.1:8765/api/v1/activation}
MODEL_BASE=${MODEL_BASE:-http://127.0.0.1:8765/v1}
EMPLOYEE_NUMBER=SWVALIDITY0906
DEPARTMENT=有效期验收部
TEMP_DIR=$(mktemp -d)

cd "$DEPLOY_DIR"
set -a
# shellcheck disable=SC1091
. ./.env
set +a

admin_request() {
  method=$1
  path=$2
  payload=$3
  output=$4
  if [ -n "$payload" ]; then
    curl -sS -o "$output" -w '%{http_code}' \
      -u "$CONTROL_PLANE_ADMIN_USERNAME:$CONTROL_PLANE_ADMIN_PASSWORD" \
      -H 'Content-Type: application/json' -X "$method" --data "$payload" "$ADMIN_BASE$path"
  else
    curl -sS -o "$output" -w '%{http_code}' \
      -u "$CONTROL_PLANE_ADMIN_USERNAME:$CONTROL_PLANE_ADMIN_PASSWORD" \
      -X "$method" "$ADMIN_BASE$path"
  fi
}

cleanup_employee() {
  token_ids=$(docker compose --env-file .env exec -T postgres psql \
    -U "$CONTROL_PLANE_POSTGRES_USER" -d "$CONTROL_PLANE_POSTGRES_DB" -Atc \
    "select k.litellm_token_id from company_harness.issued_virtual_keys k join company_harness.employees e on e.id = k.employee_id where e.employee_number = '$EMPLOYEE_NUMBER';" 2>/dev/null || true)
  if [ -n "$token_ids" ]; then
    keys_json=$(printf '%s\n' "$token_ids" | python3 -c 'import json,sys; print(json.dumps({"keys":[line.strip() for line in sys.stdin if line.strip()]}))')
    docker compose --env-file .env exec -T litellm python -c \
      'import os,sys,urllib.request; data=sys.argv[1].encode(); request=urllib.request.Request("http://127.0.0.1:4000/key/delete", data=data, headers={"Authorization":"Bearer "+os.environ["LITELLM_MASTER_KEY"],"Content-Type":"application/json"}); urllib.request.urlopen(request).read()' \
      "$keys_json" >/dev/null 2>&1 || true
  fi
  docker compose --env-file .env exec -T postgres psql \
    -U "$CONTROL_PLANE_POSTGRES_USER" -d "$CONTROL_PLANE_POSTGRES_DB" -v ON_ERROR_STOP=1 >/dev/null <<SQL || true
delete from company_harness.activation_sessions where activation_code_id in (
  select c.id from company_harness.activation_codes c join company_harness.employees e on e.id = c.employee_id where e.employee_number = '$EMPLOYEE_NUMBER');
delete from company_harness.activation_codes c using company_harness.employees e where c.employee_id = e.id and e.employee_number = '$EMPLOYEE_NUMBER';
delete from company_harness.issued_virtual_keys k using company_harness.employees e where k.employee_id = e.id and e.employee_number = '$EMPLOYEE_NUMBER';
delete from company_harness.admin_audit_events where resource_id = '$EMPLOYEE_NUMBER';
delete from company_harness.employees where employee_number = '$EMPLOYEE_NUMBER';
delete from company_harness.departments d where d.name = '$DEPARTMENT' and not exists (
  select 1 from company_harness.employees e where e.department_id = d.id);
SQL
}

cleanup() {
  cleanup_employee
  rm -rf "$TEMP_DIR"
}
trap cleanup EXIT
cleanup_employee

access_expires_at=$(date -d '+45 seconds' --iso-8601=seconds)
activation_requested_at=$(date -d '+1 day' --iso-8601=seconds)
create_payload=$(python3 -c 'import json,sys; print(json.dumps({"employeeNumber":sys.argv[1],"displayName":"有效期验收员工","department":sys.argv[2],"status":"active","accessExpiresAt":None,"monthlyBudgetCny":200,"requestsPerMinute":30,"tokensPerMinute":100000,"maxConcurrentRequests":3}, ensure_ascii=False))' "$EMPLOYEE_NUMBER" "$DEPARTMENT")

status=$(admin_request POST /employees "$create_payload" "$TEMP_DIR/create.json")
[ "$status" = 201 ] || { cat "$TEMP_DIR/create.json" >&2; exit 1; }
python3 -c 'import json,sys; assert json.load(open(sys.argv[1], encoding="utf-8")).get("accessExpiresAt") is None' "$TEMP_DIR/create.json"
printf 'PASS create long-term employee access\n'

expiry_payload=$(python3 -c 'import json,sys; print(json.dumps({"accessExpiresAt":sys.argv[1]}))' "$access_expires_at")
status=$(admin_request PUT "/employees/$EMPLOYEE_NUMBER/access-expiry" "$expiry_payload" "$TEMP_DIR/expiry.json")
[ "$status" = 200 ] || { cat "$TEMP_DIR/expiry.json" >&2; exit 1; }
python3 -c 'import datetime,json,sys; body=json.load(open(sys.argv[1], encoding="utf-8")); actual=datetime.datetime.fromisoformat(body["accessExpiresAt"]); expected=datetime.datetime.fromisoformat(sys.argv[2]); assert abs((actual-expected).total_seconds()) < 1' "$TEMP_DIR/expiry.json" "$access_expires_at"
printf 'PASS change employee to a fixed access expiry\n'

overlong=$(date -d '+91 days' --iso-8601=seconds)
overlong_payload=$(python3 -c 'import json,sys; print(json.dumps({"employeeNumbers":[sys.argv[1]],"expiresAt":sys.argv[2],"replaceExisting":True}))' "$EMPLOYEE_NUMBER" "$overlong")
status=$(admin_request POST /activation-codes/batch "$overlong_payload" "$TEMP_DIR/overlong.json")
[ "$status" = 400 ] || { printf 'Expected 400 for 91-day activation code, got %s\n' "$status" >&2; exit 1; }
printf 'PASS reject activation expiry above 90 days\n'

code_payload=$(python3 -c 'import json,sys; print(json.dumps({"employeeNumbers":[sys.argv[1]],"expiresAt":sys.argv[2],"replaceExisting":True}))' "$EMPLOYEE_NUMBER" "$activation_requested_at")
status=$(admin_request POST /activation-codes/batch "$code_payload" "$TEMP_DIR/code.json")
[ "$status" = 200 ] || { cat "$TEMP_DIR/code.json" >&2; exit 1; }
python3 -c 'import datetime,json,sys; body=json.load(open(sys.argv[1], encoding="utf-8")); actual=datetime.datetime.fromisoformat(body["codes"][0]["expiresAt"]); access=datetime.datetime.fromisoformat(sys.argv[2]); assert abs((actual-access).total_seconds()) < 1, (actual,access)' "$TEMP_DIR/code.json" "$access_expires_at"
printf 'PASS cap activation code at employee access expiry\n'

activation_code=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["codes"][0]["activationCode"])' "$TEMP_DIR/code.json")
status=$(curl -sS -o "$TEMP_DIR/preview.json" -w '%{http_code}' -H 'Content-Type: application/json' \
  --data "{\"activationCode\":\"$activation_code\",\"clientVersion\":\"acceptance\"}" "$ACTIVATION_BASE/preview")
[ "$status" = 200 ] || { cat "$TEMP_DIR/preview.json" >&2; exit 1; }
session_id=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["activationSessionId"])' "$TEMP_DIR/preview.json")
status=$(curl -sS -o "$TEMP_DIR/confirm.json" -w '%{http_code}' -H 'Content-Type: application/json' \
  --data "{\"activationSessionId\":\"$session_id\"}" "$ACTIVATION_BASE/confirm")
[ "$status" = 200 ] || { cat "$TEMP_DIR/confirm.json" >&2; exit 1; }
virtual_key=$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1], encoding="utf-8"))["virtualKey"])' "$TEMP_DIR/confirm.json")
status=$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $virtual_key" "$MODEL_BASE/models")
[ "$status" = 200 ] || { printf 'Issued key did not work before expiry\n' >&2; exit 1; }
printf 'PASS issue a working key before employee expiry\n'

deadline=$(( $(date +%s) + 110 ))
while [ "$(date +%s)" -lt "$deadline" ]; do
  status=$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $virtual_key" "$MODEL_BASE/models")
  employee_status=$(admin_request GET /employees '' "$TEMP_DIR/employees.json")
  [ "$employee_status" = 200 ] || exit 1
  current_state=$(python3 -c 'import json,sys; rows=json.load(open(sys.argv[1], encoding="utf-8")); print(next(row["status"] for row in rows if row["employeeNumber"]==sys.argv[2]))' "$TEMP_DIR/employees.json" "$EMPLOYEE_NUMBER")
  if [ "$status" = 401 ] && [ "$current_state" = disabled ]; then
    printf 'PASS automatically disable employee and revoke key after expiry\n'
    exit 0
  fi
  sleep 5
done

printf 'Timed out waiting for automatic employee expiry\n' >&2
exit 1
