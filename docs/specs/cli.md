---
trace: enforced
---

# The `vion` command line

What `vion`, the platform's operator CLI, guarantees a person at a shell and a script in a pipeline.
Its command tree, options and help are derived from the committed document `openapi/v1.json`; what
this page states is the written layer around it — how a command is named and typed, what a call
sends, what comes back on which stream, how the tool refuses. Area code `CLI`, anchored by the help
snapshot `docs/snapshots/help.txt`, every command's `--help`, which `scripts/derive.ps1` regenerates
from the document. Process: [`../spec-process.md`](../spec-process.md).

The spine is the order a caller meets the tool: the tree and its options, a call on the wire, what
comes back, the written commands.

Two rules of dale-sdk's `dale` tool are adopted as they stand and cited rather than restated:
`dale-sdk/AC-CLI-001.8` — output is UTF-8 whatever the console's encoding, verified by the same probe —
and `dale-sdk/AC-CLI-001.10` — a JSON document the tool writes itself (`ops`, an error object) has
camel-cased members, strings for enumerations and indentation. The stream rule is `vion`'s own
(below): JSON mode keeps notices on standard error, where `dale` suppresses them.

## The tree is derived

A command exists exactly when an operation does; the one verb beside them, `watch`, exists
exactly when a channel is handed out, and is [`watch.md`](watch.md)'s. The `system` domain — device and bridge protocols,
and the catalog read the tool calls itself — stays out of the tree, reachable through `api`. The
four context values and the dry-run flag are globals, never derived options, so that one value has
one spelling on every command.

- `AC-CLI-001.1` (Ubiquitous): THE SYSTEM SHALL offer one command `vion <domain> <aggregate> <verb>` for every operation of the committed document whose domain is `tenant`, `integrator`, `platform` or `me`, the aggregate and verb being the operationId's segments in kebab case, and no derived command for any other operation.
- `AC-CLI-001.2` (Optional): WHERE an operation is `deprecated` THE SYSTEM SHALL keep its command invocable and hide it from help.
- `AC-CLI-001.3` (Ubiquitous): THE SYSTEM SHALL take every path and query parameter of an operation as an option named by the parameter's name in kebab case and typed by its schema, except `tenantId`, `integratorId`, `platformId` and `projectId`, which the context supplies, and `dryRun`, which `--dry-run` carries.
- `AC-CLI-001.4` (Unwanted): IF a derived option's name equals a global option's name THEN THE SYSTEM SHALL name the derived option with the suffix `-param` and show that name in help.
- `AC-CLI-001.5` (Unwanted): IF `cli.json` names an operationId the committed document lacks THEN THE SYSTEM SHALL fail the derive step naming the entry.
- `AC-CLI-001.6` (Ubiquitous): THE SYSTEM SHALL describe each derived command by its operation's description and each derived option by its parameter's description in help.

## A call on the wire

Every derived command takes one path from the table to the request: the context fills the root, the
options fill the rest, the body comes from `--input` or the pairs, and the operation's capabilities
decide which flags apply. A refusal happens before anything is sent.

- `AC-CLI-002.1` (Event-driven): WHEN a derived command runs THE SYSTEM SHALL send the operation's method to its path with each path parameter substituted and each given query option appended, and the JSON body built from `--input` or the `-f` and `-F` pairs.
- `AC-CLI-002.2` (Unwanted): IF a required path parameter has neither an option value nor a context value THEN THE SYSTEM SHALL refuse before sending, naming the option and `context use`, and exit 2.
- `AC-CLI-002.3` (Optional): WHERE the operation is `idempotent` THE SYSTEM SHALL send an `Idempotency-Key` header carrying `--idempotency-key`'s value or a fresh one, and SHALL report an answer carrying `Idempotency-Replayed: true` as a notice on standard error.
- `AC-CLI-002.4` (Optional): WHERE the operation is `versioned` THE SYSTEM SHALL send `If-Match` from `--if-match`, else from the `--input` document's `meta.version`, else from the aggregate's `get` on the same path where one exists, and SHALL otherwise refuse before sending with exit 2.
- `AC-CLI-002.5` (Optional): WHERE the operation's effect is `Irreversible` THE SYSTEM SHALL ask for confirmation on a terminal unless `--yes` is given, and SHALL refuse with exit 2 when standard input is not a terminal and `--yes` is absent.
- `AC-CLI-002.6` (Event-driven): WHEN `--dry-run` is given THE SYSTEM SHALL send `dryRun=true` on an operation flagged `dryRun` and refuse with exit 2 on any other operation and on a watch.
- `AC-CLI-002.7` (Optional): WHERE the operation takes `multipart/form-data` THE SYSTEM SHALL send `--file` as the schema's file part and each `-f` pair as a text part.
- `AC-CLI-002.8` (Unwanted): IF the environment answers a derived command with a 404 carrying no error body THEN THE SYSTEM SHALL say the operation is not in that environment's document and name `ops --live`.

## What comes back

