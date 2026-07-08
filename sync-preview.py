#!/usr/bin/env python3
"""
sync-preview.py — Sync shared source files into the preview Unity project.

Transformations applied automatically:
  1. File-scoped namespace  →  block-scoped  (Unity C# doesn't support C# 10 file-scoped ns)
  2. Target-typed new(…)    →  explicit new TypeName(…)  (Unity C# < 9 doesn't support it)

Usage:
    python3 sync-preview.py          # sync all files
    python3 sync-preview.py --check  # dry-run: show what would change, exit 1 if stale
"""

import os
import re
import sys

REPO = os.path.dirname(os.path.abspath(__file__))

# Each tuple: (source path relative to repo root, destination path relative to repo root)
SYNC_MAP = [
    (
        "bazaarplusplus-mod/src/BazaarPlusPlus/Game/Settings/Visual/BppSettingsDockVisualConstants.cs",
        "bazaarplusplus-mod/preview/settings-ui-preview/Assets/SettingsDockPreview/BppSettingsDockVisualConstants.cs",
    ),
    # Add more pairs here when new shared files are introduced, e.g.:
    # (
    #     "bazaarplusplus-mod/src/.../SomeShared.cs",
    #     "bazaarplusplus-mod/preview/.../SomeShared.cs",
    # ),
]


def _convert_namespace(lines: list[str]) -> list[str]:
    """Convert file-scoped namespace declaration to block-scoped and indent body."""
    out: list[str] = []
    inside_ns = False

    for line in lines:
        m = re.match(r'^(namespace\s+[\w.]+)\s*;', line)
        if m:
            out.append(m.group(1))
            out.append('{')
            inside_ns = True
            continue

        if inside_ns:
            # Preserve blank lines as-is; indent non-blank lines
            out.append('    ' + line if line.strip() else line)
        else:
            out.append(line)

    if inside_ns:
        # Strip trailing blank lines, then close the namespace block
        while out and out[-1].strip() == '':
            out.pop()
        out.append('}')

    return out


def _convert_target_typed_new(lines: list[str]) -> list[str]:
    """
    Replace target-typed `new(…)` with explicit `new TypeName(…)` on field declarations.
    Handles patterns like:
        internal static readonly Color Foo = new(…);
        internal static readonly Vector2 Bar = new(…);
    """
    pattern = re.compile(
        r'\b((?:internal|public|private|protected)(?:\s+static)?\s+readonly\s+(\w+)\s+\w+\s*=\s*)new\('
    )

    result = []
    for line in lines:
        result.append(pattern.sub(lambda m: m.group(1) + f'new {m.group(2)}(', line))
    return result


def convert(src_text: str) -> str:
    lines = src_text.splitlines()
    lines = _convert_namespace(lines)
    lines = _convert_target_typed_new(lines)
    return '\n'.join(lines) + '\n'


def sync(dry_run: bool = False) -> int:
    stale = 0

    for rel_src, rel_dst in SYNC_MAP:
        src_path = os.path.join(REPO, rel_src)
        dst_path = os.path.join(REPO, rel_dst)

        if not os.path.isfile(src_path):
            print(f'  SKIP   {rel_src}  (source not found)')
            continue

        with open(src_path, encoding='utf-8') as f:
            raw = f.read()

        converted = convert(raw)

        existing: str | None = None
        if os.path.isfile(dst_path):
            with open(dst_path, encoding='utf-8') as f:
                existing = f.read()

        if converted == existing:
            print(f'  OK     {rel_dst}')
        elif dry_run:
            print(f'  STALE  {rel_dst}  (run without --check to update)')
            stale += 1
        else:
            os.makedirs(os.path.dirname(dst_path), exist_ok=True)
            with open(dst_path, 'w', encoding='utf-8') as f:
                f.write(converted)
            print(f'  SYNCED {rel_dst}')
            stale += 1

    if dry_run:
        if stale:
            print(f'\n{stale} file(s) out of date.')
        else:
            print('\nAll files up to date.')
        return stale
    else:
        print(f'\n{stale} file(s) updated.')
        return 0


if __name__ == '__main__':
    dry = '--check' in sys.argv
    sys.exit(sync(dry_run=dry))
