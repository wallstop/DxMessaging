"use strict";

const fs = require("fs");
const os = require("os");
const path = require("path");
const childProcess = require("child_process");

const {
    convertMethodNameToPascalCase,
    collectMethodRenames,
    applyMethodRenames,
} = require("../fix-csharp-underscore-methods.js");

const FIXER_SCRIPT_PATH = path.resolve(__dirname, "../fix-csharp-underscore-methods.js");
const OUTSIDE_REPO_EXCLUDED_SEGMENTS = [
    ".git",
    "node_modules",
    "Library",
    "Obj",
    "Temp",
    ".vs",
    ".venv",
    ".artifacts",
    "site",
];

describe("fix-csharp-underscore-methods", () => {
    test("convertMethodNameToPascalCase removes underscores while preserving segment casing", () => {
        expect(convertMethodNameToPascalCase("Parse_Line_Bare")).toBe("ParseLineBare");
        expect(convertMethodNameToPascalCase("E2E_Leaf_Calls_Base")).toBe("E2ELeafCallsBase");
        expect(convertMethodNameToPascalCase("__parse__line__")).toBe("ParseLine");
        expect(convertMethodNameToPascalCase("Parse__Line")).toBe("ParseLine");
    });

    test("collectMethodRenames finds method declarations and skips op_ names", () => {
        const source = [
            "public sealed class NamingTests",
            "{",
            "    [Test]",
            "    public void Parse_Line_Bare() { }",
            "",
            "    private static IEnumerable<int> Edge_Case_Test_Data() => Array.Empty<int>();",
            "",
            "    public void op_Custom_Method() { }",
            "}",
        ].join("\n");

        const renames = collectMethodRenames(source);

        expect(renames.get("Parse_Line_Bare")).toBe("ParseLineBare");
        expect(renames.get("Edge_Case_Test_Data")).toBe("EdgeCaseTestData");
        expect(renames.has("op_Custom_Method")).toBe(false);
    });

    test("applyMethodRenames updates declarations and nameof references", () => {
        const source = [
            "public sealed class NamingTests",
            "{",
            "    [Test]",
            "    public void Parse_Line_Bare()",
            "    {",
            "        string methodName = nameof(Parse_Line_Bare);",
            "        Parse_Line_Bare();",
            "    }",
            "}",
        ].join("\n");

        const renames = new Map([["Parse_Line_Bare", "ParseLineBare"]]);
        const result = applyMethodRenames(source, renames);

        expect(result.renameCount).toBe(1);
        expect(result.updatedContent).toContain("public void ParseLineBare()");
        expect(result.updatedContent).toContain("nameof(ParseLineBare)");
        expect(result.updatedContent).toContain("ParseLineBare();");
    });

    test("applyMethodRenames counts unique renamed identifiers, not total occurrences", () => {
        const source = [
            "public sealed class NamingTests",
            "{",
            "    public void Method_Name()",
            "    {",
            "        Method_Name();",
            "        Method_Name();",
            "    }",
            "}",
        ].join("\n");

        const renames = new Map([["Method_Name", "MethodName"]]);
        const result = applyMethodRenames(source, renames);

        expect(result.renameCount).toBe(1);
        expect(result.updatedContent).toContain("MethodName();");
        expect(result.updatedContent).not.toContain("Method_Name");
    });

    test("--check exits non-zero when a fix is required", () => {
        const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "dxmsg-csharp-underscore-check-"));
        const filePath = path.join(tempDir, "NeedsFix.cs");

        try {
            fs.writeFileSync(
                filePath,
                [
                    "public sealed class NeedsFix",
                    "{",
                    "    public void Parse_Line_Bare() { }",
                    "}",
                ].join("\n"),
                "utf8"
            );

            const result = childProcess.spawnSync(
                process.execPath,
                [FIXER_SCRIPT_PATH, "--check", filePath],
                {
                    cwd: path.resolve(__dirname, "../.."),
                    encoding: "utf8",
                }
            );

            expect(result.status).toBe(1);
            expect(result.stderr).toContain("Found C# methods with underscores");
            expect(result.stderr).toContain("NeedsFix.cs");
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });

    test("CLI rewrites file content in place", () => {
        const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "dxmsg-csharp-underscore-fix-"));
        const filePath = path.join(tempDir, "FixMe.cs");

        try {
            fs.writeFileSync(
                filePath,
                [
                    "public sealed class FixMe",
                    "{",
                    "    public void Parse_Line_Bare() { }",
                    "}",
                ].join("\n"),
                "utf8"
            );

            const result = childProcess.spawnSync(process.execPath, [FIXER_SCRIPT_PATH, filePath], {
                cwd: path.resolve(__dirname, "../.."),
                encoding: "utf8",
            });

            expect(result.status).toBe(0);

            const updated = fs.readFileSync(filePath, "utf8");
            expect(updated).toContain("ParseLineBare");
            expect(updated).not.toContain("Parse_Line_Bare");
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });

    test.each(OUTSIDE_REPO_EXCLUDED_SEGMENTS)(
        "--check processes explicitly passed files in outside-repo %s segment",
        (excludedSegment) => {
            const tempDir = fs.mkdtempSync(
                path.join(os.tmpdir(), "dxmsg-csharp-underscore-outside-temp-check-")
            );
            const outsideSegmentDir = path.join(tempDir, excludedSegment);
            const filePath = path.join(outsideSegmentDir, "OutsideSegmentNeedsFix.cs");

            try {
                fs.mkdirSync(outsideSegmentDir, { recursive: true });
                fs.writeFileSync(
                    filePath,
                    [
                        "public sealed class OutsideSegmentNeedsFix",
                        "{",
                        "    public void Parse_Line_Bare() { }",
                        "}",
                    ].join("\n"),
                    "utf8"
                );

                const result = childProcess.spawnSync(
                    process.execPath,
                    [FIXER_SCRIPT_PATH, "--check", filePath],
                    {
                        cwd: path.resolve(__dirname, "../.."),
                        encoding: "utf8",
                    }
                );

                expect(result.status).toBe(1);
                expect(result.stderr).toContain("Found C# methods with underscores");
                expect(result.stderr).toContain("OutsideSegmentNeedsFix.cs");
            } finally {
                fs.rmSync(tempDir, { recursive: true, force: true });
            }
        }
    );

    test.each(OUTSIDE_REPO_EXCLUDED_SEGMENTS)(
        "CLI rewrites explicitly passed files in outside-repo %s segment",
        (excludedSegment) => {
            const tempDir = fs.mkdtempSync(
                path.join(os.tmpdir(), "dxmsg-csharp-underscore-outside-temp-rewrite-")
            );
            const outsideSegmentDir = path.join(tempDir, excludedSegment);
            const filePath = path.join(outsideSegmentDir, "OutsideSegmentRewrite.cs");

            try {
                fs.mkdirSync(outsideSegmentDir, { recursive: true });
                fs.writeFileSync(
                    filePath,
                    [
                        "public sealed class OutsideSegmentRewrite",
                        "{",
                        "    public void Parse_Line_Bare() { }",
                        "}",
                    ].join("\n"),
                    "utf8"
                );

                const result = childProcess.spawnSync(process.execPath, [FIXER_SCRIPT_PATH, filePath], {
                    cwd: path.resolve(__dirname, "../.."),
                    encoding: "utf8",
                });

                expect(result.status).toBe(0);

                const updated = fs.readFileSync(filePath, "utf8");
                expect(updated).toContain("ParseLineBare");
                expect(updated).not.toContain("Parse_Line_Bare");
            } finally {
                fs.rmSync(tempDir, { recursive: true, force: true });
            }
        }
    );

    test("--check processes explicitly passed relative paths outside the repo", () => {
        const tempDir = fs.mkdtempSync(
            path.join(os.tmpdir(), "dxmsg-csharp-underscore-outside-relative-check-")
        );
        const filePath = path.join(tempDir, "OutsideRelativeNeedsFix.cs");

        try {
            fs.writeFileSync(
                filePath,
                [
                    "public sealed class OutsideRelativeNeedsFix",
                    "{",
                    "    public void Parse_Line_Bare() { }",
                    "}",
                ].join("\n"),
                "utf8"
            );

            const repoRoot = path.resolve(__dirname, "../..");
            const relativeOutsidePath = path.relative(repoRoot, filePath);
            const result = childProcess.spawnSync(
                process.execPath,
                [FIXER_SCRIPT_PATH, "--check", relativeOutsidePath],
                {
                    cwd: repoRoot,
                    encoding: "utf8",
                }
            );

            expect(result.status).toBe(1);
            expect(result.stderr).toContain("Found C# methods with underscores");
            expect(result.stderr).toContain("OutsideRelativeNeedsFix.cs");
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });

    test("explicitly passed files in repo Temp remain skipped", () => {
        const tempDir = path.resolve(__dirname, "../../Temp/dxmsg-csharp-underscore-test");
        const filePath = path.join(tempDir, "RepoTempSkip.cs");

        try {
            fs.mkdirSync(tempDir, { recursive: true });
            fs.writeFileSync(
                filePath,
                [
                    "public sealed class RepoTempSkip",
                    "{",
                    "    public void Parse_Line_Bare() { }",
                    "}",
                ].join("\n"),
                "utf8"
            );

            const checkResult = childProcess.spawnSync(
                process.execPath,
                [FIXER_SCRIPT_PATH, "--check", filePath],
                {
                    cwd: path.resolve(__dirname, "../.."),
                    encoding: "utf8",
                }
            );

            expect(checkResult.status).toBe(0);
            expect(checkResult.stdout).toContain("No C# files to process.");

            const rewriteResult = childProcess.spawnSync(
                process.execPath,
                [FIXER_SCRIPT_PATH, filePath],
                {
                    cwd: path.resolve(__dirname, "../.."),
                    encoding: "utf8",
                }
            );

            expect(rewriteResult.status).toBe(0);

            const contentAfterRewrite = fs.readFileSync(filePath, "utf8");
            expect(contentAfterRewrite).toContain("Parse_Line_Bare");
            expect(contentAfterRewrite).not.toContain("ParseLineBare");
        } finally {
            fs.rmSync(tempDir, { recursive: true, force: true });
        }
    });
});
