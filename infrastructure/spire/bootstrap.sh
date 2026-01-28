#!/bin/sh
set -euo pipefail

SOCKET_PATH="/run/spire/sockets/server.sock"
BOOTSTRAP_DIR="/run/spire/bootstrap"
TOKEN_FILE="${BOOTSTRAP_DIR}/join-token"
TOKEN_TTL="${SPIRE_JOIN_TOKEN_TTL:-86400}"
BUNDLE_FILE="${BOOTSTRAP_DIR}/trust-bundle.pem"
TRUST_DOMAIN="${SPIRE_TRUST_DOMAIN:-aether-guard.local}"

mkdir -p "${BOOTSTRAP_DIR}"

echo "Waiting for SPIRE server..."
attempt=0
while [ "${attempt}" -lt 60 ]; do
  if spire-server healthcheck -socketPath "${SOCKET_PATH}" >/dev/null 2>&1; then
    break
  fi
  attempt=$((attempt + 1))
  sleep 1
done

if ! spire-server healthcheck -socketPath "${SOCKET_PATH}" >/dev/null 2>&1; then
  echo "SPIRE server not ready; aborting bootstrap."
  exit 1
fi

if [ -s "${TOKEN_FILE}" ]; then
  token="$(tr -d '\r\n' < "${TOKEN_FILE}")"
else
  token_output="$(spire-server token generate -socketPath "${SOCKET_PATH}" -ttl "${TOKEN_TTL}" -output json)"
  token="$(printf "%s" "${token_output}" | grep -o '\"value\":\"[^\"]*\"' | sed 's/\"value\":\"//;s/\"$//')"
  if [ -z "${token}" ]; then
    echo "Failed to parse SPIRE join token."
    exit 1
  fi
  printf "%s" "${token}" > "${TOKEN_FILE}"
fi

spire-server bundle show -socketPath "${SOCKET_PATH}" -format pem > "${BUNDLE_FILE}"

parent_id="spiffe://${TRUST_DOMAIN}/spire/agent/join_token/$(cat "${TOKEN_FILE}")"

create_entry() {
  spiffe_id="$1"
  selector="$2"

  entry_output="$(spire-server entry show -socketPath "${SOCKET_PATH}" -spiffeID "${spiffe_id}")"
  if printf "%s" "${entry_output}" | grep -q "Found 0 entries"; then
    spire-server entry create \
      -socketPath "${SOCKET_PATH}" \
      -parentID "${parent_id}" \
      -spiffeID "${spiffe_id}" \
      -selector "${selector}"
    return
  fi

  entry_id="$(printf "%s" "${entry_output}" | awk -F': ' '/Entry ID/{print $2; exit}')"
  entry_parent="$(printf "%s" "${entry_output}" | awk -F': ' '/Parent ID/{print $2; exit}')"
  if [ "${entry_parent}" != "${parent_id}" ]; then
    spire-server entry delete \
      -socketPath "${SOCKET_PATH}" \
      -entryID "${entry_id}" >/dev/null
    spire-server entry create \
      -socketPath "${SOCKET_PATH}" \
      -parentID "${parent_id}" \
      -spiffeID "${spiffe_id}" \
      -selector "${selector}"
  fi
}

create_entry "spiffe://${TRUST_DOMAIN}/core-service" "docker:label:aether-guard.service:core"
create_entry "spiffe://${TRUST_DOMAIN}/agent-service" "docker:label:aether-guard.service:agent"

echo "SPIRE bootstrap completed."