Two modes. In JSON mode standard output is the API's answer, verbatim, or one error object — nothing
a script has to strip; a watch's is one such document per line. In table mode a list is rows, anything else is key–value pairs, and a failure
goes to standard error. Notices — a replayed answer, a next cursor — go to standard error in both.

- `AC-CLI-003.1` (Optional): WHERE `--json` is given THE SYSTEM SHALL write exactly one JSON document to standard output, the response body, an object with an `error` member, or under `api --include` an object with `status`, `headers` and `body`, or, on a watch, one JSON document per line, and every notice to standard error.
- `AC-CLI-003.2` (Optional): WHERE `--fields` names members THE SYSTEM SHALL project each row of a list and any other object to those members, by dotted path, in table and JSON mode alike.
- `AC-CLI-003.3` (Ubiquitous): THE SYSTEM SHALL render a list answer as a row table of its items' scalar members and any other object as a key–value table in table mode, and SHALL report a paged answer's `nextCursor` as a notice.
- `AC-CLI-003.4` (Event-driven): WHEN the API answers an error THE SYSTEM SHALL report its status and message and, where present, each `problems` entry, `requiredPermissions`, `currentVersion` and `exceptionId`, and exit 1.
- `AC-CLI-003.5` (Ubiquitous): THE SYSTEM SHALL exit 0 on success, 1 on a failed call or sign-in, 2 on a usage error, and 3 when a wait ends at its timeout with the work still running.

## Waiting for the outcome

An operation published `completion: LongRunningOperation` answers `202` with a long-running
operation, not its result. With `--wait` the tool follows it to its end and exits by it, so a job
reads one exit code; the start's answer is the first read, so a start already `Succeeded` — a dry
run always is — needs none. What is followed is read off the committed document and off the
answer's own headers, never named per operation. cloud-api's `cloud-api/AC-LRO-002.1`, `cloud-api/AC-LRO-002.4` and `cloud-api/AC-LRO-003.1`
are the contract being followed.

- `AC-CLI-008.1` (Optional): WHERE `--wait` is given and the operation is published `LongRunningOperation`, or is a `GET` answering a long-running operation, THE SYSTEM SHALL read the state from the answer and, while it is `Queued` or `Running`, read `Operation-Location`, else the path just read, again after each answer's `Retry-After` or two seconds, resolving the token again once when a read answers 401, until it is `Succeeded` or `Failed`.
- `AC-CLI-008.2` (Event-driven): WHEN a waited long-running operation ends `Succeeded` THE SYSTEM SHALL render its `result` as the command's answer, name its id where it has one as a notice, and exit 0.
- `AC-CLI-008.3` (Event-driven): WHEN a waited long-running operation ends `Failed` THE SYSTEM SHALL report its `error` as an error answer of the API and exit 1.
- `AC-CLI-008.4` (Unwanted): IF a wait reaches `--timeout`, fifteen minutes unless given, with the work `Queued` or `Running` THEN THE SYSTEM SHALL report that it is still running with its `Operation-Location` and `vion api GET <Operation-Location> --wait`, carry the last answer as `longRunningOperation` in the error object under `--json`, and exit 3.
- `AC-CLI-008.5` (Unwanted): IF a wait is interrupted, or a poll answers an error, THEN THE SYSTEM SHALL report that the work may still be running with its `Operation-Location` and the same pick-up command, and exit 1.
- `AC-CLI-008.6` (Optional): WHERE `--wait` is given and an answer carries `Operation-Location`, or a `GET`'s answer carries `Retry-After`, THE SYSTEM SHALL wait as for a long-running operation whatever the committed document publishes of the operation, reading `Operation-Location`, else the path just read.
- `AC-CLI-008.7` (Unwanted): IF `Operation-Location` names an origin other than the environment's API THEN THE SYSTEM SHALL refuse to follow it before sending the token and exit 1.
- `AC-CLI-008.8` (Event-driven): WHEN an answer carries `Operation-Location` and is rendered without being followed THE SYSTEM SHALL name it as a notice, with `--wait` where `--wait` is absent and with `vion api GET <Operation-Location> --wait` where the wait found no long-running operation in the answer.
- `AC-CLI-008.9` (Optional): WHERE `--wait` is given on an operation published `Response` that is not a `GET` answering a long-running operation, and the answer carries neither `Operation-Location` nor, on a `GET`, `Retry-After`, THE SYSTEM SHALL send and render as without `--wait`.

## Sign-in

A person signs in by device code, once per environment; automation hands a token in or signs in as a
service account. The token sources are tried in one order on every call.

