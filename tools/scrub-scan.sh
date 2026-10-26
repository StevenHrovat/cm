#!/usr/bin/env bash
# Public-repository gate: fails when any tracked file carries an identifier of a real organisation, host, network,
# person or credential. Patterns are generic shapes plus the names of the private modules this project grew out of.
# Usage: tools/scrub-scan.sh [extra-pattern-file]. Exit 0 = clean, 1 = hits (printed).
set -u
cd "$(git rev-parse --show-toplevel)"
patterns=(
  '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'                       # e-mail addresses
  '\b([0-9]{1,3}\.){3}[0-9]{1,3}\b'                                      # IPv4 addresses
  '\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b'  # GUIDs (tenant, app, object ids)
  '[äöüÄÖÜß]'                                                            # German text
  '\b[A-Z]{2,5}-[A-Z0-9]{2,}[0-9]{2}\b'                                  # host names shaped like ABC-NAME01
  '\b(svc|adm)_[a-z0-9_]+'                                               # service and admin account names
  'onmicrosoft\.com'
  '\bD-[0-9]{3}\b'                                                       # decision numbers of the private decision log
  '(sirius|adhara|cm|cm-work|wezen)#[0-9]+'                              # references to private pull requests
)
if [ "${1:-}" != "" ] && [ -f "$1" ]; then mapfile -t extra < "$1"; patterns+=("${extra[@]}"); fi
# Known technical names that share a pattern's shape (algorithm names, run ids, loopback and RFC 5737 documentation addresses, example mail
# addresses, the public UEFI GUID, OIDs, the synthetic accounts of the guard tests); removed from the hits.
allow='HMAC-SHA[0-9]+|SHA-[0-9]+|GRB-[0-9]{4}|"1\.3\.6\.1\.[0-9.]+"|generated once for CM|127\.0\.0\.1|0\.0\.0\.0|192\.0\.2\.[0-9]+|198\.51\.100\.[0-9]+|203\.0\.113\.[0-9./]+|1\.3\.6\.1\.5\.5\.7\.3\.[0-9]|[A-Za-z0-9._-]+@example\.(com|org|test)|8be4df61-93ca-11d2-aa0d-00e098032b8c|adm_(alice|bob)'
fail=0
for p in "${patterns[@]}"; do
  hits=$(git ls-files -z | grep -zv -e '^LICENSE$' -e '^tools/scrub-scan.sh$' -e '^vendor/.*/tools/scrub-scan.sh$' | xargs -0 grep -nIE -- "$p" 2>/dev/null | grep -vE -- "^[^:]+:[0-9]+:.*($allow)" || true)
  if [ -n "$hits" ]; then echo "== pattern: $p"; echo "$hits"; fail=1; fi
done
[ $fail -eq 0 ] && echo "scrub-scan: clean"
exit $fail
