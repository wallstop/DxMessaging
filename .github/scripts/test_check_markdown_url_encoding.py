#!/usr/bin/env python3
"""Tests for check_markdown_url_encoding.py."""

import contextlib
import io
import os
import tempfile
import unittest

from check_markdown_url_encoding import (
    extract_target,
    has_unencoded_chars,
    is_external,
    main,
    scan_file,
)


class TestIsExternal(unittest.TestCase):
    """Data-driven tests for external target detection."""

    def test_matrix(self):
        cases = [
            ("https://example.com/docs/readme.md", True),
            ("http://example.com/docs/readme.md", True),
            ("mailto:test@example.com", True),
            ("tel:+1234567890", True),
            ("data:text/plain;base64,SGVsbG8=", True),
            ("docs/readme.md", False),
            ("./docs/readme.md", False),
            ("#section", False),
        ]

        for target, expected in cases:
            with self.subTest(target=target):
                self.assertEqual(is_external(target), expected)


class TestHasUnencodedChars(unittest.TestCase):
    """Data-driven tests for URL encoding validation."""

    def test_matrix(self):
        cases = [
            ("docs/My File.md", True),
            ("docs/Feature+Guide.md", True),
            ("docs/My%20File.md", False),
            ("docs/Feature%2BGuide.md", False),
            ("docs/normal-file.md", False),
        ]

        for target, expected in cases:
            with self.subTest(target=target):
                self.assertEqual(has_unencoded_chars(target), expected)


class TestExtractTarget(unittest.TestCase):
    """Data-driven tests for link-body target extraction."""

    def test_matrix(self):
        cases = [
            ("docs/Guide.md", "docs/Guide.md"),
            ("docs/Guide.md \"title\"", "docs/Guide.md"),
            ("docs/Guide File.md", "docs/Guide File.md"),
            ("docs/Guide File.md \"title\"", "docs/Guide File.md"),
            ("<docs/Guide File.md>", "<docs/Guide File.md>"),
            ("", ""),
        ]

        for body, expected in cases:
            with self.subTest(body=body):
                self.assertEqual(extract_target(body), expected)


class TestScanFile(unittest.TestCase):
    """Tests for file-level URL encoding checks."""

    def test_reports_inline_and_reference_issues(self):
        markdown = "\n".join(
            [
                "[Good](docs/Good%20Guide.md)",
                "[Bad Space](docs/Bad Guide.md)",
                "![Bad Plus](images/Feature+Diagram.png)",
                "[ref-ok]: docs/Ref%2BGuide.md",
                "[ref-bad]: docs/Ref Guide.md",
                "[System.IO.File]::WriteAllText($path, $content)",
            ]
        )

        with tempfile.NamedTemporaryFile("w", suffix=".md", delete=False, encoding="utf-8") as handle:
            path = handle.name
            handle.write(markdown)

        try:
            out_buffer = io.StringIO()
            with contextlib.redirect_stdout(out_buffer):
                issues = scan_file(path)

            self.assertEqual(issues, 3)
            output = out_buffer.getvalue()
            self.assertIn("Bad Guide.md", output)
            self.assertIn("Feature+Diagram.png", output)
            self.assertIn("Ref Guide.md", output)
            self.assertNotIn("WriteAllText", output)
        finally:
            os.unlink(path)

    def test_ignores_links_inside_backtick_and_tilde_code_fences(self):
        markdown = "\n".join(
            [
                "```markdown",
                "[Bad](docs/Bad Guide.md)",
                "[ref-bad]: docs/Ref Guide.md",
                "```",
                "~~~markdown",
                "[Also Bad](docs/Another Bad.md)",
                "~~~",
                "[Good](docs/Good%20Guide.md)",
            ]
        )

        with tempfile.NamedTemporaryFile("w", suffix=".md", delete=False, encoding="utf-8") as handle:
            path = handle.name
            handle.write(markdown)

        try:
            out_buffer = io.StringIO()
            with contextlib.redirect_stdout(out_buffer):
                issues = scan_file(path)

            self.assertEqual(issues, 0)
            self.assertEqual(out_buffer.getvalue(), "")
        finally:
            os.unlink(path)


class TestMain(unittest.TestCase):
    """Tests for root scanning and exclusion behavior."""

    def test_excluded_directories_are_not_scanned(self):
        with tempfile.TemporaryDirectory() as root:
            temp_dir = os.path.join(root, "Temp")
            os.makedirs(temp_dir)

            excluded_file = os.path.join(temp_dir, "ignored.md")
            with open(excluded_file, "w", encoding="utf-8") as handle:
                handle.write("[Bad](docs/Bad Guide.md)\n")

            stderr_buffer = io.StringIO()
            with contextlib.redirect_stderr(stderr_buffer), contextlib.redirect_stdout(io.StringIO()):
                exit_code = main(root)

            self.assertEqual(exit_code, 0)
            self.assertEqual(stderr_buffer.getvalue(), "")

    def test_returns_nonzero_when_issues_exist(self):
        with tempfile.TemporaryDirectory() as root:
            markdown_file = os.path.join(root, "README.md")
            with open(markdown_file, "w", encoding="utf-8") as handle:
                handle.write("[Bad](docs/Bad Guide.md)\n")

            stderr_buffer = io.StringIO()
            with contextlib.redirect_stderr(stderr_buffer), contextlib.redirect_stdout(io.StringIO()):
                exit_code = main(root)

            self.assertEqual(exit_code, 1)
            self.assertIn("Found 1 markdown link(s)", stderr_buffer.getvalue())


if __name__ == "__main__":
    unittest.main()
