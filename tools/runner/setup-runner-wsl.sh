#!/usr/bin/env bash
# setup-runner-wsl.sh: GitHub Actions self-hosted runners on Linux (made for a WSL2 Ubuntu on a Windows machine), as systemd services under one local user.
# The Linux counterpart of setup-runner.ps1. Nothing in it is specific to one organisation, repository or host: every name, folder and label is a parameter.
#
# Run it as root inside the distro (from Windows: wsl.exe -d <distro> -u root -- bash -s -- <stage> < setup-runner-wsl.sh). Idempotent: a second run says "already".
# A secret never touches a file: the registration token comes in the environment (REG_TOKEN) and lives only in the running process.
#
# Stages:
#   provision   The distro and the user. systemd on, the Windows drives NOT mounted, no Windows programs callable from Linux (so a workflow cannot reach the
#               Windows account that owns the distro), the local user (no password, sudo inside this distro only), the packages the workflows usually need
#               (git, curl, jq, build tools, swtpm) and Rust for that user. Needs a distro restart (wsl.exe --shutdown) to take effect: it says so.
#   runner      REPO=owner/name COUNT=n REG_TOKEN=<token> [LABELS=wsl] [PREFIX=wsl-<name>] [FROM=1]: runners FROM..COUNT, each in
#               ~/runners/<name>-k, each a systemd service, all with the same labels (plus the self-hosted, Linux, X64 GitHub adds).
#   undo        REPO=owner/name COUNT=n REMOVE_TOKEN=<token> [FROM=1]: stops the services and removes the runners from GitHub. The folders stay.
set -euo pipefail

stage="${1:?stage: provision | runner | undo}"
user="${RUNNER_USER:-ci-runner}"
home="/home/$user"

say() { printf '%s\n' "$*"; }
need_root() { [ "$(id -u)" -eq 0 ] || { say "must run as root inside the distro"; exit 1; }; }
need_root

write_if_different() { # path, content (stdin)
    local path="$1" new; new="$(cat)"
    if [ -f "$path" ] && [ "$(cat "$path")" = "$new" ]; then say "   already  $path"; return 0; fi
    printf '%s\n' "$new" > "$path"; say "   wrote    $path"; return 1
}

provision() {
    say "== provision: systemd, no Windows drives, no interop, user $user, packages"
    local restart=0
    write_if_different /etc/wsl.conf <<EOF || restart=1
[boot]
systemd=true

[automount]
enabled=false

[interop]
enabled=false
appendWindowsPath=false

[user]
default=$user
EOF
    if ! id "$user" >/dev/null 2>&1; then
        useradd --create-home --shell /bin/bash --comment 'CI GitHub Actions runners' "$user"
        passwd -l "$user" >/dev/null
        say "   created  user $user (password locked)"
    else say "   already  user $user"; fi
    write_if_different /etc/sudoers.d/ci-runner <<EOF || true
$user ALL=(ALL) NOPASSWD:ALL
EOF
    chmod 0440 /etc/sudoers.d/ci-runner

    export DEBIAN_FRONTEND=noninteractive
    local pkgs=(ca-certificates curl git jq unzip zip build-essential pkg-config libssl-dev python3 swtpm swtpm-tools)
    local missing=()
    for p in "${pkgs[@]}"; do dpkg -s "$p" >/dev/null 2>&1 || missing+=("$p"); done
    if [ "${#missing[@]}" -gt 0 ]; then apt-get update -q && apt-get install -y -q "${missing[@]}"; say "   installed ${missing[*]}"
    else say "   already  packages"; fi

    if [ ! -x "$home/.cargo/bin/cargo" ]; then
        runuser -u "$user" -- bash -c 'curl -sSfL https://sh.rustup.rs | sh -s -- -y --profile minimal --no-modify-path >/dev/null 2>&1'
        say "   installed Rust (rustup, stable) for $user"
    else say "   already  Rust for $user"; fi
    [ "$restart" -eq 0 ] || say "NOTE: /etc/wsl.conf changed. From Windows run: wsl.exe --shutdown   (then start the distro again)."
}

latest_runner() { # sets RUN_VERSION RUN_URL RUN_SHA
    local json; json="$(curl -sSfL -H 'User-Agent: setup-runner-wsl' https://api.github.com/repos/actions/runner/releases/latest)"
    RUN_VERSION="$(jq -r '.tag_name' <<<"$json" | sed 's/^v//')"
    local asset="actions-runner-linux-x64-$RUN_VERSION.tar.gz"
    RUN_URL="$(jq -r --arg n "$asset" '.assets[]|select(.name==$n)|.browser_download_url' <<<"$json")"
    RUN_SHA="$(jq -r --arg n "$asset" '.assets[]|select(.name==$n)|.digest // empty' <<<"$json" | sed 's/^sha256://')"
    if [ -z "$RUN_SHA" ]; then RUN_SHA="$(jq -r '.body' <<<"$json" | grep -oE '<!-- BEGIN SHA linux-x64 -->[0-9a-fA-F]{64}' | grep -oE '[0-9a-fA-F]{64}$' || true)"; fi
    [ -n "$RUN_URL" ] && [ -n "$RUN_SHA" ] || { say "no download URL or SHA-256 for $asset; refusing to install an unverified download"; exit 1; }
}

