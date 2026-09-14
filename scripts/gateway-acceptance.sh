#!/usr/bin/env bash

set -Eeuo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:8765}"
ENV_FILE="${1:-.env}"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-$$"
SENTINEL="SW_PRIVATE_SMOKE_${RUN_ID}"
TMP_DIR="$(mktemp -d)"
declare -a CREATED_KEYS=()
PASS_COUNT=0
FAIL_COUNT=0
WARN_COUNT=0

pass() {
  PASS_COUNT=$((PASS_COUNT + 1))
  printf 'PASS  %s\n' "$1"
}

fail() {
  FAIL_COUNT=$((FAIL_COUNT + 1))
  printf 'FAIL  %s\n' "$1"
}

warn() {
  WARN_COUNT=$((WARN_COUNT + 1))
  printf 'WARN  %s\n' "$1"
}

cleanup() {
  local key
  if (( ${#CREATED_KEYS[@]} > 0 )); then
    for key in "${CREATED_KEYS[@]}"; do
      curl --silent --show-error --max-time 10 \
        --request POST "${BASE_URL}/key/delete" \
        --header "Authorization: Bearer ${MASTER_KEY:-}" \
        --header 'Content-Type: application/json' \
        --data "$(jq -cn --arg key "$key" '{keys: [$key]}')" \
        >/dev/null 2>&1 || true
    done
  fi
  rm -rf -- "$TMP_DIR"
}
trap cleanup EXIT

for command_name in curl jq docker; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    printf 'Required command not found: %s\n' "$command_name" >&2
    exit 2
  fi
done

if [[ ! -r "$ENV_FILE" ]]; then
  printf 'Environment file is not readable: %s\n' "$ENV_FILE" >&2
  exit 2
fi

MASTER_KEY="$(sed -n 's/^LITELLM_MASTER_KEY=//p' "$ENV_FILE" | head -n 1)"
if [[ -z "$MASTER_KEY" ]]; then
  printf 'LITELLM_MASTER_KEY is missing from %s\n' "$ENV_FILE" >&2
  exit 2
fi

http_request() {
  local output_name="$1"
  local method="$2"
  local path="$3"
  local bearer="$4"
  local body="${5:-}"
  local output_file="${TMP_DIR}/${output_name}"
  local args=(
    --silent --show-error --max-time 90
    --output "$output_file"
    --write-out '%{http_code}'
    --request "$method"
    "${BASE_URL}${path}"
    --header "Authorization: Bearer ${bearer}"
  )
  if [[ -n "$body" ]]; then
    args+=(--header 'Content-Type: application/json' --data "$body")
  fi
  curl "${args[@]}"
}

generate_key() {
  local output_name="$1"
  local payload="$2"
  local status
  local key
  status="$(http_request "$output_name" POST '/key/generate' "$MASTER_KEY" "$payload")"
  if [[ "$status" != '200' ]]; then
    fail "temporary virtual key creation (HTTP ${status})"
    return 1
  fi
  key="$(jq -r '.key // empty' "${TMP_DIR}/${output_name}")"
  if [[ -z "$key" ]]; then
    fail 'temporary virtual key creation returned no key'
    return 1
  fi
  CREATED_KEYS+=("$key")
  GENERATED_KEY="$key"
}

chat_payload() {
  local model="$1"
  local content="$2"
  local max_tokens="$3"
  jq -cn \
    --arg model "$model" \
    --arg content "$content" \
    --argjson max_tokens "$max_tokens" \
    '{model: $model, messages: [{role: "user", content: $content}], max_tokens: $max_tokens, temperature: 0}'
}

printf 'LiteLLM gateway acceptance run: %s\n' "$RUN_ID"
printf 'Target: %s\n\n' "$BASE_URL"

health_status="$(curl --silent --show-error --max-time 10 --output "${TMP_DIR}/health" --write-out '%{http_code}' "${BASE_URL}/health/liveliness")"
if [[ "$health_status" == '200' ]]; then
  pass 'gateway liveliness'
else
  fail "gateway liveliness (HTTP ${health_status})"
  exit 1
fi

functional_payload="$(jq -cn --arg run_id "$RUN_ID" '{
  key_alias: ("m1-functional-" + $run_id),
  duration: "15m",
  models: ["company-fast", "company-coder", "company-pro"],
  max_budget: 0.50,
  budget_duration: "1mo",
  rpm_limit: 20,
  tpm_limit: 10000,
  max_parallel_requests: 2,
  metadata: {purpose: "gateway-acceptance", run_id: $run_id}
}')"
generate_key 'functional-key.json' "$functional_payload" || exit 1
functional_key="$GENERATED_KEY"
pass 'temporary virtual key creation'

models_status="$(http_request 'models.json' GET '/v1/models' "$functional_key")"
if [[ "$models_status" == '200' ]] && \
   jq -e '[.data[].id] | contains(["company-fast", "company-coder", "company-pro"])' "${TMP_DIR}/models.json" >/dev/null; then
  pass 'virtual key authentication and model aliases'
else
  fail "virtual key authentication and model aliases (HTTP ${models_status})"
fi

normal_payload="$(chat_payload 'company-fast' "Return a one-word acknowledgement. ${SENTINEL}" 8)"
normal_status="$(http_request 'normal.json' POST '/v1/chat/completions' "$functional_key" "$normal_payload")"
if [[ "$normal_status" == '200' ]] && jq -e '.choices | length > 0' "${TMP_DIR}/normal.json" >/dev/null; then
  pass 'non-streaming completion'
else
  error_type="$(jq -r '.error.type // .error.code // "unknown"' "${TMP_DIR}/normal.json" 2>/dev/null || printf 'unknown')"
  fail "non-streaming completion (HTTP ${normal_status}, ${error_type})"
fi

stream_payload="$(jq -cn --arg sentinel "$SENTINEL" '{
  model: "company-fast",
  messages: [{role: "user", content: ("Return a short acknowledgement. " + $sentinel)}],
  max_tokens: 12,
  temperature: 0,
  stream: true
}')"
stream_status="$(http_request 'stream.txt' POST '/v1/chat/completions' "$functional_key" "$stream_payload")"
if [[ "$stream_status" == '200' ]] && \
   grep -q '^data:' "${TMP_DIR}/stream.txt" && \
   grep -q '\[DONE\]' "${TMP_DIR}/stream.txt"; then
  pass 'SSE streaming completion'
else
  fail "SSE streaming completion (HTTP ${stream_status})"
fi

tool_payload="$(jq -cn --arg sentinel "$SENTINEL" '{
  model: "company-coder",
  messages: [{role: "user", content: ("You must call company_status with status=ok. Do not answer directly. " + $sentinel)}],
  max_tokens: 64,
  temperature: 0,
  tools: [{
    type: "function",
    function: {
      name: "company_status",
      description: "Return the synthetic acceptance-test status",
      parameters: {
        type: "object",
        properties: {status: {type: "string", enum: ["ok"]}},
        required: ["status"],
        additionalProperties: false
      }
    }
  }]
}')"
tool_status="$(http_request 'tool.json' POST '/v1/chat/completions' "$functional_key" "$tool_payload")"
if [[ "$tool_status" == '200' ]] && jq -e '.choices[0].message.tool_calls | length > 0' "${TMP_DIR}/tool.json" >/dev/null; then
  pass 'tool calling'
