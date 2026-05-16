# ACOP HTTP+JSON Transport Binding v1.0

## Status

Stability: **stable** (core).

This document is normative. It defines how ACOP core verbs (claim, renew,
release, complete, create work item, accept handoff, post blackboard) are
transported over HTTP+JSON. Implementations of ACOP that expose an HTTP
surface MUST conform to this binding.

ACOP is transport-neutral in principle. Other bindings (e.g. A2A messages,
direct stdio JSON-RPC via MCP) MAY exist and MUST preserve the same verb
semantics, but only the HTTP+JSON binding is normative at v1.0.

## Conventions

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY, and
OPTIONAL in this document are to be interpreted as described in
[RFC 2119](https://www.rfc-editor.org/rfc/rfc2119) and
[RFC 8174](https://www.rfc-editor.org/rfc/rfc8174) when, and only when,
they appear in all capitals.

### Media type

All request bodies and all response bodies (including error bodies) MUST be
encoded as `application/json; charset=utf-8`. Implementations MUST send
`Content-Type: application/json` on every request and response that carries
a body. Clients SHOULD send `Accept: application/json` on every request.

### Base path

The binding defines paths relative to a server-chosen base URL. The base
URL is implementation-defined and is configured by the client (the
reference [HttpAcopBackend](../../BogDb.Acop.Mcp.Server/Adapters/HttpAcopBackend.cs)
takes it as a constructor argument or `ACOP_BACKEND_BASE_URI` env var).

All paths in this binding MUST be interpreted as relative to that base
URL and MUST NOT include a leading slash so they compose correctly with
base URLs that contain a sub-path.

### Identifier escaping

Identifiers that appear as URL path segments (`{claim_id}`,
`{work_item_id}`) MUST be percent-encoded by the client and MUST be
percent-decoded by the server. The reference implementation uses
`Uri.EscapeDataString` for this purpose.

### Time

All timestamps in request and response bodies MUST be ISO 8601 strings in
UTC ending in `Z` (e.g. `2026-05-16T19:13:02Z`). Implementations MUST NOT
emit local-time offsets.

### Protocol version

Every request body MUST carry `"protocol_version": "acop/1.0"` (or a later
version the server accepts; see [versioning policy](./acop.md#versioning-policy)).
A server that receives an unsupported `protocol_version` MUST respond
`400 Bad Request` with the `unsupported_protocol_version` error code.

### Idempotency

Any request that creates or mutates state SHOULD accept an optional
`Idempotency-Key` request header. When set, the server MUST either replay
the previously stored response (and original status code) for that key or
return the result of the original processing. Idempotency keys SHOULD have
a server-defined retention of at least 24 hours.

`acop_create_work_item` is naturally idempotent on `work_item_id` and MUST
treat repeated identical requests as success. Other verbs are not
idempotent unless the client supplies `Idempotency-Key`.

## Verb table

| Verb              | Method | Path                                  | Success status |
| ----------------- | ------ | ------------------------------------- | -------------- |
| Claim work item   | POST   | `claims`                              | `201 Created`  |
| Renew claim       | POST   | `claims/{claim_id}/renew`             | `200 OK`       |
| Release claim     | POST   | `claims/{claim_id}/release`           | `200 OK`       |
| Complete claim    | POST   | `claims/{claim_id}/complete`          | `200 OK`       |
| Create work item  | PUT    | `work-items`                          | `200 OK` / `201 Created` |
| Accept handoff    | POST   | `work-items/{work_item_id}/accept`    | `200 OK`       |
| Post blackboard   | POST   | `blackboard`                          | `201 Created`  |

A server MAY expose additional read endpoints (e.g. `GET work-items/{id}`)
but those are out of scope for the v1.0 binding; the read surface is owned
by a separate MCP read-side server in the reference architecture.

## Verbs in detail

### Claim work item

Submit a [`ClaimIntent`](./acop.md#claimintent) and receive a granted
[`Claim`](./acop.md#claim).

- **Method / Path:** `POST claims`
- **Request body:** the `ClaimIntent` object, optionally embedded under a
  top-level `claim_intent` key. The flat form (fields at the top level)
  MUST be accepted; the nested form (`{"claim_intent": {...}}`) SHOULD be
  accepted for forward compatibility.
- **Success response (`201 Created`):** the granted `Claim` plus the
  resulting `work_item.status`.

Request:

```http
POST /claims HTTP/1.1
Content-Type: application/json
Authorization: Bearer <token>
Idempotency-Key: 6f1c8d4e-...

{
  "protocol_version": "acop/1.0",
  "work_item_id": "work:refactor:resource-hub-http",
  "worker_agent_uid": "worker-http",
  "requested_ttl_seconds": 1800,
  "scope": "owned_paths"
}
```

Success response:

```http
HTTP/1.1 201 Created
Content-Type: application/json
Location: /claims/claim:worker-http:001

{
  "protocol_version": "acop/1.0",
  "claim": {
    "claim_id": "claim:worker-http:001",
    "work_item_id": "work:refactor:resource-hub-http",
    "worker_agent_uid": "worker-http",
    "granted_at_utc": "2026-05-16T19:13:02Z",
    "expires_at_utc": "2026-05-16T19:43:02Z",
    "claim_status": "active"
  },
  "work_item_status": "claimed"
}
```

Failure: any other worker already holds an active claim ⇒ `409 Conflict`
with `claim_conflict`.

### Renew claim

Extend the lease window on an active claim.

- **Method / Path:** `POST claims/{claim_id}/renew`
- **Request body:**

```json
{
  "protocol_version": "acop/1.0",
  "worker_agent_uid": "worker-http",
  "requested_ttl_seconds": 1800,
  "progress_summary": "extracted HTTP seam, running targeted tests"
}
```

- **Success response (`200 OK`):** the updated `Claim` with a new
  `expires_at_utc`.

A server MUST reject a renew on a non-`active` claim with `409 Conflict`
and one of `lease_expired`, `claim_released`, or `claim_revoked`. A server
MUST reject a renew from a `worker_agent_uid` that does not match the
claim owner with `403 Forbidden` and `unauthorized_worker`.

### Release claim

Explicitly relinquish a claim without completing the work.

- **Method / Path:** `POST claims/{claim_id}/release`
- **Request body:**

```json
{
  "protocol_version": "acop/1.0",
  "worker_agent_uid": "worker-http",
  "release_reason": "blocked_on_review"
}
```

- **Success response (`200 OK`):** the `Claim` with `claim_status:
  released` and the resulting `work_item.status` (typically `ready` or
  `blocked`).

Release is idempotent: a release against an already-released claim MUST
return `200 OK` with the existing released claim and MUST NOT raise a
conflict.

### Complete claim

Assert that the worker has produced the result the work item required and
attached the required completion evidence.

- **Method / Path:** `POST claims/{claim_id}/complete`
- **Request body:**

```json
{
  "protocol_version": "acop/1.0",
  "worker_agent_uid": "worker-http",
  "completion_evidence": {
    "validation_results": ["validation:http-targeted-tests"],
    "artifact_ids": ["artifact:http-impl"]
  }
}
```

- **Success response (`200 OK`):** the `Claim` with `claim_status:
  released` and the updated `work_item.status` (typically `completed`).

A server MUST reject a complete when required
[`ValidationRequirement`](./acop.md#validationrequirement) evidence is
missing with `409 Conflict` and `validation_required`.

### Create work item

Upsert a work item (lead/supervisor agents seeding new coordination
units).

- **Method / Path:** `PUT work-items`
- **Request body:** an ACOP core document with the `work_item` populated
  and any associated `blockers`, `operations`, `artifacts`, or
  `validation_requirements`. See [acop_examples.md](./acop_examples.md).
- **Success response:** `201 Created` on first insertion, `200 OK` on
  idempotent re-submission with no change, `200 OK` with the updated work
  item on accepted change.

`work_item.work_item_id` MUST be stable. Servers MUST reject a request
whose body carries an `additionalProperties` violation against
[`acop.schema.json`](./acop.schema.json) with `400 Bad Request` and
`schema_violation`.

### Accept handoff

Mark a work item as accepted by a downstream consumer; gates that depend
on this acceptance may then release.

- **Method / Path:** `POST work-items/{work_item_id}/accept`
- **Request body:**

```json
{
  "protocol_version": "acop/1.0",
  "accepting_agent_uid": "lead-01",
  "acceptance_notes": "review acknowledged, ready to consume"
}
```

- **Success response (`200 OK`):** the updated `work_item.status`
  (typically `consumed`) and the IDs of any downstream work items that
  newly became `ready` as a result.

### Post blackboard

Append a [`BlackboardEntry`](./acop.md#blackboardentry) to a work item.

- **Method / Path:** `POST blackboard`
- **Request body:** the `BlackboardEntry` object. The `entry_id` MAY be
  omitted; the server MUST generate one if missing.
- **Success response (`201 Created`):** the stored entry, including its
  `entry_id`.

Blackboard entries are append-only. Servers MUST NOT permit mutation of an
existing entry. Logical supersession is expressed by posting a new entry
with `supersedes_entry_ids` populated.

## Error model

All error responses MUST be encoded as JSON bodies with the following
shape:

```json
{
  "protocol_version": "acop/1.0",
  "error": {
    "code": "claim_conflict",
    "message": "work item is currently claimed by worker-other-01",
    "retry_after_seconds": 60,
    "details": {
      "work_item_id": "work:refactor:resource-hub-http",
      "current_claim_id": "claim:worker-other-01:002"
    }
  }
}
```

Required fields:

- `code` — one of the codes in the [error code vocabulary](#error-code-vocabulary).
  Servers MAY add implementation-specific codes but MUST namespace them
  with a vendor prefix (e.g. `acmecorp.policy_rejected`).
- `message` — a human-readable explanation. MUST NOT contain secrets.

Optional fields:

- `retry_after_seconds` — when present, the client SHOULD wait at least
  this many seconds before retrying. When the server returns HTTP
  `Retry-After`, the two values MUST agree.
- `details` — a free-form object with code-specific structured context.

### Error code vocabulary

These codes are reserved by ACOP core. Implementations MUST use them with
the semantics defined here.

| Code                            | HTTP  | Retryable?            | Meaning |
| ------------------------------- | ----- | --------------------- | ------- |
| `claim_conflict`                | 409   | yes, after backoff    | Another worker already holds an active claim on this work item. |
| `lease_expired`                 | 409   | no, re-claim required | The claim's `expires_at_utc` has passed; the lease is no longer valid. |
| `claim_released`                | 409   | no                    | The targeted claim was already released. |
| `claim_revoked`                 | 409   | no                    | The targeted claim was revoked by orchestration middleware. |
| `unauthorized_worker`           | 403   | no                    | The authenticated identity is not the claim owner. |
| `work_item_not_found`           | 404   | no                    | No work item with that ID exists. |
| `work_item_superseded`          | 409   | no                    | The work item was superseded by a newer one; consume that instead. |
| `work_item_cancelled`           | 409   | no                    | The work item was cancelled and is no longer actionable. |
| `work_item_not_actionable`      | 409   | yes, on state change  | The work item is not in a state that permits the requested transition. |
| `validation_required`           | 409   | yes, after evidence   | Completion attempted but required validation evidence is missing. |
| `gate_blocked`                  | 409   | yes, after gate opens | A policy or orchestration gate is blocking the requested transition. |
| `unsupported_protocol_version`  | 400   | no                    | The client's `protocol_version` is not accepted by this server. |
| `schema_violation`              | 400   | no                    | The request body does not validate against the ACOP schema. |
| `unauthenticated`               | 401   | no                    | No valid credential was presented. |
| `forbidden`                     | 403   | no                    | Authenticated but not authorized for this resource. |
| `rate_limited`                  | 429   | yes, after `Retry-After` | Server-imposed rate limit. |
| `internal_error`                | 500   | yes, with backoff     | Unexpected server failure. |
| `backend_unavailable`           | 503   | yes, after `Retry-After` | A downstream coordination authority is unreachable. |

Clients SHOULD implement exponential backoff for retryable codes with a
base of at least 500ms and a cap of at least 30s. When the server
specifies `retry_after_seconds` or the `Retry-After` header, the client
MUST honor it as the minimum wait.

### Error machine-readable schema

The error body shape is normative and is captured in
[acop-errors.schema.json](./acop-errors.schema.json).

## Authentication and authorization

The HTTP+JSON binding has the following authentication and authorization
requirements:

1. **Implementations MUST authenticate every request that mutates state**
   (all verbs in this document are mutating). Unauthenticated requests
   MUST be rejected with `401 Unauthenticated`.
2. **Implementations MUST associate every authenticated request with a
   stable worker identity** and MUST surface that identity as the
   `worker_agent_uid` (or `accepting_agent_uid`, `author_agent_uid`) of
   any state it writes. If the body carries a `worker_agent_uid` that
   does not match the authenticated identity, the server MUST reject
   with `403 forbidden` unless the authenticated identity is explicitly
   authorized to act on behalf of others (an orchestration middleware
   role).
3. **Claims MUST be scoped to their owning identity.** A renew, release,
   or complete request MUST come from the same identity that holds the
   claim, or from an authorized orchestration role, or be rejected with
   `unauthorized_worker`.

The binding RECOMMENDS — but does not require — OIDC bearer tokens
presented in the `Authorization: Bearer` header. Implementations MAY use
mTLS, signed JWTs, or any other scheme that meets the requirements above.
The reference [HttpAcopBackend](../../BogDb.Acop.Mcp.Server/Adapters/HttpAcopBackend.cs)
defaults to bearer tokens for this reason.

The spec deliberately does not mandate a specific identity provider; that
is a deployment choice.

## Sequence diagrams

### Worker happy path

```
+--------+                +-----------------+               +----------+
| Worker |                | ACOP HTTP API   |               | Storage  |
+--------+                +-----------------+               +----------+
    |                              |                              |
    | POST /claims                 |                              |
    |  ClaimIntent                 |                              |
    |----------------------------->|                              |
    |                              | check current claim          |
    |                              |----------------------------->|
    |                              |   no active claim            |
    |                              |<-----------------------------|
    |                              | persist Claim                |
    |                              |----------------------------->|
    | 201 Claim (active)           |                              |
    |<-----------------------------|                              |
    |                              |                              |
    | (work happens)               |                              |
    |                              |                              |
    | POST /claims/{id}/renew      |                              |
    |  progress_summary            |                              |
    |----------------------------->|                              |
    | 200 Claim (new expires)      |                              |
    |<-----------------------------|                              |
    |                              |                              |
    | POST /claims/{id}/complete   |                              |
    |  completion_evidence         |                              |
    |----------------------------->|                              |
    |                              | validate evidence            |
    |                              |----------------------------->|
    | 200 Claim (released)         |                              |
    |     work_item.status=        |                              |
    |     "completed"              |                              |
    |<-----------------------------|                              |
```

### Claim conflict path

```
+----------+              +-----------------+              +----------+
| Worker B |              | ACOP HTTP API   |              | Worker A |
+----------+              +-----------------+              +----------+
    |                              |                            |
    |                              |  POST /claims (granted)    |
    |                              |<---------------------------|
    |                              |                            |
    | POST /claims                 |                            |
    |  ClaimIntent                 |                            |
    |----------------------------->|                            |
    | 409 claim_conflict           |                            |
    |   current_claim_id=...       |                            |
    |   retry_after_seconds=60     |                            |
    |<-----------------------------|                            |
    |                              |                            |
    | (backoff)                    |                            |
    | (Worker A completes / releases)                           |
    |                              |  POST /claims/.../complete |
    |                              |<---------------------------|
    |                              |                            |
    | POST /claims (retry)         |                            |
    |----------------------------->|                            |
    | 201 Claim (active)           |                            |
    |<-----------------------------|                            |
```

### Handoff path

```
+----------------+        +-----------------+        +---------------+
| Producer Agent |        | ACOP HTTP API   |        | Consumer Agent|
+----------------+        +-----------------+        +---------------+
    |                            |                          |
    | PUT /work-items            |                          |
    |  work_item (consumer)      |                          |
    |--------------------------->|                          |
    | 201 work_item (status=ready)                          |
    |<---------------------------|                          |
    |                            | (consumer polls/listens) |
    |                            |  POST /claims            |
    |                            |<-------------------------|
    |                            |  201 Claim               |
    |                            |------------------------->|
    |                            |                          |
    |                            |  POST /claims/{id}/complete
    |                            |<-------------------------|
    |                            |  200 work_item.status=completed
    |                            |------------------------->|
    |                            |                          |
    |                            |  POST /work-items/{id}/accept
    |                            |<-------------------------|
    |                            |  200 work_item.status=consumed
    |                            |     released: [<downstream ids>]
    |                            |------------------------->|
```

## Concurrency and consistency

Claim ownership is a strongly-consistent property: at most one
`claim_status: active` claim MUST exist per `work_item_id` at any instant.

When two workers race for the same claim, the server MUST grant exactly
one and respond `409 claim_conflict` to the other. Both workers MUST be
able to determine which won by inspecting the granted claim's
`worker_agent_uid` — for the loser, that field MUST be visible in the
`details.current_claim_id` or via a subsequent read.

When a worker's claim expires under contention (i.e. the worker takes
longer than the TTL and another worker is waiting), the server MUST:

1. Treat the claim as `claim_status: expired` at the instant
   `expires_at_utc` passes.
2. Reject any subsequent renew/release/complete from the original worker
   with `lease_expired`.
3. Allow the next claim request from any worker to succeed normally.

A worker that intends to hold a claim across long-running work SHOULD
renew before expiration, not at expiration. The exact renewal window is
implementation-defined; renewing at 50–80% of the TTL is RECOMMENDED.

## Conformance

An HTTP+JSON ACOP implementation conforms to this binding if and only if:

1. Every verb in [the verb table](#verb-table) is reachable at the
   specified method and path.
2. Every request body validates against the relevant ACOP core schema
   ([acop.schema.json](./acop.schema.json)) when `additionalProperties`
   is enforced.
3. Every error response uses the [error body shape](#error-model) and an
   error code from [the vocabulary](#error-code-vocabulary) (or a
   vendor-prefixed extension code).
4. The state-transition rules in [acop.md](./acop.md#state-machines) are
   honored for `WorkItem.status` and `Claim.claim_status`.
5. Authentication is enforced per the
   [auth section](#authentication-and-authorization).
6. The conformance test fixtures in [fixtures/](./fixtures/) pass when
   executed against the implementation. A subset of fixtures is OPTIONAL
   if the implementation explicitly opts out of an extension; opting out
   of any core verb makes the implementation non-conformant.
