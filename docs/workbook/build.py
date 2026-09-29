#!/usr/bin/env python3
"""Builds kim1-workbook.pdf from workbook.html and the answer programs in answers/.

Each <div class="answer" data-file="NAME"> in workbook.html gets the listing of answers/NAME.s
(without its header comment, which repeats the problem). A routine copied from an earlier answer,
marked with ";>>> NAME (problem N)" ... ";<<<", is shown as a one-line reference instead.
The page is then printed to PDF with a headless Chromium browser (Edge or Chrome).

Usage:  python build.py [--browser PATH] [--html-only]
Needs Python 3.9+ and Edge, Chrome or Chromium (set CHROME to its path if it isn't found).
The answers are tested by tests/Kimulator.Tests/Workbook: run `dotnet test` after changing one.
"""

import argparse
import html
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
SOURCE = HERE / "workbook.html"
ANSWERS = HERE / "answers"
OUTPUT = HERE / "kim1-workbook.pdf"

MNEMONICS = set("""
    adc and asl bcc bcs beq bit bmi bne bpl brk bvc bvs clc cld cli clv cmp cpx cpy dec dex dey
    eor inc inx iny jmp jsr lda ldx ldy lsr nop ora pha php pla plp rol ror rti rts sbc sec sed
    sei sta stx sty tax tay tsx txa txs tya
""".split())

MARKER = re.compile(r"^;>>>\s*(.*)$")
PROBLEM_REF = re.compile(r"\(problem (\d+)")


def split_comment(line: str) -> tuple[str, str]:
    """Splits a source line at the first ';' that isn't inside a string or character constant."""
    i = 0
    while i < len(line):
        c = line[i]
        if c == '"':
            end = line.find('"', i + 1)
            i = len(line) if end < 0 else end + 1
            continue
        if c == "'":
            # 'x' (the closing quote is optional, as in the assembler)
            i += 3 if line[i + 2:i + 3] == "'" else 2
            continue
        if c == ";":
            return line[:i], line[i:]
        i += 1
    return line, ""


def highlight(line: str) -> str:
    code, comment = split_comment(line)
    out = []
    m = re.match(r"^([A-Za-z_@][\w@]*:?)", code)
    rest = code
    if m and not code[0].isspace():
        out.append(f'<span class="l">{html.escape(m.group(1))}</span>')
        rest = code[m.end():]
    m = re.match(r"^(\s+)([.\w]+)(.*)$", rest)
    if m:
        word = m.group(2)
        cls = "m" if word.lower() in MNEMONICS else "d" if word.startswith(".") else None
        out.append(html.escape(m.group(1)))
        out.append(f'<span class="{cls}">{html.escape(word)}</span>' if cls else html.escape(word))
        out.append(html.escape(m.group(3)))
    else:
        out.append(html.escape(rest))
    if comment:
        out.append(f'<span class="c">{html.escape(comment)}</span>')
    return "".join(out)


def listing(name: str) -> str:
    path = ANSWERS / f"{name}.s"
    lines = path.read_text(encoding="utf-8").splitlines()
    number = int(re.match(r"p(\d+)", name).group(1))

    # Drop the header comment: it repeats the problem statement.
    i = 0
    while i < len(lines) and (lines[i].startswith(";") or not lines[i].strip()):
        i += 1
    lines = lines[i:]

    out = []
    skipping = False
    for line in lines:
        marker = MARKER.match(line)
        if marker:
            ref = PROBLEM_REF.search(marker.group(1))
            if ref and int(ref.group(1)) != number:
                skipping = True
                out.append(f'<span class="omit">; {html.escape(marker.group(1))}: not repeated here</span>')
            continue
        if line.startswith(";<<<"):
            skipping = False
            continue
        if not skipping:
            out.append(highlight(line.rstrip()))
    while out and not out[-1].strip():
        out.pop()
    return "<pre><code>" + "\n".join(out) + "</code></pre>"


def build_html() -> str:
    page = SOURCE.read_text(encoding="utf-8")
    names = re.findall(r'<div class="answer" data-file="([^"]+)">', page)
    missing = [n for n in names if not (ANSWERS / f"{n}.s").exists()]
    if missing:
        sys.exit(f"Missing answer files: {', '.join(missing)}")
    unused = sorted({p.stem for p in ANSWERS.glob("*.s")} - set(names))
    if unused:
        print(f"Note: answers not referenced by workbook.html: {', '.join(unused)}")

    pattern = re.compile(r'(<div class="answer" data-file="([^"]+)">)(.*?)(</div>)', re.S)
    return pattern.sub(lambda m: m.group(1) + m.group(3) + listing(m.group(2)) + m.group(4), page)


def find_browser(explicit: str | None) -> str:
    candidates = [explicit, os.environ.get("CHROME")]
    candidates += [shutil.which(n) for n in ("msedge", "microsoft-edge", "google-chrome", "chrome", "chromium", "chromium-browser")]
    candidates += [
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/Applications/Chromium.app/Contents/MacOS/Chromium",
    ]
    for c in candidates:
        if c and Path(c).exists():
            return c
    sys.exit("No Chromium browser found: pass --browser or set CHROME.")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--browser", help="path to Edge, Chrome or Chromium")
    parser.add_argument("--html-only", action="store_true", help="write the filled-in HTML next to the PDF and stop")
    args = parser.parse_args()

    page = build_html()
    if args.html_only:
        out = OUTPUT.with_suffix(".html")
        out.write_text(page, encoding="utf-8")
        print(f"Wrote {out}")
        return

    browser = find_browser(args.browser)
    OUTPUT.unlink(missing_ok=True)
    with tempfile.TemporaryDirectory(ignore_cleanup_errors=True) as tmp:
        src = Path(tmp) / "workbook.html"
        src.write_text(page, encoding="utf-8")
        subprocess.run(
            [browser, "--headless", "--disable-gpu", "--no-pdf-header-footer", f"--user-data-dir={Path(tmp) / 'profile'}",
             f"--print-to-pdf={OUTPUT}", src.as_uri()],
            check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=120)
        # On Windows the launcher can return before the headless browser has written the file.
        size, deadline = -1, time.monotonic() + 90
        while time.monotonic() < deadline:
            if OUTPUT.exists() and OUTPUT.stat().st_size == size and size > 0:
                break
            size = OUTPUT.stat().st_size if OUTPUT.exists() else -1
            time.sleep(1)
        else:
            sys.exit("The browser didn't write the PDF.")
    print(f"Wrote {OUTPUT} ({OUTPUT.stat().st_size // 1024} KB)")


if __name__ == "__main__":
    main()
