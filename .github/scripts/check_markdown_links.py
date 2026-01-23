#!/usr/bin/env python3
import os
import re
import sys
import urllib.parse


EXCLUDE_DIRS = {".git", "node_modules", ".vs"}


LINK_RE = re.compile(r"(?<!\!)\[(?P<text>[^\]]+)\]\((?P<target>[^)\s]+)(?:\s+\"[^\"]*\")?\)")


def should_check_target(target: str) -> bool:
    """Check if a link target should be validated for human-readable text."""
    if re.match(r"^(#|https?://|mailto:|tel:|data:)", target):
        return False
    # only check links that end in .md (ignoring anchors/query)
    core = re.sub(r"[?#].*$", "", target)
    try:
        core = urllib.parse.unquote(core)
    except Exception:
        # Ignore malformed URL encoding - continue with the original string
        pass
    return core.lower().endswith(".md")


def is_link_text_problematic(text: str, target: str) -> bool:
    """
    Check if link text is problematic (not human-readable).

    Returns True if the text is:
    - An exact match to the file name
    - A path-like string (contains / or \\ without spaces)
    - Ends with .md
    """
    target_core = re.sub(r"[?#].*$", "", target)
    try:
        target_core = urllib.parse.unquote(target_core)
    except Exception:
        # Ignore malformed URL encoding - continue with the original string
        pass
    file_name = os.path.basename(target_core)

    is_exact_file_name = text.lower() == file_name.lower()
    looks_like_path = (("/" in text) or ("\\" in text)) and not re.search(r"\s", text)
    looks_like_markdown = text.strip().lower().endswith(".md")

    return is_exact_file_name or looks_like_path or looks_like_markdown


def remove_inline_code(line: str) -> str:
    """Remove inline code spans from a line."""
    return re.sub(r"`[^`]+`", "", line)


def check_code_fence(stripped_line: str, in_code_block: bool, code_fence_pattern: str):
    """
    Check if a line is a code fence marker and update code block state.

    Args:
        stripped_line: Line with leading whitespace stripped
        in_code_block: Whether we're currently inside a code block
        code_fence_pattern: The fence pattern that opened the current block

    Returns:
        Tuple of (new_in_code_block, new_code_fence_pattern, is_fence_line)
    """
    if not stripped_line.startswith("```"):
        return in_code_block, code_fence_pattern, False

    # Count the backticks at the start
    backtick_count = 0
    for ch in stripped_line:
        if ch == "`":
            backtick_count += 1
        else:
            break
    fence = "`" * backtick_count

    if not in_code_block:
        # Entering a code block
        return True, fence, True
    elif stripped_line.startswith(code_fence_pattern) and stripped_line.strip() == code_fence_pattern:
        # Exiting the code block (must match the opening fence exactly)
        return False, None, True

    return in_code_block, code_fence_pattern, False


def check_line_for_issues(line: str, in_code_block: bool) -> list:
    """
    Check a single line for problematic markdown links.

    Args:
        line: The line to check
        in_code_block: Whether we're inside a code block

    Returns:
        List of tuples (text, target) for each problematic link found
    """
    if in_code_block:
        return []

    issues = []
    line_to_check = remove_inline_code(line)

    for m in LINK_RE.finditer(line_to_check):
        text = m.group("text").strip()
        target_raw = m.group("target").strip()

        if not should_check_target(target_raw):
            continue

        if is_link_text_problematic(text, target_raw):
            issues.append((text, target_raw))

    return issues


def check_file_content(lines: list) -> list:
    """
    Check file content for problematic markdown links.

    Args:
        lines: List of lines in the file

    Returns:
        List of tuples (line_number, text, target) for each issue found
    """
    issues = []
    in_code_block = False
    code_fence_pattern = None

    for idx, line in enumerate(lines, start=1):
        stripped = line.lstrip()

        in_code_block, code_fence_pattern, is_fence = check_code_fence(
            stripped, in_code_block, code_fence_pattern
        )
        if is_fence:
            continue

        line_issues = check_line_for_issues(line, in_code_block)
        for text, target in line_issues:
            issues.append((idx, text, target))

    return issues


def main(root: str) -> int:
    issues = 0
    for dirpath, dirnames, filenames in os.walk(root):
        # prune excluded directories
        dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
        for filename in filenames:
            if not filename.lower().endswith(".md"):
                continue
            path = os.path.join(dirpath, filename)
            try:
                with open(path, "r", encoding="utf-8") as f:
                    lines = f.readlines()
            except Exception:
                # Skip files that cannot be read (permission errors, encoding issues, etc.)
                continue

            file_issues = check_file_content(lines)
            for line_num, text, target in file_issues:
                issues += 1
                msg = f"{path}:{line_num}: Link text '{text}' should be human-readable, not a raw file name or path (target: {target})"
                print(msg)

    if issues:
        print(
            f"Found {issues} documentation link(s) with non-human-readable text.",
            file=sys.stderr,
        )
        print(
            "Use a descriptive phrase instead of the raw file name.", file=sys.stderr
        )
        return 1
    return 0


if __name__ == "__main__":
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    sys.exit(main(root))
