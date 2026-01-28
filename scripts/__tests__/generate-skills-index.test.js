/**
 * @fileoverview Tests for generate-skills-index.js logic.
 *
 * These tests validate the core logic of the skills index generator:
 * - Brand name capitalization (BRAND_NAMES mapping)
 * - Category title formatting (categoryToTitle function)
 * - YAML frontmatter parsing
 * - Edge cases for various inputs
 */

"use strict";

// SYNC: Keep brand names in sync with generate-skills-index.js BRAND_NAMES
const BRAND_NAMES = {
    github: "GitHub",
    javascript: "JavaScript",
    typescript: "TypeScript",
    nodejs: "Node.js",
    csharp: "C#",
    dotnet: ".NET",
    nuget: "NuGet",
    npm: "npm",
    api: "API",
    apis: "APIs",
    cli: "CLI",
    json: "JSON",
    yaml: "YAML",
    xml: "XML",
    html: "HTML",
    css: "CSS",
    sql: "SQL",
    url: "URL",
    urls: "URLs",
    uri: "URI",
    uris: "URIs",
    http: "HTTP",
    https: "HTTPS",
    rest: "REST",
    graphql: "GraphQL",
    oauth: "OAuth",
    jwt: "JWT",
    sdk: "SDK",
    ide: "IDE",
    vscode: "VS Code",
    visualstudio: "Visual Studio",
    macos: "macOS",
    ios: "iOS",
    webgl: "WebGL",
    opengl: "OpenGL",
    directx: "DirectX",
    llm: "LLM",
    ai: "AI",
    ml: "ML",
    ci: "CI",
    cd: "CD",
};

/**
 * Apply proper brand name capitalization to a word.
 * Returns the properly capitalized form if it's a known brand,
 * otherwise returns the word with standard title case.
 *
 * SYNC: Keep logic in sync with generate-skills-index.js applyBrandCapitalization
 */
function applyBrandCapitalization(word) {
    const lowerWord = word.toLowerCase();
    if (BRAND_NAMES[lowerWord]) {
        return BRAND_NAMES[lowerWord];
    }
    // Standard title case: capitalize first letter
    return word.charAt(0).toUpperCase() + word.slice(1).toLowerCase();
}

/**
 * Convert a category slug (e.g., "github-actions") to a properly
 * capitalized title (e.g., "GitHub Actions").
 *
 * SYNC: Keep logic in sync with generate-skills-index.js categoryToTitle
 */
function categoryToTitle(category) {
    return category
        .split("-")
        .map((word) => applyBrandCapitalization(word))
        .join(" ");
}

