#!/usr/bin/env bash
# Downloads Tom Harte's SingleStepTests 6502 suite (~1.1 GB) into test-data/harte/6502.
# The per-cycle CPU tests are skipped when this folder is missing.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
dest="$root/test-data/harte/6502"
mkdir -p "$dest"
base="https://raw.githubusercontent.com/SingleStepTests/65x02/main/6502/v1"
for i in $(seq 0 255); do
  name=$(printf "%02x.json" "$i")
  if [ ! -s "$dest/$name" ]; then
    curl -sSfL --retry 3 "$base/$name" -o "$dest/$name.tmp" && mv "$dest/$name.tmp" "$dest/$name"
  fi
done
echo "Harte 6502 tests in $dest"
