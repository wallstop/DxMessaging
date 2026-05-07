#!/usr/bin/env bash
# =============================================================================
# Devcontainer Caching Validation Script
# =============================================================================
# Validates the cache mount contract across:
#   1) Dockerfile + devcontainer.json configuration
#   2) Lifecycle scripts that enforce permissions
#   3) Runtime mount state, ownership, and writability when run in-container
#
# When run outside a container, the runtime mount-state block is skipped
# entirely (a single warning is emitted). Inside a properly-built container,
# every mount-point assertion is a hard failure (matching Shiro).
# =============================================================================

set -euo pipefail

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ ! -f "${SCRIPT_DIR}/cache-contract.sh" ]]; then
    echo -e "${RED}FATAL: cache-contract.sh not found at ${SCRIPT_DIR}/cache-contract.sh${NC}"
    exit 1
fi

# shellcheck source=.devcontainer/cache-contract.sh
source "${SCRIPT_DIR}/cache-contract.sh" || {
    echo -e "${RED}FATAL: failed to source cache-contract.sh${NC}"
    exit 1
}

CHECKS_PASSED=0
CHECKS_FAILED=0
CHECKS_TOTAL=0
CHECKS_WARNINGS=0

log_header() {
    echo ""
    echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
    echo -e "${BLUE}  $1${NC}"
    echo -e "${BLUE}━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━${NC}"
}

check_pass() {
    echo -e "${GREEN}✓${NC} $1"
    ((++CHECKS_PASSED))
    ((++CHECKS_TOTAL))
}

check_fail() {
    echo -e "${RED}✗${NC} $1"
    ((++CHECKS_FAILED))
    ((++CHECKS_TOTAL))
}

check_warning() {
    echo -e "${YELLOW}⚠${NC} $1"
    ((++CHECKS_WARNINGS))
}

check_required_file() {
    local file_path="$1"
    local label="$2"

    if [[ -f "$file_path" ]]; then
        check_pass "$label exists"
        return 0
    fi

    check_fail "$label missing: $file_path"
    return 1
}

matches_expected_mount() {
    local mount_entry="$1"
    local source_name="$2"
    local target_dir="$3"

    [[ "$mount_entry" == *"source=${source_name},"* ]] \
        && [[ "$mount_entry" == *"target=${target_dir},"* ]] \
        && [[ "$mount_entry" == *",type=volume"* ]]
}

is_exact_mount_point() {
    local target_dir="$1"

    if ! command -v findmnt >/dev/null 2>&1; then
        return 2
    fi

    local mount_target
    mount_target="$(findmnt -T "$target_dir" -o TARGET -n 2>/dev/null || true)"
    if [[ "$mount_target" == "$target_dir" ]]; then
        return 0
    fi

    return 1
}

script_sources_contract() {
    local script_file="$1"

    grep -Eq '^[[:space:]]*(source|\.)[[:space:]]+"?[^"]*cache-contract\.sh"?' "$script_file"
}

log_header "Checking Contract and Static Files"

if cache_contract_validate_shape; then
    check_pass "Cache contract shape is valid (${#CACHE_MOUNT_SOURCES[@]} entries)"
else
    check_fail "Cache contract shape invalid (sources/targets length mismatch)"
fi

check_required_file "${SCRIPT_DIR}/cache-contract.sh" "cache-contract.sh" || true
check_required_file "${SCRIPT_DIR}/Dockerfile" "Dockerfile" || true
check_required_file "${SCRIPT_DIR}/devcontainer.json" "devcontainer.json" || true
check_required_file "${SCRIPT_DIR}/post-create.sh" "post-create.sh" || true
check_required_file "${SCRIPT_DIR}/post-start.sh" "post-start.sh" || true

log_header "Checking Dockerfile and Lifecycle Script Wiring"

if grep -q "# syntax=docker/dockerfile:1" "${SCRIPT_DIR}/Dockerfile"; then
    check_pass "Dockerfile has BuildKit syntax directive"
else
    check_fail "Dockerfile missing BuildKit syntax directive"
fi

if grep -q -- "--mount=type=cache,target=/var/cache/apt" "${SCRIPT_DIR}/Dockerfile"; then
    check_pass "Dockerfile uses BuildKit cache mounts for apt"
else
    check_fail "Dockerfile missing BuildKit cache mounts"
fi

if script_sources_contract "${SCRIPT_DIR}/post-create.sh"; then
    check_pass "post-create.sh sources cache-contract.sh"
else
    check_fail "post-create.sh does not source cache-contract.sh"
fi

