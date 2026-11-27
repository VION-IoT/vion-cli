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
# The record, $VION_HOME/install.json, holds the binary's full path and nothing else: `vion update`
# replaces only the binary the record names. Another vion on this machine, one in /usr/local/bin or
# a hand-placed copy, is left as it is; the script says when one comes first on PATH.
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
    if ! command -v sha256sum > /dev/null 2>&1 && ! command -v shasum > /dev/null 2>&1; then
        fail "neither sha256sum nor shasum is installed, and the archive is never placed unverified"
    fi

    # GitHub answers releases/latest with a redirect to the latest release's page, whose address
    # ends in its tag, so the tag is the last part of the address the redirect leads to.
    # install.ps1 reads the same tag from GitHub's API instead, because Windows PowerShell 5.1
    # cannot read where a redirect leads without following it.
    version=${VION_VERSION:-}
    if [ -z "$version" ]; then
        latest=$(curl -fsSLI -o /dev/null -w '%{url_effective}' "$repo/releases/latest") ||
            fail "could not read the latest release from $repo/releases/latest"
        version=${latest##*/}
    fi

    version=${version#v}
    if [ -z "$version" ] || printf '%s' "$version" | grep -q '[^0-9A-Za-z.+-]'; then
        fail "'$version' is not a release version, such as 0.8.0"
    fi

    if [ -n "${VION_INSTALL_DIR:-}" ]; then
        dir=$VION_INSTALL_DIR
    elif [ -n "${XDG_BIN_HOME:-}" ]; then
        dir=$XDG_BIN_HOME
    else
        dir=$HOME/.local/bin
    fi

    home=${VION_HOME:-$HOME/.vion}
    archive=vion-$version-linux-musl-$arch.tar.gz
    url=$repo/releases/download/v$version/$archive

    tmp=$(mktemp -d)
    staged=
    record=
    trap cleanup EXIT
    # Without this, Ctrl+C would stop the script without running the EXIT trap in some shells.
    trap 'exit 1' INT TERM

    say "downloading $archive"
    fetch "$archive" "$url" "$tmp/$archive"
    fetch "$archive.sha256" "$url.sha256" "$tmp/$archive.sha256"

    expected=$(cut -d ' ' -f 1 < "$tmp/$archive.sha256" | tr 'A-F' 'a-f')
    actual=$(sha256 "$tmp/$archive")
    if [ "$expected" != "$actual" ]; then
        fail "$archive does not match its .sha256: the checksum file says $expected, the download hashes to $actual. Nothing was installed."
    fi

    tar -xzf "$tmp/$archive" -C "$tmp" vion || fail "$archive holds no vion. Nothing was installed."

    mkdir -p "$dir" 2> /dev/null || fail "could not create $dir; set VION_INSTALL_DIR to a folder you own. Nothing was installed."
    [ -w "$dir" ] || fail "$dir is not writable by $(id -un); set VION_INSTALL_DIR to a folder you own. Nothing was installed."

    # The physical path, with no symbolic link in it, because that is the path the kernel reports
    # as the running binary's, and `vion update` compares the record against that.
    dir=$(cd "$dir" && pwd -P)
    target=$dir/vion

    # A file beside the target, renamed over it: the rename is atomic, and a vion running from
    # the old file keeps running. mktemp names it unpredictably and creates it itself, so a name
    # planted in the folder beforehand is never written through.
    staged=$(mktemp "$dir/.vion.XXXXXXXX")
    cp "$tmp/vion" "$staged"
    chmod 0755 "$staged"
    mv -f "$staged" "$target"

    # The home directory also holds the credentials, so a folder this script creates is the
    # owner's alone, as vion creates it. The record is written beside its name and renamed over
    # it, as the binary is; mktemp makes the file readable by the owner alone, and the record is
    # no secret, so it is opened to other readers as any file in the folder is.
    (umask 077 && mkdir -p "$home")
    record=$(mktemp "$home/install.json.XXXXXXXX")
    printf '{\n  "path": "%s"\n}\n' "$(json "$target")" > "$record"
    chmod 0644 "$record"
    mv -f "$record" "$home/install.json"

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

# The temporary folder, and a staging file the script did not get to rename into place.
cleanup() {
    rm -rf "$tmp"
    if [ -n "$staged" ]; then
        rm -f "$staged"
    fi
    if [ -n "$record" ]; then
        rm -f "$record"
    fi
}

# A release is published before its build has attached the archives, so for a few minutes the
# latest release can lack them. curl -f exits 22 on an HTTP error, which is the asset not being
# there; any other exit is the network's, and says so.
fetch() {
    if curl -fsSL -o "$3" "$2"; then
        return
    else
        status=$?
    fi

    if [ "$status" -eq 22 ]; then
        fail "release v$version carries no $1. Its build attaches the archives in the minutes after the release is published: try again shortly, or set VION_VERSION to an earlier release. Nothing was installed."
    fi
    fail "could not download $2 (curl exit $status). Nothing was installed."
}

sha256() {
    if command -v sha256sum > /dev/null 2>&1; then
        sha256sum "$1" | cut -d ' ' -f 1
    else
        shasum -a 256 "$1" | cut -d ' ' -f 1
    fi
}

# A path as a JSON string's contents: a backslash and a double quote escaped.
json() {
    printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g'
}

main "$@"
