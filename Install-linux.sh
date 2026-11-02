#!/bin/sh
# Run the shared installer on the Linux host; Steam runs the game with Proton.
set -eu
script_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if ! command -v mono >/dev/null 2>&1; then
    printf '%s\n' 'Mono is required. Install your distribution’s full Mono runtime (including System.Web.Extensions), then retry.' >&2
    exit 1
fi
if [ ! -f "$script_dir/setup.exe" ]; then
    printf '%s\n' 'setup.exe is missing. Extract the complete Linux release archive first; this source checkout does not contain the built installer.' >&2
    exit 1
fi
exec mono "$script_dir/setup.exe" --no-pause "$@"
