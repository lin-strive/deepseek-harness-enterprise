#!/usr/bin/env sh
set -eu

COMPOSE_DIR=${COMPOSE_DIR:-/opt/company-harness/litellm}
ADMIN_BASE=${ADMIN_BASE:-http://127.0.0.1:8766/api/v1/admin}
ACTIVATION_BASE=${ACTIVATION_BASE:-http://127.0.0.1:8765/api/v1/activation}
MODEL_BASE=${MODEL_BASE:-http://127.0.0.1:8765/v1}
RUN_ID=$(date +%H%M%S)$$
PLAIN_EMPLOYEE="SWACPT${RUN_ID}A"
KEY_EMPLOYEE="SWACPT${RUN_ID}K"
OVERFLOW_EMPLOYEE="SWACPT${RUN_ID}O"
TEMP_DIR=$(mktemp -d)
VIRTUAL_KEY=""

cd "$COMPOSE_DIR"
set -a
. ./.env
set +a

cleanup_employee() {
  employee_number=$1
  token_ids=$(docker compose --env-file .env exec -T postgres psql -At \
    -U "$CONTROL_PLANE_POSTGRES_USER" -d "$CONTROL_PLANE_POSTGRES_DB" \
    -c "select k.litellm_token_id from company_harness.issued_virtual_keys k join company_harness.employees e on e.id = k.employee_id where e.employee_number = '$employee_number';" 2>/dev/null || true)
  if [ -n "$token_ids" ]; then
    keys_json=$(printf '%s\n' "$token_ids" | python3 -c 'import json,sys; print(json.dumps({"keys":[line.strip() for line in sys.stdin if line.strip()]}))')
    docker compose --env-file .env exec -T litellm python -c \
      'import os,sys,urllib.request; data=sys.argv[1].encode(); request=urllib.request.Request("http://127.0.0.1:4000/key/delete", data=data, headers={"Authorization":"Bearer "+os.environ["LITELLM_MASTER_KEY"],"Content-Type":"application/json"}); urllib.request.urlopen(request).read()' \
      "$keys_json" >/dev/null 2>&1 || true
  fi
  docker compose --env-file .env exec -T postgres psql -v ON_ERROR_STOP=1 \
    -U "$CONTROL_PLANE_POSTGRES_USER" -d "$CONTROL_PLANE_POSTGRES_DB" >/dev/null <<SQL
begin;
delete from company_harness.activation_sessions where activation_code_id in (
  select c.id from company_harness.activation_codes c join company_harness.employees e on e.id = c.employee_id where e.employee_number = '$employee_number');
delete from company_harness.activation_codes c using company_harness.employees e where c.employee_id = e.id and e.employee_number = '$employee_number';
delete from company_harness.issued_virtual_keys k using company_harness.employees e where k.employee_id = e.id and e.employee_number = '$employee_number';
delete from company_harness.admin_audit_events where resource_id = '$employee_number';
delete from company_harness.employees where employee_number = '$employee_number';
delete from company_harness.departments d where d.name = '验收测试部' and not exists (
  select 1 from company_harness.employees e where e.department_id = d.id);
commit;
SQL
}

cleanup() {
  cleanup_employee "$PLAIN_EMPLOYEE" || true
  cleanup_employee "$KEY_EMPLOYEE" || true
  cleanup_employee "$OVERFLOW_EMPLOYEE" || true
  rm -f "$TEMP_DIR"/*
  rmdir "$TEMP_DIR" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

admin_request() {
  method=$1
  path=$2
  body=${3:-}
  output=$4
  if [ -n "$body" ]; then
    curl -sS -o "$output" -w '%{http_code}' -u "$CONTROL_PLANE_ADMIN_USERNAME:$CONTROL_PLANE_ADMIN_PASSWORD" \
      -X "$method" -H 'Content-Type: application/json' --data "$body" "$ADMIN_BASE$path"
  else
    curl -sS -o "$output" -w '%{http_code}' -u "$CONTROL_PLANE_ADMIN_USERNAME:$CONTROL_PLANE_ADMIN_PASSWORD" \
      -X "$method" "$ADMIN_BASE$path"
  fi
}

assert_status() {
  actual=$1
  expected=$2
  label=$3
  if [ "$actual" != "$expected" ]; then
    printf 'FAIL %s: expected %s, got %s\n' "$label" "$expected" "$actual" >&2
    exit 1
  fi
  printf 'PASS %s (%s)\n' "$label" "$actual"
}

plain_payload="{\"employeeNumber\":\"$PLAIN_EMPLOYEE\",\"displayName\":\"验收员工A\",\"department\":\"验收测试部\",\"status\":\"active\",\"monthlyBudgetCny\":200,\"requestsPerMinute\":30,\"tokensPerMinute\":100000,\"maxConcurrentRequests\":3}"

overflow_payload="{\"employeeNumber\":\"$OVERFLOW_EMPLOYEE\",\"displayName\":\"超限验收员工\",\"department\":\"验收测试部\",\"status\":\"active\",\"monthlyBudgetCny\":200,\"requestsPerMinute\":30,\"tokensPerMinute\":10000000000000,\"maxConcurrentRequests\":3}"
status=$(admin_request POST /employees "$overflow_payload" "$TEMP_DIR/overflow.json")
assert_status "$status" 400 'reject integer overflow as bad request'
error_code=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["code"])' < "$TEMP_DIR/overflow.json")
if [ "$error_code" != 'request.invalid_number' ]; then
  printf 'FAIL overflow error code: expected request.invalid_number, got %s\n' "$error_code" >&2
  exit 1
fi
printf 'PASS overflow returns actionable error code\n'

limit_payload="{\"employeeNumber\":\"$OVERFLOW_EMPLOYEE\",\"displayName\":\"超限验收员工\",\"department\":\"验收测试部\",\"status\":\"active\",\"monthlyBudgetCny\":200,\"requestsPerMinute\":30,\"tokensPerMinute\":2000001,\"maxConcurrentRequests\":3}"
status=$(admin_request POST /employees "$limit_payload" "$TEMP_DIR/limit.json")
assert_status "$status" 400 'reject quota above gateway limit'
error_message=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["message"])' < "$TEMP_DIR/limit.json")
if [ "$error_message" != 'TPM 不能超过 2000000。' ]; then
  printf 'FAIL quota validation message: %s\n' "$error_message" >&2
  exit 1
fi
printf 'PASS quota limit returns field-specific message\n'

status=$(admin_request POST /employees "$plain_payload" "$TEMP_DIR/plain-create.json")
assert_status "$status" 201 'create employee'

status=$(admin_request PUT "/employees/$PLAIN_EMPLOYEE/status" '{"status":"disabled"}' "$TEMP_DIR/plain-disable.json")
assert_status "$status" 200 'disable employee without key'

status=$(admin_request PUT "/employees/$PLAIN_EMPLOYEE/status" '{"status":"active"}' "$TEMP_DIR/plain-enable.json")
assert_status "$status" 200 'enable employee'

status=$(admin_request DELETE "/employees/$PLAIN_EMPLOYEE" '' "$TEMP_DIR/plain-delete.json")
assert_status "$status" 204 'delete employee without key history'

key_payload="{\"employeeNumber\":\"$KEY_EMPLOYEE\",\"displayName\":\"验收员工K\",\"department\":\"验收测试部\",\"status\":\"active\",\"monthlyBudgetCny\":200,\"requestsPerMinute\":30,\"tokensPerMinute\":100000,\"maxConcurrentRequests\":3}"
status=$(admin_request POST /employees "$key_payload" "$TEMP_DIR/key-create.json")
assert_status "$status" 201 'create employee for key revocation'

code_payload="{\"employeeNumbers\":[\"$KEY_EMPLOYEE\"],\"expiresInHours\":1,\"replaceExisting\":true}"
status=$(admin_request POST /activation-codes/batch "$code_payload" "$TEMP_DIR/code.json")
assert_status "$status" 200 'generate activation code'
ACTIVATION_CODE=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["codes"][0]["activationCode"])' < "$TEMP_DIR/code.json")

status=$(curl -sS -o "$TEMP_DIR/preview.json" -w '%{http_code}' -H 'Content-Type: application/json' \
  --data "{\"activationCode\":\"$ACTIVATION_CODE\",\"clientVersion\":\"acceptance\"}" "$ACTIVATION_BASE/preview")
assert_status "$status" 200 'preview activation'
SESSION_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["activationSessionId"])' < "$TEMP_DIR/preview.json")

status=$(curl -sS -o "$TEMP_DIR/confirm.json" -w '%{http_code}' -H 'Content-Type: application/json' \
  --data "{\"activationSessionId\":\"$SESSION_ID\"}" "$ACTIVATION_BASE/confirm")
assert_status "$status" 200 'confirm activation and issue key'
VIRTUAL_KEY=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["virtualKey"])' < "$TEMP_DIR/confirm.json")

status=$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $VIRTUAL_KEY" "$MODEL_BASE/models")
assert_status "$status" 200 'issued key works'

status=$(admin_request PUT "/employees/$KEY_EMPLOYEE/status" '{"status":"disabled"}' "$TEMP_DIR/key-disable.json")
assert_status "$status" 200 'disable employee with active key'
revoked_count=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["revokedVirtualKeyCount"])' < "$TEMP_DIR/key-disable.json")
if [ "$revoked_count" -ne 1 ]; then
  printf 'FAIL revoke active key: expected 1, got %s\n' "$revoked_count" >&2
  exit 1
fi
printf 'PASS revoke active key (1)\n'

status=$(curl -sS -o /dev/null -w '%{http_code}' -H "Authorization: Bearer $VIRTUAL_KEY" "$MODEL_BASE/models")
assert_status "$status" 401 'revoked key is rejected'
VIRTUAL_KEY=""

status=$(admin_request DELETE "/employees/$KEY_EMPLOYEE" '' "$TEMP_DIR/key-delete.json")
assert_status "$status" 409 'delete employee with key history is blocked'

printf 'Employee management acceptance passed.\n'