if script_sources_contract "${SCRIPT_DIR}/post-start.sh"; then
    check_pass "post-start.sh sources cache-contract.sh"
else
    check_fail "post-start.sh does not source cache-contract.sh"
fi

log_header "Checking devcontainer.json Mount Contract"

declare -a configured_mounts=()
if command -v jq >/dev/null 2>&1; then
    jq_output=""
    if jq_output="$(jq -r '.mounts[]? // empty' "${SCRIPT_DIR}/devcontainer.json" 2>/dev/null)"; then
        if [[ -n "$jq_output" ]]; then
            mapfile -t configured_mounts <<< "$jq_output"
            check_pass "Parsed mounts from devcontainer.json using jq (count=${#configured_mounts[@]})"
        else
            check_warning "Parsed devcontainer.json with jq, but mounts array is empty"
        fi
    else
        check_warning "jq could not parse devcontainer.json (comments likely); falling back to grep parsing"
        mapfile -t configured_mounts < <(grep -o 'source=[^"]*,target=[^"]*,type=volume' "${SCRIPT_DIR}/devcontainer.json" || true)
    fi
else
    check_warning "jq is not available; falling back to grep parsing for mount checks"
    mapfile -t configured_mounts < <(grep -o 'source=[^"]*,target=[^"]*,type=volume' "${SCRIPT_DIR}/devcontainer.json" || true)
fi

if [[ "${#configured_mounts[@]}" -eq 0 ]]; then
    check_fail "No mounts found in devcontainer.json"
fi

for i in "${!CACHE_MOUNT_SOURCES[@]}"; do
    source_name="${CACHE_MOUNT_SOURCES[$i]}"
    target_dir="${CACHE_MOUNT_TARGETS[$i]}"

    found_match=false
    for mount_entry in "${configured_mounts[@]}"; do
        if matches_expected_mount "$mount_entry" "$source_name" "$target_dir"; then
            found_match=true
            break
        fi
    done

    if [[ "$found_match" == "true" ]]; then
        check_pass "Mount contract entry configured: ${source_name} -> ${target_dir}"
    else
        check_fail "Mount contract entry missing from devcontainer.json: ${source_name} -> ${target_dir}"
    fi
done

if grep -Eq '"remoteUser"[[:space:]]*:[[:space:]]*"vscode"' "${SCRIPT_DIR}/devcontainer.json"; then
    check_pass "devcontainer.json remoteUser is vscode (matches cache mount targets)"
else
    check_fail "devcontainer.json remoteUser is not vscode; update cache-contract.sh targets or remoteUser"
fi

check_devcontainer_test_workflow() {
    local workflow_file="${SCRIPT_DIR}/../.github/workflows/devcontainer-test.yml"

    if [[ ! -f "$workflow_file" ]]; then
        check_warning "Workflow file not found: ${workflow_file} (expected once Phase 3 lands)"
        return
    fi

    check_pass "devcontainer-test workflow file found"

    if grep -q "packages: write" "$workflow_file"; then
        check_pass "devcontainer-test workflow has packages:write permission"
    else
        check_fail "devcontainer-test workflow missing packages:write permission"
    fi

    if grep -q "docker/login-action@v4" "$workflow_file"; then
        check_pass "devcontainer-test workflow has current GHCR login step"
    else
        check_fail "devcontainer-test workflow missing current GHCR login step"
    fi

    if grep -q "eventFilterForPush: \"\"" "$workflow_file"; then
        check_pass "devcontainer-test workflow disables devcontainers/ci event push gate"
    else
        check_fail "devcontainer-test workflow missing eventFilterForPush override"
    fi

    if grep -q "\.devcontainer/validate-caching.sh" "$workflow_file"; then
        check_pass "devcontainer-test workflow runs validate-caching.sh"
    else
        check_warning "devcontainer-test workflow does not run validate-caching.sh (recommended)"
    fi
}

check_devcontainer_prebuild_workflow() {
    local workflow_file="${SCRIPT_DIR}/../.github/workflows/devcontainer-prebuild.yml"

    if [[ ! -f "$workflow_file" ]]; then
        check_fail "devcontainer-prebuild workflow file not found: ${workflow_file}"
        return
    fi

    check_pass "devcontainer-prebuild workflow file found"

    if grep -q "packages: write" "$workflow_file"; then
        check_pass "devcontainer-prebuild workflow has packages:write permission"
    else
        check_fail "devcontainer-prebuild workflow missing packages:write permission"
    fi

    if grep -q "docker/login-action@v4" "$workflow_file"; then
        check_pass "devcontainer-prebuild workflow has current GHCR login step"
    else
        check_fail "devcontainer-prebuild workflow missing current GHCR login step"
    fi

    if grep -q "push: never" "$workflow_file" && grep -q 'docker push "${IMAGE}"' "$workflow_file"; then
        check_pass "devcontainer-prebuild workflow pushes explicitly before verification"
    else
        check_fail "devcontainer-prebuild workflow must use push: never plus explicit docker push"
    fi

    if grep -q 'docker pull "${IMAGE}"' "$workflow_file"; then
        check_pass "devcontainer-prebuild workflow verifies GHCR pull"
    else
        check_fail "devcontainer-prebuild workflow missing GHCR pull verification"
    fi
}

