/**
 * @fileoverview Behavioral + unit tests for the module-install RESILIENCE and
 * DIAGNOSTICS added to scripts/unity/ensure-editor.ps1.
 *
 * WHY THIS EXISTS: on the self-hosted Windows Unity runners, the standalone
 * Unity CLI's Android NDK module install has two failure modes that the bootstrap
 * could not survive:
 *   (a) it sometimes FAILS after spamming thousands of IDENTICAL
 *       `{"type":"progress","pct":96,"msg":"Installing Android NDK..."}` lines and
 *       then exits 6 -- so the thrown "CLI output tail" was thousands of copies of
 *       one line (unreadable); and
 *   (b) it sometimes HANGS so long the GitHub job is CANCELLED ("The operation was
 *       canceled"), so the retry never fires and NO diagnostics are produced.
 *
 * The fix added three things, pinned here:
 *   1. A TOTAL WALL-CLOCK TIMEOUT on the captured CLI invocation
 *      (Invoke-UnityCliCaptureWithTimeout, which Invoke-UnityCliCapture delegates
 *      to): a hung install is tree-killed, classified as a (retryable) failure
 *      with a sentinel exit code (124), and annotated with a wrap-immune
 *      `::error::`. Configurable via DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS.
 *   2. A pure de-dup tail formatter (Get-CollapsedCliOutputTail) that collapses a
 *      run of N identical lines to one line with a "(xN)" suffix, so a failure
 *      tail is READABLE.
 *   3. A wrap-immune failure summary (Write-ModuleInstallFailureDiagnostics) that
 *      names the version, verb/args, outcome (exit code OR "timed out after Ns"),
 *      the last progress message, and the install-drive free space.
 *
 * Strategy mirrors unity-ensure-editor-il2cpp-idempotency.test.js: AST-extract a
 * named function from the script (via Parser::ParseFile) into a throwaway temp
 * .ps1 and invoke it (the script has top-level executable code, so dot-sourcing
 * the whole file would have side effects); and for the timeout/diagnostics
 * behavior, run the WHOLE ensure-editor.ps1 against a fake `unity` CLI shim built
 * under os.tmpdir with a hermetic spawn env (sandboxHostFolderEnv). All phrase
 * assertions go through normalizePwshText (via combinedText) so they are immune to
 * the ConciseView word-wrap on the narrower Windows runner -- enforced repo-wide
 * by pwsh-output-assertion-policy.test.js.
 *
 * pwsh is preinstalled on GitHub's runners; when it is absent locally the
 * behavioral assertions are skipped, but always-on sanity assertions still run so
 * a zero-coverage regression cannot hide.
 *
 * @cross-platform-regression -- this marker requires the file to be gated on
 * ubuntu/windows/macos via the targeted step in
 * .github/workflows/cross-platform-preflight.yml; enforced by
 * scripts/__tests__/cross-platform-preflight-coverage.test.js. The timeout
 * process-tree-kill and the System.Diagnostics.Process drain are platform-
 * divergent, so first-failure attribution on every OS is warranted.
 */

"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const { spawnSync } = require("child_process");

const { prependPathEnv, sandboxHostFolderEnv } = require("../lib/spawn-env-sandbox");
const { normalizePwshText } = require("../lib/pwsh-output");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const ENSURE_EDITOR = path.join(REPO_ROOT, "scripts", "unity", "ensure-editor.ps1");

// Merge a pwsh run's stdout+stderr into one string and NORMALIZE it for phrase
// assertions (rejoins the ConciseView word-wrap gutter + strips ANSI). Use ONLY
// for phrase assertions; reads that depend on line structure use the raw stream.
function combinedText(run) {
  return normalizePwshText(`${run.stdout || ""}\n${run.stderr || ""}`);
}

function pwshAvailable() {
  const probe = spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-Command", "exit 0"], {
    encoding: "utf8"
  });
  return probe.status === 0;
}

const PWSH_PRESENT = pwshAvailable();

const workspaces = [];

afterAll(() => {
  for (const ws of workspaces) {
    try {
      fs.rmSync(ws, { recursive: true, force: true });
    } catch {
      // best-effort temp cleanup; never fail the suite on teardown.
    }
  }
});

