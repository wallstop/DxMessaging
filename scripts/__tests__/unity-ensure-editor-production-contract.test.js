/**
 * @fileoverview Static contract test pinning three production fixes in
 * scripts/unity/ensure-editor.ps1. These are AST/text contracts (no Unity, no
 * network); they are sub-millisecond and run on every host, so a regression that
 * silently drops any of the three is caught at pre-push time.
 *
 * The three fixes (see the branch's CI triage):
 *
 *   2a. Module install passes `--accept-eula`. The standalone Unity CLI aborts
 *       `install-modules -m` with "One or more modules require license
 *       acceptance. Pass --accept-eula ..." when an EULA-bearing module (Android
 *       SDK/NDK/OpenJDK) is requested. The flag MUST be on the INSTALL (`-m`)
 *       call and MUST NOT be added to the `-l` listing call (which takes no EULA).
 *
 *   2b. The quarantine `Move-Item` is wrapped in `Invoke-WithRetry`. Moving a
 *       Unity editor directory on Windows can fail transiently with "The process
 *       cannot access the file ... because it is being used by another process."
 *       (Unity/AV/indexer holding a handle). The move must retry with backoff and
 *       rethrow on persistent failure.
 *
 *   2c. The CI-managed install-root guard (Confirm-UnityCliManagedInstallRoot)
 *       emits a wrap-IMMUNE `::error::` annotation before each `throw`. A thrown
 *       message is word-wrapped by PowerShell's ConciseView formatter at the
 *       console width; a `Write-Host "::error::..."` line is not, so CI gets a
 *       clean annotation AND a stable assertion target.
 *
 * The contract is asserted two ways: an always-on static text scan of the
 * function bodies (extracted by bounding each `function <Name>` to the next
 * top-level `function`), and -- when pwsh is present -- a PowerShell-AST
 * cross-check that the extracted-by-name function bodies agree, so the text scan
 * cannot be fooled by a same-named string elsewhere in the file.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const { spawnSync } = require("child_process");

const { normalizePwshText } = require("../lib/pwsh-output");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const ENSURE_EDITOR = path.join(REPO_ROOT, "scripts", "unity", "ensure-editor.ps1");

function pwshAvailable() {
  const probe = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "exit 0"], {
    encoding: "utf8"
  });
  return probe.status === 0;
}

const PWSH_PRESENT = pwshAvailable();

/**
 * Extract the source text of a top-level `function <name> { ... }` by bounding it
 * at the next top-level `\nfunction ` definition. Mirrors the slicing the
 * idempotency test uses. Returns "" when not found.
 */
function extractFunctionBody(scriptText, functionName) {
  const start = scriptText.indexOf(`function ${functionName}`);
  if (start < 0) {
    return "";
  }
  const after = scriptText.indexOf("\nfunction ", start + 1);
  return after === -1 ? scriptText.slice(start) : scriptText.slice(start, after);
}

let scriptText;

beforeAll(() => {
  expect(fs.existsSync(ENSURE_EDITOR)).toBe(true);
  scriptText = fs.readFileSync(ENSURE_EDITOR, "utf8");
});

