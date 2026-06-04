#!/usr/bin/env bash
# OpenMono agent container entrypoint wrapper.
#
# When the agent runs with --user (host UID:GID), the bind-mounted
# ~/.openmono directory may be owned by root or another user, making it
# unwritable. This entrypoint detects that situation and redirects
# OPENMONO_DATA_DIR to a writable temp directory so the agent can still
# function (artifacts/sessions just won't persist across container runs).
set -euo pipefail

DATA_DIR="${OPENMONO_DATA_DIR:-${HOME}/.openmono}"

# Try to create a test file inside the data directory. If it fails,
# redirect to /tmp so the agent doesn't crash on startup.
if ! mkdir -p "${DATA_DIR}" 2>/dev/null || ! touch "${DATA_DIR}/.writable-test" 2>/dev/null; then
    echo "[openmono-entrypoint] ${DATA_DIR} is not writable — redirecting data to /tmp/openmono"
    DATA_DIR="/tmp/openmono"
    mkdir -p "${DATA_DIR}"
    export OPENMONO_DATA_DIR="${DATA_DIR}"
fi

# Clean up the test file
rm -f "${DATA_DIR}/.writable-test" 2>/dev/null || true

# Execute the real openmono binary with whatever args were passed
exec /usr/local/bin/openmono/openmono "$@"