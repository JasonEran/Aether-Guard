#!/bin/sh
set -euo pipefail

TOKEN_FILE="${SPIRE_JOIN_TOKEN_FILE:-/run/spire/bootstrap/join-token}"

if [ ! -s "$TOKEN_FILE" ]; then
  echo "SPIRE join token file missing or empty: $TOKEN_FILE" >&2
  exit 1
fi

JOIN_TOKEN="$(tr -d '\r\n' < "$TOKEN_FILE")"
if [ -z "$JOIN_TOKEN" ]; then
  echo "SPIRE join token is empty." >&2
  exit 1
fi

exec /opt/spire/bin/spire-agent run -joinToken "$JOIN_TOKEN" "$@"
