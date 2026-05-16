# ACOP spec polish

This file tracks remaining work on the ACOP specification.

ACOP core is now at **v1.0** (stable). The items below tracked the
v0.1 → v1.0 promotion and are checked off where done. Remaining work is
either follow-on hosting (publishing schemas under a stable host) or
promotion work for the experimental extensions.

## Required for v1.0 — DONE

- [x] **HTTP+JSON transport binding.** See
  [acop-http-binding.md](./acop-http-binding.md). Defines request/response
  shapes, status codes, and operation paths for every core verb (claim,
  renew, release, complete, post blackboard, create work item, accept
  handoff). Mirrors the conventions in
  [HttpAcopBackend](../../BogDb.Acop.Mcp.Server/Adapters/HttpAcopBackend.cs).
- [x] **RFC 2119 normative language.** [acop.md](./acop.md) and
  [acop-http-binding.md](./acop-http-binding.md) now use MUST / SHOULD /
  MAY in normative clauses. A "Conformance" section is in both documents.
- [x] **State-machine diagrams.** [acop.md § State machines](./acop.md#state-machines)
  documents the `WorkItem.status` and `Claim.claim_status` transition
  tables (required, optional, forbidden) plus ASCII diagrams.
- [x] **Error model.** [acop-http-binding.md § Error model](./acop-http-binding.md#error-model)
  defines the vocabulary and per-code retry guidance; the body shape is
  captured in [acop-errors.schema.json](./acop-errors.schema.json).
- [x] **Authentication and authorization sketch.** See
  [acop-http-binding.md § Authentication and authorization](./acop-http-binding.md#authentication-and-authorization).
  Implementations MUST authenticate workers and scope claims to
  authenticated identity; OIDC bearer tokens are RECOMMENDED.
- [x] **Versioning policy.** See
  [acop.md § Versioning policy](./acop.md#versioning-policy). Documents
  the additive vs breaking distinction, minor-version negotiation rules,
  and per-extension stability labels.
- [x] **Conformance test fixtures.** See [fixtures/](./fixtures/). One
  JSON file per scenario, covering the happy path, conflict, expiry,
  schema violation, authentication, and protocol-version negotiation.

## Strongly recommended — DONE

- [x] **Sequence diagrams.** Worker happy path, claim conflict path, and
  handoff path are in
  [acop-http-binding.md § Sequence diagrams](./acop-http-binding.md#sequence-diagrams).
- [x] **Concurrency and consistency notes.** See
  [acop-http-binding.md § Concurrency and consistency](./acop-http-binding.md#concurrency-and-consistency)
  for race semantics and renewal guidance.
- [x] **Stability labels per extension.** Core, HTTP binding,
  orchestration, compliance, and read-side MCP are each labeled in
  [acop.md § Stability labels per extension](./acop.md#stability-labels-per-extension)
  and on each extension document.
- [x] **Recommended blocker code namespaces.** See
  [acop.md § Blocker code namespaces](./acop.md#blocker-code-namespaces).
  Eight core codes reserved; `x.<vendor>.` prefix open for extensions.

## Nice to have — open

- [ ] A two-page "ACOP at a glance" document for first-time readers.
- [ ] A second backend adapter in `BogDb.Acop.Mcp.Server` to prove the
  protocol is genuinely backend-agnostic (e.g. an in-memory implementation
  for testing).
- [ ] Public hosting of the JSON schemas under the `acop.dev` (or chosen)
  domain so `$id` URLs resolve. The schemas are currently authored against
  `https://acop.dev/schemas/v1.0/...` (and `v0.1/...` for experimental
  extensions) as a placeholder.
- [ ] A reference conformance runner that executes [fixtures/](./fixtures/)
  end-to-end against an implementation. The fixtures themselves are
  runner-agnostic but no runner ships in this repo yet.

## Extension promotion to v1.0 — open

- [ ] Promote `acop-orchestration` from experimental v0.1 to stable v1.0
  (RFC 2119 pass, state machine for gate/lane/stage, conformance
  fixtures).
- [ ] Promote `acop-compliance` from experimental v0.1 to stable v1.0
  (RFC 2119 pass, conformance fixtures, formal exception lifecycle).

## Out of scope for ACOP core v1.0

- A full A2A binding (separate spec workstream).
- Scheduler internals — ACOP defines the coordination contract, not the
  scheduler.
- Source-code semantics for any particular programming language.