describe("generate-skills-index", () => {
    describe("BRAND_NAMES mapping", () => {
        test("should contain expected number of brand entries", () => {
            const brandCount = Object.keys(BRAND_NAMES).length;
            expect(brandCount).toBeGreaterThan(30);
        });

        test("should have all lowercase keys", () => {
            for (const key of Object.keys(BRAND_NAMES)) {
                expect(key).toBe(key.toLowerCase());
            }
        });

        test("should have non-empty values for all keys", () => {
            for (const value of Object.values(BRAND_NAMES)) {
                expect(value).toBeTruthy();
                expect(typeof value).toBe("string");
            }
        });
    });

    describe("applyBrandCapitalization", () => {
        describe("known brand names", () => {
            test("should return GitHub for github", () => {
                expect(applyBrandCapitalization("github")).toBe("GitHub");
            });

            test("should return npm for npm (lowercase brand)", () => {
                expect(applyBrandCapitalization("npm")).toBe("npm");
            });

            test("should return API for api", () => {
                expect(applyBrandCapitalization("api")).toBe("API");
            });

            test("should return JavaScript for javascript", () => {
                expect(applyBrandCapitalization("javascript")).toBe("JavaScript");
            });

            test("should return TypeScript for typescript", () => {
                expect(applyBrandCapitalization("typescript")).toBe("TypeScript");
            });

            test("should return Node.js for nodejs", () => {
                expect(applyBrandCapitalization("nodejs")).toBe("Node.js");
            });

            test("should return C# for csharp", () => {
                expect(applyBrandCapitalization("csharp")).toBe("C#");
            });

            test("should return .NET for dotnet", () => {
                expect(applyBrandCapitalization("dotnet")).toBe(".NET");
            });

            test("should return VS Code for vscode", () => {
                expect(applyBrandCapitalization("vscode")).toBe("VS Code");
            });

            test("should return macOS for macos", () => {
                expect(applyBrandCapitalization("macos")).toBe("macOS");
            });

            test("should return iOS for ios", () => {
                expect(applyBrandCapitalization("ios")).toBe("iOS");
            });

            test("should return GraphQL for graphql", () => {
                expect(applyBrandCapitalization("graphql")).toBe("GraphQL");
            });

            test("should return LLM for llm", () => {
                expect(applyBrandCapitalization("llm")).toBe("LLM");
            });

            test("should return CI for ci", () => {
                expect(applyBrandCapitalization("ci")).toBe("CI");
            });

            test("should return CD for cd", () => {
                expect(applyBrandCapitalization("cd")).toBe("CD");
            });
        });

        describe("case-insensitive input", () => {
            test("should handle GITHUB (all uppercase)", () => {
                expect(applyBrandCapitalization("GITHUB")).toBe("GitHub");
            });

            test("should handle GitHub (mixed case)", () => {
                expect(applyBrandCapitalization("GitHub")).toBe("GitHub");
            });

            test("should handle NPM (all uppercase)", () => {
                expect(applyBrandCapitalization("NPM")).toBe("npm");
            });

            test("should handle API (already uppercase)", () => {
                expect(applyBrandCapitalization("API")).toBe("API");
            });

            test("should handle JavaScript (mixed case)", () => {
                expect(applyBrandCapitalization("JavaScript")).toBe("JavaScript");
            });

            test("should handle VSCODE (all uppercase)", () => {
                expect(applyBrandCapitalization("VSCODE")).toBe("VS Code");
            });
        });

        describe("unknown words - standard title case", () => {
            test("should title-case unknown word", () => {
                expect(applyBrandCapitalization("unknown")).toBe("Unknown");
            });

            test("should title-case testing", () => {
                expect(applyBrandCapitalization("testing")).toBe("Testing");
            });

            test("should title-case documentation", () => {
                expect(applyBrandCapitalization("documentation")).toBe("Documentation");
            });

            test("should title-case scripting", () => {
                expect(applyBrandCapitalization("scripting")).toBe("Scripting");
            });

            test("should title-case actions", () => {
                expect(applyBrandCapitalization("actions")).toBe("Actions");
            });

            test("should convert UPPERCASE unknown to title case", () => {
                expect(applyBrandCapitalization("TESTING")).toBe("Testing");
            });

            test("should convert MixedCase unknown to title case", () => {
                expect(applyBrandCapitalization("TeStiNg")).toBe("Testing");
            });
        });

        describe("edge cases", () => {
            test("should handle empty string", () => {
                const result = applyBrandCapitalization("");
                expect(result).toBe("");
            });

            test("should handle single character lowercase", () => {
                expect(applyBrandCapitalization("a")).toBe("A");
            });

            test("should handle single character uppercase", () => {
                expect(applyBrandCapitalization("A")).toBe("A");
            });

            test("should handle two-letter word", () => {
                expect(applyBrandCapitalization("ab")).toBe("Ab");
            });

            test("should handle word with numbers", () => {
                expect(applyBrandCapitalization("test123")).toBe("Test123");
            });

            test("should handle number-only string", () => {
                expect(applyBrandCapitalization("123")).toBe("123");
            });

            test("should handle word starting with number", () => {
                expect(applyBrandCapitalization("3d")).toBe("3d");
            });

            test("should handle already-capitalized unknown word", () => {
                expect(applyBrandCapitalization("Already")).toBe("Already");
            });
        });
    });

    describe("categoryToTitle", () => {
        describe("single-word categories", () => {
            test("should format testing category", () => {
                expect(categoryToTitle("testing")).toBe("Testing");
            });

            test("should format documentation category", () => {
                expect(categoryToTitle("documentation")).toBe("Documentation");
            });

            test("should format scripting category", () => {
                expect(categoryToTitle("scripting")).toBe("Scripting");
            });

            test("should format github as brand name", () => {
                expect(categoryToTitle("github")).toBe("GitHub");
            });

            test("should format api as brand name", () => {
                expect(categoryToTitle("api")).toBe("API");
            });

            test("should format npm as brand name", () => {
                expect(categoryToTitle("npm")).toBe("npm");
            });

            test("should format javascript as brand name", () => {
                expect(categoryToTitle("javascript")).toBe("JavaScript");
            });
        });

        describe("multi-word categories (hyphenated)", () => {
            test("should format github-actions with brand capitalization", () => {
                expect(categoryToTitle("github-actions")).toBe("GitHub Actions");
            });

            test("should format api-design with brand capitalization", () => {
                expect(categoryToTitle("api-design")).toBe("API Design");
            });

            test("should format javascript-testing with brand capitalization", () => {
                expect(categoryToTitle("javascript-testing")).toBe("JavaScript Testing");
            });

            test("should format test-coverage with standard title case", () => {
                expect(categoryToTitle("test-coverage")).toBe("Test Coverage");
            });

            test("should format error-handling with standard title case", () => {
                expect(categoryToTitle("error-handling")).toBe("Error Handling");
            });

            test("should format cross-platform with standard title case", () => {
                expect(categoryToTitle("cross-platform")).toBe("Cross Platform");
            });

            test("should format ci-cd with brand names", () => {
                expect(categoryToTitle("ci-cd")).toBe("CI CD");
            });

            test("should format typescript-nodejs with multiple brands", () => {
                expect(categoryToTitle("typescript-nodejs")).toBe("TypeScript Node.js");
            });
        });

        describe("complex multi-word categories", () => {
            test("should format three-word category", () => {
                expect(categoryToTitle("test-error-handling")).toBe("Test Error Handling");
            });

            test("should format category with multiple brands", () => {
                expect(categoryToTitle("github-api-testing")).toBe("GitHub API Testing");
            });

            test("should format long category name", () => {
                expect(categoryToTitle("comprehensive-test-coverage")).toBe(
                    "Comprehensive Test Coverage"
                );
            });
        });

        describe("edge cases", () => {
            test("should handle empty string", () => {
                expect(categoryToTitle("")).toBe("");
            });

            test("should handle single character", () => {
                expect(categoryToTitle("a")).toBe("A");
            });

            test("should handle category with numbers", () => {
                expect(categoryToTitle("test123")).toBe("Test123");
            });

            test("should handle category starting with number", () => {
                expect(categoryToTitle("3d-graphics")).toBe("3d Graphics");
            });

            test("should handle multiple consecutive hyphens as empty words", () => {
                // Edge case: multiple hyphens create empty strings
                const result = categoryToTitle("test--case");
                expect(result).toBe("Test  Case");
            });

            test("should handle leading hyphen", () => {
                const result = categoryToTitle("-testing");
                expect(result).toBe(" Testing");
            });

            test("should handle trailing hyphen", () => {
                const result = categoryToTitle("testing-");
                expect(result).toBe("Testing ");
            });

            test("should handle already-capitalized input", () => {
                expect(categoryToTitle("TESTING")).toBe("Testing");
            });

            test("should handle mixed-case input", () => {
                expect(categoryToTitle("GitHub-Actions")).toBe("GitHub Actions");
            });
        });
    });

    describe("brand name coverage", () => {
        test("should have all pure acronym brands in uppercase", () => {
            // Only single-form acronyms (not plurals like APIs/URLs which keep lowercase 's')
            const pureAcronymBrands = ["api", "cli", "json", "yaml", "xml", "html", "css", "sql", "url", "uri", "http", "https", "rest", "jwt", "sdk", "ide", "llm", "ai", "ml", "ci", "cd"];
            for (const brand of pureAcronymBrands) {
                const result = BRAND_NAMES[brand];
                expect(result).toBeDefined();
                expect(result).toBe(result.toUpperCase());
            }
        });

        test("should have plural acronyms with lowercase s suffix", () => {
            const pluralAcronyms = {
                apis: "APIs",
                urls: "URLs",
                uris: "URIs",
            };
            for (const [input, expected] of Object.entries(pluralAcronyms)) {
                expect(BRAND_NAMES[input]).toBe(expected);
            }
        });

        test("should have proper camelCase brands", () => {
            const camelCaseBrands = {
                github: "GitHub",
                javascript: "JavaScript",
                typescript: "TypeScript",
                graphql: "GraphQL",
                webgl: "WebGL",
                opengl: "OpenGL",
                directx: "DirectX",
            };
            for (const [input, expected] of Object.entries(camelCaseBrands)) {
                expect(BRAND_NAMES[input]).toBe(expected);
            }
        });

        test("should have special case brands", () => {
            const specialCases = {
                npm: "npm", // all lowercase
                macos: "macOS", // lowercase first letter
                ios: "iOS", // lowercase first letter
                nodejs: "Node.js", // with dot
                csharp: "C#", // with symbol
                dotnet: ".NET", // starts with dot
                vscode: "VS Code", // with space
                visualstudio: "Visual Studio", // with space
                nuget: "NuGet", // mixed case
                oauth: "OAuth", // mixed case
            };
            for (const [input, expected] of Object.entries(specialCases)) {
                expect(BRAND_NAMES[input]).toBe(expected);
            }
        });
    });

    describe("integration scenarios", () => {
        test("should format realistic category names correctly", () => {
            const testCases = [
                ["testing", "Testing"],
                ["github-actions", "GitHub Actions"],
                ["documentation", "Documentation"],
                ["scripting", "Scripting"],
                ["api", "API"],
                ["javascript", "JavaScript"],
                ["typescript", "TypeScript"],
            ];
            for (const [input, expected] of testCases) {
                expect(categoryToTitle(input)).toBe(expected);
            }
        });

        test("should handle all single-word brands as categories", () => {
            for (const [slug, expected] of Object.entries(BRAND_NAMES)) {
                expect(categoryToTitle(slug)).toBe(expected);
            }
        });
    });
});
