# vion

The command line for the VION platform. One binary, no runtime to install: every operation of the
cloud API is a command, named after the operation — `vion tenant edge-gateway list` calls
`tenant.edgeGateway.list` — with the API's own descriptions as its help.

## Install

**Windows**

```powershell
winget install vion
```

**Linux** (x64 or arm64; a static binary, any distribution)

```bash
v=0.1.0; a=x64   # or arm64
curl -fsSLO "https://github.com/VION-IoT/vion-cli/releases/download/v$v/vion-$v-linux-musl-$a.tar.gz"{,.sha256}
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
```

`--help` on any command shows its options; `--json` makes the output the API's response; `--yes`
confirms an irreversible operation; an operation that honours an idempotency key gets one per call.

## Source-available

This repository is source-available under [Apache 2.0](LICENSE). Issues and pull requests are not
accepted from outside the `VION-IoT` organization. See [CONTRIBUTING.md](CONTRIBUTING.md),
[SUPPORT.md](SUPPORT.md), and [SECURITY.md](SECURITY.md).

Maintainers: the release process lives in [docs/releasing.md](docs/releasing.md); how the tree is
derived from the API's document, in [CLAUDE.md](CLAUDE.md) § The three layers.
