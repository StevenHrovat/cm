#!/usr/bin/env bash
# Rebuilds CM.BlackHole/tests/Doppler/Tpm/fixtures from a real TPM 2.0 implementation (swtpm with libtpms) and checks that a
# credential sealed by Doppler's TpmCredential.Make opens in that TPM (TPM2_ActivateCredential). Needs: swtpm, tpm2-tools, the
# .NET 10 SDK. Nothing here touches a real machine's TPM: swtpm is a throw-away software TPM in a temporary folder.
# Security context: defensive security engineering under the charter in CLAUDE.md (testing Doppler's own attestation checks).
set -euo pipefail
repo="$(cd "$(dirname "$0")/../.." && pwd)"
out="$repo/CM.BlackHole/tests/Doppler/Tpm/fixtures"
work="$(mktemp -d)"
trap 'kill "$(cat "$work/swtpm.pid" 2>/dev/null)" 2>/dev/null || true; rm -rf "$work"' EXIT
mkdir -p "$work/state"
swtpm socket --tpmstate dir="$work/state" --tpm2 --server type=tcp,port=2421 --ctrl type=tcp,port=2422 \
  --flags not-need-init,startup-clear --daemon --pid file="$work/swtpm.pid"
export TPM2TOOLS_TCTI="swtpm:host=127.0.0.1,port=2421"
cd "$work"
flush() { tpm2_flushcontext -t >/dev/null 2>&1 || true; tpm2_flushcontext -s >/dev/null 2>&1 || true; }
tpm2_createek -c ek.ctx -G rsa -u ek.pub >/dev/null; flush
tpm2_createak -C ek.ctx -c ak.ctx -G rsa -g sha256 -s rsassa -u ak.pub -n ak.name -r ak.priv >/dev/null; flush
tpm2_createprimary -C o -c prim.ctx -G ecc >/dev/null; flush
tpm2_create -C prim.ctx -G ecc256 -a "fixedtpm|fixedparent|sensitivedataorigin|userwithauth|sign" -u dev.pub -r dev.priv >/dev/null; flush
tpm2_load -C prim.ctx -u dev.pub -r dev.priv -c dev.ctx >/dev/null; flush
tpm2_certify -c dev.ctx -C ak.ctx -g sha256 -o certify.attest -s certify.sig >/dev/null; flush
tpm2_pcrextend 7:sha256="$(printf hello | sha256sum | cut -c1-64)"
tpm2_quote -c ak.ctx -l sha256:7 -q 00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff -m quote.attest -s quote.sig -g sha256 >/dev/null; flush
tpm2_pcrread sha256:7 -o pcr7.sha256 >/dev/null

# Seal a credential with Doppler's own code and open it in the TPM.
dotnet run --project "$repo/tools/tpm-fixtures/src/Cm.TpmFixtures.csproj" -- make-credential ek.pub ak.name cred.bin secret.bin
tpm2_startauthsession --policy-session -S session.ctx >/dev/null
tpm2_policysecret -S session.ctx -c e >/dev/null
tpm2_activatecredential -c ak.ctx -C ek.ctx -i cred.bin -o opened.bin -P"session:session.ctx" >/dev/null
cmp secret.bin opened.bin && echo "make-credential: the TPM opened Doppler's credential"
cp ek.pub ak.pub ak.name dev.pub certify.attest certify.sig quote.attest quote.sig pcr7.sha256 "$out/"
echo "fixtures written to $out"
