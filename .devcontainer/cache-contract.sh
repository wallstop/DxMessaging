#!/usr/bin/env bash
# shellcheck shell=bash

# Shared devcontainer cache mount contract.
# Keep these arrays aligned by index: source[i] mounts to target[i].
#
# Entries:
#   1. dxm-nuget-cache          -> NuGet package cache for .NET restore
#   2. dxm-dotnet-tools         -> Global dotnet tools (csharpier, etc.)
#   3. dxm-powershell-modules   -> PowerShell module cache
#   4. dxm-python-cache         -> pip wheel/download cache
#
# Unity Library caches are owned by scripts/unity/run-tests.sh and
# scripts/unity/run-tests.ps1 because they must be keyed by Unity image tag and
# test mode. Do not add a static .unity-test-project/Library mount here.

# Re-source guard: this file is sourced by post-create.sh, post-start.sh,
# validate-caching.sh, and (in Phase 4) the contract test harness. Multiple
# sources in the same shell would otherwise re-declare the readonly arrays
# and abort under `set -e`.
[[ "${_DXM_CACHE_CONTRACT_LOADED:-}" == "1" ]] && return 0
_DXM_CACHE_CONTRACT_LOADED=1

readonly CACHE_MOUNT_SOURCES=(
    "dxm-nuget-cache"
    "dxm-dotnet-tools"
    "dxm-powershell-modules"
    "dxm-python-cache"
)

readonly CACHE_MOUNT_TARGETS=(
    "/home/vscode/.nuget"
    "/home/vscode/.dotnet/tools"
    "/home/vscode/.local/share/powershell"
    "/home/vscode/.cache/pip"
)

cache_contract_validate_shape() {
    if [[ "${#CACHE_MOUNT_SOURCES[@]}" -eq 0 ]] \
        || [[ "${#CACHE_MOUNT_TARGETS[@]}" -eq 0 ]] \
        || [[ "${#CACHE_MOUNT_SOURCES[@]}" -ne "${#CACHE_MOUNT_TARGETS[@]}" ]]; then
        return 1
    fi

    return 0
}

cache_contract_get_owner_uid() {
    local target="$1"
    local owner_uid

    if owner_uid="$(stat -c %u "$target" 2>/dev/null)" && [[ "$owner_uid" =~ ^[0-9]+$ ]]; then
        echo "$owner_uid"
        return 0
    fi

    if owner_uid="$(stat -f %u "$target" 2>/dev/null)" && [[ "$owner_uid" =~ ^[0-9]+$ ]]; then
        echo "$owner_uid"
        return 0
    fi

    return 1
}

cache_contract_is_container_runtime() {
    if [[ -f "/.dockerenv" ]]; then
        return 0
    fi

    if [[ "${DEVCONTAINER:-}" == "true" ]] || [[ "${REMOTE_CONTAINERS:-}" == "true" ]]; then
        return 0
    fi

    if grep -qaE '(docker|containerd|kubepods)' /proc/1/cgroup 2>/dev/null; then
        return 0
    fi

    return 1
}
