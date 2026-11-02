#!/bin/sh
# Standalone Linux installer: download only the compiled setup, verify, and run.
set -eu
release_url="https://github.com/LennonAmos/scam-with-your-friends-locally-running-ai/releases/latest/download"

as_root() {
    if [ "$(id -u)" -eq 0 ]; then
        "$@"
    elif command -v sudo >/dev/null 2>&1; then
        sudo "$@"
    elif command -v doas >/dev/null 2>&1; then
        doas "$@"
    else
        printf '%s\n' 'Automatic setup needs administrator access, but this system has no supported password prompt.' >&2
        return 1
    fi
}

install_requirements() {
    ID= ID_LIKE=
    if [ -r /etc/os-release ]; then . /etc/os-release; fi
    printf '%s\n' 'Preparing your computer for setup. It may ask for your login password.'
    # Only the package manager runs as administrator; game setup stays as you.
    case " $ID $ID_LIKE " in
        *' arch '*|*' cachyos '*)
            as_root pacman -S --needed --noconfirm mono curl ca-certificates coreutils gawk || return 1
            ;;
        *' debian '*|*' ubuntu '*)
            as_root apt-get update || return 1
            as_root apt-get install -y mono-runtime libmono-system-web-extensions4.0-cil libmono-system-io-compression4.0-cil libmono-system-io-compression-filesystem4.0-cil curl ca-certificates coreutils gawk || return 1
            ;;
        *' fedora '*)
            as_root dnf install -y mono-core 'mono(System.Web.Extensions)' 'mono(System.IO.Compression.FileSystem)' curl ca-certificates coreutils gawk || return 1
            ;;
        *)
            printf '%s\n' 'Automatic setup supports Arch/CachyOS, Debian/Ubuntu and Fedora desktop systems. This distribution is not supported yet.' >&2
            return 1
            ;;
    esac
}

need_requirements=0
for required in mono sha256sum awk mktemp; do
    if ! command -v "$required" >/dev/null 2>&1; then need_requirements=1; fi
done
if ! command -v curl >/dev/null 2>&1 && ! command -v wget >/dev/null 2>&1; then need_requirements=1; fi
if [ "$need_requirements" -eq 1 ]; then
    if ! install_requirements; then
        printf '%s\n' 'Automatic setup could not finish. Check the package-manager message above, then run this script again.' >&2
        exit 1
    fi
fi
for required in mono sha256sum awk mktemp; do
    if ! command -v "$required" >/dev/null 2>&1; then
        printf '%s\n' 'The required setup components could not be installed automatically.' >&2
        exit 1
    fi
done
if command -v curl >/dev/null 2>&1; then
    download() { curl --fail --location --show-error --silent --connect-timeout 20 --max-time 300 --retry 2 --proto '=https' --proto-redir '=https' "$1" --output "$2"; }
elif command -v wget >/dev/null 2>&1; then
    download() { wget --https-only --quiet --timeout=20 --tries=3 --output-document="$2" "$1"; }
else
    printf '%s\n' 'The download component could not be installed automatically.' >&2
    exit 1
fi

work=$(mktemp -d "${TMPDIR:-/tmp}/ScamOllama.XXXXXXXX")
trap 'rm -rf "$work"' 0
trap 'exit 129' HUP
trap 'exit 130' INT
trap 'exit 143' TERM

printf '%s\n' 'Downloading the latest Local AI installer...'
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
if ! mono "$work/setup.exe" --help --no-pause >"$work/runtime-check.log" 2>&1; then
    # Repair an existing minimal runtime that lacks the setup libraries.
    if ! install_requirements; then
        printf '%s\n' 'Automatic setup could not install the remaining components.' >&2
        exit 1
    fi
    if ! mono "$work/setup.exe" --help --no-pause >"$work/runtime-check.log" 2>&1; then
        printf '%s\n' 'The downloaded installer could not start on this computer.' >&2
        exit 1
    fi
fi
printf '%s\n' 'Download verified. Starting setup...'
mono "$work/setup.exe" --no-pause "$@"
printf '%s\n' 'To remove the mod later: sh Install-linux-easy.sh --uninstall'
