# vion

The command line for the VION platform. One binary, no runtime to install: every operation of the
cloud API is a command, named after the operation — `vion tenant edge-gateway list` calls
`tenant.edgeGateway.list` — with the API's own descriptions as its help.

## Install

**Windows**

```powershell
winget install vion
```

**Linux** (x64 or arm64; a static binary with TLS built in — any distribution, no OpenSSL to install)

```bash
a=x64   # or arm64
v=$(curl -fsSLI -o /dev/null -w '%{url_effective}' https://github.com/VION-IoT/vion-cli/releases/latest); v=${v##*/v}
curl -fsSL --remote-name-all "https://github.com/VION-IoT/vion-cli/releases/download/v$v/vion-$v-linux-musl-$a.tar.gz"{,.sha256}
sha256sum -c "vion-$v-linux-musl-$a.tar.gz.sha256"
tar -xzf "vion-$v-linux-musl-$a.tar.gz" && sudo install -m 0755 vion /usr/local/bin/vion
```

Every release on the [releases page](https://github.com/VION-IoT/vion-cli/releases) carries the
three archives and a `.sha256` for each. `vion` tells you once a day when a newer release exists.

## Sign in

```bash
vion context use --env test --tenant <tenant id>   # or --env production; --integrator, --project, --platform
vion auth login                                     # a device code: open the link, or add --no-browser and use a phone
vion whoami
```

Automation signs in without a browser: set `VION_CLIENT_ID` and `VION_CLIENT_SECRET` to a service
account's credentials, or pipe a token into `vion … --token -`. `VION_ENV` selects the environment.

## Use

```bash
vion tenant edge-gateway list --json --fields id,name
vion tenant dashboard get --id <id> --json > dashboard.json
vion tenant dashboard update --id <id> --input dashboard.json       # If-Match from the document's meta.version
vion tenant logic-configuration activate --id <id> --dry-run
vion can-i tenant.edgeGateway.delete
vion ops                                                             # every command, its operation, effect and flags
vion api GET /tenant/<id>/edgeGateways                               # the raw call
vion tenant sync-status watch --edge-gateway-id <id> --json          # each message of the bus as one JSON line
vion tenant system-health watch --edge-gateway-id <id> --timeout 30s # … for half a minute, then exit 0
```

`--help` on any command shows its options; `--json` makes the output the API's response; `--yes`
confirms an irreversible operation; an operation that honours an idempotency key gets one per call.
A `watch` prints what the bus publishes after it says `watching`, until `--timeout` or Ctrl+C; pipe
its `--json` lines to `jq` to wait for one in particular.

## Source-available

This repository is source-available under [Apache 2.0](LICENSE). Issues and pull requests are not
accepted from outside the `VION-IoT` organization. See [CONTRIBUTING.md](CONTRIBUTING.md),
[SUPPORT.md](SUPPORT.md), and [SECURITY.md](SECURITY.md).

Maintainers: the release process lives in [docs/releasing.md](docs/releasing.md); how the tree is
derived from the API's two documents — `openapi/v1.json` for the operations, `asyncapi/v1.json`
for the channels a `watch` subscribes to, each pulled by its own script under `scripts/` — in
[CLAUDE.md](CLAUDE.md) § The three layers.
