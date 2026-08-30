"""Fails if a PowerShell script would not parse on Windows.

Windows PowerShell 5.1 reads a .ps1 file as the system ANSI codepage unless the
file carries a UTF-8 BOM. A non-ASCII character then decodes to mojibake, and
inside a quoted string that is a parse error rather than a display glitch —
the whole script refuses to run.

tools/logs.ps1 was broken this way for several milestones. Nothing caught it
because the scripts are not exercised in CI, and the two other scripts survived
only because their non-ASCII happened to sit in comments.

    python tools/check-encoding.py

Exits non-zero and names the offenders.
"""

import pathlib
import sys

BOM = b"\xef\xbb\xbf"
ROOT = pathlib.Path(__file__).resolve().parent.parent


def main() -> int:
    offenders = []
    checked = 0

    for path in sorted(ROOT.glob("**/*.ps1")):
        if "build" in path.relative_to(ROOT).parts:
            continue

        checked += 1
        raw = path.read_bytes()
        has_bom = raw.startswith(BOM)
        body = raw[len(BOM):] if has_bom else raw

        try:
            body.decode("ascii")
        except UnicodeDecodeError:
            if not has_bom:
                offenders.append(path.relative_to(ROOT).as_posix())

    if offenders:
        print("These PowerShell scripts contain non-ASCII but no UTF-8 BOM.")
        print("Windows PowerShell will fail to parse them:")
        for name in offenders:
            print(f"  {name}")
        print("\nFix: prepend a UTF-8 BOM, or keep the file ASCII-only.")
        return 1

    print(f"{checked} PowerShell script(s) are safe for Windows PowerShell")
    return 0


if __name__ == "__main__":
    sys.exit(main())
