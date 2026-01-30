/**
 * @fileoverview Tests for validate-skills.js optional field validation logic.
 *
 * These tests validate the missing optional field detection in skill files:
 * - complexity.level - affects Complexity column in skills index
 * - impact.performance.rating - affects Performance column in skills index
 *
 * The validation logic detects missing optional fields that cause '?'
 * placeholders in the generated skills index.
 */

"use strict";

/**
 * Validation result object matching the ValidationError class pattern.
 * @typedef {Object} ValidationWarning
 * @property {string} file - The file path
 * @property {string} field - The field being validated
 * @property {string} message - The warning message
 */

/**
 * Validates the complexity.level field of a frontmatter object.
 *
 * SYNC: Keep logic in sync with validate-skills.js validateSkill() complexity.level validation block
 *
 * @param {Object} frontmatter - The parsed frontmatter object
 * @param {string} relativePath - The relative path for error reporting
 * @returns {ValidationWarning[]} Array of validation warnings
 */
function validateComplexityLevel(frontmatter, relativePath) {
    const warnings = [];

    if (frontmatter.complexity == null || frontmatter.complexity.level == null) {
        warnings.push({
            file: relativePath,
            field: "complexity.level",
            message: `Missing 'complexity.level' - will show '?' in Complexity column of skills index`,
        });
    } else if (frontmatter.complexity.level === '') {
        warnings.push({
            file: relativePath,
            field: "complexity.level",
            message: `Empty 'complexity.level' - will show '?' in Complexity column of skills index`,
        });
    }

    return warnings;
}

/**
 * Validates the impact.performance.rating field of a frontmatter object.
 *
 * SYNC: Keep logic in sync with validate-skills.js validateSkill() impact.performance.rating validation block
 *
 * @param {Object} frontmatter - The parsed frontmatter object
 * @param {string} relativePath - The relative path for error reporting
 * @returns {ValidationWarning[]} Array of validation warnings
 */
function validatePerformanceRating(frontmatter, relativePath) {
    const warnings = [];

    if (frontmatter.impact == null || frontmatter.impact.performance == null || frontmatter.impact.performance.rating == null) {
        warnings.push({
            file: relativePath,
            field: "impact.performance.rating",
            message: `Missing 'impact.performance.rating' - will show '?' in Performance column of skills index`,
        });
    } else if (frontmatter.impact.performance.rating === '') {
        warnings.push({
            file: relativePath,
            field: "impact.performance.rating",
            message: `Empty 'impact.performance.rating' - will show '?' in Performance column of skills index`,
        });
    }

    return warnings;
}