// AST-extract one or more named functions from ensure-editor.ps1 and Invoke-
// Expression their bodies, so an extracted function can be called in isolation
// without the script's top-level side effects.
function extractEnsureEditorFunctions(functionNames) {
  const escapedPath = ENSURE_EDITOR.replace(/'/g, "''");
  const names = functionNames.map((name) => `'${name.replace(/'/g, "''")}'`).join(", ");
  return [
    `$src = '${escapedPath}'`,
    `$wanted = @(${names})`,
    "$tokens = $null; $errs = $null",
    "$ast = [System.Management.Automation.Language.Parser]::ParseFile($src, [ref]$tokens, [ref]$errs)",
    "$functions = $ast.FindAll({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $wanted -contains $n.Name }, $true)",
    'foreach ($name in $wanted) { if (-not ($functions | Where-Object { $_.Name -eq $name })) { Write-Error "Function $name not found"; exit 3 } }',
    "foreach ($fn in $functions) { Invoke-Expression $fn.Extent.Text }"
  ].join("\n");
}

function runPwshScript(scriptText) {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-resilience-harness-"));
  workspaces.push(base);
  const harnessPath = path.join(base, "harness.ps1");
  fs.writeFileSync(harnessPath, scriptText, "utf8");
  return spawnSync("pwsh", ["-NoProfile", "-NonInteractive", "-File", harnessPath], {
    encoding: "utf8",
    maxBuffer: 32 * 1024 * 1024
  });
}

// ---------------------------------------------------------------------------
// Fake `unity` CLI shim, parameterized by a node "mode" so the SAME builder can
// produce a fast-success CLI, a slow/hanging CLI, or a failing-with-progress CLI.
// ---------------------------------------------------------------------------
function makeFakeUnityCli(binDir, bodyLines) {
  fs.mkdirSync(binDir, { recursive: true });
  const scriptPath = path.join(binDir, "fake-unity-cli.js");
  fs.writeFileSync(
    scriptPath,
    [
      "#!/usr/bin/env node",
      '"use strict";',
      "const fs = require('fs');",
      "const args = process.argv.slice(2);",
      "function write(line) { process.stdout.write(`${line}\\n`); }",
      "function exit(code) { process.exit(code); }",
      "if (args.includes('-version')) { write('Unity fake version'); exit(0); }",
      ...bodyLines,
      "exit(0);",
      ""
    ].join("\n"),
    "utf8"
  );
  fs.chmodSync(scriptPath, 0o755);

  if (process.platform === "win32") {
    const unityCmdPath = path.join(binDir, "unity.cmd");
    fs.writeFileSync(
      unityCmdPath,
      ["@echo off", 'node "%~dp0fake-unity-cli.js" %*', ""].join("\r\n"),
      "utf8"
    );
    return unityCmdPath;
  }

  const unityPath = path.join(binDir, "unity");
  fs.writeFileSync(
    unityPath,
    ["#!/usr/bin/env sh", 'exec node "$(dirname "$0")/fake-unity-cli.js" "$@"', ""].join("\n"),
    "utf8"
  );
  fs.chmodSync(unityPath, 0o755);
  return unityPath;
}

// Run the WHOLE ensure-editor.ps1 against a fake CLI built from `bodyLines`,
// hermetically (host-folder vars sandboxed; node stub-startup probe skipped).
// `extraEnv` lets a test set DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS etc.
function runEnsureEditorWithFakeCli(bodyLines, installRoot, extraEnv = {}) {
  const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-resilience-full-"));
  workspaces.push(base);
  const binDir = path.join(base, "bin");
  makeFakeUnityCli(binDir, bodyLines);

  const sandboxRoot = path.join(base, "host-env-sandbox");
  const env = prependPathEnv(sandboxHostFolderEnv(process.env, sandboxRoot), binDir);
  delete env.UNITY_EDITOR_INSTALL_ROOT;
  env.DXM_ENSURE_EDITOR_RETRY_DELAY_SECONDS = "0";
  env.DXM_UNITY_SKIP_NATIVE_STARTUP_PROBE = "1";
  for (const [k, v] of Object.entries(extraEnv)) {
    env[k] = v;
  }

  return spawnSync(
    "pwsh",
    [
      "-NoProfile",
      "-NonInteractive",
      "-File",
      ENSURE_EDITOR,
      "-UnityVersion",
      "6000.0.32f1",
      "-InstallRoot",
      installRoot,
      "-CiManagedOnly"
    ],
    { env, encoding: "utf8", maxBuffer: 64 * 1024 * 1024 }
  );
}

describe("ensure-editor.ps1 module-install resilience + diagnostics", () => {
  // Always runs (even without pwsh): a rename/move cannot silently no-op the guard.
  test("the script under test exists", () => {
    expect(fs.existsSync(ENSURE_EDITOR)).toBe(true);
  });

  // Static corroboration (no pwsh needed): the resilience surface is present in
  // the script. Cheap and always-on so a refactor that drops a piece is caught
  // even where the behavioral run is skipped.
  test("the resilience+diagnostics functions and the env knob exist in the script", () => {
    const text = fs.readFileSync(ENSURE_EDITOR, "utf8");
    expect(text).toContain("function Invoke-UnityCliCaptureWithTimeout");
    expect(text).toContain("function Get-EnsureEditorInstallTimeoutSeconds");
    expect(text).toContain("function Get-CollapsedCliOutputTail");
    expect(text).toContain("function Get-LastCliProgressMessage");
    expect(text).toContain("function Write-ModuleInstallFailureDiagnostics");
    expect(text).toContain("DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS");
    // The capturing invoker must DELEGATE to the timeout runner so the module-
    // install paths are bounded without changing the call sites (the AST contract
    // still routes -Arguments through Get-UnityCliModuleInstallArguments).
    const captureStart = text.indexOf("function Invoke-UnityCliCapture {");
    expect(captureStart).toBeGreaterThanOrEqual(0);
    const after = text.indexOf("\nfunction ", captureStart + 1);
    const body = after === -1 ? text.slice(captureStart) : text.slice(captureStart, after);
    expect(body).toContain("Invoke-UnityCliCaptureWithTimeout");
    // The timeout runner must tree-kill (the bool overload) and use the sentinel.
    const timeoutStart = text.indexOf("function Invoke-UnityCliCaptureWithTimeout {");
    const timeoutAfter = text.indexOf("\nfunction ", timeoutStart + 1);
    const timeoutBody = text.slice(timeoutStart, timeoutAfter);
    expect(timeoutBody).toContain("Kill($true)");
    expect(timeoutBody).toContain("124");
  });

  if (!PWSH_PRESENT) {
    // eslint-disable-next-line no-console
    console.warn(
      "[install-resilience] pwsh not found on PATH; skipping behavioral probes (CI runners have pwsh)."
    );
    // Count-parity placeholders for the data-driven de-dup table below so the
    // suite shape is stable whether or not pwsh is present.
    test.skip.each([
      ["long run collapses to (xN)"],
      ["mixed runs preserved"],
      ["no repeated lines pass through unchanged"],
      ["cap is applied AFTER collapsing"],
      ["empty input yields the placeholder"],
      ["whitespace-only input yields the placeholder (not an empty string)"],
      ["a lone null element yields the placeholder"]
    ])("Get-CollapsedCliOutputTail: %s", () => {});
    return;
  }

  // --- #2 DE-DUP TAIL: unit-test the pure Get-CollapsedCliOutputTail helper. ---
  // Data-driven: each row feeds an input line array + MaxLines and asserts the
  // exact collapsed, capped, newline-joined result. The helper is AST-extracted
  // and invoked directly (it depends only on built-ins).
  describe("#2 de-dup tail (Get-CollapsedCliOutputTail, data-driven)", () => {
    // Build a tiny harness that extracts the helper, runs it on a JSON-encoded
    // input array, and prints the raw result bracketed so the test can read it
    // off the raw stdout (structure-preserving; not a phrase assertion).
    function collapseTail(inputLines, maxLines) {
      const inputJson = JSON.stringify(inputLines).replace(/'/g, "''");
      const out = runPwshScript(
        [
          "Set-StrictMode -Version Latest",
          "$ErrorActionPreference = 'Stop'",
          extractEnsureEditorFunctions(["Get-CollapsedCliOutputTail"]),
          `$inputLines = @('${inputJson}' | ConvertFrom-Json)`,
          `$result = Get-CollapsedCliOutputTail -Output $inputLines -MaxLines ${maxLines}`,
          // Bracket the result so trailing/leading whitespace is unambiguous.
          "Write-Output ('<<<' + $result + '>>>')"
        ].join("\n")
      );
      expect(out.status).toBe(0);
      const m = /<<<([\s\S]*)>>>/.exec(out.stdout || "");
      expect(m).not.toBeNull();
      return m[1];
    }

    test.each([
      {
        name: "long run collapses to (xN)",
        // 3847 identical NDK progress lines bracketed by distinct lines.
        input: ["START"]
          .concat(
            Array.from(
              { length: 3847 },
              () => '{"type":"progress","pct":96,"msg":"Installing Android NDK..."}'
            )
          )
          .concat(["INSTALL_FAILED"]),
        maxLines: 40,
        expected: [
          "START",
          '{"type":"progress","pct":96,"msg":"Installing Android NDK..."}  (x3847)',
          "INSTALL_FAILED"
        ].join("\n")
      },
      {
        name: "mixed runs preserved",
        input: ["x", "x", "y", "z", "z", "z"],
        maxLines: 40,
        expected: ["x  (x2)", "y", "z  (x3)"].join("\n")
      },
      {
        name: "no repeated lines pass through unchanged",
        input: ["one", "two", "three"],
        maxLines: 40,
        expected: ["one", "two", "three"].join("\n")
      },
      {
        name: "cap is applied AFTER collapsing (counts distinct runs)",
        // 'a','a' collapses to one entry; with MaxLines 2 we keep the LAST two
        // collapsed entries: 'd','e' (NOT raw duplicates of 'a').
        input: ["a", "a", "b", "c", "d", "e"],
        maxLines: 2,
        expected: ["d", "e"].join("\n")
      },
      {
        name: "empty input yields the placeholder",
        input: [],
        maxLines: 10,
        expected: "(no output captured)"
      },
      {
        // REGRESSION (MINOR-3): @($Output) on all-blank lines is NOT count-0
        // (each blank is a 1-element string), so a naive `.Count -eq 0` check let
        // whitespace-only input slip through and return '' instead of the
        // documented placeholder. The content probe now returns the placeholder.
        name: "whitespace-only input yields the placeholder (not an empty string)",
        input: ["   ", "\t", ""],
        maxLines: 10,
        expected: "(no output captured)"
      },
      {
        // REGRESSION (MINOR-3): @($null) is a ONE-element array whose element is
        // $null (NOT count-0), which previously returned '' rather than the
        // placeholder. JSON `null` round-trips through ConvertFrom-Json to $null,
        // exercising exactly that 1-element-$null path.
        name: "a lone null element yields the placeholder",
        input: [null],
        maxLines: 10,
        expected: "(no output captured)"
      }
    ])("Get-CollapsedCliOutputTail: $name", ({ input, maxLines, expected }) => {
      expect(collapseTail(input, maxLines)).toBe(expected);
    });

    test("a single identical pair collapses to (x2) but a lone line gets no suffix", () => {
      expect(collapseTail(["dup", "dup"], 40)).toBe("dup  (x2)");
      expect(collapseTail(["solo"], 40)).toBe("solo");
    });
  });

  // --- #1 TIMEOUT FIRES: a fake CLI that SLEEPS far longer than a tiny timeout
  //     is tree-killed; the run surfaces the wrap-immune ::error:: timeout
  //     annotation; the shim process does not survive. ---
  describe("#1 install timeout", () => {
    test("a hung module install is killed and surfaces the wrap-immune ::error:: timeout annotation", () => {
      const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-resilience-timeout-"));
      workspaces.push(base);
      const installRoot = path.join(base, "configured-root");
      const pidFile = path.join(base, "cli.pid");
      const childPidFile = path.join(base, "cli-child.pid");

      // The fake CLI: on the module install it records its PID + a spawned child
      // PID, emits two progress lines, then HANGS (timers keep both alive). A
      // child is spawned so the test also proves TREE-kill, not just a parent
      // kill. Everything else (install-path getter, editors list) responds fast.
      const bodyLines = [
        `const pidFile = ${JSON.stringify(pidFile)};`,
        `const childPidFile = ${JSON.stringify(childPidFile)};`,
        `const installRoot = ${JSON.stringify(installRoot)};`,
        "if (args.length === 1 && args[0] === 'install-path') { write(installRoot); exit(0); }",
        "if (args.length >= 1 && args[0] === 'install-path') { exit(0); }",
        "if (args[0] === 'install-modules' && args.includes('-l')) { write('windows-il2cpp webgl android'); exit(0); }",
        "if (args[0] === 'editors') { write(JSON.stringify({ editors: [] })); exit(0); }",
        // The base `install` (no editor on disk -> ensure-editor takes the install
        // path) is the one that hangs.
        "if (args[0] === 'install') {",
        "  fs.writeFileSync(pidFile, String(process.pid));",
        "  const { spawn } = require('child_process');",
        "  const child = spawn(process.execPath, ['-e', 'setInterval(()=>{}, 100000)'], { stdio: 'ignore' });",
        "  fs.writeFileSync(childPidFile, String(child.pid));",
        '  write(\'{"type":"progress","pct":10,"msg":"Installing Android SDK..."}\');',
        '  write(\'{"type":"progress","pct":96,"msg":"Installing Android NDK..."}\');',
        "  setInterval(() => {}, 100000);",
        "  return;",
        "}"
      ];

      const startedAt = Date.now();
      const out = runEnsureEditorWithFakeCli(bodyLines, installRoot, {
        // Tiny timeout so the hang is killed in ~2s. Retry delay is already 0.
        DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS: "2"
      });
      const elapsedMs = Date.now() - startedAt;
      const combined = combinedText(out);

      // It must NOT have run anywhere near forever. Two retry attempts at a 2s
      // timeout each (+ overhead) is comfortably under 60s; a regression that
      // disables the timeout would blow past this. (Also bounded by maxBuffer/the
      // jest per-test timeout, but assert explicitly for a clear failure.)
      expect(elapsedMs).toBeLessThan(60000);

      // The run fails (the install never produced a usable editor)...
      expect(out.status).not.toBe(0);
      // ...and the wrap-immune ::error:: timeout annotation is present, naming the
      // timeout, the env knob, and the last progress message seen before the kill.
      expect(combined).toContain("TIMED OUT after 2 second(s)");
      expect(combined).toContain("process tree was killed");
      expect(combined).toContain("DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS");
      expect(combined).toContain("Installing Android NDK...");

      // The shim process (and its child) must NOT survive the tree-kill. Allow a
      // brief grace period for OS reaping, then assert both are gone.
      const sleepFor = (ms) => {
        const until = Date.now() + ms;
        // Busy-wait is fine here (short, test-only) and avoids a foreground sleep.
        while (Date.now() < until) {
          /* spin */
        }
      };
      sleepFor(750);
      const stillAlive = (pidPath) => {
        if (!fs.existsSync(pidPath)) {
          return false;
        }
        const pid = Number(fs.readFileSync(pidPath, "utf8").trim());
        if (!Number.isInteger(pid) || pid <= 0) {
          return false;
        }
        try {
          process.kill(pid, 0); // signal 0 = liveness probe
          // It is alive; clean it up so a leaked hang process does not linger.
          try {
            process.kill(pid, "SIGKILL");
          } catch {
            /* ignore */
          }
          return true;
        } catch {
          return false; // ESRCH -> already dead
        }
      };
      expect(stillAlive(pidFile)).toBe(false);
      expect(stillAlive(childPidFile)).toBe(false);
    }, 90000);

    test("Get-EnsureEditorInstallTimeoutSeconds honors the env override and defaults/validates", () => {
      const out = runPwshScript(
        [
          "Set-StrictMode -Version Latest",
          "$ErrorActionPreference = 'Stop'",
          extractEnsureEditorFunctions(["Get-EnsureEditorInstallTimeoutSeconds"]),
          // Default (unset).
          "$env:DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS = $null",
          "Write-Output ('DEFAULT=' + (Get-EnsureEditorInstallTimeoutSeconds))",
          // Valid override.
          "$env:DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS = '5'",
          "Write-Output ('OVERRIDE=' + (Get-EnsureEditorInstallTimeoutSeconds))",
          // Explicit opt-out (0 is allowed -> unbounded).
          "$env:DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS = '0'",
          "Write-Output ('OPTOUT=' + (Get-EnsureEditorInstallTimeoutSeconds))",
          // Invalid -> warn + default.
          "$env:DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS = 'nope'",
          "Write-Output ('INVALID=' + (Get-EnsureEditorInstallTimeoutSeconds))",
          // Negative -> warn + default.
          "$env:DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS = '-7'",
          "Write-Output ('NEGATIVE=' + (Get-EnsureEditorInstallTimeoutSeconds))"
        ].join("\n")
      );
      expect(out.status).toBe(0);
      const stdout = out.stdout || "";
      // Default rationale documented in the script: 2700s (45 min).
      expect(stdout).toContain("DEFAULT=2700");
      expect(stdout).toContain("OVERRIDE=5");
      expect(stdout).toContain("OPTOUT=0");
      expect(stdout).toContain("INVALID=2700");
      expect(stdout).toContain("NEGATIVE=2700");
    });

    // --- #4 (optional retry tuning): the install-retry-attempts knob exists,
    //     defaults to 2 (unchanged), and is override-aware/validating. ---
    test("Get-EnsureEditorInstallRetryAttempts defaults to 2 and honors a valid override", () => {
      const out = runPwshScript(
        [
          "Set-StrictMode -Version Latest",
          "$ErrorActionPreference = 'Stop'",
          extractEnsureEditorFunctions(["Get-EnsureEditorInstallRetryAttempts"]),
          "$env:DXM_ENSURE_EDITOR_INSTALL_RETRY_ATTEMPTS = $null",
          "Write-Output ('DEFAULT=' + (Get-EnsureEditorInstallRetryAttempts))",
          "$env:DXM_ENSURE_EDITOR_INSTALL_RETRY_ATTEMPTS = '3'",
          "Write-Output ('OVERRIDE=' + (Get-EnsureEditorInstallRetryAttempts))",
          // Below 1 is invalid (Invoke-WithRetry clamps, but the knob rejects) -> default.
          "$env:DXM_ENSURE_EDITOR_INSTALL_RETRY_ATTEMPTS = '0'",
          "Write-Output ('ZERO=' + (Get-EnsureEditorInstallRetryAttempts))",
          "$env:DXM_ENSURE_EDITOR_INSTALL_RETRY_ATTEMPTS = 'nope'",
          "Write-Output ('INVALID=' + (Get-EnsureEditorInstallRetryAttempts))"
        ].join("\n")
      );
      expect(out.status).toBe(0);
      const stdout = out.stdout || "";
      // The default is intentionally UNCHANGED at 2.
      expect(stdout).toContain("DEFAULT=2");
      expect(stdout).toContain("OVERRIDE=3");
      expect(stdout).toContain("ZERO=2");
      expect(stdout).toContain("INVALID=2");
    });
  });

  // --- NORMAL COMPLETION IS UNAFFECTED: a fast fake CLI returns promptly with
  //     the correct exit code + full streamed output through the timeout runner. ---
  test("#1 normal completion is unaffected: a fast install streams full output and resolves the editor", () => {
    const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-resilience-fast-"));
    workspaces.push(base);
    const installRoot = path.join(base, "configured-root");
    const editorRoot = path.join(installRoot, "6000.0.32f1");
    const editorExe = path.join(editorRoot, "Editor", "Unity.exe");

    // A fast CLI that, on `install`, fabricates the full CI module layout on disk
    // and exits 0 promptly. install-modules is then a clean no-op (modules on
    // disk). This exercises the SAME timeout-wrapped capture path as a real
    // install, just with a fast child, proving no behavior change.
    const bodyLines = [
      "const path = require('path');",
      `const installRoot = ${JSON.stringify(installRoot)};`,
      "const editorRoot = path.join(installRoot, '6000.0.32f1');",
      "const editorExe = path.join(editorRoot, 'Editor', 'Unity.exe');",
      "function mkdirp(p) { fs.mkdirSync(p, { recursive: true }); }",
      "function writeFile(p, value) { mkdirp(path.dirname(p)); fs.writeFileSync(p, value); fs.chmodSync(p, 0o755); }",
      "function createModules() {",
      "  writeFile(editorExe, '#!/usr/bin/env sh\\necho \"Unity fake version\"\\nexit 0\\n');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'windowsstandalonesupport', 'Variations', 'win64_player_development_il2cpp', 'UnityPlayer.dll'), '');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'WebGLSupport', 'UnityEditor.WebGL.Extensions.dll'), '');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'WebGLSupport', 'BuildTools', 'Emscripten', 'emscripten', 'emcc.py'), '');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'AndroidPlayer', 'UnityEditor.Android.Extensions.dll'), '');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'AndroidPlayer', 'SDK', 'platform-tools', 'adb.exe'), '');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'AndroidPlayer', 'NDK', 'source.properties'), '');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'AndroidPlayer', 'NDK', 'toolchains', 'llvm', 'prebuilt', 'linux-x86_64', 'bin', 'clang++'), '');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'AndroidPlayer', 'OpenJDK', 'bin', 'java'), '');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'LinuxStandaloneSupport', 'Variations', 'linux64_player_development_mono', 'LinuxPlayer'), '');",
      "  writeFile(path.join(editorRoot, 'Editor', 'Data', 'PlaybackEngines', 'LinuxStandaloneSupport', 'Variations', 'linux64_player_development_il2cpp', 'LinuxPlayer'), '');",
      "}",
      "if (args.length === 1 && args[0] === 'install-path') { write(installRoot); exit(0); }",
      "if (args.length >= 1 && args[0] === 'install-path') { exit(0); }",
      "if (args[0] === 'install-modules' && args.includes('-l')) { write('windows-il2cpp webgl android android-sdk-ndk-tools android-open-jdk linux-mono linux-il2cpp'); exit(0); }",
      "if (args[0] === 'install-modules') { write('No modules found to install'); exit(6); }",
      "if (args[0] === 'install') {",
      '  write(\'{"type":"progress","pct":50,"msg":"Downloading editor"}\');',
      "  createModules();",
      "  write('Editor installed successfully');",
      "  exit(0);",
      "}",
      "if (args[0] === 'editors') { write(JSON.stringify({ editors: [{ version: '6000.0.32f1', path: editorRoot }] })); exit(0); }"
    ];

    const out = runEnsureEditorWithFakeCli(bodyLines, installRoot, {
      // A generous timeout that a fast install never approaches.
      DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS: "120"
    });
    const stdout = out.stdout || "";
    const combined = combinedText(out);

    if (out.status !== 0) {
      throw new Error(combined);
    }
    // Resolution lands on the fabricated editor (structural read: last raw line).
    expect(stdout.trim().split(/\r?\n/).pop()).toBe(editorExe);
    // Full output streamed through the timeout runner (both the progress line and
    // the success line are present)...
    expect(combined).toContain("Downloading editor");
    expect(combined).toContain("Editor installed successfully");
    // ...and NO timeout annotation fired on the healthy path.
    expect(combined).not.toContain("TIMED OUT");
  }, 60000);

  // --- #3 WRAP-IMMUNE DIAGNOSTICS: a failing fake CLI (exit 6 with JSON progress
  //     lines) surfaces the wrap-immune ::error:: naming the last progress msg +
  //     disk space. ---
  test("#3 a failing install surfaces the wrap-immune ::error:: with last progress msg + disk space", () => {
    const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-resilience-diag-"));
    workspaces.push(base);
    const installRoot = path.join(base, "configured-root");

    // The fake CLI: `install` emits JSON progress lines (including the NDK line)
    // then FAILS exit 6 without ever producing a usable editor, and `editors`
    // reports none -- so resolution fails and the genuine-failure throw path runs,
    // which is preceded by Write-ModuleInstallFailureDiagnostics. Repair is
    // disabled so the run ends at the first failure (deterministic, fast).
    const bodyLines = [
      `const installRoot = ${JSON.stringify(installRoot)};`,
      "if (args.length === 1 && args[0] === 'install-path') { write(installRoot); exit(0); }",
      "if (args.length >= 1 && args[0] === 'install-path') { exit(0); }",
      "if (args[0] === 'install-modules' && args.includes('-l')) { write('windows-il2cpp webgl android'); exit(0); }",
      "if (args[0] === 'install') {",
      '  write(\'{"type":"progress","pct":10,"msg":"Installing Android SDK..."}\');',
      '  write(\'{"type":"progress","pct":96,"msg":"Installing Android NDK..."}\');',
      "  write('INSTALL_FAILED');",
      "  exit(6);",
      "}",
      "if (args[0] === 'editors') { write(JSON.stringify({ editors: [] })); exit(0); }"
    ];

    const out = runEnsureEditorWithFakeCli(bodyLines, installRoot, {
      DXM_UNITY_DISABLE_EDITOR_REPAIR: "1",
      DXM_ENSURE_EDITOR_INSTALL_TIMEOUT_SECONDS: "120"
    });
    const combined = combinedText(out);

    expect(out.status).not.toBe(0);
    // The wrap-immune failure summary names the version, the outcome, the LAST
    // progress message parsed from the JSON, and the install-drive free space.
    expect(combined).toContain("Unity 6000.0.32f1 module install FAILED");
    expect(combined).toContain("exit code 6");
    expect(combined).toContain("Installing Android NDK...");
    expect(combined).toContain("free space:");
  }, 60000);

  // --- Get-LastCliProgressMessage unit coverage (the parser the summary uses). ---
  test("Get-LastCliProgressMessage parses the last JSON msg, else the last non-empty line", () => {
    const out = runPwshScript(
      [
        "Set-StrictMode -Version Latest",
        "$ErrorActionPreference = 'Stop'",
        extractEnsureEditorFunctions(["Get-LastCliProgressMessage"]),
        // Last JSON msg wins (even when a later line has no msg).
        '$a = @(\'{"msg":"first"}\', \'{"type":"progress","msg":"Installing Android NDK..."}\', \'trailing plain line\')',
        "Write-Output ('JSON=' + (Get-LastCliProgressMessage -Output $a))",
        // No JSON msg -> last non-empty line.
        "$b = @('plain one', 'plain two', '', '   ')",
        "Write-Output ('PLAIN=' + (Get-LastCliProgressMessage -Output $b))",
        // Empty -> placeholder.
        "Write-Output ('EMPTY=' + (Get-LastCliProgressMessage -Output @()))"
      ].join("\n")
    );
    expect(out.status).toBe(0);
    const stdout = out.stdout || "";
    expect(stdout).toContain("JSON=Installing Android NDK...");
    expect(stdout).toContain("PLAIN=plain two");
    expect(stdout).toContain("EMPTY=(no output captured)");
  });

  // --- LIVE PER-LINE STREAMING (BLOCKER-1): the timeout runner must echo each
  //     line the INSTANT it arrives, not buffer the whole stream until the child
  //     exits. A 45-minute install with a buffered reader is a blank CI console
  //     for 45 minutes -- an observer cannot tell a slow install from a hang.
  //
  // HOW IT IS PROVEN PORTABLY: spawnSync buffers a child's stdout and returns it
  // only at exit, so the PARENT cannot observe per-line timing. Instead the pwsh
  // harness AST-extracts Invoke-UnityCliCaptureWithTimeout, PROXIES Write-Host so
  // every echoed line is stamped with [DateTime]::UtcNow.Ticks into a timing file
  // (the stamp is taken INSIDE pwsh at echo time, immune to spawnSync buffering),
  // and runs the function against a CLI that prints "FIRST", sleeps, prints
  // "SECOND". A LIVE implementation stamps FIRST a sleep-interval BEFORE SECOND; a
  // ReadToEnd/buffer-until-exit implementation stamps both together at exit (gap
  // ~0). The CLI is plain `node <script>` (UnityCliPath = 'node') so it resolves
  // identically on every OS without a .sh/.cmd shim. ----------------------------
  describe("BLOCKER-1 live per-line streaming", () => {
    // Build a node CLI script the harness drives via `node <scriptPath> install`.
    function writeNodeCli(bodyJs) {
      const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-resilience-livecli-"));
      workspaces.push(base);
      const scriptPath = path.join(base, "cli.js");
      fs.writeFileSync(
        scriptPath,
        ['"use strict";', "const fs = require('fs');", bodyJs, ""].join("\n"),
        "utf8"
      );
      return scriptPath;
    }

    // Run Invoke-UnityCliCaptureWithTimeout (AST-extracted) against `node
    // <cliScript> install`, proxying Write-Host to stamp each echoed line into a
    // timing file. Returns { status, stdout, stderr, timingPath, timingRows }
    // where timingRows is [{ ticks, text }] in echo order.
    function runCaptureWithTiming(cliScript, timeoutSeconds) {
      const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-resilience-liverun-"));
      workspaces.push(base);
      const timingPath = path.join(base, "timing.tsv");
      const cliScriptLiteral = cliScript.replace(/'/g, "''");
      const timingLiteral = timingPath.replace(/'/g, "''");
      const out = runPwshScript(
        [
          "Set-StrictMode -Version Latest",
          "$ErrorActionPreference = 'Stop'",
          extractEnsureEditorFunctions([
            "Invoke-UnityCliCaptureWithTimeout",
            "Get-LastCliProgressMessage"
          ]),
          // `node` resolves via PATH in ProcessStartInfo on every OS, so no shell
          // shim is needed; the CLI script path is the first -Argument.
          "$script:UnityCliPath = 'node'",
          `$timingFile = '${timingLiteral}'`,
          // Proxy Write-Host: stamp UtcNow.Ticks + the line into the timing file,
          // then forward to the real cmdlet so the live console behavior is
          // unchanged (and order is preserved -- all on the main thread).
          "function Write-Host { param([Parameter(ValueFromRemainingArguments=$true)]$msg)",
          "  $text = ($msg -join ' ')",
          '  Add-Content -LiteralPath $timingFile -Value ("$([DateTime]::UtcNow.Ticks)`t$text")',
          "  Microsoft.PowerShell.Utility\\Write-Host $text",
          "}",
          `$result = Invoke-UnityCliCaptureWithTimeout -Arguments @('${cliScriptLiteral}', 'install') -TimeoutSeconds ${timeoutSeconds}`,
          'Microsoft.PowerShell.Utility\\Write-Output ("RESULT Success=$($result.Success) Exit=$($result.ExitCode)")',
          "Microsoft.PowerShell.Utility\\Write-Output (\"OUTPUT=$($result.Output -join '|')\")"
        ].join("\n")
      );
      let timingRows = [];
      try {
        timingRows = fs
          .readFileSync(timingPath, "utf8")
          .split(/\r?\n/)
          .filter((l) => l.includes("\t"))
          .map((l) => {
            const tab = l.indexOf("\t");
            return { ticks: Number(l.slice(0, tab)), text: l.slice(tab + 1) };
          });
      } catch {
        // left empty; the assertions below surface a missing file clearly.
      }
      return {
        status: out.status,
        stdout: out.stdout || "",
        stderr: out.stderr || "",
        timingPath,
        timingRows
      };
    }

    function firstStamp(rows, text) {
      const row = rows.find((r) => r.text === text);
      return row ? row.ticks : null;
    }

    // Generous (2.5s) sleep keeps the timing margin huge so the assertion is robust
    // on a slow/loaded CI box, never flaky. A LIVE run stamps FIRST ~2.5s before
    // SECOND; a buffered run stamps them within a few ms of each other at exit.
    test("each line is echoed LIVE as it arrives (FIRST is streamed well before SECOND)", () => {
      const cli = writeNodeCli(
        [
          "console.log('FIRST');",
          "setTimeout(() => { console.log('SECOND'); process.exit(0); }, 2500);"
        ].join("\n")
      );
      const run = runCaptureWithTiming(cli, 30);

      // The function completed normally and captured both lines in arrival order.
      expect(combinedText(run)).toContain("RESULT Success=True Exit=0");
      expect(combinedText(run)).toContain("OUTPUT=FIRST|SECOND");

      const firstAt = firstStamp(run.timingRows, "FIRST");
      const secondAt = firstStamp(run.timingRows, "SECOND");
      // Both lines must have been individually echoed (so both were stamped).
      expect(firstAt).not.toBeNull();
      expect(secondAt).not.toBeNull();

      // THE LIVE-STREAMING PROOF: the gap between echoing FIRST and echoing SECOND
      // must be a large fraction of the 2.5s child sleep. A ReadToEnd/buffer-until-
      // exit regression stamps both together at exit, collapsing this to ~0ms
      // (empirically ~1ms) -- so a 1500ms floor cleanly separates live from
      // buffered with a >900ms safety margin. (1 tick = 100ns; 10000 ticks = 1ms.)
      const gapMs = (secondAt - firstAt) / 10000;
      expect(gapMs).toBeGreaterThan(1500);
    }, 60000);

    // Belt-and-braces (also live-distinguishing in the kill direction): with a
    // SHORT timeout that fires AFTER FIRST is printed but BEFORE SECOND, FIRST must
    // be present in the captured output -- i.e. it was drained as it arrived, not
    // lost. (This complements the timing proof above and the orphan-pipe test.)
    test("a short timeout firing between two prints still captures the line drained before the kill", () => {
      const cli = writeNodeCli(
        [
          // Print FIRST immediately, then HANG well past the timeout (never print
          // SECOND, never exit) so the deadline fires between the two.
          "console.log('FIRST');",
          "setTimeout(() => { console.log('SECOND'); }, 60000);",
          "setInterval(() => {}, 100000);"
        ].join("\n")
      );
      const run = runCaptureWithTiming(cli, 2);

      const combined = combinedText(run);
      // It timed out (sentinel 124) and was classified as a failure...
      expect(combined).toContain("RESULT Success=False Exit=124");
      // ...yet FIRST -- printed before the kill -- was streamed/captured live.
      expect(combined).toContain("OUTPUT=FIRST");
      expect(firstStamp(run.timingRows, "FIRST")).not.toBeNull();
      // SECOND was never reached, so it must NOT appear.
      expect(run.timingRows.some((r) => r.text === "SECOND")).toBe(false);
    }, 60000);
  });

  // --- ORPHAN-PIPE HANG (MAJOR-1): the OLD WaitForExit(timeoutMs)+ReadToEnd shape
  //     mis-handled a child that EXITS quickly while a grandchild inherits the
  //     redirected stdout and holds it open: WaitForExit returned TRUE (the direct
  //     child exited) so nothing was killed, ReadToEnd never completed (pipe still
  //     open), the run stalled, and -- the real defect -- it reported Success=TRUE
  //     with NO output. The poll loop fixes this structurally: EOF never arrives
  //     (the pipe stays open), so the loop runs to the DEADLINE, tree-kills, and
  //     classifies the run as a TIMEOUT failure (Success=$false / exit 124) with
  //     the pre-exit output captured -- never a silent false success.
  //
  // NOTE ON THE GRANDCHILD: once the direct child exits, the OS reparents the
  // grandchild to init (PID 1 on POSIX), so it is no longer in any process tree
  // rooted at the (dead) direct child -- Kill($true)/any tree walk provably cannot
  // reach a fully-orphaned grandchild on any platform. The load-bearing, portable
  // guarantee this test pins is therefore the CORRECTNESS one: the run is NOT a
  // false success and DOES hit the timeout path. The test cleans up the orphan it
  // intentionally creates so the suite leaves nothing behind. -------------------
  describe("MAJOR-1 orphan-pipe hang is classified as a timeout, not a false success", () => {
    test("a child that exits 0 but leaves a grandchild holding stdout hits the deadline (Success=$false / exit 124)", () => {
      const base = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-resilience-orphan-"));
      workspaces.push(base);
      const gcPidFile = path.join(base, "grandchild.pid");

      // The CLI: spawn a DETACHED, unref'd grandchild that inherits this process's
      // stdout (fd 1) so the redirected pipe stays open after we exit; record its
      // pid; print one line; then exit 0 IMMEDIATELY. The held-open pipe means the
      // capture loop never sees stdout EOF.
      const cliScript = (() => {
        const dir = fs.mkdtempSync(path.join(os.tmpdir(), "dxm-ensure-resilience-orphancli-"));
        workspaces.push(dir);
        const p = path.join(dir, "cli.js");
        fs.writeFileSync(
          p,
          [
            '"use strict";',
            "const fs = require('fs');",
            "const { spawn } = require('child_process');",
            `const gcPidFile = ${JSON.stringify(gcPidFile)};`,
            // Grandchild inherits parent's stdout (fd 1) -> pipe stays open.
            "const gc = spawn(process.execPath, ['-e', 'setInterval(() => {}, 100000)'], { stdio: ['ignore', 1, 'ignore'], detached: true });",
            "fs.writeFileSync(gcPidFile, String(gc.pid));",
            "gc.unref();",
            "process.stdout.write('PARENT_EXITING\\n');",
            "process.exit(0);",
            ""
          ].join("\n"),
          "utf8"
        );
        return p;
      })();

      const startedAt = Date.now();
      const run = runCaptureViaAst(cliScript, 2);
      const elapsedMs = Date.now() - startedAt;
      const combined = combinedText(run);

      // It must NOT have run forever: a 2s deadline + a bounded reap is well under
      // 30s. The OLD code returned ~instantly with a WRONG success here; the new
      // code spends ~the deadline then reports failure -- both are << 30s, so the
      // load-bearing assertion is the OUTCOME (below), not the wall time.
      expect(elapsedMs).toBeLessThan(30000);

      // THE FIX: classified as a TIMEOUT failure (sentinel 124), NOT a silent
      // success-with-no-output.
      expect(combined).toContain("Success=False");
      expect(combined).toContain("Exit=124");
      expect(combined).toContain("TIMED OUT after 2 second(s)");
      // The line printed before the parent exited was drained live, not discarded
      // (the OLD ReadToEnd path discarded ALL output in this scenario).
      expect(combined).toContain("PARENT_EXITING");

      // The direct child (`node <cli>`) is gone (it exited on its own). Clean up
      // the intentionally-orphaned grandchild so the suite leaks nothing; assert it
      // existed so the scenario is real (its survival is an OS guarantee we do NOT
      // assert -- see the describe note).
      const reapOrphan = (pidPath) => {
        if (!fs.existsSync(pidPath)) {
          return;
        }
        const pid = Number(fs.readFileSync(pidPath, "utf8").trim());
        if (!Number.isInteger(pid) || pid <= 0) {
          return;
        }
        try {
          process.kill(pid, "SIGKILL");
        } catch {
          // already gone -- nothing to clean up.
        }
      };
      expect(fs.existsSync(gcPidFile)).toBe(true);
      reapOrphan(gcPidFile);
    }, 60000);
  });
});

// Run Invoke-UnityCliCaptureWithTimeout (AST-extracted) against `node <cliScript>
// install` WITHOUT the Write-Host timing proxy -- a plain capture run used where
// only the returned Success/Exit/Output and the streamed annotations matter.
function runCaptureViaAst(cliScript, timeoutSeconds) {
  const cliScriptLiteral = cliScript.replace(/'/g, "''");
  const out = runPwshScript(
    [
      "Set-StrictMode -Version Latest",
      "$ErrorActionPreference = 'Stop'",
      extractEnsureEditorFunctions([
        "Invoke-UnityCliCaptureWithTimeout",
        "Get-LastCliProgressMessage"
      ]),
      "$script:UnityCliPath = 'node'",
      `$result = Invoke-UnityCliCaptureWithTimeout -Arguments @('${cliScriptLiteral}', 'install') -TimeoutSeconds ${timeoutSeconds}`,
      'Write-Output ("RESULT Success=$($result.Success) Exit=$($result.ExitCode)")',
      "Write-Output (\"OUTPUT=$($result.Output -join '|')\")"
    ].join("\n")
  );
  return { status: out.status, stdout: out.stdout || "", stderr: out.stderr || "" };
}