- `AC-CLI-004.1` (Event-driven): WHEN `auth login` runs THE SYSTEM SHALL request a device code from the environment's realm with `client_id` `vion-cli` and the scopes `openid`, `user_impersonation` and `web_mqtt_access`, and SHALL print the verification URL and user code, opening the browser unless `--no-browser`.
- `AC-CLI-004.2` (State-driven): WHILE the token endpoint answers `authorization_pending` or `slow_down` THE SYSTEM SHALL keep polling at the granted interval, five seconds longer after `slow_down`, until a token, a refusal or the code's expiry.
- `AC-CLI-004.3` (Ubiquitous): THE SYSTEM SHALL keep credentials per environment in `credentials.json` under its home directory, readable by the owner only.
- `AC-CLI-004.4` (Ubiquitous): THE SYSTEM SHALL take the bearer token from, in order, `--token -` on standard input, `--client-id` with `--client-secret`, `VION_CLIENT_ID` with `VION_CLIENT_SECRET`, then the stored credentials, refreshing a stored token that expires within thirty seconds with its refresh token.
- `AC-CLI-004.5` (Unwanted): IF no credential source yields a token THEN THE SYSTEM SHALL refuse naming `auth login`, the automation variables and `--token`, and exit 1.
- `AC-CLI-004.6` (Event-driven): WHEN `auth logout` runs THE SYSTEM SHALL remove the environment's stored credentials and say whether any were stored.
- `AC-CLI-004.7` (Event-driven): WHEN `auth status` runs THE SYSTEM SHALL report, per stored environment, the subject, the expiry and whether a refresh token is held, and the source the next call would use.

## Environments and contexts

Two environments are built in; another is defined by its URLs. A context names an environment and
the roots a call fills in; the flags and the `VION_*` variables override it per call.

- `AC-CLI-005.1` (Ubiquitous): THE SYSTEM SHALL know the environments `production` and `test` with their API, realm and broker URLs, and take any other environment's URLs from `context use --env <name> --api-url <url> --auth-url <url>`, with `--broker-url <url>` for its broker, kept in `config.json` under its home directory.
- `AC-CLI-005.2` (Event-driven): WHEN `context use` runs THE SYSTEM SHALL store the named context with its environment and whichever of integrator, tenant, project and platform were given, and make it current.
- `AC-CLI-005.3` (Ubiquitous): THE SYSTEM SHALL resolve the environment and each of integrator, tenant, project and platform from, in order, the command's option, the `VION_ENV`, `VION_API_URL`, `VION_AUTH_URL` and `VION_BROKER_URL` variables, then the current context.
- `AC-CLI-005.4` (Event-driven): WHEN `context list` or `context show` runs THE SYSTEM SHALL print the stored contexts with the current one marked, or the current context with its resolved URLs and values.

## The written commands

`ops` is the tree as a table, offline, the watches included; `ops --live` reads what an
environment serves and diffs it against the committed documents; `can-i` joins the environment's catalog with the caller's own
permissions as the dashboard does; `api` is the raw call for anything the tree does not name.

- `AC-CLI-006.1` (Event-driven): WHEN `ops` runs THE SYSTEM SHALL list, from the committed documents and without a request, every tree command with its operationId, protocol, method, path, effect, completion and flags — a watch with the operation of the event document that sends on its channel, the protocol the event document's servers declare, no method or path, the effect of its channel's registration operation where the channel registers and `Read` otherwise, and completion `Stream` — and with `--all` the `system` operations too.
- `AC-CLI-006.2` (Event-driven): WHEN `ops --live` runs THE SYSTEM SHALL read the environment's two documents and print the operations added, removed or changed against the committed ones — in method, path, `x-vion-authorization` or `x-vion-capability` for the OpenAPI document, and in action, channel, parameters, message names, `x-vion-handedOutBy` or `x-vion-registeredBy` for the event document, never in a channel's address — exiting 0 only when none differ.
- `AC-CLI-006.3` (Event-driven): WHEN `can-i <operationId>` runs THE SYSTEM SHALL decide from the environment's catalog row and the context root's own permissions — a `Root` operation from the root set, a `Filtered` or `Scope` operation at `--project` from the held scope of that project else the root set, any other case allowed, an unlisted operation unknown — and exit 0 when allowed, 1 when denied and 2 when unknown.
- `AC-CLI-006.4` (Event-driven): WHEN `whoami` runs THE SYSTEM SHALL print the caller's identity and memberships with their ids from `me.identity.get`.
- `AC-CLI-006.5` (Event-driven): WHEN `api <METHOD> <path>` runs THE SYSTEM SHALL send that request to the environment's API with the resolved token, the `-f` and `-F` pairs as the query on `GET` and as the JSON body otherwise, the write-safety headers and refusals of the operation whose template the path matches, and render the answer as any command's.
- `AC-CLI-006.6` (Optional): WHERE `api` is given `--no-write-safety` THE SYSTEM SHALL send the method, path, query and body as given, with `Idempotency-Key` and `If-Match` only from `--idempotency-key` and `--if-match`, reading nothing before the request and refusing no `--dry-run`.
- `AC-CLI-006.7` (Optional): WHERE `api` is given `--include` THE SYSTEM SHALL write the answer's status and headers to standard output before the body in table mode, and under `--json` one object with `status`, `headers` and `body` whatever the status, and SHALL refuse `--wait` beside it with exit 2.

## The update check

- `AC-CLI-007.1` (Event-driven): WHEN a command runs on a terminal and the last check is older than a day THE SYSTEM SHALL read the latest release once, cache the result under its home directory, and name a newer version on standard error.
