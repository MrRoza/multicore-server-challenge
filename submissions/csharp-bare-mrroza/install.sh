#!/usr/bin/env bash
# Runs once as root on a clean Ubuntu 24.04 x86-64: clang and zlib for the Native AOT link, ICU for the SDK itself,
# and the pinned .NET SDK from Microsoft's tarball, checked against its published SHA-512.
set -euo pipefail
export DEBIAN_FRONTEND=noninteractive
apt-get -o DPkg::Lock::Timeout=300 update -q
apt-get -o DPkg::Lock::Timeout=300 install -y -q --no-install-recommends ca-certificates curl clang binutils zlib1g-dev libicu74

VERSION=10.0.402
SHA512=1904a98dda2db34ba78ba3c4fb1b224455329af91c341d83547843c1839e112b04ae699ee92fa2449a8f62249196a938723078c0c60b9f65c88539d5687729c2
ROOT=/usr/local/dotnet-bare
if [ "$("$ROOT/dotnet" --version 2>/dev/null || true)" = "$VERSION" ]; then exit 0; fi

ARCHIVE=$(mktemp)
trap 'rm -f "$ARCHIVE"' EXIT
curl -fsSL --retry 5 -o "$ARCHIVE" "https://builds.dotnet.microsoft.com/dotnet/Sdk/$VERSION/dotnet-sdk-$VERSION-linux-x64.tar.gz"
echo "$SHA512  $ARCHIVE" | sha512sum -c --quiet -
rm -rf "$ROOT"
mkdir -p "$ROOT"
tar -xzf "$ARCHIVE" -C "$ROOT"
chmod -R a+rX "$ROOT"
DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 "$ROOT/dotnet" --version
