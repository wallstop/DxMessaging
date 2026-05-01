#!/usr/bin/env python3
import os
import re
import sys


EXCLUDE_DIRS = {".git", "node_modules", ".vs", ".vscode", "Library", "Temp"}


# Inline markdown link or image: ![alt](target "title") or [text](target "title")
INLINE_LINK_RE = re.compile(r"!?\[[^\]]+\]\((?P<body>[^)]*)\)")

# Reference-style link definitions: [id]: target "title"
# Ignore PowerShell static-member syntax like [System.IO.File]::WriteAllText(...)
# by rejecting a second colon immediately after the delimiter colon.
REF_DEF_RE = re.compile(r"^\s*\[[^\]]+\]:\s*(?!:)(?P<body>.+?)\s*$")

# Optional quoted title suffix used by both inline and reference-style links.
TITLE_SUFFIX_RE = re.compile(r'^(?P<target>.+?)(?:\s+"[^"]*")?\s*$')


def is_external(target: str) -> bool:
    return target.startswith("http://") or target.startswith("https://") or target.startswith("mailto:") or target.startswith("tel:") or target.startswith("data:")


def has_unencoded_chars(target: str) -> bool:
    # Only flag raw spaces or plus signs in the path/query/fragment
    return (" " in target) or ("+" in target)


def extract_target(raw_body: str) -> str:
    """Extract the link target from a markdown link body that may include a quoted title."""
    body = raw_body.strip()
    if not body:
        return ""

    m = TITLE_SUFFIX_RE.match(body)
    if not m:
        return body

    return m.group("target").strip()


def update_code_fence_state(stripped_line: str, in_code_block: bool, code_fence_pattern: str):
    """Track fenced code blocks delimited by backticks or tildes."""
    if not stripped_line:
        return in_code_block, code_fence_pattern, False

    fence_char = stripped_line[0]
    if fence_char not in ("`", "~"):
        return in_code_block, code_fence_pattern, False

    if not stripped_line.startswith(fence_char * 3):
        return in_code_block, code_fence_pattern, False

    fence_count = 0
    for ch in stripped_line:
        if ch == fence_char:
            fence_count += 1
        else:
            break
    fence = fence_char * fence_count

    if not in_code_block:
        return True, fence, True
    if stripped_line.startswith(code_fence_pattern) and stripped_line.strip() == code_fence_pattern:
        return False, None, True

    return in_code_block, code_fence_pattern, False


def scan_file(path: str) -> int:
    issues = 0
    try:
        with open(path, "r", encoding="utf-8") as f:
            lines = f.readlines()
    except Exception:
        return 0

    in_code_block = False
    code_fence_pattern = None

    for idx, line in enumerate(lines, start=1):
        stripped = line.lstrip()
        in_code_block, code_fence_pattern, is_fence = update_code_fence_state(
            stripped,
            in_code_block,
            code_fence_pattern,
        )
        if is_fence or in_code_block:
            continue

        # Inline links/images
        for m in INLINE_LINK_RE.finditer(line):
            target = extract_target(m.group("body"))
            if is_external(target):
                continue
            if has_unencoded_chars(target):
                issues += 1
                print(f"{path}:{idx}: Unencoded character(s) in link target: '{target}'. Encode spaces as %20 and '+' as %2B.")

        # Reference-style link definitions
        m = REF_DEF_RE.match(line)
        if m:
            target = extract_target(m.group("body"))
            if not is_external(target) and has_unencoded_chars(target):
                issues += 1
                print(f"{path}:{idx}: Unencoded character(s) in link definition: '{target}'. Encode spaces as %20 and '+' as %2B.")

    return issues


def main(root: str) -> int:
    issues = 0
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in EXCLUDE_DIRS]
        for filename in filenames:
            if filename.lower().endswith(".md"):
                issues += scan_file(os.path.join(dirpath, filename))
    if issues:
        print(f"Found {issues} markdown link(s) with unencoded spaces or plus signs.", file=sys.stderr)
        print("Please URL-encode spaces as %20 and '+' as %2B in relative links.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    root = sys.argv[1] if len(sys.argv) > 1 else "."
    sys.exit(main(root))

