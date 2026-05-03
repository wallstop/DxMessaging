#!/usr/bin/env bash
# Post-create bootstrap for the DxMessaging devcontainer.

set -euo pipefail

BLUE='\033[0;34m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

log_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

log_success() {
    echo -e "${GREEN}[OK]${NC} $1"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $1"
}

fail() {
    echo -e "${RED}[ERROR]${NC} $1" >&2
    exit 1
}

run_optional() {
    local label="$1"
    shift

    log_info "$label"
    if "$@"; then
        log_success "$label completed"
    else
        log_warn "$label failed (continuing)"
    fi
}

ensure_path_line() {
    local rc_file="$1"
    local path_line='export PATH="$HOME/.local/bin:$PATH"'

    if [[ ! -f "$rc_file" ]]; then
        return
    fi

    if ! grep -Fqx "$path_line" "$rc_file"; then
        {
            echo ""
            echo "# Ensure npm user-global binaries are available"
            echo "$path_line"
        } >> "$rc_file"
    fi
}

trap 'fail "post-create setup failed at line $LINENO"' ERR

log_info "Starting post-create setup"

mkdir -p "$HOME/.local/bin"

log_info "Configuring npm global prefix for non-root installs"
npm config set prefix "$HOME/.local"

current_prefix="$(npm config get prefix)"
if [[ "$current_prefix" != "$HOME/.local" ]]; then
    fail "npm prefix is '$current_prefix', expected '$HOME/.local'"
fi
log_success "npm prefix configured: $current_prefix"

# Make codex immediately available in this session, and persist for future shells.
export PATH="$HOME/.local/bin:$PATH"
ensure_path_line "$HOME/.bashrc"
ensure_path_line "$HOME/.zshrc"

workspace_dir="${containerWorkspaceFolder:-$PWD}"

run_optional "Restoring .NET local tools" dotnet tool restore
run_optional "Installing workspace npm dependencies" npm install
run_optional "Configuring git safe.directory" git config --global --add safe.directory "$workspace_dir"
run_optional "Updating tldr cache" tldr --update
run_optional "Installing pre-commit hooks" pre-commit install --install-hooks

log_info "Installing Codex CLI"
npm install -g --prefix "$HOME/.local" @openai/codex@latest

if ! command -v codex >/dev/null 2>&1; then
    fail "Codex CLI was installed but is not on PATH"
fi

codex_version="$(codex --version 2>/dev/null || true)"
if [[ -z "$codex_version" ]]; then
    fail "Codex CLI did not return a version"
fi

log_success "Codex ready: $codex_version"
log_success "Post-create setup finished"
