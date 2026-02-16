const fs = require("fs");
const path = require("path");
const {
  generateLlmsTxt,
  countSkillFiles,
  getSkillCategories,
} = require("../update-llms-txt");

const ROOT_DIR = path.resolve(__dirname, "../..");
const LLMS_TXT_PATH = path.join(ROOT_DIR, "llms.txt");
const PACKAGE_JSON_PATH = path.join(ROOT_DIR, "package.json");

describe("update-llms-txt.js", () => {
  describe("countSkillFiles()", () => {
    test("should return a positive number of skill files", () => {
      const count = countSkillFiles();
      expect(count).toBeGreaterThan(0);
      expect(typeof count).toBe("number");
    });
  });

  describe("getSkillCategories()", () => {
    test("should return an array of category names", () => {
      const categories = getSkillCategories();
      expect(Array.isArray(categories)).toBe(true);
      expect(categories.length).toBeGreaterThan(0);
    });

    test("should return sorted category names", () => {
      const categories = getSkillCategories();
      const sorted = [...categories].sort();
      expect(categories).toEqual(sorted);
    });

    test("should include known categories", () => {
      const categories = getSkillCategories();
      const knownCategories = [
        "documentation",
        "github-actions",
        "testing",
        "scripting",
      ];

      for (const known of knownCategories) {
        expect(categories).toContain(known);
      }
    });
  });

  describe("generateLlmsTxt()", () => {
    test("should generate markdown content", () => {
      const content = generateLlmsTxt();
      expect(typeof content).toBe("string");
      expect(content.length).toBeGreaterThan(0);
    });

    test("should include project name in header", () => {
      const content = generateLlmsTxt();
      expect(content).toMatch(/^# DxMessaging/);
    });

    test("should include version from package.json", () => {
      const pkg = JSON.parse(fs.readFileSync(PACKAGE_JSON_PATH, "utf8"));
      const content = generateLlmsTxt();
      expect(content).toContain(`**Version:** ${pkg.version}`);
    });

    test("should include repository URL", () => {
      const content = generateLlmsTxt();
      expect(content).toContain("https://github.com/wallstop/DxMessaging");
    });

    test("should include documentation URL", () => {
      const content = generateLlmsTxt();
      expect(content).toContain("https://wallstop.github.io/DxMessaging/");
    });

    test("should include skill count", () => {
      const count = countSkillFiles();
      const content = generateLlmsTxt();
      expect(content).toContain(`${count}+`);
    });

    test("should include skill categories", () => {
      const categories = getSkillCategories();
      const content = generateLlmsTxt();

      for (const category of categories) {
        expect(content).toContain(`**${category}/**`);
      }
    });

    test("should include current date in Last Updated", () => {
      const content = generateLlmsTxt();
      const today = new Date().toISOString().split("T")[0];
      expect(content).toContain(`**Last Updated:** ${today}`);
    });

    test("should include all main sections", () => {
      const content = generateLlmsTxt();
      const requiredSections = [
        "## Overview",
        "## Quick Facts",
        "## Key Features",
        "## Core Concepts",
        "## Project Structure",
        "## Getting Started",
        "## Documentation Structure",
        "## Key Files",
        "## Development",
        "## AI Agent Context",
        "## Common Pitfalls & Solutions",
        "## Performance Characteristics",
        "## Examples",
        "## Support & Community",
        "## License",
      ];

      for (const section of requiredSections) {
        expect(content).toContain(section);
      }
    });

    test("should include code examples", () => {
      const content = generateLlmsTxt();
      expect(content).toContain("```csharp");
      expect(content).toContain("```bash");
    });

    test("should include message type documentation", () => {
      const content = generateLlmsTxt();
      expect(content).toContain("Untargeted Messages");
      expect(content).toContain("Targeted Messages");
      expect(content).toContain("Broadcast Messages");
    });

    test("should include installation instructions", () => {
      const content = generateLlmsTxt();
      expect(content).toContain("openupm add");
    });
  });

  describe("File consistency", () => {
    test("llms.txt should exist", () => {
      expect(fs.existsSync(LLMS_TXT_PATH)).toBe(true);
    });

    test("llms.txt should be up to date", () => {
      const currentContent = fs.readFileSync(LLMS_TXT_PATH, "utf8");
      const expectedContent = generateLlmsTxt();
      // Normalize line endings for comparison (support both LF and CRLF)
      const normalize = (str) => str.replace(/\r\n/g, '\n').trim();
      expect(normalize(currentContent)).toBe(normalize(expectedContent));
    });
  });
});