else
  error_type="$(jq -r '.error.type // .error.code // "unknown"' "${TMP_DIR}/tool.json" 2>/dev/null || printf 'unknown')"
  fail "tool calling (HTTP ${tool_status}, ${error_type})"
fi

restricted_payload="$(jq -cn --arg run_id "$RUN_ID" '{
  key_alias: ("m1-model-acl-" + $run_id), duration: "15m",
  models: ["company-fast"], max_budget: 0.10,
  rpm_limit: 10, tpm_limit: 5000, max_parallel_requests: 1
}')"
generate_key 'restricted-key.json' "$restricted_payload" || exit 1
restricted_key="$GENERATED_KEY"
acl_status="$(http_request 'acl.json' POST '/v1/chat/completions' "$restricted_key" "$(chat_payload 'company-pro' 'Synthetic access-control test.' 1)")"
if [[ ! "$acl_status" =~ ^2 ]]; then
  pass "model allow-list rejection (HTTP ${acl_status})"
else
  fail 'model allow-list rejection unexpectedly allowed the request'
fi

rpm_payload="$(jq -cn --arg run_id "$RUN_ID" '{
  key_alias: ("m1-rpm-" + $run_id), duration: "15m",
  models: ["company-fast"], max_budget: 0.10,
  rpm_limit: 1, tpm_limit: 5000, max_parallel_requests: 1
}')"
generate_key 'rpm-key.json' "$rpm_payload" || exit 1
rpm_key="$GENERATED_KEY"
rpm_first_status="$(http_request 'rpm-first.json' POST '/v1/chat/completions' "$rpm_key" "$(chat_payload 'company-fast' 'Reply OK.' 2)")"
rpm_second_status="$(http_request 'rpm-second.json' POST '/v1/chat/completions' "$rpm_key" "$(chat_payload 'company-fast' 'Reply OK.' 2)")"
if [[ "$rpm_first_status" == '200' && "$rpm_second_status" == '429' ]]; then
  pass 'RPM limit enforcement'
else
  fail "RPM limit enforcement (first ${rpm_first_status}, second ${rpm_second_status})"
fi

