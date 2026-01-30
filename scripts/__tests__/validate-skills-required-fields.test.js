/**
 * @fileoverview Tests for validate-skills.js required field validation logic.
 *
 * These tests validate the required field detection in skill files:
 * - Missing required fields (undefined/null)
 * - Empty required fields (empty string)
 * - Valid required fields
 *
 * The validation logic ensures that skill files have all required frontmatter
 * fields properly populated.
 */

"use strict";

/**
 * Required fields that must be present in all skill files.
 *
 * SYNC: Keep in sync with validate-skills.js REQUIRED_FIELDS
 */
const REQUIRED_FIELDS = ['title', 'id', 'category', 'version', 'created', 'updated', 'status'];

/**
 * Validation result object matching the ValidationError class pattern.
 * @typedef {Object} ValidationError
 * @property {string} file - The file path
 * @property {string} field - The field being validated
 * @property {string} message - The error message
 */

/**
 * Validates a single required field of a frontmatter object.
 *
 * SYNC: Keep logic in sync with validate-skills.js validateSkill() required fields validation block
 *
 * @param {Object} frontmatter - The parsed frontmatter object
 * @param {string} field - The field name to validate
 * @param {string} relativePath - The relative path for error reporting
 * @returns {ValidationError[]} Array of validation errors
 */
function validateRequiredField(frontmatter, field, relativePath) {
    const errors = [];

    if (frontmatter[field] === undefined || frontmatter[field] === null) {
        errors.push({
            file: relativePath,
            field: field,
            message: `Required field '${field}' is missing`,
        });
    } else if (frontmatter[field] === '') {
        errors.push({
            file: relativePath,
            field: field,
            message: `Required field '${field}' is empty`,
        });
    }

    return errors;
}

/**
 * Validates all required fields of a frontmatter object.
 *
 * SYNC: Keep logic in sync with validate-skills.js validateSkill() required fields validation block
 *
 * @param {Object} frontmatter - The parsed frontmatter object
 * @param {string} relativePath - The relative path for error reporting
 * @returns {ValidationError[]} Array of validation errors
 */
function validateRequiredFields(frontmatter, relativePath) {
    const errors = [];

    for (const field of REQUIRED_FIELDS) {
        errors.push(...validateRequiredField(frontmatter, field, relativePath));
    }

    return errors;
}

describe("validate-skills required field validation", () => {
    const testPath = "testing/sample-skill.md";

    describe("missing required fields (undefined/null)", () => {
        test("should error when required field is undefined", () => {
            const frontmatter = {
                id: "sample-skill",
                category: "testing",
                version: "1.0.0",
                created: "2025-01-01",
                updated: "2025-01-01",
                status: "active",
                // title is undefined
            };

            const errors = validateRequiredField(frontmatter, 'title', testPath);

            expect(errors).toHaveLength(1);
            expect(errors[0].field).toBe("title");
            expect(errors[0].message).toContain("Required field 'title' is missing");
        });

        test("should error when required field is null", () => {
            const frontmatter = {
                title: null,
                id: "sample-skill",
                category: "testing",
                version: "1.0.0",
                created: "2025-01-01",
                updated: "2025-01-01",
                status: "active",
            };

            const errors = validateRequiredField(frontmatter, 'title', testPath);

            expect(errors).toHaveLength(1);
            expect(errors[0].field).toBe("title");
            expect(errors[0].message).toContain("Required field 'title' is missing");
        });
    });

    describe("empty required fields", () => {
        test("should error when required field is empty string", () => {
            const frontmatter = {
                title: "",
                id: "sample-skill",
                category: "testing",
                version: "1.0.0",
                created: "2025-01-01",
                updated: "2025-01-01",
                status: "active",
            };

            const errors = validateRequiredField(frontmatter, 'title', testPath);

            expect(errors).toHaveLength(1);
            expect(errors[0].field).toBe("title");
            expect(errors[0].message).toContain("Required field 'title' is empty");
        });

        test("should distinguish between missing and empty in error messages", () => {
            const missingFrontmatter = {
                id: "sample-skill",
                // title is undefined
            };

            const emptyFrontmatter = {
                title: "",
                id: "sample-skill",
            };

            const missingErrors = validateRequiredField(missingFrontmatter, 'title', testPath);
            const emptyErrors = validateRequiredField(emptyFrontmatter, 'title', testPath);

            expect(missingErrors[0].message).toContain("is missing");
            expect(emptyErrors[0].message).toContain("is empty");
            expect(missingErrors[0].message).not.toEqual(emptyErrors[0].message);
        });
    });

    describe("valid required fields", () => {
        test("should not error when required field has valid string value", () => {
            const frontmatter = {
                title: "Sample Skill",
                id: "sample-skill",
                category: "testing",
                version: "1.0.0",
                created: "2025-01-01",
                updated: "2025-01-01",
                status: "active",
            };

            const errors = validateRequiredField(frontmatter, 'title', testPath);

            expect(errors).toHaveLength(0);
        });

        test("should not error when all required fields are present", () => {
            const frontmatter = {
                title: "Sample Skill",
                id: "sample-skill",
                category: "testing",
                version: "1.0.0",
                created: "2025-01-01",
                updated: "2025-01-01",
                status: "active",
            };

            const errors = validateRequiredFields(frontmatter, testPath);

            expect(errors).toHaveLength(0);
        });
    });

    describe("multiple missing fields", () => {
        test("should report errors for all missing required fields", () => {
            const frontmatter = {
                title: "Sample Skill",
                // id is undefined
                category: "testing",
                // version is undefined
                created: "2025-01-01",
                updated: "2025-01-01",
                status: "active",
            };

            const errors = validateRequiredFields(frontmatter, testPath);

            expect(errors).toHaveLength(2);
            expect(errors.map(e => e.field)).toContain("id");
            expect(errors.map(e => e.field)).toContain("version");
        });

        test("should report errors for mix of missing and empty fields", () => {
            const frontmatter = {
                title: "Sample Skill",
                id: null, // missing (null)
                category: "", // empty
                version: "1.0.0",
                created: "2025-01-01",
                updated: "2025-01-01",
                status: "active",
            };

            const errors = validateRequiredFields(frontmatter, testPath);

            expect(errors).toHaveLength(2);

            const idError = errors.find(e => e.field === "id");
            const categoryError = errors.find(e => e.field === "category");

            expect(idError.message).toContain("is missing");
            expect(categoryError.message).toContain("is empty");
        });
    });

    describe("each required field", () => {
        test.each(REQUIRED_FIELDS)("should validate '%s' as required field", (field) => {
            const frontmatter = {};

            const errors = validateRequiredField(frontmatter, field, testPath);

            expect(errors).toHaveLength(1);
            expect(errors[0].field).toBe(field);
            expect(errors[0].message).toContain(`Required field '${field}' is missing`);
        });
    });
});
