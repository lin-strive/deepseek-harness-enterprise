#!/usr/bin/env bash

set -Eeuo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:8765}"
ENV_FILE="${1:-.env}"
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-$$"
KEY_ALIAS="m1-web-search-${RUN_ID}"
SENTINEL="SW_WEB_SEARCH_PRIVATE_${RUN_ID}"
TMP_DIR="$(mktemp -d)"
MASTER_KEY=''
TEST_KEY=''

cleanup() {
  if [[ -n "$TEST_KEY" && -n "$MASTER_KEY" ]]; then
    curl --silent --show-error --max-time 10 \
      --request POST "${BASE_URL}/key/delete" \
      --header "Authorization: Bearer ${MASTER_KEY}" \
      --header 'Content-Type: application/json' \
      --data "$(jq -cn --arg key "$TEST_KEY" '{keys: [$key]}')" \
      >/dev/null 2>&1 || true
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

key_payload="$(jq -cn --arg alias "$KEY_ALIAS" --arg run_id "$RUN_ID" '{
  key_alias: $alias,
  duration: "15m",
  models: ["company-fast"],
  max_budget: 0.25,
  budget_duration: "1mo",
  rpm_limit: 5,
  tpm_limit: 20000,
  max_parallel_requests: 1,
  metadata: {purpose: "web-search-compatibility", run_id: $run_id}
}')"

key_status="$(curl --silent --show-error --max-time 20 \
  --output "${TMP_DIR}/key.json" \
  --write-out '%{http_code}' \
  --request POST "${BASE_URL}/key/generate" \
  --header "Authorization: Bearer ${MASTER_KEY}" \
  --header 'Content-Type: application/json' \
  --data "$key_payload")"

if [[ "$key_status" != '200' ]]; then
  printf 'FAIL  temporary key creation (HTTP %s)\n' "$key_status"
  exit 1
fi

TEST_KEY="$(jq -r '.key // empty' "${TMP_DIR}/key.json")"
if [[ -z "$TEST_KEY" ]]; then
  printf 'FAIL  temporary key response did not contain a key\n'
  exit 1
fi
printf 'PASS  temporary virtual key created\n'

messages_payload="$(jq -cn --arg marker "$SENTINEL" '{
  model: "company-fast",
  max_tokens: 256,
  messages: [{
    role: "user",
    content: ("Search the web for the official DeepSeek Harness GitHub repository and return one source. Privacy test marker: " + $marker)
  }],
  tools: [{
    type: "web_search_20250305",
    name: "web_search",
    max_uses: 1
  }]
}')"

message_status="$(curl --silent --show-error --max-time 120 \
  --output "${TMP_DIR}/messages.json" \
  --write-out '%{http_code}' \
  --request POST "${BASE_URL}/v1/messages" \
  --header "Authorization: Bearer ${TEST_KEY}" \
  --header "x-api-key: ${TEST_KEY}" \
  --header 'anthropic-version: 2023-06-01' \
  --header 'Content-Type: application/json' \
  --data "$messages_payload")"

printf 'INFO  /v1/messages returned HTTP %s\n' "$message_status"

if [[ "$message_status" == '200' ]]; then
  result_count="$(jq '[.. | objects | select(.type? == "web_search_tool_result")] | length' "${TMP_DIR}/messages.json")"
  source_count="$(jq '[.. | objects | select((.url? | type) == "string") | .url] | unique | length' "${TMP_DIR}/messages.json")"
  response_types="$(jq -r '[.. | objects | .type? // empty] | unique | join(",")' "${TMP_DIR}/messages.json")"
  printf 'INFO  response block types: %s\n' "${response_types:-none}"
  printf 'INFO  web search result blocks: %s; unique source URLs: %s\n' "$result_count" "$source_count"
  if (( result_count > 0 )); then
    printf 'PASS  LiteLLM supports Harness native DeepSeek web search\n'
    compatibility_ok=1
  else
    printf 'FAIL  response contained no web_search_tool_result block\n'
    compatibility_ok=0
  fi
else
  error_type="$(jq -r '.error.type // .type // "unknown"' "${TMP_DIR}/messages.json" 2>/dev/null || printf 'unknown')"
  error_code="$(jq -r '.error.code // .code // "unknown"' "${TMP_DIR}/messages.json" 2>/dev/null || printf 'unknown')"
  error_message="$(jq -r '.error.message // .message // "no error message"' "${TMP_DIR}/messages.json" 2>/dev/null | tr '\n' ' ' | cut -c1-500 || printf 'unreadable')"
  printf 'FAIL  native search request rejected (type=%s, code=%s)\n' "$error_type" "$error_code"
  printf 'INFO  redacted diagnostic: %s\n' "$error_message"
  compatibility_ok=0
fi

sleep 2
if docker compose logs --no-color litellm 2>/dev/null | grep -Fq "$SENTINEL"; then
  printf 'FAIL  synthetic search marker was found in LiteLLM logs\n'
  privacy_ok=0
else
  printf 'PASS  synthetic search marker absent from LiteLLM logs\n'
  privacy_ok=1
fi

postgres_container="$(docker compose ps -q postgres 2>/dev/null || true)"
if [[ -n "$postgres_container" ]]; then
  if docker exec "$postgres_container" sh -lc 'pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB"' 2>/dev/null | grep -Fq "$SENTINEL"; then
    printf 'FAIL  synthetic search marker was found in PostgreSQL data\n'
    privacy_ok=0
  else
    printf 'PASS  synthetic search marker absent from PostgreSQL data\n'
  fi
fi

if (( compatibility_ok == 1 && privacy_ok == 1 )); then
  exit 0
fi
exit 1
