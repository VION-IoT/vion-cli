# Testing conventions

§1–3 are this repo's own: the framework, the seams, the two kinds of fixture. §4–§11 are the
authoring discipline, adapted from dale-sdk's and applied in full whenever a test is written or
rewritten.

## 1. Framework

MSTest, in `Vion.Cli.Test`, which references both `Vion.Cli` and `Vion.Cli.Derive`. A spec id is
cited as `[TestProperty("spec", "AC-CLI-001.1")]` on the test method — the one quoted-literal form
`docs/specs/_corpus.yml` counts.

## 2. The seams — everything a test needs is injected at one composition root

The tool runs through `VionCli.RunAsync(args, CliServices)`. Production composes `CliServices` once,
in `Program.cs`, from the real process; a test composes it from fakes. There is no DI container and
no static state a test must reset. The seams, and what each stands in for:

| seam | production | a test |
| --- | --- | --- |
| `HttpMessageHandler` | the socket handler | `RecordingHandler`: answers canned bodies by method and path, records every request sent (method, URL, headers, body) |
| `Out`, `Error`, `In` (`TextWriter`, `TextWriter`, `TextReader`) | the console streams, UTF-8 | `StringWriter`s and a `StringReader`; a test reads the whole stream, never a line count |
| `HomeDirectory` | `VION_HOME`, else the user profile | a temporary directory, deleted in cleanup — no test reads or writes the developer's `~/.vion` |
| `Environment` (`IReadOnlyDictionary<string, string?>`) | the process environment | a dictionary; `VION_ENV`, `VION_CLIENT_ID`, … are set per test, never through `Environment.SetEnvironmentVariable` |
| `TimeProvider` | `TimeProvider.System` | `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) — expiry, the daily check, the device-code interval |
| `IConsoleShape` (`IsInputTerminal`, `IsErrorTerminal`) | `Console.IsInputRedirected`, `Console.IsErrorRedirected` | a value: the `--yes` refusal and the update notice turn on it |
| `OpenBrowser` (`Func<Uri, bool>`) | the platform's opener | a recording delegate |

A test that needs something these do not give has found a missing seam: add it to `CliServices`,
composed from production too, rather than reflecting into the tool or adding a test-only accessor.
A seam a test overrides is not a test-only accessor when production composes the tool through the
same member from one composition root.

## 3. Two kinds of fixture document

- **The real snapshot**, `openapi/v1.json`, for what is true of the whole tree: the command count,
  the absence of option collisions, every `system` operation absent. A test over it recounts; it
  never carries a number from a doc.
- **Synthetic documents** under `Vion.Cli.Test/Fixtures/`, a few operations each, for a rule the
  real document does not exercise today — a `deprecated` operation, a parameter colliding with a
  global, a `cli.json` entry the document lacks. Each fixture's header comment names the real shape
  it mirrors (an operationId) and what it adds. A rule proven only on a fixture says so in the test's
  class summary.

## 4. A test pins behaviour, not the process that produced it

- **Assert what was sent, not that something was sent.** The recording handler's request has a
  method, a URL, headers and a body; assert the field the criterion is about, whole — a query
  string, an `If-Match` value, a JSON member — not `Contains` on the URL.
- **A "no-throw" assertion is not a test.** Assert the exit code and the stream, both.
- **Assert what did not move, too.** A refusal test proves no request was sent: the handler's
  recorded list is empty.
- **A fix has a test proven red.** Revert the fix, watch the new test fail, restore. Say so in the PR
  description — there is no cheap gate for this, so the claim is the record.
- **Prefer parameterised rows over near-duplicate methods** — `[DataRow]`.

## 5. Coverage — every observable behaviour, not every line

The discriminator is **observability**: a behaviour is something a user, a script or the API can
detect — an exit code, a line on a stream, a request or its absence, a file under the home
directory, a value in a header. A branch that changes nothing observable needs no test. Walk the SUT
statement by statement: a method that resolves a token, builds a request, sends it and renders the
answer is doing four things, each independently deletable. Commonly missed, all observable:

- **A sequence of side effects is a sequence of behaviours** — ask of each call whether deleting it
  changes something a user can see.
- **Both sides of a conditional construction** — a flag given and a flag absent, a context value
  and a missing one, a terminal and a redirected stream.
- **Exception branches that change the outcome** — a different exit code, a skipped request.
- **A side effect that survives a branch** — the notice that goes to standard error in table and in
  JSON mode alike takes a row on each side.

Don't chase line or branch percentages, and don't assert negatives the language already guarantees.

## 6. Enumerate the behaviours before writing the tests

Read the SUT and produce a table of its observable behaviours and the test that will cover each —
**in the response, before any test code**. Rows with no test are the point: list them with the
reason (unobservable, covered elsewhere) rather than omitting them. Derive rows from the SUT and
the spec page, never from the tests already written. Produce it whenever the request is about
*coverage*; skip it when the request names the behaviour.

## 7. A test must be able to fail — the vacuous-test catalogue

If deleting the Arrange leaves the assertion passing, the test asserts what the Act alone produces.

- **Idempotency**: capture the first result and assert the second equals *it* — a minted key is
  asserted present and well-formed, a given key asserted equal to the one given.
- **Substring on a rendering**: `Contains("0 rows")` passes on `"10 rows"`. Where a test
  discriminates two renderings of one field, assert the field whole, or the whole line.
- **The fixture has to be able to carry the observable.** A document with no `versioned` operation
  cannot show the `If-Match` rule, whatever the assertion says.

**Prove every new behavioural test red**: revert the fix or delete the branch, watch it fail,
restore — and **name the mutation in the PR** ("dropped the `Idempotency-Key` header →
`SendMintedIdempotencyKey` fails"). A red-proof belongs to the assertion it ran against; re-prove
after rewriting it.

**A red run is read for the assertion that failed.** A test that fails on its own reddens under
every mutation, so a red summary line proves nothing until the failure message is the claim's own
and the same test is green without the mutation. A runner that filters by `Name~` matches a
`[DataRow]` test by its `DisplayName`, not its method — filter by `FullyQualifiedName~`. A mutation
that does not compile runs nothing. Restore a mutation by its exact reverse edit, never
`git checkout`, which also discards the uncommitted work that file carries. Restoring the source
does not restore the build: rebuild before any later `--no-build` run.

**A criterion that decides between two readings is tested where they part.** The `If-Match`
resolution order is proven with an `--input` document *and* a `--if-match` flag both present, not
with one of them.

**Settle step-versus-field with the mutation.** Assertions that redden under *different* mutations
are different behaviours and belong in different tests; assertions that can only redden together are
one behaviour in one test.

## 8. Naming

Form a sentence with the class name — `[Sut]Should` + `[ExpectedResult][Condition]`:
`RefuseWhenTenantMissing`, `SendIfMatchFromInputDocument`, `ExitTwoOnUnknownOption`.

- **Name the behaviour, not the collaborator.** **Name the condition, not the exception type.**
- **No specifics that churn** — no counts, no format details, no magic numbers; the assertion owns
  the specifics.
- **Outcome, not mechanism.** **No articles or filler** — `RefuseWhenTenantMissing`, never
  `RefuseWhenTheTenantIsMissing`.
- **Numeric suffixes for same-role peers** — `request1`, `request2` — never `first…`, `other…`.
- **`expected`/`actual` are prefixes on a meaningful base noun**, used only when both sides of a
  comparison are variables.

## 9. Structure — Triple-A and its discipline

Every test carries `// Arrange`, `// Act`, `// Assert` — always, even when Arrange is empty.

