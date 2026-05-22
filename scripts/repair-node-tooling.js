#!/usr/bin/env node
"use strict";

const path = require("path");
const jestErrorDecoderModule = require("./lib/jest-error-decoder");
const { isTruthyEnv } = jestErrorDecoderModule;
const {
  probeIntegrity,
  probeIntegrityInSubprocess,
  probeResolverHealth
} = require("./lib/node-modules-integrity");
const {
  isAutoRepairAllowed: defaultIsAutoRepairAllowed,
  runIntegrityGateWithRecovery
} = require("./lib/integrity-gate-with-recovery");
const {
  attemptNpmCiRecovery,
  getNpmMajorVersion,
  printActionableRepairBanner
} = require("./run-managed-jest");

const REPO_ROOT = path.join(__dirname, "..");

function repairNodeTooling(options = {}) {
  const {
    env = process.env,
    repoRoot = REPO_ROOT,
    runIntegrityGateWithRecoveryFn = runIntegrityGateWithRecovery,
    probeIntegrityFn = probeIntegrity,
    probeIntegrityInSubprocessFn = probeIntegrityInSubprocess,
    probeResolverHealthFn = probeResolverHealth,
    attemptNpmCiRecoveryFn = attemptNpmCiRecovery,
    getNpmMajorVersionFn = getNpmMajorVersion,
    printActionableRepairBannerFn = printActionableRepairBanner,
    warnFn = console.warn
  } = options;

  if (isTruthyEnv(env.DXMSG_HOOK_SKIP_INTEGRITY)) {
    warnFn("WARNING: DXMSG_HOOK_SKIP_INTEGRITY=1 set; skipping node_modules repair bootstrap.");
    return { status: 0, skipped: true };
  }

  const gateResult = runIntegrityGateWithRecoveryFn({
    repoRoot,
    probeIntegrityFn,
    probeIntegrityInSubprocessFn,
    probeResolverHealthFn,
    attemptNpmCiRecoveryFn,
    isAutoRepairAllowedFn: () =>
      defaultIsAutoRepairAllowed({
        env,
        repoRoot,
        getNpmMajorVersionFn
      }),
    printActionableRepairBannerFn,
    decoder: jestErrorDecoderModule,
    warnFn,
    env,
    bypassCache: true
  });

  return {
    status: gateResult && gateResult.ok ? 0 : 1,
    skipped: false,
    gateResult
  };
}

function main() {
  const result = repairNodeTooling();
  process.exit(result.status);
}

if (require.main === module) {
  main();
}

module.exports = {
  REPO_ROOT,
  repairNodeTooling
};
