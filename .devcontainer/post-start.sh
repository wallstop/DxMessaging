#!/usr/bin/env bash
# shellcheck shell=bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ ! -f "${SCRIPT_DIR}/cache-contract.sh" ]]; then
    echo "[post-start] FATAL: cache-contract.sh not found at ${SCRIPT_DIR}/cache-contract.sh"
    exit 1
fi

# shellcheck source=.devcontainer/cache-contract.sh
source "${SCRIPT_DIR}/cache-contract.sh" || {
    echo "[post-start] FATAL: failed to source cache-contract.sh"
    exit 1
}

if ! cache_contract_validate_shape; then
    echo "[post-start] Cache mount contract is invalid (sources/targets length mismatch)."
    exit 1
fi

if cache_contract_is_container_runtime; then
    current_uid="$(id -u)"
    current_gid="$(id -g)"

    for i in "${!CACHE_MOUNT_TARGETS[@]}"; do
        source_name="${CACHE_MOUNT_SOURCES[$i]}"
        target_dir="${CACHE_MOUNT_TARGETS[$i]}"

        mkdir -p "${target_dir}" 2>/dev/null || true

        owner_uid="$(cache_contract_get_owner_uid "${target_dir}" 2>/dev/null || echo "unknown")"
        if [[ "${owner_uid}" != "${current_uid}" ]]; then
            echo "[post-start] Fixing ownership for ${target_dir} (source=${source_name}, owner=${owner_uid}, expected=${current_uid})"
            sudo chown -R "${current_uid}:${current_gid}" "${target_dir}" 2>/dev/null || true
            owner_uid="$(cache_contract_get_owner_uid "${target_dir}" 2>/dev/null || echo "unknown")"
            if [[ "${owner_uid}" != "${current_uid}" ]]; then
                echo "[post-start] ERROR: ${target_dir} ownership remains ${owner_uid} (expected ${current_uid}); sudo chown appears to have failed silently" >&2
            fi
        fi
    done
else
    echo "[post-start] Non-container runtime detected; skipping cache ownership checks."
fi

git lfs pull || true

# Install / refresh the OpenAI Codex CLI (@openai/codex) from npm. The script
# is idempotent and never fails the caller: it skips when already at latest,
# and degrades gracefully when offline.
if [[ -x "${SCRIPT_DIR}/install-codex-cli.sh" ]]; then
    bash "${SCRIPT_DIR}/install-codex-cli.sh" || true
else
    echo "[post-start] WARN: install-codex-cli.sh missing or not executable; skipping codex CLI install"
fi

# Verify the docker socket is reachable (Phase 2+ Unity test runner depends on
# docker-outside-of-docker). Don't fail the post-start on socket absence so a
# pure .NET-only workflow still degrades gracefully.
if ! docker info >/dev/null 2>&1; then
    echo "[post-start] WARN: docker socket not accessible — Unity test runner will fail in Phase 2+"
fi
