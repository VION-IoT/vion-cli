#!/bin/sh
# Installs vion for this user on Linux, x64 or arm64, with no root:
#
#   curl -fsSL https://github.com/VION-IoT/vion-cli/releases/latest/download/install.sh | sh
#
# It downloads the release's archive and its .sha256, refuses the archive unless the two match,
# places `vion` in the install folder, and writes the install record that lets `vion update`
# replace it later. Nothing is placed before the checksum matched.
#
#   VION_VERSION      a release to install, such as 0.8.0; the latest when unset
#   VION_INSTALL_DIR  the folder to place vion in; $XDG_BIN_HOME, else ~/.local/bin, when unset
#   VION_HOME         vion's home directory, where the record goes; ~/.vion when unset
#
# The record, $VION_HOME/install.json, names the binary's full path: `vion update` replaces only
# a binary a record names. Another vion on this machine — one in /usr/local/bin, a hand-placed
# copy — is left as it is; the script says when one comes first on PATH.
#
# Everything runs inside main, called on the last line, so a download cut short runs nothing.

set -eu

main() {
    repo=https://github.com/VION-IoT/vion-cli

    case "$(uname -s)" in
        Linux) ;;
        *) fail "this script installs vion on Linux; on Windows run: irm $repo/releases/latest/download/install.ps1 | iex" ;;
    esac

    case "$(uname -m)" in
        x86_64 | amd64) arch=x64 ;;
        aarch64 | arm64) arch=arm64 ;;
        *) fail "no vion is published for $(uname -m); the releases carry linux-musl-x64 and linux-musl-arm64" ;;
    esac

    need curl
    need tar
    if command -v sha256sum > /dev/null 2>&1; then
        sha256() { sha256sum "$1" | cut -d ' ' -f 1; }
    elif command -v shasum > /dev/null 2>&1; then
        sha256() { shasum -a 256 "$1" | cut -d ' ' -f 1; }
    else
        fail "neither sha256sum nor shasum is installed, and the archive is never placed unverified"
    fi

    # The latest release is read off the redirect GitHub answers for releases/latest, which ends
    # in the tag; the API would do as well, but answers an unauthenticated caller sixty times an hour.
    version=${VION_VERSION:-}
    if [ -z "$version" ]; then
        latest=$(curl -fsSLI -o /dev/null -w '%{url_effective}' "$repo/releases/latest") ||
            fail "could not read the latest release from $repo/releases/latest"
        version=${latest##*/}
    fi
    version=${version#v}
    case "$version" in
        '' | *[!0-9A-Za-z.+-]*) fail "'$version' is not a release version, such as 0.8.0" ;;
    esac

    dir=${VION_INSTALL_DIR:-${XDG_BIN_HOME:-$HOME/.local/bin}}
    home=${VION_HOME:-$HOME/.vion}
    archive=vion-$version-linux-musl-$arch.tar.gz
    url=$repo/releases/download/v$version/$archive

    tmp=$(mktemp -d)
    trap 'rm -rf "$tmp"' EXIT
    trap 'exit 1' INT TERM

    say "downloading $archive"
    # A release is published before its build has attached the archives, so for a few minutes the
    # latest release can lack them; the message says so rather than leaving a bare 404.
    curl -fsSL -o "$tmp/$archive" "$url" ||
        fail "release v$version carries no $archive. Its build attaches the archives in the minutes after the release is published: try again shortly, or set VION_VERSION to an earlier release. Nothing was installed."
    curl -fsSL -o "$tmp/$archive.sha256" "$url.sha256" ||
        fail "release v$version carries no $archive.sha256. Try again shortly, or set VION_VERSION to an earlier release. Nothing was installed."

    expected=$(cut -d ' ' -f 1 < "$tmp/$archive.sha256" | tr 'A-F' 'a-f')
    actual=$(sha256 "$tmp/$archive")
    [ "$expected" = "$actual" ] ||
        fail "$archive does not match its .sha256: the checksum file says $expected, the download hashes to $actual. Nothing was installed."

    tar -xzf "$tmp/$archive" -C "$tmp" vion || fail "$archive holds no vion. Nothing was installed."

    mkdir -p "$dir" 2> /dev/null || fail "could not create $dir; set VION_INSTALL_DIR to a folder you own. Nothing was installed."
    [ -w "$dir" ] || fail "$dir is not writable by $(id -un); set VION_INSTALL_DIR to a folder you own. Nothing was installed."

    # The physical path, with no symbolic link in it, because that is the path the kernel reports
    # as the running binary's, and `vion update` compares the record against that.
    dir=$(cd "$dir" && pwd -P)
    target=$dir/vion

    # A file beside the target, renamed over it: the rename is atomic, and a vion running from
    # the old file keeps running.
    cp "$tmp/vion" "$dir/.vion.$$.tmp"
    chmod 0755 "$dir/.vion.$$.tmp"
    mv -f "$dir/.vion.$$.tmp" "$target"

    mkdir -p "$home"
    printf '{\n  "path": "%s",\n  "version": "%s",\n  "installedBy": "install.sh",\n  "installedAt": "%s"\n}\n' \
        "$(json "$target")" "$version" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$home/install.json.$$.tmp"
    mv -f "$home/install.json.$$.tmp" "$home/install.json"

    say "installed vion $version at $target"

    case ":$PATH:" in
        *":$dir:"*)
            first=$(command -v vion 2> /dev/null || true)
            if [ -n "$first" ] && [ "$first" != "$target" ]; then
                say "another vion, $first, comes first on PATH and runs instead of this one; remove it, for example: rm '$first' (sudo where its folder needs it)"
            fi
            ;;
        *)
            say "$dir is not on PATH. Add it, for example to ~/.profile, and open a new shell:"
            say "  export PATH=\"$dir:\$PATH\""
            ;;
    esac

    say "update it with: vion update"
}

say() {
    printf '%s\n' "$*" >&2
}

fail() {
    printf 'vion install: %s\n' "$*" >&2
    exit 1
}

need() {
    command -v "$1" > /dev/null 2>&1 || fail "$1 is not installed, and the install needs it"
}

# A path as a JSON string's contents: a backslash and a double quote escaped.
json() {
    printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

main "$@"