log_header "Checking Workflow Configuration"
check_devcontainer_test_workflow
check_devcontainer_prebuild_workflow

log_header "Checking Runtime Mount State"

echo "Container detection signals:"
echo "  - /.dockerenv exists: $( [[ -f /.dockerenv ]] && echo yes || echo no )"
echo "  - DEVCONTAINER=${DEVCONTAINER:-<unset>}"
echo "  - REMOTE_CONTAINERS=${REMOTE_CONTAINERS:-<unset>}"

if cache_contract_is_container_runtime; then
    check_pass "Container runtime detected"

    current_uid="$(id -u)"
    current_gid="$(id -g)"
    echo "Current user: uid=${current_uid}, gid=${current_gid}"

    for i in "${!CACHE_MOUNT_SOURCES[@]}"; do
        source_name="${CACHE_MOUNT_SOURCES[$i]}"
        target_dir="${CACHE_MOUNT_TARGETS[$i]}"

        if [[ -d "$target_dir" ]]; then
            check_pass "Target directory exists: ${target_dir}"
        else
            check_fail "Target directory missing: ${target_dir}"
            continue
        fi

        owner_uid="$(cache_contract_get_owner_uid "$target_dir" 2>/dev/null || echo unknown)"
        if [[ "$owner_uid" == "$current_uid" ]]; then
            check_pass "Ownership OK for ${target_dir} (uid=${owner_uid})"
        else
            check_fail "Ownership mismatch for ${target_dir} (owner=${owner_uid}, expected=${current_uid})"
        fi

        if is_exact_mount_point "$target_dir"; then
            mount_source="$(findmnt -T "$target_dir" -o SOURCE -n 2>/dev/null || echo unknown)"
            check_pass "Mount point OK for ${target_dir} (source=${mount_source}, contract=${source_name})"
        else
            mount_check_status=$?
            if [[ "$mount_check_status" -eq 2 ]]; then
                check_warning "findmnt unavailable; cannot verify mount-point state for ${target_dir}"
            else
                actual_target="$(findmnt -T "$target_dir" -o TARGET -n 2>/dev/null || echo unresolved)"
                actual_source="$(findmnt -T "$target_dir" -o SOURCE -n 2>/dev/null || echo unresolved)"
                check_fail "${target_dir} is not a dedicated mount point (resolved target=${actual_target}, source=${actual_source})"
            fi
        fi

        probe_file="${target_dir}/.cache-write-probe-$$"
        if touch "$probe_file" 2>/dev/null; then
            rm -f "$probe_file"
            check_pass "Write probe OK for ${target_dir}"
        else
            check_fail "Write probe failed for ${target_dir}"
        fi

        size_value="$(du -sh "$target_dir" 2>/dev/null | cut -f1 || echo unknown)"
        echo "   Diagnostic: source=${source_name}, target=${target_dir}, owner=${owner_uid}, size=${size_value}"
    done
else
    check_warning "Container runtime not detected; skipping runtime mount-state checks (safe when invoked outside a container)"
fi

log_header "Validation Summary"

if [[ "$CHECKS_TOTAL" -eq 0 ]]; then
    PASS_PERCENTAGE=0
else
    PASS_PERCENTAGE=$((CHECKS_PASSED * 100 / CHECKS_TOTAL))
fi

echo ""
echo "  Checks Passed:   ${CHECKS_PASSED}"
echo "  Checks Failed:   ${CHECKS_FAILED}"
echo "  Warnings:        ${CHECKS_WARNINGS}"
echo "  Total Evaluated: ${CHECKS_TOTAL}"
echo "  Pass Percentage: ${PASS_PERCENTAGE}%"
echo ""

if [[ "$CHECKS_FAILED" -eq 0 ]]; then
    echo -e "${GREEN}✓ Validation passed. Cache configuration and runtime checks are healthy.${NC}"
    exit 0
fi

echo -e "${RED}✗ Validation failed. Review errors above.${NC}"
exit 1
