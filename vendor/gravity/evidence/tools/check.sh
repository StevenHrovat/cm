#!/usr/bin/env bash
# evidence/tools/check.sh - builds the Lean evidence chain and asks Lean's kernel which axioms every theorem rests on.
#
#   check.sh            from evidence/ (lake and lean on the PATH, toolchain from lean-toolchain)
#
# Passes only when `lake build` succeeds, no file uses an escape hatch, and every theorem rests on nothing beyond Lean's standard
# foundations (propext, Quot.sound, Classical.choice). Writes report/axioms.txt, the record of the run, and report/theorems.txt,
# the list the independent kernel re-checks.
set -euo pipefail
cd "$(dirname "$0")/.."
hatch='\bsorry\b|\badmit\b|^\s*(private\s+)?axiom\b|\bnative_decide\b|\bunsafe\b|implemented_by|@\[extern'
if grep -rEn "$hatch" Dmp Dmp.lean; then echo "FAIL escape hatch above"; exit 1; fi
lake build
mkdir -p report
names="$(grep -hE '^theorem ' Dmp/*.lean | sed -E 's/^theorem ([^ :({\[]+).*/\1/' | sort -u)"
count="$(echo "$names" | grep -c . || true)"
echo "$names" > report/theorems.txt
{ echo "import Dmp"; for n in $names; do echo "#print axioms Dmp.$n"; done; } > report/Report.lean
lake env lean report/Report.lean > report/axioms.txt 2>&1
bad=0
for n in $names; do
  line="$(grep -E "^'Dmp\.$n'( does not depend on any axioms| depends on axioms:)" report/axioms.txt | head -1 || true)"
  if [ -z "$line" ]; then echo "FAIL no axiom report for Dmp.$n"; bad=1; continue; fi
  extra="$(echo "$line" | sed -n 's/.*depends on axioms: \[\(.*\)\]/\1/p' | tr ',' '\n' | tr -d ' ' | grep -vxE 'propext|Classical\.choice|Quot\.sound' || true)"
  if [ -n "$extra" ]; then echo "FAIL Dmp.$n rests on $extra"; bad=1; fi
done
cat report/axioms.txt
[ $bad -eq 0 ] || exit 1
echo "OK $count theorems, none resting on more than propext, Quot.sound, Classical.choice"