describe("ensure-editor.ps1 production contract", () => {
  test("the script under test exists and requires PowerShell 5.1 and StrictMode", () => {
    // The fixes must remain compatible with the script's declared baseline.
    expect(scriptText).toContain("#Requires -Version 5.1");
    expect(scriptText).toContain("Set-StrictMode -Version Latest");
  });

  // --- 2a: --accept-eula on the module INSTALL call, not the listing call. ---
  describe("2a: module install passes --accept-eula", () => {
    test("the install-modules INSTALL (-m) call includes --accept-eula", () => {
      // Find the single capturing install-modules call (the `-m` install, not the
      // `-l` listing) and assert the EULA flag is present in that same invocation.
      const installCall = /Invoke-UnityCliCapture[^\n]*install-modules[^\n]*-m[^\n]*/.exec(
        scriptText
      );
      expect(installCall).not.toBeNull();
      expect(installCall[0]).toContain("--accept-eula");
    });

    test("the install-modules LISTING (-l) call does NOT include --accept-eula", () => {
      // The `-l` listing call takes no license; the flag there would be wrong.
      const listCall = /install-modules'[^\n]*'-l'[^\n]*/.exec(scriptText);
      expect(listCall).not.toBeNull();
      expect(listCall[0]).not.toContain("--accept-eula");
    });

    test("exactly one install-modules invocation carries --accept-eula", () => {
      const matches = scriptText.match(/install-modules[^\n]*--accept-eula/g) || [];
      expect(matches).toHaveLength(1);
    });
  });

  // --- 2b: the quarantine Move-Item is wrapped in Invoke-WithRetry. ---
  describe("2b: quarantine Move-Item is retried", () => {
    let quarantineBody;

    beforeAll(() => {
      quarantineBody = extractFunctionBody(scriptText, "Move-UnityInstallDirectoryToQuarantine");
    });

    test("the quarantine helper exists and performs the Move-Item", () => {
      expect(quarantineBody).not.toBe("");
      expect(quarantineBody).toContain("Move-Item -LiteralPath $InstallDirectory");
    });

    test("the Move-Item is wrapped in Invoke-WithRetry with multiple attempts", () => {
      // Invoke-WithRetry must appear, declare more than one attempt, and the
      // Move-Item must sit inside its -Action block (i.e. the retry precedes the
      // move within the body).
      expect(quarantineBody).toMatch(/Invoke-WithRetry\b/);
      expect(quarantineBody).toMatch(/Invoke-WithRetry[^\n]*-MaxAttempts\s+([2-9]|\d{2,})/);
      const retryIndex = quarantineBody.indexOf("Invoke-WithRetry");
      const moveIndex = quarantineBody.indexOf("Move-Item -LiteralPath $InstallDirectory");
      expect(retryIndex).toBeGreaterThanOrEqual(0);
      expect(moveIndex).toBeGreaterThan(retryIndex);
    });

    test("the retry delay is sourced from the shared override-aware helper", () => {
      // So tests can zero the backoff via DXM_ENSURE_EDITOR_RETRY_DELAY_SECONDS
      // and CI keeps the production default.
      expect(quarantineBody).toContain("Get-EnsureEditorRetryDelaySeconds");
    });
  });

  // --- 2c: the managed-root guard emits a wrap-immune ::error:: before throwing. ---
  describe("2c: managed-root guard emits a wrap-immune ::error:: annotation", () => {
    let guardBody;

    beforeAll(() => {
      guardBody = extractFunctionBody(scriptText, "Confirm-UnityCliManagedInstallRoot");
    });

    test("the guard exists and still throws on an external/absent CLI root", () => {
      expect(guardBody).not.toBe("");
      // Throw semantics preserved (additive change only).
      expect(guardBody).toContain('throw "CI-managed Unity provisioning cannot mutate editors');
      expect(guardBody).toContain("outside the managed root");
    });

    test("each guard throw is preceded by a single-line ::error:: Write-Host with the same reason", () => {
      // Both failure branches (no CLI root; CLI root outside the managed root)
      // must emit a Write-Host "::error::..." annotation. Write-Host content is
      // not subject to ConciseView word-wrap, so it is a stable CI annotation.
      const errorAnnotations =
        guardBody.match(
          /Write-Host\s+"::error::CI-managed Unity provisioning cannot mutate editors/g
        ) || [];
      expect(errorAnnotations.length).toBeGreaterThanOrEqual(2);

      // The "outside the managed root" branch specifically must annotate before
      // it throws (annotation index < throw index for that phrase).
      const annotateIndex = guardBody.indexOf(
        'Write-Host "::error::CI-managed Unity provisioning cannot mutate editors because the Unity CLI install root is outside the managed root'
      );
      const throwIndex = guardBody.indexOf(
        'throw "CI-managed Unity provisioning cannot mutate editors because the Unity CLI install root is outside the managed root'
      );
      expect(annotateIndex).toBeGreaterThanOrEqual(0);
      expect(throwIndex).toBeGreaterThan(annotateIndex);
    });
  });

  // --- pwsh-AST cross-check: the by-name function bodies are real functions. ---
  // This guards the text-slice approach from being fooled by a same-named token
  // appearing in a string/comment elsewhere: the PowerShell parser confirms each
  // contract-relevant function is a genuine FunctionDefinitionAst whose own body
  // text carries the asserted content.
  if (!PWSH_PRESENT) {
    // eslint-disable-next-line no-console
    console.warn(
      "[ensure-editor-production-contract] pwsh not found; skipping AST cross-check (CI runners have pwsh)."
    );
    test.skip("pwsh AST confirms the contract-relevant functions and their bodies", () => {});
  } else {
    test("pwsh AST confirms the contract-relevant functions and their bodies", () => {
      const harness = [
        "Set-StrictMode -Version Latest",
        "$ErrorActionPreference = 'Stop'",
        `$src = '${ENSURE_EDITOR.replace(/'/g, "''")}'`,
        "$tokens = $null; $errs = $null",
        "$ast = [System.Management.Automation.Language.Parser]::ParseFile($src, [ref]$tokens, [ref]$errs)",
        "if ($errs -and $errs.Count -gt 0) { Write-Error 'parse errors'; exit 3 }",
        "function Get-Fn([string]$name) {",
        "  $hits = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name }, $true)",
        "  if (-not $hits -or $hits.Count -lt 1) { return $null }",
        "  return $hits[0].Extent.Text",
        "}",
        "$quarantine = Get-Fn 'Move-UnityInstallDirectoryToQuarantine'",
        "$guard = Get-Fn 'Confirm-UnityCliManagedInstallRoot'",
        "$ensure = Get-Fn 'Ensure-UnityCiModules'",
        "if (-not $quarantine) { Write-Error 'no quarantine fn'; exit 4 }",
        "if (-not $guard) { Write-Error 'no guard fn'; exit 5 }",
        "if (-not $ensure) { Write-Error 'no ensure fn'; exit 6 }",
        "$ok = $true",
        "if ($quarantine -notmatch 'Invoke-WithRetry') { Write-Host 'FAIL quarantine retry'; $ok = $false }",
        "if ($quarantine -notmatch 'Move-Item') { Write-Host 'FAIL quarantine move'; $ok = $false }",
        "if ($guard -notmatch '::error::') { Write-Host 'FAIL guard annotation'; $ok = $false }",
        "if ($ensure -notmatch 'install-modules[^\\r\\n]*--accept-eula') { Write-Host 'FAIL ensure eula'; $ok = $false }",
        "if ($ok) { Write-Output 'CONTRACT-OK' } else { exit 7 }"
      ].join("\n");

      const run = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", harness], {
        encoding: "utf8",
        maxBuffer: 16 * 1024 * 1024
      });

      // Normalize before the phrase assertion: the harness can emit a thrown
      // (ConciseView-wrapped) error on the failure path, so route the output
      // through normalizePwshText for a width-independent assertion.
      const combined = normalizePwshText(`${run.stdout || ""}\n${run.stderr || ""}`);
      if (run.status !== 0) {
        throw new Error(combined);
      }
      expect(combined).toContain("CONTRACT-OK");
    });
  }
});
