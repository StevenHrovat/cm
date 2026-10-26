#!/usr/bin/env bash
# proof/tools/check.sh - the proof of IM: builds the Lean package and asks the kernel which axioms every theorem rests on.
#
#   proof/tools/check.sh      from anywhere (lake and lean on the PATH, toolchain from proof/lean-toolchain; vendor/dmp/SETUP.md says how)
#
# Passes only when the vendored DMP and Gravity match their PINs, the setup check passes, no file uses an escape hatch, `lake build`
# succeeds, every theorem rests on nothing beyond Lean's standard foundations (propext, Quot.sound, Classical.choice), and the committed
# table proof/table/shapes.tsv is what the proof prints. Writes report/axioms.txt and report/theorems.txt (the list a second kernel re-checks).
# The shape of this script is Gravity's (vendor/gravity/evidence/tools/check.sh, Apache License 2.0).
set -euo pipefail
cd "$(dirname "$0")/.."
# First act: the sentences this proof is stated from are the pinned DMP and Gravity (the root chain), or the run proves nothing.
../tools/check-pin.sh
# Second act: this repository needs nothing the one setup file does not list.
../vendor/dmp/tools/check-setup.sh .. im
hatch='\bsorry\b|\badmit\b|^\s*(private\s+)?axiom\b|\bnative_decide\b|\bunsafe\b|implemented_by|@\[extern'
if grep -rEn "$hatch" Im Im.lean; then echo "FAIL escape hatch above"; exit 1; fi
lake build
mkdir -p report
names="$(grep -hE '^theorem ' Im/*.lean | sed -E 's/^theorem ([^ :({\[]+).*/\1/' | sort -u)"
count="$(echo "$names" | grep -c . || true)"
echo "$names" > report/theorems.txt
{ echo "import Im"; for n in $names; do echo "#print axioms Im.$n"; done; } > report/Report.lean
lake env lean report/Report.lean > report/axioms.txt 2>&1
bad=0
for n in $names; do
  line="$(grep -E "^'Im\.$n'( does not depend on any axioms| depends on axioms:)" report/axioms.txt | head -1 || true)"
  if [ -z "$line" ]; then echo "FAIL no axiom report for Im.$n"; bad=1; continue; fi
  extra="$(echo "$line" | sed -n 's/.*depends on axioms: \[\(.*\)\]/\1/p' | tr ',' '\n' | tr -d ' ' | grep -vxE 'propext|Classical\.choice|Quot\.sound' || true)"
  if [ -n "$extra" ]; then echo "FAIL Im.$n rests on $extra"; bad=1; fi
done
cat report/axioms.txt
[ $bad -eq 0 ] || exit 1
# Third act: the table the C# replays is what the proof prints.
lake env lean --run Table.lean > report/shapes.tsv
if ! diff -q report/shapes.tsv table/shapes.tsv >/dev/null 2>&1; then
  echo "FAIL proof/table/shapes.tsv differs from what the proof prints (copy report/shapes.tsv over it and say so in the pull request)"; diff report/shapes.tsv table/shapes.tsv || true; exit 1
fi
echo "OK $count theorems, none resting on more than propext, Quot.sound, Classical.choice; table of $(grep -vc '^#' table/shapes.tsv) shapes matches"
