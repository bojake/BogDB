# ACOP Conformance Fixtures (v1.0)

This directory contains request/response fixtures that exercise every
core ACOP verb defined in
[acop-http-binding.md](../acop-http-binding.md). They are intended to be
executable against any HTTP+JSON ACOP implementation as a baseline
conformance suite.

## File naming

Each fixture is a single JSON object with the shape:

```json
{
  "name": "claim-grant.success",
  "verb": "claim_work_item",
  "request": {
    "method": "POST",
    "path": "claims",
    "headers": {
      "Content-Type": "application/json",
      "Authorization": "Bearer <test-token>"
    },
    "body": { "...": "..." }
  },
  "expected_response": {
    "status": 201,
    "headers": {
      "Content-Type": "application/json"
    },
    "body_match": {
      "kind": "subset",
      "value": { "...": "..." }
    }
  }
}
```

### `body_match` semantics

- `kind: "subset"` — the actual response body MUST be a JSON object that
  contains every key from `value` with equal values (recursively).
  Implementations MAY return additional fields.
- `kind: "exact"` — the actual response body MUST equal `value` exactly.
- `kind: "schema"` — the actual response body MUST validate against the
  JSON Schema in `value`.

When `kind` is omitted, runners MUST treat it as `subset`.

### Variables

Some fixtures reference values that depend on prior calls (e.g. a
`claim_id` returned by a grant fixture). These appear as
`${variable_name}` strings and MUST be substituted by the runner from
the response of a previously-executed fixture.

## Fixture inventory

| File                              | Verb              | Scenario |
| --------------------------------- | ----------------- | -------- |
| `claim-grant.success.json`        | claim_work_item   | First grant succeeds. |
| `claim-grant.conflict.json`       | claim_work_item   | Second worker collides on an already-claimed work item. |
| `claim-renew.success.json`        | renew             | Owner extends the lease. |
| `claim-renew.unauthorized.json`   | renew             | Non-owner attempts to renew. |
| `claim-renew.expired.json`        | renew             | Expired claim cannot be renewed. |
| `claim-release.success.json`      | release           | Owner releases the claim. |
| `claim-release.idempotent.json`   | release           | Release on an already-released claim returns 200. |
| `claim-complete.success.json`     | complete          | Completion with required evidence. |
| `claim-complete.no-evidence.json` | complete          | Completion missing required evidence is rejected. |
| `work-item-create.success.json`   | create_work_item  | New work item inserted. |
| `work-item-create.idempotent.json`| create_work_item  | Re-submitting the same work item returns 200. |
| `work-item-create.schema-violation.json` | create_work_item | Rejected with schema_violation. |
| `accept-handoff.success.json`     | accept_handoff    | Downstream consumer accepts. |
| `accept-handoff.not-found.json`   | accept_handoff    | Unknown work item returns 404. |
| `blackboard-post.success.json`    | post_blackboard   | New entry appended. |
| `blackboard-post.with-supersede.json` | post_blackboard | Entry supersedes a prior one. |
| `protocol-version.rejected.json`  | any               | Unsupported protocol_version is rejected. |
| `auth.unauthenticated.json`       | any               | Missing credential is rejected with 401. |

## Running the suite

A reference runner is out of scope for v1.0 (it lives outside the spec
repo). Implementations are encouraged to ship their own runner that walks
the inventory above in dependency order:

1. `work-item-create.success` (seeds a work item)
2. `claim-grant.success` (binds a claim)
3. `claim-renew.success`
4. `claim-complete.success`
5. `accept-handoff.success`

Independent scenarios (conflict, unauthorized, expired, schema violation,
etc.) MAY be run in any order against a freshly-seeded state.
