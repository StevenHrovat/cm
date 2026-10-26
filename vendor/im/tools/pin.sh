#!/usr/bin/env bash
# tools/pin.sh - re-copies a root repository into vendor/<name>/ and writes its PIN.
#
#   tools/pin.sh <name> <source-url> <path-to-local-clone> <commit> [<version>]
#
# Copies `git archive <commit>` of the clone (without .github) over vendor/<name>/, reads the version from its VERSION file when none
# is given, and writes PIN (source, commit, version, sha256) in the format tools/check-pin.sh verifies. Run tools/check-pin.sh after it.
set -euo pipefail
[ $# -ge 4 ] || { echo "usage: tools/pin.sh <name> <source-url> <local-clone> <commit> [<version>]"; exit 2; }
name="$1"; source="$2"; clone="$3"; commit="$(git -C "$clone" rev-parse --verify "$4^{commit}")"
cd "$(git -C "$(dirname "$0")" rev-parse --show-toplevel)"
export LC_ALL=C
folder="vendor/$name"
rm -rf "$folder" && mkdir -p "$folder"
git -C "$clone" archive "$commit" | tar -x -C "$folder"
rm -rf "$folder/.github"
version="${5:-$(tr -d '[:space:]' < "$folder/VERSION")}"
hash="$({ while IFS= read -r -d '' f; do printf '%s\n' "${f#"$folder"/}"; cat "$f"; done \
          < <(find "$folder" -type f -print0 | sort -z); } | sha256sum | cut -d' ' -f1)"
printf 'source\t%s\ncommit\t%s\nversion\t%s\nsha256\t%s\n' "$source" "$commit" "$version" "$hash" > "$folder/PIN"
echo "$folder/PIN written: $commit (version $version, sha256 $hash)"