- `// Act` holds only the call to `VionCli.RunAsync` (or the one SUT method). A conversion applied
  solely for comparability belongs in `// Assert`. When Act and Assert can't separate, `// Act /
  Assert` with `Assert.Throws*` — never `[ExpectedException]`, never `try/catch`.
- **Declare locals next to their use**, ordered by when each value is needed.
- **Inline expected values in the assertion.** Bind an `expected…` local only when a second reader
  needs it.
- **Helpers earn their keep**: a `RecordingHandler` and a `TemporaryHome` live in `TestHelpers/`;
  a single-use helper is inlined; the call to the SUT always stays inline in the test's own
  `// Act`.

One test file per SUT, named and classed `[Sut]Should`, its path mirroring the source path
(`Vion.Cli/Http/RequestBuilder.cs` → `Vion.Cli.Test/Http/RequestBuilderShould.cs`); a criterion
about a whole command's behaviour lives under `Vion.Cli.Test/Commands/`. Order tests to mirror the
SUT's flow. Initialise the SUT inline at field declaration when possible; otherwise
`[TestInitialize]`. Default to **no** `[TestCleanup]` — the temporary home is the one exception,
and it is a disposable.

`[DataRow]` rows are **value variations of one condition** — a parameter selects values, never
structure; an `if` on the parameter means the rows are different scenarios, so split them.
Passthrough tests take **at least two distinct value sets**.

## 10. Test data

- **Don't supply values that aren't asserted on.** `Guid.NewGuid().ToString()` for a string the
  SUT requires but the test doesn't care about; literal carve-outs only for `[DataRow]` constants,
  asserted values and numerics.
- **Never fabricate calendar literals.** Time comes from the `FakeTimeProvider`.
- **Never a real token, host or id from an environment.** A fixture token is a string the test
  mints; the fixture's host is `https://api.example.test`.

## 11. Citing spec ids

A test that proves an acceptance criterion carries the id as `[TestProperty("spec", "AC-CLI-…")]`;
`docs/spec-process.md` § IDs & EARS says what a citation proves and when a criterion stays `GAP`.
Read the criterion's text against the assertion before adding the tag, whoever asked for it: the
gate checks that the id exists, not that its sentence states what the test proves.

**A premise test cites no id, by design.** A test that pins an implementation premise — the
emitted global-option list equal to the tool's own, the fixture document parsing — has no
user-observable requirement to hang itself on; its class summary says so, and `spec-trace` never
sees it. Never mint a criterion to hold a premise test.
