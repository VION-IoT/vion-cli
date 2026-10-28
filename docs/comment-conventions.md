# Comment conventions

How inline comments are written in this repo. Read before writing or reworking a `//` or `/* */`
comment. This doc is about comments inside method bodies and above private members; a `<summary>`
on a public member follows the same rules in XML form. dale-sdk's text, adapted from mesh's comment
conventions; the journal's most repeated defect class there is prose asserting something false
about the code, and every rule here exists to make a comment cheap to verify.

## Comment the why, never the what

The code already says what it does. A comment earns its place only where a reader who understands
the code would still ask *why is it like this* — a deliberate trade-off, a constraint imposed from
outside, a failure mode that motivated the shape. Everything else is noise that goes stale.

```csharp
// The root parameters are filled from the context and never derived as options: a derived
// --tenant-id beside the global --tenant would be two spellings of one value on every tenant
// command, and a script that sets both would have to be told which one wins.
```

A comment like that survives a rewrite of the code below it, because it explains a decision rather
than a line.

## A comment is a claim — it carries the same burden as code

Reviews keep finding **load-bearing comments that are false**: an ordering justified by an event no
handler observes, an invariant asserted two lines above a call that violates it, advice still given
for a reason that stopped being true. Before writing a comment that states a mechanism, **verify the
mechanism the way you would verify code** — read the implementation it describes, not the
neighbouring prose. A comment describing a guard is not evidence the guard exists. When a change
falsifies a comment elsewhere, fixing that comment is part of the change.

## Build the reasoning up in order

A comment that needs more than one sentence is an explanation, and an explanation has an order:
the setup a reader needs, then the problem it creates, then what the code does about it. Each
sentence readable knowing only the ones before it. State cause before consequence.

## Terse means fewer ideas, not fewer words per idea

Cut whole points that don't need making. Do not cut the connective tissue inside a point that does —
a compressed clause standing in for a sentence is shorter to write and much slower to read. If a
phrase can only be understood by someone who already knows the answer, it is not terse, it is a
reminder — and the reader it was written for does not need it.

## Explain the mechanism, not the domain

The reader is looking at this code and needs to follow *it*. Reach for the surrounding system (the
API, Keycloak, a shell) only when the mechanism genuinely cannot be understood without it.

## Name the concrete failure

"Fails loudly", "handles the error", "would be unsafe" say nothing a reader can check against the
code. Say what actually happens and what it prevents: *a 404 without a body is the host's own
answer for a route it does not have, so rendering it as a missing resource would send the user
looking for an id* — not *this avoids confusion*.

## No history, no tickets, no numbers that rot

No issue keys, no RFC or change-doc references, no "now"/"new"/before-after framing, no version
numbers — git and the archived change docs own the history, and a comment describes **today**. The
one exception: a user-visible workaround may cite the `VION-nn` it waits on, so it can be retired
when the answer ships.

## Write it impersonally

State what the code does, declaratively. No `we`, `our`, or `you`.

## Form

- More than one line goes in a `/* */` block (or a `//` run) directly above the member, indented
  with it; blank lines separate paragraphs.
- A single line goes at the site it explains.
- Full sentences with terminal punctuation; a colon-introduced fragment is acceptable for a
  one-line comment on a constant.

## Read it back before handing it over

Re-read the finished comment for: a comma before `and`/`but` joining independent clauses; parallel
forms after `rather than` / `instead of`; comparisons that name the wrong noun; sentences over
about thirty words (split them) — and, above all, whether every claim in it is one the code below
still makes true.