names() { # sets REPO_NAME, FROM, COUNT, PREFIX
    : "${REPO:?REPO=owner/name}"; : "${COUNT:?COUNT=n}"
    REPO_NAME="${REPO#*/}"; FROM="${FROM:-1}"; PREFIX="${PREFIX:-wsl-$REPO_NAME}"
}

runner() {
    names; : "${REG_TOKEN:?REG_TOKEN (gh api -X POST repos/<repo>/actions/runners/registration-token --jq .token)}"
    local labels="${LABELS:-wsl}"
    say "== runner: $REPO, runners $FROM..$COUNT, labels $labels"
    latest_runner
    say "   runner version $RUN_VERSION, SHA-256 from the release data: $RUN_SHA"
    local cache="/var/cache/setup-runner-wsl/actions-runner-linux-x64-$RUN_VERSION.tar.gz"
    if [ ! -f "$cache" ] || [ "$(sha256sum "$cache" | cut -d' ' -f1)" != "$RUN_SHA" ]; then
        mkdir -p "$(dirname "$cache")"; curl -sSfL "$RUN_URL" -o "$cache"
        [ "$(sha256sum "$cache" | cut -d' ' -f1)" = "$RUN_SHA" ] || { rm -f "$cache"; say "SHA-256 of the download differs from the release data; nothing unpacked"; exit 1; }
    fi
    local k name dir svc
    for k in $(seq "$FROM" "$COUNT"); do
        name="$PREFIX-$k"; dir="$home/runners/$REPO_NAME-$k"
        svc="actions.runner.${REPO//\//-}.$name.service"
        if [ -f "$dir/.runner" ] && systemctl is-active --quiet "$svc"; then say "   already  $name ($dir, $svc active)"; continue; fi
        if [ ! -x "$dir/config.sh" ]; then
            mkdir -p "$dir"; tar -xzf "$cache" -C "$dir"; chown -R "$user:$user" "$home/runners"
            [ -f "$dir/.installed-deps" ] || { "$dir/bin/installdependencies.sh" >/dev/null 2>&1 && touch "$dir/.installed-deps"; }
        fi
        # DOTNET_INSTALL_DIR per runner: actions/setup-dotnet otherwise wants /usr/share/dotnet (root) and runners would race on one folder.
        printf 'PATH=%s/.cargo/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin\nDOTNET_INSTALL_DIR=%s/.dotnet\nDOTNET_CLI_TELEMETRY_OPTOUT=1\nDOTNET_NOLOGO=1\n' "$home" "$dir" > "$dir/.env"
        chown "$user:$user" "$dir/.env"
        if [ ! -f "$dir/.runner" ]; then
            ( cd "$dir" && runuser -u "$user" -- ./config.sh --unattended --url "https://github.com/$REPO" --token "$REG_TOKEN" --name "$name" --labels "$labels" --work _work --replace >/dev/null )
            say "   registered $name"
        fi
        ( cd "$dir" && ./svc.sh install "$user" >/dev/null && ./svc.sh start >/dev/null )
        systemctl is-active --quiet "$svc" && say "   OK       $name runs as $svc" || { say "   MISSING  $svc is not active"; exit 1; }
    done
}

undo() {
    names; : "${REMOVE_TOKEN:?REMOVE_TOKEN (gh api -X POST repos/<repo>/actions/runners/remove-token --jq .token)}"
    say "== undo: $REPO, runners $FROM..$COUNT"
    local k name dir
    for k in $(seq "$FROM" "$COUNT"); do
        name="$PREFIX-$k"; dir="$home/runners/$REPO_NAME-$k"
        [ -f "$dir/.runner" ] || { say "   already  no runner $name"; continue; }
        pgrep -f "$dir/bin/Runner.Worker" >/dev/null && { say "a job runs on $name; undo when it is idle"; exit 1; }
        ( cd "$dir" && ./svc.sh stop >/dev/null 2>&1 || true; ./svc.sh uninstall >/dev/null 2>&1 || true
          runuser -u "$user" -- ./config.sh remove --token "$REMOVE_TOKEN" >/dev/null )
        say "   removed  $name ($dir stays)"
    done
}

case "$stage" in
    provision) provision ;;
    runner) runner ;;
    undo) undo ;;
    *) say "unknown stage $stage"; exit 1 ;;
esac

exit 0
