#!/usr/bin/env node
/**
 * postinstall.js
 *
 * Runs validate-node-tooling.js after npm install to surface drift
 * (missing/broken Jest runner files, etc.) immediately. Always exits 0
 * so a non-fatal warning never blocks `npm install` on broken machines.
 * Cross-platform: uses Node only, no shell-specific syntax.
 */

"use strict";

const path = require("path");
const childProcess = require("child_process");

const VALIDATOR = path.join(__dirname, "validate-node-tooling.js");

try {
  const result = childProcess.spawnSync(process.execPath, [VALIDATOR], { stdio: "inherit" });

  if (result.error) {
    console.warn(
      `⚠️ postinstall: validate-node-tooling launch failed (${result.error.message}); continuing.`
    );
  } else if (typeof result.status === "number" && result.status !== 0) {
    console.warn(
      "⚠️ postinstall: validate-node-tooling reported drift; install continues (non-fatal)."
    );
  }
} catch (error) {
  console.warn(
    `⚠️ postinstall: unexpected error running validate-node-tooling (${error.message}); continuing.`
  );
}

process.exit(0);