describe("validate-skills optional field validation", () => {
    const testPath = "testing/sample-skill.md";

    describe("complexity.level validation", () => {
        describe("missing complexity field", () => {
            test("should warn when complexity is undefined", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    // complexity is undefined
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("complexity.level");
                expect(warnings[0].message).toContain("Missing 'complexity.level'");
                expect(warnings[0].message).toContain("Complexity column");
            });

            test("should warn when complexity is null", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    complexity: null,
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("complexity.level");
                expect(warnings[0].message).toContain("Missing 'complexity.level'");
                expect(warnings[0].message).toContain("Complexity column");
            });

            test("should warn when complexity is an empty object", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    complexity: {},
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("complexity.level");
                expect(warnings[0].message).toContain("Missing 'complexity.level'");
            });

            test("should warn when complexity.level is undefined", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    complexity: {
                        // level is undefined
                        description: "Some description",
                    },
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("complexity.level");
                expect(warnings[0].message).toContain("Missing 'complexity.level'");
            });

            test("should warn when complexity.level is null", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    complexity: {
                        level: null,
                    },
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("complexity.level");
                expect(warnings[0].message).toContain("Missing 'complexity.level'");
            });

            test("should warn when complexity.level is an empty string", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    complexity: {
                        level: "",
                    },
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("complexity.level");
                expect(warnings[0].message).toContain("Empty 'complexity.level'");
            });
        });

        describe("valid complexity.level", () => {
            test("should not warn when complexity.level is a valid string", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    complexity: {
                        level: "intermediate",
                    },
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings).toHaveLength(0);
            });

            test("should not warn when complexity.level is a number", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    complexity: {
                        level: 3,
                    },
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings).toHaveLength(0);
            });
        });

        describe("warning message format", () => {
            test("should include correct column name in warning message", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings[0].message).toBe(
                    "Missing 'complexity.level' - will show '?' in Complexity column of skills index"
                );
            });

            test("should include correct field name in warning", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                };

                const warnings = validateComplexityLevel(frontmatter, testPath);

                expect(warnings[0].field).toBe("complexity.level");
            });
        });
    });

    describe("impact.performance.rating validation", () => {
        describe("missing impact field", () => {
            test("should warn when impact is undefined", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    // impact is undefined
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("impact.performance.rating");
                expect(warnings[0].message).toContain("Missing 'impact.performance.rating'");
                expect(warnings[0].message).toContain("Performance column");
            });

            test("should warn when impact is null", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: null,
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("impact.performance.rating");
                expect(warnings[0].message).toContain("Missing 'impact.performance.rating'");
                expect(warnings[0].message).toContain("Performance column");
            });

            test("should warn when impact is an empty object", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: {},
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("impact.performance.rating");
                expect(warnings[0].message).toContain("Missing 'impact.performance.rating'");
            });
        });

        describe("missing impact.performance field", () => {
            test("should warn when impact.performance is undefined", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: {
                        // performance is undefined
                        reliability: { rating: "high" },
                    },
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("impact.performance.rating");
                expect(warnings[0].message).toContain("Missing 'impact.performance.rating'");
            });

            test("should warn when impact.performance is null", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: {
                        performance: null,
                    },
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("impact.performance.rating");
                expect(warnings[0].message).toContain("Missing 'impact.performance.rating'");
            });

            test("should warn when impact.performance is an empty object", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: {
                        performance: {},
                    },
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("impact.performance.rating");
                expect(warnings[0].message).toContain("Missing 'impact.performance.rating'");
            });
        });

        describe("missing impact.performance.rating field", () => {
            test("should warn when impact.performance.rating is undefined", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: {
                        performance: {
                            // rating is undefined
                            description: "Some description",
                        },
                    },
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("impact.performance.rating");
                expect(warnings[0].message).toContain("Missing 'impact.performance.rating'");
            });

            test("should warn when impact.performance.rating is null", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: {
                        performance: {
                            rating: null,
                        },
                    },
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("impact.performance.rating");
                expect(warnings[0].message).toContain("Missing 'impact.performance.rating'");
            });

            test("should warn when impact.performance.rating is an empty string", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: {
                        performance: {
                            rating: "",
                        },
                    },
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(1);
                expect(warnings[0].field).toBe("impact.performance.rating");
                expect(warnings[0].message).toContain("Empty 'impact.performance.rating'");
            });
        });

        describe("valid impact.performance.rating", () => {
            test("should not warn when impact.performance.rating is a valid string", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: {
                        performance: {
                            rating: "high",
                        },
                    },
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(0);
            });

            test("should not warn when impact.performance.rating is a number", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                    impact: {
                        performance: {
                            rating: 5,
                        },
                    },
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings).toHaveLength(0);
            });
        });

        describe("warning message format", () => {
            test("should include correct column name in warning message", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings[0].message).toBe(
                    "Missing 'impact.performance.rating' - will show '?' in Performance column of skills index"
                );
            });

            test("should include correct field name in warning", () => {
                const frontmatter = {
                    title: "Sample Skill",
                    id: "sample-skill",
                };

                const warnings = validatePerformanceRating(frontmatter, testPath);

                expect(warnings[0].field).toBe("impact.performance.rating");
            });
        });
    });

    describe("edge cases", () => {
        test("should handle frontmatter with both fields missing", () => {
            const frontmatter = {
                title: "Sample Skill",
                id: "sample-skill",
            };

            const complexityWarnings = validateComplexityLevel(frontmatter, testPath);
            const performanceWarnings = validatePerformanceRating(frontmatter, testPath);

            expect(complexityWarnings).toHaveLength(1);
            expect(performanceWarnings).toHaveLength(1);
        });

        test("should handle frontmatter with both fields present", () => {
            const frontmatter = {
                title: "Sample Skill",
                id: "sample-skill",
                complexity: {
                    level: "intermediate",
                },
                impact: {
                    performance: {
                        rating: "high",
                    },
                },
            };

            const complexityWarnings = validateComplexityLevel(frontmatter, testPath);
            const performanceWarnings = validatePerformanceRating(frontmatter, testPath);

            expect(complexityWarnings).toHaveLength(0);
            expect(performanceWarnings).toHaveLength(0);
        });

        test("should not warn for complexity.level value of zero (uses explicit null check)", () => {
            const frontmatter = {
                title: "Sample Skill",
                id: "sample-skill",
                complexity: {
                    level: 0, // 0 is present (not null/undefined), so not "missing"
                },
            };

            const warnings = validateComplexityLevel(frontmatter, testPath);

            // Uses explicit null check, so 0 is treated as present (not missing)
            expect(warnings).toHaveLength(0);
        });

        test("should not warn for impact.performance.rating value of zero (uses explicit null check)", () => {
            const frontmatter = {
                title: "Sample Skill",
                id: "sample-skill",
                impact: {
                    performance: {
                        rating: 0, // 0 is present (not null/undefined), so not "missing"
                    },
                },
            };

            const warnings = validatePerformanceRating(frontmatter, testPath);

            // Uses explicit null check, so 0 is treated as present (not missing)
            expect(warnings).toHaveLength(0);
        });

        test("should not warn for complexity.level value of false (uses explicit null check)", () => {
            const frontmatter = {
                title: "Sample Skill",
                id: "sample-skill",
                complexity: {
                    level: false, // false is present (not null/undefined), so not "missing"
                },
            };

            const warnings = validateComplexityLevel(frontmatter, testPath);

            // Uses explicit null check, so false is treated as present (not missing)
            expect(warnings).toHaveLength(0);
        });

        test("should not warn for impact.performance.rating value of false (uses explicit null check)", () => {
            const frontmatter = {
                title: "Sample Skill",
                id: "sample-skill",
                impact: {
                    performance: {
                        rating: false, // false is present (not null/undefined), so not "missing"
                    },
                },
            };

            const warnings = validatePerformanceRating(frontmatter, testPath);

            // Uses explicit null check, so false is treated as present (not missing)
            expect(warnings).toHaveLength(0);
        });
    });
});
