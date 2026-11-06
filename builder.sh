#!/usr/bin/env bash
cd "$(dirname "$0")"
# make also copies this script into Build/, beside Builder.exe
exe=Builder.exe
[ -f "$exe" ] || exe=Build/Builder.exe
export LD_LIBRARY_PATH="$PWD/$(dirname "$exe"):$LD_LIBRARY_PATH"
mono "$exe" "$@"
