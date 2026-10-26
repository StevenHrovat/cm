#!/usr/bin/env bash
# tools/check-pin.sh - Gravity holds the version of DMP it is based on (DMP 00-preamble, "The root chain").
#
#   tools/check-pin.sh            checks every vendor/<name>/ folder against its PIN
#
# A PIN has four tab-separated lines: source, commit, version, sha256. The sha256 is over every file of the folder except PIN, in
# ordinal path order, each as "<relative path>\n<bytes>" (the recipe CM's rule DMP-022 uses, so the two gates compute the same hash).
# Fails when a folder has no PIN, a PIN lacks a line, the hash differs, or vendor/dmp/VERSION is not the pinned version. A vendored copy
# is never edited here: change it in its source repository, re-copy it (git archive, without .github) and re-pin it (tools/pin.sh).
set -euo pipefail
cd "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
export LC_ALL=C
fail=0
hash_folder() {
  local folder="$1"
  { while IFS= read -r -d '' f; do printf '%s\n' "${f#"$folder"/}"; cat "$f"; done \
      < <(find "$folder" -type f ! -path "$folder/PIN" -print0 | sort -z); } | sha256sum | cut -d' ' -f1
}
[ -d vendor ] || { echo "FAIL vendor/ is missing: Gravity holds DMP (the root chain)"; exit 1; }
for folder in vendor/*/; do
  folder="${folder%/}"
  if [ ! -f "$folder/PIN" ]; then echo "FAIL $folder has no PIN"; fail=1; continue; fi
  declare -A pin=()
  while IFS=$'\t' read -r k v; do [ -n "${k:-}" ] && pin["$k"]="${v:-}"; done < "$folder/PIN"
  for k in source commit version sha256; do
    [ -n "${pin[$k]:-}" ] || { echo "FAIL $folder/PIN has no '$k' line"; fail=1; }
  done
  actual="$(hash_folder "$folder")"
  if [ "$actual" != "${pin[sha256]:-}" ]; then
    echo "FAIL $folder no longer matches its PIN (sha256 ${pin[sha256]:-?} pinned, $actual found)"; fail=1
  fi
  if [ -f "$folder/VERSION" ] && [ "$(tr -d '[:space:]' < "$folder/VERSION")" != "${pin[version]:-}" ]; then
    echo "FAIL $folder/VERSION says $(cat "$folder/VERSION") but the PIN says ${pin[version]:-?}"; fail=1
  fi
  [ $fail -eq 0 ] && echo "OK $folder = ${pin[source]} @ ${pin[commit]} (version ${pin[version]}, sha256 ${pin[sha256]})"
  unset pin
done
[ -f vendor/dmp/PIN ] || { echo "FAIL vendor/dmp is missing: Gravity holds DMP"; fail=1; }
exit $fail