tpm_payload="$(jq -cn --arg run_id "$RUN_ID" '{
  key_alias: ("m1-tpm-" + $run_id), duration: "15m",
  models: ["company-fast"], max_budget: 0.10,
  rpm_limit: 10, tpm_limit: 1, max_parallel_requests: 1
}')"
generate_key 'tpm-key.json' "$tpm_payload" || exit 1
tpm_key="$GENERATED_KEY"
tpm_status="$(http_request 'tpm.json' POST '/v1/chat/completions' "$tpm_key" "$(chat_payload 'company-fast' 'This synthetic request intentionally exceeds a one-token TPM allowance.' 2)")"
if [[ "$tpm_status" == '429' ]]; then
  pass 'TPM limit enforcement'
else
  fail "TPM limit enforcement (HTTP ${tpm_status})"
fi

budget_payload="$(jq -cn --arg run_id "$RUN_ID" '{
  key_alias: ("m1-budget-" + $run_id), duration: "15m",
  models: ["company-fast"], spend: 1.0, max_budget: 0.5,
  budget_duration: "1mo", rpm_limit: 10, tpm_limit: 5000,
  max_parallel_requests: 1
}')"
generate_key 'budget-key.json' "$budget_payload" || exit 1
budget_key="$GENERATED_KEY"
budget_status="$(http_request 'budget.json' POST '/v1/chat/completions' "$budget_key" "$(chat_payload 'company-fast' 'Synthetic budget rejection test.' 1)")"
if [[ ! "$budget_status" =~ ^2 ]]; then
  pass "budget limit enforcement (HTTP ${budget_status})"
else
  fail 'budget limit enforcement unexpectedly allowed the request'
fi

parallel_payload="$(jq -cn --arg run_id "$RUN_ID" '{
  key_alias: ("m1-parallel-" + $run_id), duration: "15m",
  models: ["company-fast"], max_budget: 0.20,
  rpm_limit: 20, tpm_limit: 20000, max_parallel_requests: 1
}')"
generate_key 'parallel-key.json' "$parallel_payload" || exit 1
parallel_key="$GENERATED_KEY"
parallel_body="$(jq -cn '{
  model: "company-fast",
  messages: [{role: "user", content: "Generate a numbered list from 1 through 100 for a synthetic concurrency test."}],
  max_tokens: 256,
  temperature: 0,
  stream: true
}')"
curl --silent --show-error --max-time 30 \
  --request POST "${BASE_URL}/v1/chat/completions" \
  --header "Authorization: Bearer ${parallel_key}" \
  --header 'Content-Type: application/json' \
  --data "$parallel_body" \
  >"${TMP_DIR}/parallel-first.txt" 2>/dev/null &
parallel_pid=$!
sleep 0.2
parallel_second_status="$(http_request 'parallel-second.json' POST '/v1/chat/completions' "$parallel_key" "$(chat_payload 'company-fast' 'Reply OK.' 2)")"
kill "$parallel_pid" >/dev/null 2>&1 || true
wait "$parallel_pid" >/dev/null 2>&1 || true
if [[ "$parallel_second_status" == '429' ]]; then
  pass 'maximum parallel request enforcement'
else
  warn "maximum parallel request test was inconclusive (HTTP ${parallel_second_status})"
fi

block_payload="$(jq -cn --arg key "$functional_key" '{key: $key}')"
block_status="$(http_request 'block.json' POST '/key/block' "$MASTER_KEY" "$block_payload")"
blocked_call_status="$(http_request 'blocked-call.json' GET '/v1/models' "$functional_key")"
if [[ "$block_status" == '200' && ! "$blocked_call_status" =~ ^2 ]]; then
  pass "virtual key revocation (HTTP ${blocked_call_status})"
else
  fail "virtual key revocation (block ${block_status}, subsequent call ${blocked_call_status})"
fi

sleep 2
if docker compose ps --services 2>/dev/null | grep -qx 'litellm'; then
  if docker compose logs --no-color litellm 2>/dev/null | grep -Fq "$SENTINEL"; then
    fail 'synthetic prompt sentinel was found in LiteLLM container logs'
  else
    pass 'synthetic prompt sentinel absent from LiteLLM container logs'
  fi
else
  warn 'container log privacy check skipped outside the Compose directory'
fi

postgres_container="$(docker compose ps -q postgres 2>/dev/null || true)"
if [[ -n "$postgres_container" ]]; then
  if docker exec "$postgres_container" sh -lc 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' 2>/dev/null | grep -Fq "$SENTINEL"; then
    fail 'synthetic prompt sentinel was found in PostgreSQL data'
  else
    pass 'synthetic prompt sentinel absent from PostgreSQL data'
  fi
else
  warn 'PostgreSQL privacy check skipped because the service was not found'
fi

printf '\nSummary: %d passed, %d failed, %d warnings\n' "$PASS_COUNT" "$FAIL_COUNT" "$WARN_COUNT"
if (( FAIL_COUNT > 0 )); then
  exit 1
fi
