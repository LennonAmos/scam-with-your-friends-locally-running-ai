#!/bin/sh
# Standalone Linux installer: download only the compiled setup, verify, and run.
set -eu
release_version=1.0.8
release_url="https://github.com/LennonAmos/scam-with-your-friends-locally-running-ai/releases/download/v$release_version"

for required in mono sha256sum awk mktemp; do
    if ! command -v "$required" >/dev/null 2>&1; then
        printf 'Missing requirement: %s\n' "$required" >&2
        if [ "$required" = mono ]; then
            printf '%s\n' 'Install the full Mono runtime (including System.Web.Extensions), then run this script again.' >&2
        fi
        exit 1
    fi
done
if command -v curl >/dev/null 2>&1; then
    download() { curl --fail --location --show-error --silent --connect-timeout 20 --max-time 300 --retry 2 --proto '=https' --proto-redir '=https' "$1" --output "$2"; }
elif command -v wget >/dev/null 2>&1; then
    download() { wget --https-only --quiet --timeout=20 --tries=3 --output-document="$2" "$1"; }
else
    printf '%s\n' 'Install curl or wget, then run this script again.' >&2
    exit 1
fi

work=$(mktemp -d "${TMPDIR:-/tmp}/ScamOllama.XXXXXXXX")
trap 'rm -rf "$work"' 0
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

printf 'Downloading Local AI v%s installer...\n' "$release_version"
download "$release_url/setup.exe" "$work/setup.exe"
download "$release_url/SHA256SUMS.txt" "$work/SHA256SUMS.txt"
expected=$(awk '{ sub(/\r$/, "", $2); if ($2 == "setup.exe") print $1 }' "$work/SHA256SUMS.txt")
case "$expected" in
    ''|*[!a-fA-F0-9]*) printf '%s\n' 'The release checksum is missing or invalid. Setup was not run.' >&2; exit 1 ;;
esac
if [ "${#expected}" -ne 64 ]; then
    printf '%s\n' 'The release checksum is invalid. Setup was not run.' >&2
    exit 1
fi
if ! (cd "$work" && printf '%s  setup.exe\n' "$expected" | sha256sum --check --status -); then
    printf '%s\n' 'Installer verification failed. Setup was not run; please download it again.' >&2
    exit 1
fi
printf '%s\n' 'Download verified. Starting setup...'
mono "$work/setup.exe" --no-pause "$@"
printf '%s\n' 'To remove the mod later: sh Install-linux-easy.sh --uninstall'
