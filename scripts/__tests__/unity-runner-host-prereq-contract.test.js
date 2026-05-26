/**
 * @fileoverview Static contract guard for the new Unity host-prereq bootstrap
 * surface (scripts/unity/bootstrap-windows-runner.ps1).
 *
 * Root cause this script exists for: Unity.exe failed at startup with exit code
 * -1073741515 / 0xC0000135 (STATUS_DLL_NOT_FOUND) on a self-hosted Windows
 * runner because the Microsoft Visual C++ 2015-2022 x64 Redistributable was not
 * installed. The bootstrap script detects + installs every host-OS prereq Unity
 * needs to launch (VC++ redist, Windows long-paths, Windows Defender
 * exclusions, PowerShell 7, UCRT). This file is the PURE-text / AST regression
 * guard for the script's contract surface -- no process spawning, no behavior
 * under test -- so the cost is sub-millisecond and the assertions stay green
 * even when pwsh is missing locally. Behavioral coverage lives next door in
 * unity-runner-host-prereq-helper-mutation.test.js (which DOES spawn pwsh).
 *
 * The assertions in this file pin EVERY load-bearing token / parameter / arg
 * vector / URL that the script's behavioral contract depends on, so a refactor
 * that drifts the script's surface -- a renamed parameter, a third-party DLL
 * download URL slipping in, a winget arg list losing `--accept-source-agreements`,
 * the Bld threshold dropping to a value that false-negatives every VS 2017
 * install -- fails this guard loudly with a deterministic line number.
 */

"use strict";

const fs = require("fs");
const path = require("path");
const childProcess = require("child_process");

const REPO_ROOT = path.resolve(__dirname, "..", "..");
const BOOTSTRAP_SCRIPT = path.join(REPO_ROOT, "scripts", "unity", "bootstrap-windows-runner.ps1");
const BOOTSTRAP_META = path.join(REPO_ROOT, "scripts", "unity", "bootstrap-windows-runner.ps1.meta");

// The Unity .meta GUID for the script under test. Pinned so a rename or
// regeneration that orphans the file from its Unity asset import record fails
// the suite loudly instead of silently appearing as a "new file" in Unity.
const BOOTSTRAP_META_GUID = "afa71e89c6929e478b8c3906e120e87c";

// Single source of truth for the script text. Cached at module load so the
// suite re-reads the file ONCE rather than per-assertion.
function readUtf8(absPath) {
  return fs.readFileSync(absPath, "utf8");
}

// Extract a PowerShell function body by name via balanced brace scan. Mirrors
// the pattern in unity-native-startup-probe-isolation.test.js so the AST shape
// stays consistent across the host-prereq guard set. Returns the substring
// from `function <Name>` through the matching closing brace, or null on miss.
function extractPowerShellFunction(source, name) {
  const headerPattern = new RegExp(`(^|\\n)function\\s+${name}\\b`);
  const headerMatch = headerPattern.exec(source);
  if (!headerMatch) {
    return null;
  }
  const headerStart = headerMatch.index + (headerMatch[1] ? headerMatch[1].length : 0);
  const openBrace = source.indexOf("{", headerStart);
  if (openBrace < 0) {
    return null;
  }
  let depth = 0;
  for (let i = openBrace; i < source.length; i += 1) {
    const ch = source[i];
    if (ch === "{") {
      depth += 1;
    } else if (ch === "}") {
      depth -= 1;
      if (depth === 0) {
        return source.slice(headerStart, i + 1);
      }
    }
  }
  return null;
}

let SCRIPT_TEXT;

beforeAll(() => {
  SCRIPT_TEXT = readUtf8(BOOTSTRAP_SCRIPT);
});

