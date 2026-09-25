#!/usr/bin/env python3
"""Creates .zip / .tar.gz archives that keep Unix execute permissions, even when built on Windows.

Usage: archive.py zip|tar OUTPUT SOURCE_DIR [--exec RELATIVE_PATH ...]

SOURCE_DIR itself becomes the top-level entry in the archive. Paths given with --exec (relative to
SOURCE_DIR's parent, e.g. "Kimulator.app/Contents/MacOS/Kimulator") are marked executable.
"""
import os
import sys
import tarfile
import time
import zipfile


def entries(source):
    parent = os.path.dirname(os.path.abspath(source))
    for folder, _, files in os.walk(source):
        for name in sorted(files):
            full = os.path.join(folder, name)
            yield full, os.path.relpath(full, parent).replace(os.sep, "/")


def main():
    if len(sys.argv) < 4 or sys.argv[1] not in ("zip", "tar"):
        sys.exit(__doc__)
    kind, output, source = sys.argv[1:4]
    executables = set(sys.argv[sys.argv.index("--exec") + 1:]) if "--exec" in sys.argv else set()

    def mode(rel, full):
        if rel in executables or os.access(full, os.X_OK) and os.name != "nt":
            return 0o755
        return 0o644

    if kind == "zip":
        with zipfile.ZipFile(output, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
            for full, rel in entries(source):
                info = zipfile.ZipInfo(rel, time.localtime(os.path.getmtime(full))[:6])
                info.external_attr = (0o100000 | mode(rel, full)) << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                with open(full, "rb") as f:
                    z.writestr(info, f.read())
    else:
        with tarfile.open(output, "w:gz") as t:
            for full, rel in entries(source):
                info = t.gettarinfo(full, rel)
                info.mode = mode(rel, full)
                info.uid = info.gid = 0
                info.uname = info.gname = ""
                with open(full, "rb") as f:
                    t.addfile(info, f)


if __name__ == "__main__":
    main()
