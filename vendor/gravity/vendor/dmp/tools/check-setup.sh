#!/usr/bin/env bash
# check-setup.sh - a repository needs nothing that SETUP.md does not list (SETUP.md, "The check").
#
#   check-setup.sh <repository root> [dmp|gravity|im|cm]
#
# Reads the tables of SETUP.md (this file's own folder, so the vendored copy checks the repository that vendors it) and fails with one
# line per finding. It installs nothing and never reaches the network. Exit 0 or 1.
set -euo pipefail
root="${1:?repository root}"
here="$(cd "$(dirname "$0")/.." && pwd)"
setup="${SETUP_FILE:-$here/SETUP.md}"
[ -f "$setup" ] || { echo "FAIL no SETUP.md at $setup"; exit 1; }
cd "$root"

repo="${2:-}"
if [ -z "$repo" ]; then
  if [ -f Im.slnx ]; then repo=im
  elif [ -f CM.slnx ]; then repo=cm
  elif [ -f evidence/lakefile.toml ] && [ -f tools/pin.sh ]; then repo=gravity
  elif [ -f 01-constitution.md ] && [ -f VERSION ]; then repo=dmp
  else echo "FAIL cannot tell which root repository this is (pass dmp|gravity|im|cm)"; exit 1; fi
fi

# A table row: "| a | b | c |" -> the cells, trimmed. Rows of one section only.
section_rows() { # $1 = heading text
  awk -v h="## $1" '
    $0 == h { on = 1; next }
    /^## / { on = 0 }
    on && /^\| / && !/^\| *Id *\|/ && !/^\| *Host *\|/ && !/^\| *Repository *\|/ && !/^\|---/ { print }
  ' "$setup"
}
cell() { echo "$1" | awk -F'|' -v n="$2" '{ gsub(/^[ \t]+|[ \t]+$/, "", $(n+1)); print $(n+1) }'; }

bad=0
fail() { echo "FAIL $*"; bad=1; }

# The toolchain rows: id -> version.
declare -A tool_version
while IFS= read -r row; do
  id="$(cell "$row" 1)"; ver="$(cell "$row" 3)"
  [ -n "$id" ] && tool_version["$id"]="$ver"
done < <(section_rows "Toolchains")

# The hosts: one set.
declare -A host_ok
while IFS= read -r row; do
  hosts="$(cell "$row" 1)"
  IFS=',' read -ra parts <<< "$hosts"
  for h in "${parts[@]}"; do h="$(echo "$h" | tr -d ' ')"; [ -n "$h" ] && host_ok["$h"]=1; done
done < <(section_rows "Hosts")

# What this repository needs.
needs=""
while IFS= read -r row; do
  [ "$(cell "$row" 1)" = "$repo" ] && needs="$(cell "$row" 2)"
done < <(section_rows "What each repository needs")
[ -n "$needs" ] || fail "SETUP.md has no row for repository '$repo'"

# 5. every toolchain the row names has a toolchain row
for t in $(echo "$needs" | tr ',' ' '); do
  [ -n "${tool_version[$t]:-}" ] || fail "repository '$repo' needs toolchain '$t' and SETUP.md has no row for it"
done

# 1. the pinned Lean version is the lean row's
lean_row="${tool_version[lean]:-}"
while IFS= read -r f; do
  v="$(cat "$f" | tr -d '[:space:]')"; v="${v##*:}"
  [ "$v" = "$lean_row" ] || fail "$f pins Lean $v, SETUP.md says $lean_row"
done < <(find . -name lean-toolchain -not -path './vendor/*' -not -path '*/.lake/*' -not -path './.git/*' 2>/dev/null)

# 2. the .NET target is the dotnet row's major version
dotnet_row="${tool_version[dotnet]:-}"
if [ -f Directory.Build.props ]; then
  tf="$(grep -oE '<TargetFramework>[^<]+' Directory.Build.props | head -1 | sed 's/<TargetFramework>net//')"
  [ "${tf%%.*}" = "${dotnet_row%%.*}" ] || fail "Directory.Build.props targets net$tf, SETUP.md says .NET SDK $dotnet_row"
fi
if [ -f global.json ]; then
  gv="$(grep -oE '"version" *: *"[^"]+' global.json | head -1 | sed -E 's/.*"([^"]+)$/\1/')"
  [ -z "$gv" ] || [ "$gv" = "$dotnet_row" ] || fail "global.json pins SDK $gv, SETUP.md says $dotnet_row"
fi

# 3. every https host a workflow or a tool reaches is listed
while IFS= read -r h; do
  [ -n "${host_ok[$h]:-}" ] || fail "host '$h' is reached by a workflow or a tool and is not in SETUP.md"
done < <(grep -rhoE 'https://[A-Za-z0-9.-]+' .github tools evidence/tools proof/tools 2>/dev/null | sed 's#https://##' | sort -u)

# 4. every vendored PIN names a listed source host
while IFS= read -r pin; do
  src="$(grep -E '^source' "$pin" | head -1 | awk '{print $2}' | sed -E 's#^https?://##; s#/.*$##')"
  [ -z "$src" ] || [ -n "${host_ok[$src]:-}" ] || fail "$pin names source host '$src', not in SETUP.md"
done < <(find vendor -maxdepth 2 -name PIN 2>/dev/null)

if [ $bad -eq 0 ]; then
  echo "setup: OK ($repo; ${#tool_version[@]} toolchains, ${#host_ok[@]} hosts listed)"
else
  exit 1
fi