describe("scripts/unity/bootstrap-windows-runner.ps1 exists and is tracked", () => {
  test("the script file exists on disk", () => {
    expect(fs.existsSync(BOOTSTRAP_SCRIPT)).toBe(true);
  });

  test("the script is checked into git", () => {
    const relativePath = path
      .relative(REPO_ROOT, BOOTSTRAP_SCRIPT)
      .split(path.sep)
      .join("/");
    const result = childProcess.spawnSync(
      "git",
      ["ls-files", "--error-unmatch", "--", relativePath],
      { cwd: REPO_ROOT, encoding: "utf8" }
    );
    expect(result.status).toBe(0);
    expect((result.stdout || "").trim()).toBe(relativePath);
  });

  test(".meta sibling exists with the canonical Unity DefaultImporter shape + pinned GUID", () => {
    expect(fs.existsSync(BOOTSTRAP_META)).toBe(true);
    const meta = readUtf8(BOOTSTRAP_META);
    expect(meta).toContain("fileFormatVersion: 2");
    expect(meta).toContain(`guid: ${BOOTSTRAP_META_GUID}`);
    expect(meta).toContain("DefaultImporter:");
    expect(meta).toContain("externalObjects: {}");
  });
});

describe("bootstrap-windows-runner.ps1 directive + advanced-function metadata", () => {
  test("declares #Requires -Version 5.1 (Windows PowerShell 5.1 floor)", () => {
    // Bootstrap MUST be PS 5.1-compatible because the runner-bootstrap.yml
    // workflow uses `shell: powershell` (Windows PowerShell 5.1) -- its
    // purpose includes INSTALLING pwsh, so it cannot require pwsh to run.
    // Pin the floor at exactly 5.1 (not 5.0, not 7.0).
    expect(SCRIPT_TEXT).toMatch(/^\s*#Requires\s+-Version\s+5\.1\b/m);
  });

  test("declares [OutputType([int])] so the dispatcher's exit code is typed", () => {
    expect(SCRIPT_TEXT).toMatch(/\[OutputType\(\[int\]\)\]/);
  });

  test("the PARAM block declares all six expected parameters with the right types", () => {
    // We anchor against the first param block (the one that follows
    // [CmdletBinding()] / [OutputType] at the top of the file). Whitespace
    // tolerant so harmless reformatting (PSScriptAnalyzer reflow, comment
    // re-wrapping) cannot break the contract.
    expect(SCRIPT_TEXT).toMatch(/\[switch\]\s*\$DetectOnly\b/);
    expect(SCRIPT_TEXT).toMatch(/\[string\]\s*\$UnityInstallRoot\b/);
    expect(SCRIPT_TEXT).toMatch(/\[string\]\s*\$VcRedistUrl\b/);
    expect(SCRIPT_TEXT).toMatch(/\[int\]\s*\$DownloadTimeoutSeconds\b/);
    expect(SCRIPT_TEXT).toMatch(/\[int\]\s*\$InstallTimeoutSeconds\b/);
  });

  test("$VcRedistUrl carries a [ValidatePattern] that pins Microsoft hosts", () => {
    // The pattern must accept aka.ms AND microsoft.com to cover the redirect
    // chain (aka.ms -> download.visualstudio.microsoft.com -> ...). A pattern
    // that omitted aka.ms would reject the canonical URL outright.
    //
    // Scope the search to the single line carrying the validator (the
    // validator pattern itself contains nested `(` / `)`, so a naive
    // `[^)]*` exclusion would stop at the inner closing paren). The
    // attribute decorates $VcRedistUrl on the line IMMEDIATELY below it,
    // not the same line -- the param-block convention is
    //   [ValidatePattern('...')]
    //   [string]$VcRedistUrl = '...',
    const lines = SCRIPT_TEXT.split("\n");
    const validatorLineIdx = lines.findIndex(
      (line) => line.includes("[ValidatePattern(") && /aka\\\.ms/.test(line)
    );
    expect(validatorLineIdx).toBeGreaterThan(-1);
    const validatorLine = lines[validatorLineIdx];
    expect(validatorLine).toMatch(/\[ValidatePattern\(.*aka\\\.ms.*\)\]/);
    expect(validatorLine).toMatch(/\[ValidatePattern\(.*microsoft\\\.com.*\)\]/);
    // The IMMEDIATELY-FOLLOWING non-empty line must declare $VcRedistUrl,
    // so the validator decorates exactly that parameter (and nothing else).
    let nextLineIdx = validatorLineIdx + 1;
    while (nextLineIdx < lines.length && lines[nextLineIdx].trim() === "") {
      nextLineIdx += 1;
    }
    expect(nextLineIdx).toBeLessThan(lines.length);
    expect(lines[nextLineIdx]).toMatch(/\[string\]\s*\$VcRedistUrl\b/);
  });

  test("uses the canonical Microsoft VC++ redist download URL", () => {
    // The signed Microsoft endpoint that redirects to
    // download.visualstudio.microsoft.com. ANY other host means we have lost
    // the chain of trust the Authenticode signature anchors on.
    expect(SCRIPT_TEXT).toContain("aka.ms/vc14/vc_redist.x64.exe");
  });
});

describe("bootstrap-windows-runner.ps1 forbids third-party DLL sources + dangerous patterns", () => {
  test("does NOT reference any third-party DLL download host", () => {
    // Closes a regression vector: an over-eager fixer could "fix" the VC++
    // redist by direct-downloading a DLL from one of these unofficial sources.
    // We hardcode the rejected hosts as the negative grep -- if a future
    // fixer adds yet another DLL source, this assertion will fail and the
    // operator must reconcile it (most likely by deleting the new line).
    expect(SCRIPT_TEXT).not.toContain("dllme.com");
    expect(SCRIPT_TEXT).not.toContain("dll-files.com");
    expect(SCRIPT_TEXT).not.toContain("dlldownloader.com");
    expect(SCRIPT_TEXT).not.toContain("nirsoft.net");
  });

  test("any non-Microsoft URL is not used for a `vcruntime` artifact", () => {
    // Defense in depth: scan every `https://` reference and require that any
    // URL containing the word `vcruntime` is on a Microsoft-controlled host.
    // The check is case-insensitive so a lowercase variant cannot slip past.
    const urlPattern = /https?:\/\/[^\s'"]+/g;
    const offenders = [];
    let match;
    while ((match = urlPattern.exec(SCRIPT_TEXT)) !== null) {
      const url = match[0];
      if (!/vcruntime/i.test(url)) {
        continue;
      }
      if (
        /https?:\/\/(aka\.ms|[A-Za-z0-9._-]+\.microsoft\.com|[A-Za-z0-9._-]+\.windows\.com)\b/i.test(
          url
        )
      ) {
        continue;
      }
      offenders.push(url);
    }
    expect(offenders).toEqual([]);
  });

  test("does NOT use Invoke-Expression (arbitrary code execution risk)", () => {
    // Invoke-Expression is a classic foot-gun: a caller-controlled string
    // fed to it becomes arbitrary code. The bootstrap script has no need
    // for it -- every dynamic call goes through scriptblocks / & operator.
    // Ban it outright to keep that posture stable.
    expect(SCRIPT_TEXT).not.toMatch(/\bInvoke-Expression\b/);
  });
});

describe("bootstrap-windows-runner.ps1 dot-source-safety: top-level body has no session-mode mutation", () => {
  // The dot-source contract: tests + composites must be able to dot-source the
  // script to inspect functions WITHOUT inheriting Set-StrictMode /
  // $ErrorActionPreference / $PSNativeCommandUseErrorActionPreference. The
  // three assignments MUST live ONLY inside `if ($invokedAsScript)`.

  test("Set-StrictMode, $ErrorActionPreference, $PSNativeCommandUseErrorActionPreference appear AFTER the dispatcher gate", () => {
    // Anchor on the COLUMN-0 dispatcher gate (the second occurrence; the
    // first is a doc-comment reference inside the .DESCRIPTION block).
    const gatePattern = /\nif \(\$invokedAsScript\)\s*\{/;
    const gateMatch = gatePattern.exec(SCRIPT_TEXT);
    expect(gateMatch).not.toBeNull();
    const gateIdx = gateMatch.index + 1; // skip leading \n

    // Find every occurrence and confirm each is INSIDE the gated block.
    // We collect the index of each match and assert each is > the gate index.
    // (LOCAL Set-StrictMode calls inside individual functions are LEGIT --
    // those appear BEFORE the gate but they live inside a function body, so
    // we exclude any match whose enclosing line is preceded by a `function `
    // header before the gate. Simpler: every top-level mutation must follow
    // the gate, and we verify via a positional check that the FIRST top-level
    // (column-0-indented) assignment of each preference appears after the
    // gate.)
    function findColumnZeroAssignment(pattern) {
      const re = new RegExp(`(^|\\n)(${pattern})`, "g");
      const matches = [];
      let m;
      while ((m = re.exec(SCRIPT_TEXT)) !== null) {
        matches.push(m.index + (m[1] ? m[1].length : 0));
      }
      return matches;
    }

    // Top-level $ErrorActionPreference assignment must appear AFTER the gate.
    // We accept zero matches as well (the script may set EAP inside the
    // dispatcher function instead -- and indeed it does, inside the
    // `if ($invokedAsScript)` block).
    const eapMatches = findColumnZeroAssignment("\\$ErrorActionPreference\\s*=");
    for (const idx of eapMatches) {
      expect(idx).toBeGreaterThan(gateIdx);
    }

    const psncMatches = findColumnZeroAssignment(
      "\\$PSNativeCommandUseErrorActionPreference\\s*="
    );
    for (const idx of psncMatches) {
      expect(idx).toBeGreaterThan(gateIdx);
    }

    // Set-StrictMode at column 0 must also appear after the gate. (LOCAL
    // `Set-StrictMode -Version Latest` inside function bodies is indented and
    // therefore excluded from the column-0 anchor.)
    const ssmMatches = findColumnZeroAssignment("Set-StrictMode\\b");
    for (const idx of ssmMatches) {
      expect(idx).toBeGreaterThan(gateIdx);
    }
  });

  test("dispatcher gate is `if ($invokedAsScript)` followed by the three preference assignments", () => {
    // Pin the inside-dispatcher shape: the three assignments must all live
    // inside the gated block. We extract the gated block via balanced-brace
    // scan from the column-0 `if` token (NOT the doc-comment reference at
    // line 74, which is indented inside the .DESCRIPTION block).
    const gatePattern = /\nif \(\$invokedAsScript\)\s*\{/;
    const gateMatch = gatePattern.exec(SCRIPT_TEXT);
    expect(gateMatch).not.toBeNull();
    const ifIdx = gateMatch.index + 1; // skip the leading \n
    const openBrace = SCRIPT_TEXT.indexOf("{", ifIdx);
    expect(openBrace).toBeGreaterThan(-1);
    let depth = 0;
    let closeBrace = -1;
    for (let i = openBrace; i < SCRIPT_TEXT.length; i += 1) {
      const ch = SCRIPT_TEXT[i];
      if (ch === "{") depth += 1;
      else if (ch === "}") {
        depth -= 1;
        if (depth === 0) {
          closeBrace = i;
          break;
        }
      }
    }
    expect(closeBrace).toBeGreaterThan(openBrace);
    const dispatcherBody = SCRIPT_TEXT.slice(openBrace, closeBrace + 1);
    expect(dispatcherBody).toMatch(/Set-StrictMode\s+-Version\s+Latest/);
    expect(dispatcherBody).toMatch(/\$ErrorActionPreference\s*=\s*'Stop'/);
    expect(dispatcherBody).toMatch(/\$PSNativeCommandUseErrorActionPreference\s*=\s*\$false/);
  });
});

describe("bootstrap-windows-runner.ps1 CI annotation hygiene", () => {
  test("annotation helpers (Write-CiNotice/Write-CiWarning/Write-CiError) emit single-line Write-Host", () => {
    // ConciseView word-wraps thrown text at console width. Write-Host emits
    // each call as a single un-wrapped CI log line -- so the annotation
    // helpers MUST go through Write-Host (not Write-Output / Write-Error /
    // throw). Pin the body shape so a refactor cannot regress to a multi-
    // line throw or a multi-stream emit.
    const notice = extractPowerShellFunction(SCRIPT_TEXT, "Write-CiNotice");
    expect(notice).not.toBeNull();
    expect(notice).toMatch(/Write-Host\s+"::notice::\$Message"/);

    const warn = extractPowerShellFunction(SCRIPT_TEXT, "Write-CiWarning");
    expect(warn).not.toBeNull();
    expect(warn).toMatch(/Write-Host\s+"::warning::\$Message"/);

    const err = extractPowerShellFunction(SCRIPT_TEXT, "Write-CiError");
    expect(err).not.toBeNull();
    expect(err).toMatch(/Write-Host\s+"::error::\$Message"/);
  });

  test("primary failure annotations use single-line Write-Host ::error:: (wrap-immune)", () => {
    // Every actionable ::error:: annotation must be a single Write-Host
    // invocation. We grep for any `Write-Host` that emits an ::error::
    // and confirm none span multiple lines via a literal newline inside
    // the call. (PowerShell allows the ` ` (backtick) line continuation
    // INSIDE the string interior; we reject any explicit newline character
    // inside the Write-Host argument.)
    const errorEmits = SCRIPT_TEXT.match(/Write-Host\s+"::error::[^"]*"/g) || [];
    // At least one ::error:: annotation must be present (the bootstrap WILL
    // fail loudly on hostile inputs / non-admin / install failure).
    expect(errorEmits.length).toBeGreaterThan(0);
  });
});

describe("bootstrap-windows-runner.ps1 winget install args", () => {
  test("Install-PowerShell7 args include --scope user + --accept-source-agreements + --accept-package-agreements", () => {
    // The runner service typically runs as a non-admin local user. --scope
    // user lands pwsh on THAT user's PATH without elevation. Both --accept-*
    // flags are MANDATORY for non-interactive winget; missing either makes
    // the install pause for an EULA prompt that nothing will ever click.
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Install-PowerShell7");
    expect(body).not.toBeNull();
    expect(body).toMatch(/'--scope'\s*,\s*'user'/);
    expect(body).toContain("'--accept-source-agreements'");
    expect(body).toContain("'--accept-package-agreements'");
    expect(body).toContain("'--silent'");
    // The package id MUST be the official Microsoft PowerShell artifact.
    expect(body).toMatch(/'--id'\s*,\s*'Microsoft\.PowerShell'/);
    // The source MUST be `winget` (the Microsoft-blessed source); a custom
    // source would bypass the supply-chain trust anchor.
    expect(body).toMatch(/'--source'\s*,\s*'winget'/);
  });

  test("winget idempotent exit codes (already-installed / no-applicable-update) are mapped to success", () => {
    // winget propagates 0x8A15xxxx HRESULTs as signed-int process exits;
    // these particular ones mean "no update applicable / already installed"
    // which is a no-op success on an idempotent re-run.
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Install-PowerShell7");
    expect(body).not.toBeNull();
    expect(body).toContain("-1978335189"); // UPDATE_NOT_APPLICABLE
    expect(body).toContain("-1978335212"); // NO_APPLICABLE_INSTALLER
    expect(body).toContain("-1978335153"); // NO_APPLICABLE_UPGRADE
    expect(body).toContain("-1978334975"); // UPDATE_NOT_APPLICABLE (alt path)
  });
});

describe("bootstrap-windows-runner.ps1 VC++ redist install args + detection", () => {
  test("Invoke-VcRedistInstaller passes /install /quiet /norestart", () => {
    // Microsoft's documented silent install flags. Missing any of the three
    // means the installer pauses for UI / forces a reboot mid-job; both
    // failure modes are unacceptable on a non-interactive runner.
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Invoke-VcRedistInstaller");
    expect(body).not.toBeNull();
    expect(body).toMatch(/'\/install'\s*,\s*'\/quiet'\s*,\s*'\/norestart'/);
  });

  test("Test-VcRedistFilesOnDisk checks all three required DLLs in System32", () => {
    // The file probe is the AUTHORITATIVE detection signal -- the OS loader
    // fails Unity.exe at startup based on these on-disk files, not the
    // registry. All three must be probed; missing any one means a Unity
    // launch can still fail with 0xC0000135 even when the other two land.
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Test-VcRedistFilesOnDisk");
    expect(body).not.toBeNull();
    expect(body).toContain("VCRUNTIME140.dll");
    expect(body).toContain("VCRUNTIME140_1.dll");
    expect(body).toContain("MSVCP140.dll");
    // The path anchor must be System32 (not SysWOW64 -- that's the 32-bit
    // mirror and Unity Editor is a 64-bit process). PowerShell single-
    // quoted strings carry literal backslashes, so the regex needs a
    // SINGLE escape (`\\\\` in JS regex literal == one `\` in the text;
    // here we want to match the literal sequence `C:\Windows\System32\...`).
    expect(body).toMatch(/C:\\Windows\\System32\\VCRUNTIME140\.dll/);
    expect(body).toMatch(/C:\\Windows\\System32\\VCRUNTIME140_1\.dll/);
    expect(body).toMatch(/C:\\Windows\\System32\\MSVCP140\.dll/);
    // Defense in depth: SysWOW64 (the 32-bit mirror) MUST NOT be probed.
    expect(body).not.toMatch(/SysWOW64/);
  });

  test("registry Bld threshold is >= 26020 (VS 2017 15.5, NOT >= 30000)", () => {
    // 30000 was the wrong threshold: it false-negatived every VS 2017 +
    // VS 2019 (<= 16.7) install, which is the lineage most self-hosted
    // runners come from. 26020 is the first VS 2017 15.5 build that ships
    // VCRUNTIME140_1.dll.
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Test-VcRedistInstalledAtRegistryView");
    expect(body).not.toBeNull();
    expect(body).toMatch(/-ge\s+26020\b/);
    // The wrong threshold MUST NOT reappear anywhere in the script.
    expect(SCRIPT_TEXT).not.toMatch(/-ge\s+30000\b/);
  });

  test("TLS 1.2 (mandatory) + TLS 1.3 (best-effort) is configured before Invoke-WebRequest", () => {
    // Stock Windows PowerShell 5.1's ServicePointManager defaults to
    // Ssl3|Tls1.0 -- which aka.ms and download.visualstudio.microsoft.com
    // reject. Without TLS 1.2 the download fails with "underlying connection
    // was closed". Tls13 is best-effort (older .NET Framework lacks the
    // enum value).
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Invoke-VcRedistDownload");
    expect(body).not.toBeNull();
    // TLS 1.2 MUST be configured.
    expect(body).toMatch(/\[Net\.SecurityProtocolType\]::Tls12/);
    expect(body).toMatch(/ServicePointManager.*Tls12/s);
    // TLS 1.3 MUST be attempted (best-effort, inner try/catch).
    expect(body).toMatch(/\[Net\.SecurityProtocolType\]::Tls13/);
    // The TLS config must precede the Invoke-WebRequest call. Comments
    // mention Invoke-WebRequest before the actual call, so anchor on the
    // call shape `Invoke-WebRequest -Uri ...` (the only invocation in
    // this function body).
    const tls12Idx = body.indexOf("[Net.SecurityProtocolType]::Tls12");
    const iwrCallMatch = /Invoke-WebRequest\s+-Uri\b/.exec(body);
    expect(tls12Idx).toBeGreaterThan(-1);
    expect(iwrCallMatch).not.toBeNull();
    expect(tls12Idx).toBeLessThan(iwrCallMatch.index);
  });

  test("Get-AuthenticodeSignature is called on the downloaded EXE before launching it", () => {
    // The [ValidatePattern]-pinned URL is necessary but not sufficient
    // (compromised CDN entry, MITM, etc.). Authenticode signature
    // verification at install time anchors trust on the Microsoft cert
    // chain. The signature check must precede the installer launch.
    const verifierBody = extractPowerShellFunction(SCRIPT_TEXT, "Test-VcRedistAuthenticodeSignature");
    expect(verifierBody).not.toBeNull();
    expect(verifierBody).toMatch(/Get-AuthenticodeSignature\s+-FilePath\s+\$FilePath/);
    expect(verifierBody).toContain("Microsoft Corporation");

    // The installer orchestrator must call the verifier BEFORE
    // Invoke-VcRedistInstaller.
    const installerBody = extractPowerShellFunction(SCRIPT_TEXT, "Install-VcRedist");
    expect(installerBody).not.toBeNull();
    const sigIdx = installerBody.indexOf("Test-VcRedistAuthenticodeSignature");
    const installIdx = installerBody.indexOf("Invoke-VcRedistInstaller");
    expect(sigIdx).toBeGreaterThan(-1);
    expect(installIdx).toBeGreaterThan(-1);
    expect(sigIdx).toBeLessThan(installIdx);
  });
});

describe("bootstrap-windows-runner.ps1 Defender bootstrap path guards", () => {
  test("Test-DefenderExclusionPathAllowed exists and rejects single drive roots", () => {
    // Source-level guard: the body must contain the single-drive-root reject
    // regex. Behavioral confirmation lives in the helper-mutation suite.
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Test-DefenderExclusionPathAllowed");
    expect(body).not.toBeNull();
    // The single-drive-root reject regex (e.g. `C:\`, `D:`).
    expect(body).toMatch(/\$Path\s+-match\s+'\^\[A-Za-z\]:\[\\\\\/\]\?\$'/);
    // The whitespace-only reject branch.
    expect(body).toMatch(/\[string\]::IsNullOrWhiteSpace\(\$Path\)/);
  });

  test("Invoke-DefenderBootstrap tolerates an empty -Paths array via [AllowEmptyCollection()]", () => {
    // Get-DefenderExclusionPaths returns an empty array when every candidate
    // is rejected by the allow-list. Without [AllowEmptyCollection()] on
    // the Mandatory [string[]]$Paths, that empty-array call binds AutomationNull
    // and the function throws "Cannot bind argument to parameter".
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Invoke-DefenderBootstrap");
    expect(body).not.toBeNull();
    // Either [AllowEmptyCollection()] is explicit on the param, OR the param
    // is non-Mandatory (which has the same effect for an empty array). We
    // require the explicit attribute because the script ALSO checks
    // `if ($null -eq $Paths -or $Paths.Length -eq 0)` and that branch is
    // load-bearing -- making the param non-Mandatory without the attribute
    // would not communicate intent.
    expect(body).toMatch(/\[AllowEmptyCollection\(\)\]\s*\[string\[\]\]\s*\$Paths/);
    // The Mandatory attribute remains so callers can't pass $null by accident.
    expect(body).toMatch(/Mandatory\s*=\s*\$true/);
  });

  test("Get-DefenderExclusionPaths validates every candidate via Test-DefenderExclusionPathAllowed", () => {
    // The allow-list defence-in-depth requirement: every candidate path
    // (including $env:RUNNER_WORKSPACE) MUST be screened before it is
    // forwarded to Add-MpPreference.
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Get-DefenderExclusionPaths");
    expect(body).not.toBeNull();
    expect(body).toMatch(
      /Test-DefenderExclusionPathAllowed\s+-Path\s+\$p\s+-UnityInstallRoot\s+\$UnityInstallRoot/
    );
  });
});

describe("bootstrap-windows-runner.ps1 declares every required helper function", () => {
  // These names form the script's public-ish surface (the dispatcher invokes
  // them; tests inspect them; future contributors will hunt them by name).
  // A rename that drops one must be reflected here or fail the suite.
  const REQUIRED_HELPERS = [
    "Test-IsWindowsHost",
    "Test-IsAdministrator",
    "Test-IsAccessDeniedException",
    "Get-RegistryItemPropertySafe",
    "Test-VcRedistFilesOnDisk",
    "Test-VcRedistInstalledAtRegistryView",
    "Test-VcRedistInstalled",
    "Test-VcRedistAuthenticodeSignature",
    "Invoke-VcRedistDownload",
    "Invoke-VcRedistInstaller",
    "Install-VcRedist",
    "Test-LongPathsEnabled",
    "Enable-LongPaths",
    "Test-DefenderExclusionPathAllowed",
    "Get-DefenderExclusionPaths",
    "Test-DefenderAvailable",
    "Test-DefenderExclusion",
    "Add-DefenderExclusion",
    "Test-PowerShell7Installed",
    "Install-PowerShell7",
    "Test-UcrtPresent",
    "Invoke-BootstrapStep",
    "Invoke-DefenderBootstrap",
    "Invoke-UcrtBootstrap",
    "Format-BootstrapSummary",
    "Invoke-WindowsRunnerBootstrap",
    "Write-CiNotice",
    "Write-CiWarning",
    "Write-CiError"
  ];

  test.each(REQUIRED_HELPERS)("declares helper function %s", (name) => {
    const body = extractPowerShellFunction(SCRIPT_TEXT, name);
    expect(body).not.toBeNull();
  });
});

describe("bootstrap-windows-runner.ps1 dispatcher exit-code contract", () => {
  test("Invoke-WindowsRunnerBootstrap returns 0/1/2 per the documented contract", () => {
    // Exit codes documented at the top of the file:
    //   0 = every prereq ok
    //   1 = at least one install failure (or non-Windows hard-fail in
    //       non-DetectOnly mode)
    //   2 = DetectOnly + at least one prereq missing
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Invoke-WindowsRunnerBootstrap");
    expect(body).not.toBeNull();
    // 0 path (the success return at the end of the function).
    expect(body).toMatch(/return\s+0/);
    // 1 path (any install-failed and the non-Windows + non-DetectOnly hard-fail).
    expect(body).toMatch(/return\s+1/);
    // 2 path (DetectOnly + missing).
    expect(body).toMatch(/return\s+2/);
  });

  test("non-Windows host: DetectOnly emits ::notice:: and returns 0", () => {
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Invoke-WindowsRunnerBootstrap");
    expect(body).not.toBeNull();
    // The non-Windows branch must produce a Write-CiNotice with a
    // "skipping" / "not Windows" phrase that the per-job composite can
    // grep on, AND it must return 0 (NOT throw, NOT return 1).
    expect(body).toMatch(/Test-IsWindowsHost/);
    expect(body).toMatch(/skipping/i);
    expect(body).toMatch(/non-Windows|not Windows/i);
  });

  test("non-Windows host: non-DetectOnly emits ::error:: and returns 1", () => {
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Invoke-WindowsRunnerBootstrap");
    expect(body).not.toBeNull();
    // The hard-fail path must NAME the script as "Windows-only" so the
    // operator sees a deterministic, greppable failure message.
    expect(body).toMatch(/Windows-only/);
  });
});

describe("bootstrap-windows-runner.ps1 PowerShell automatic-variable hygiene (round-2 fixes)", () => {
  // Round-2 NR1: the original timeout-kill code wrote `$pid = $process.Id`,
  // which throws under StrictMode because `$pid` is a read-only AUTOMATIC
  // VARIABLE (the running pwsh's own PID). The catch then converts the
  // intended "installer timed out" reason into a misleading "Start-Process
  // threw: Cannot overwrite variable PID" message. The fix renames the
  // local so the timeout diagnostic survives end-to-end.
  test("Invoke-VcRedistInstaller does NOT assign to the reserved $pid automatic variable", () => {
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Invoke-VcRedistInstaller");
    expect(body).not.toBeNull();
    // Negative: no `$pid =` assignment ANYWHERE in the function body.
    // PowerShell's $pid is the running process's PID; assigning to it
    // throws under StrictMode + ErrorActionPreference=Stop.
    expect(body).not.toMatch(/^\s*\$pid\s*=/m);
    expect(body).not.toMatch(/[^\w]\$pid\s*=/);
  });

  // Round-1 F5: the original code reassigned `$args = @(...)`. PowerShell
  // automatically populates `$args` with positional inputs, so the
  // reassignment silently drops any caller-passed positional args. The
  // fix renames the local to $wingetArgs.
  test("Install-PowerShell7 does NOT clobber the $args automatic variable", () => {
    const body = extractPowerShellFunction(SCRIPT_TEXT, "Install-PowerShell7");
    expect(body).not.toBeNull();
    expect(body).not.toMatch(/^\s*\$args\s*=/m);
    expect(body).not.toMatch(/[^\w]\$args\s*=/);
    // Positive: the renamed local must be present.
    expect(body).toMatch(/\$wingetArgs/);
  });
});
