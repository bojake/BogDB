# ACOP v1.0

## Status

Stability: **stable**.

ACOP stands for `Agentic Code Orchestration Protocol`. This document is
normative for the ACOP core contract. Extensions (orchestration,
compliance) are versioned and labeled independently.

## Conventions

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, RECOMMENDED, MAY, and
OPTIONAL in this document are to be interpreted as described in
[RFC 2119](https://www.rfc-editor.org/rfc/rfc2119) and
[RFC 8174](https://www.rfc-editor.org/rfc/rfc8174) when, and only when,
they appear in all capitals. Lowercase appearances of these words carry
their ordinary English meaning and are not normative.

Where the prose says "the spec" or "this protocol," it refers to ACOP
core as defined by this document and [acop.schema.json](./acop.schema.json).

## Purpose

ACOP defines the coordination semantics needed for multi-agent software
development work that are not fully covered by:

- `MCP`, which standardizes model-to-tool/context access
- `A2A`, which standardizes agent-to-agent interoperability and message
  exchange

ACOP is not intended to replace either of those.

The intended stack is:

- `MCP` for tools, resources, and context
- `A2A` for inter-agent communication and delegation
- `ACOP` for code-work coordination semantics layered on top

## Problem statement

Agentic code generation needs stronger coordination than "send another
agent a message" and stronger structure than "call a tool."

The missing layer is responsible for:

- what work exists
- who should pick it up
- whether it is ready
- what blocks it
- whether it has been claimed
- how long a claim is valid
- what artifacts, branches, or files are in scope
- what validation and review requirements apply
- whether work has been completed, consumed, or superseded

Without that layer, agents step on each other during cross-agent
development work.

## Design goals

- strong work identity
- explicit readiness and blocker state
- clean claim / lease hooks
- repo / branch / artifact aware
- review and validation aware
- transport-neutral semantics
- compatible with A2A and MCP rather than competitive with them

## Non-goals

ACOP does not define:

- a replacement for MCP tool/resource transport
- a replacement for A2A messaging
- source-code semantics for one programming language
- storage implementation details
- scheduler internals

ACOP defines the coordination contract, not the full runtime.

## Layering

### Producers

A planning producer (for example, a refactor planner or work-decomposition
service) emits:

- work items
- plans
- handoffs
- blockers
- readiness evidence
- artifact and operation identities

### Query and transport

A read-side MCP server projects coordination state and exposes:

- resources
- artifact reads
- indexed work lookup
- filtered handoff queries

### Coordination owner

An orchestration middleware layer owns:

- claims
- leases
- arbitration
- retries
- stale-work recovery
- pickup and completion transitions

### Workers

Coding agents, review agents, and validation agents consume ACOP work
items and act on them.

## Core concepts

### `WorkItem`

The top-level unit of coordinated code work.

Required fields (a conforming `WorkItem` MUST carry all of these):

- `work_item_id`
- `protocol_version`
- `work_kind`
- `created_at_utc`
- `producer`
- `status`
- `priority`
- `actionability_score`

Recommended fields (a conforming producer SHOULD emit these when the
information is available):

- `title`
- `summary`
- `target_repo_id`
- `target_workspace_root`
- `target_branch`
- `target_worktree`
- `source_handoff_id`
- `correlation_id`

`work_item_id` MUST be stable across re-publishes of the same logical
work item; producers MUST NOT reuse a `work_item_id` for different work.
Implementations MUST treat `work_item_id` as opaque.

### `WorkKind`

The `work_kind` field MUST be one of the values defined in
[acop.schema.json](./acop.schema.json):

- `implement`
- `refactor`
- `review`
- `validate`
- `repair`
- `handoff_followup`
- `merge_prepare`

Implementations MAY accept additional vendor-prefixed kinds (e.g.
`acmecorp.deploy_prepare`) but MUST NOT redefine the core values.

### `Status`

The `status` field MUST be one of:

- `ready` — immediately pickable
- `requires_attention` — human or lead-agent decision likely needed, but
  still relevant
- `blocked` — not pickable until blocker resolution
- `claimed` — reserved by one worker under a live claim
- `in_progress` — actively being executed
- `completed` — worker asserts done
- `accepted` — downstream accepted the work
- `released` — claim released without completion (work is back on the
  market)
- `consumed` — downstream system accepted output and no further pickup
  should occur
- `cancelled` — abandoned intentionally
- `superseded` — replaced by a newer work item

The allowed transitions between these states are normative and are defined
in [state machines](#state-machines).

### `Blocker`

Represents why work is not directly actionable.

Required fields:

- `blocker_code`
- `severity`
- `summary`

Recommended fields:

- `artifact_id`
- `operation_id`
- `depends_on_work_item_id`
- `depends_on_external_decision`
- `suggested_resolution`

`blocker_code` SHOULD come from one of the
[reserved namespaces](#blocker-code-namespaces).

### `Operation`

A planned step in a work item.

Required fields:

- `operation_id`
- `kind`
- `description`

Recommended fields:

- `depends_on_operation_ids`
- `outcome_target`
- `artifact_ids`
- `validation_focus`

Common `kind` values (non-exhaustive):

- `extract_seam`
- `materialize_contract`
- `materialize_core`
- `apply_patch`
- `run_tests`
- `request_review`

### `Artifact`

A stable object the work item refers to.

Required fields:

- `artifact_id`
- `artifact_kind`
- `artifact_role`

Recommended fields:

- `resource_uri`
- `repo_relative_path`
- `namespace_hint`
- `symbol_hint`
- `readiness`

Common artifact kinds: source file, patch preview, contract interface,
generated scaffold, registration preview, review report.

### `ValidationRequirement`

Describes the evidence required before a work item can complete.

Required fields:

- `requirement_id`
- `kind`
- `summary`

A server MUST reject a `complete` transition for a work item whose
declared `ValidationRequirement`s lack matching evidence in the request
body with the `validation_required` error code.

### `BlackboardEntry`

Represents a shared coordination note or intermediate artifact in a
multi-agent development flow.

Blackboard coordination belongs in ACOP core because it is a general
collaboration primitive rather than a domain-specific extension.

Required fields:

- `entry_id`
- `work_item_id`
- `entry_kind`
- `author_agent_uid`
- `created_at_utc`
- `status`

Recommended fields:

- `summary`
- `details`
- `artifact_ids`
- `operation_ids`
- `confidence`
- `supersedes_entry_ids`

`entry_kind` MUST be one of `finding`, `hypothesis`, `partial_result`,
`risk`, `decision`, `constraint`, `review_request`, or `integration_note`.

Blackboard entries MUST be treated as append-only. Implementations MUST
NOT mutate a stored entry; logical revision MUST be expressed by posting
a new entry with `supersedes_entry_ids` populated.

Design intent:

- the blackboard is the shared development surface where agents can
  publish partial understanding
- it should support incremental progress without forcing early completion
  claims
- it should let lead and worker agents coordinate around evidence instead
  of only around chat

## Claim and lease hooks

ACOP defines claim / lease semantics so that cross-agent code work can
detect and resolve collisions. The lease authority itself MUST live in
orchestration middleware; producers MUST NOT silently become the lease
owner.

### `ClaimIntent`

Represents an attempt by a worker to take ownership of a work item.

Required fields:

- `claim_intent_id`
- `work_item_id`
- `worker_agent_uid`
- `requested_at_utc`
- `requested_ttl_seconds`
- `scope`

### `Claim`

Represents granted temporary ownership.

Required fields:

- `claim_id`
- `work_item_id`
- `worker_agent_uid`
- `granted_at_utc`
- `expires_at_utc`
- `claim_status`

`claim_status` MUST be one of `active`, `released`, `expired`, or
`revoked`. The allowed transitions between these states are normative
and are defined in [state machines](#state-machines).

### `LeaseHeartbeat`

Optional liveness updates from the worker or middleware. Implementations
MAY use heartbeats to inform stale-work recovery; receipt of a heartbeat
MUST NOT by itself extend `expires_at_utc` — use the renew verb for that.

Required fields:

- `claim_id`
- `observed_at_utc`

Recommended fields:

- `progress_status`
- `progress_summary`

### Claim design rules

- ACOP MUST support claim / lease semantics because cross-agent code work
  frequently collides.
- Claim truth MUST live in orchestration middleware, not in producers and
  not in MCP.
- Producers MAY emit claimable work and MCP MAY expose claim state, but
  neither MUST become the lease owner.

## State machines

The state machines below are normative. A conforming implementation MUST
implement every transition labeled MUST, MAY implement every transition
labeled MAY, and MUST NOT permit any transition that is neither.

### `WorkItem.status`

```
                       +---------+
                       | (start) |
                       +----+----+
                            |
                            v
                     +--------------+
        +----------> |    ready     | <--+
        |            +------+-------+    |
        |                   |            |
        | (blocker resolved)|            | (release)
        |                   v            |
   +----+-------+      +---------+       |
   |  blocked   |<-----+ claimed +-------+
   +-----^------+      +----+----+
         |                  |
         | (new blocker)    |  (work starts)
         |                  v
         |             +-------------+
         +-------------+ in_progress |
                       +------+------+
                              |
              (worker asserts |
                done & evidence)
                              v
                       +-------------+
                       |  completed  |
                       +------+------+
                              |
                  (downstream |
                accept handoff)
                              v
                       +-------------+
                       |   accepted  |
                       +------+------+
                              |
                              v
                       +-------------+
                       |  consumed   |
                       +-------------+

  Terminal escapes (allowed from any non-terminal state):

    any non-terminal  --(producer/lead cancels)-->  cancelled
    any non-terminal  --(replaced by new work)----> superseded
```

Required transitions (MUST be supported):

| From               | To                  | Trigger                                  |
| ------------------ | ------------------- | ---------------------------------------- |
| (start)            | `ready`             | producer emits an actionable work item   |
| (start)            | `blocked`           | producer emits with one or more blockers |
| (start)            | `requires_attention`| producer signals human/lead decision     |
| `ready`            | `claimed`           | claim granted                            |
| `claimed`          | `in_progress`       | worker reports first heartbeat or work   |
| `claimed`          | `ready`             | claim released without completion        |
| `claimed`          | `released`          | claim released without completion        |
| `in_progress`      | `completed`         | worker completes with evidence           |
| `in_progress`      | `ready`             | claim released or expired                |
| `in_progress`      | `released`          | claim released without completion        |
| `completed`        | `accepted`          | downstream consumer accepts handoff      |
| `accepted`         | `consumed`          | downstream consumes the output           |
| `ready`            | `blocked`           | new blocker raised                       |
| `blocked`          | `ready`             | blocker resolved                         |
| any non-terminal   | `cancelled`         | producer or lead cancels                 |
| any non-terminal   | `superseded`        | replaced by a newer work item            |

Optional transitions (MAY be supported):

| From               | To                  | Trigger                                  |
| ------------------ | ------------------- | ---------------------------------------- |
| `requires_attention`| `ready`            | decision made                            |
| `requires_attention`| `blocked`          | decision deferred behind a blocker       |
| `completed`        | `consumed`          | implementations that skip the `accepted` step (acceptance is implicit) |
| `released`         | `ready`             | re-enqueue after release                 |

Forbidden transitions (MUST NOT be permitted):

- Any transition out of `consumed`, `cancelled`, or `superseded`. These
  are terminal.
- `completed` → `in_progress` (re-opening a completed work item requires
  a new work item via `superseded`).
- `blocked` → `claimed` directly (a work item MUST pass through `ready`
  before claim).

### `Claim.claim_status`

```
    (claim granted)
          |
          v
     +---------+
     | active  +---------------+
     +----+----+               |
          |                    | (worker calls release)
          | (TTL passes        v
          |  with no renew) +-----------+
          |                  | released |
          v                  +-----------+
     +----------+
     | expired  |
     +----------+

     +---------+
     | active  +--(orchestration revokes)--+
     +---------+                            v
                                       +----------+
                                       | revoked  |
                                       +----------+
```

Required transitions (MUST be supported):

| From      | To         | Trigger                                  |
| --------- | ---------- | ---------------------------------------- |
| (start)   | `active`   | claim granted                            |
| `active`  | `released` | worker explicitly releases               |
| `active`  | `expired`  | `expires_at_utc` passes without renew    |
| `active`  | `revoked`  | orchestration middleware revokes         |

Forbidden transitions (MUST NOT be permitted):

- `released` → `active` (re-claim requires a new `ClaimIntent` and a new
  `claim_id`).
- `expired` → `active` (same as above).
- `revoked` → `active` (same as above).
- Any mutation of a non-`active` claim other than to inspect it.

## Pickup semantics

The main query shape an ACOP-backed system MUST support is:

- latest actionable work for agent X

Secondary useful shapes that implementations SHOULD support:

- latest work between agent A and agent B
- all blocked work for repo Y
- all claimable work for branch Z
- all work blocked on blocker code K

This is why ACOP requires explicit `status`, `blocker_codes`,
`actionability_score`, and `target_agent_uid`.

## Repo-aware coordination

Producers SHOULD scope work items to a concrete code surface using the
recommended fields:

- `repo_id`
- `workspace_root`
- `branch_name`
- `worktree_id`
- `base_commit`
- `expected_head_commit`
- `owned_paths`
- `owned_symbols`

These are important for avoiding collisions during cross-agent code
generation. An implementation MAY use `owned_paths` and `owned_symbols`
to detect cross-claim collision at finer granularity than `work_item_id`,
but the v1.0 spec does not require it.

## Review-aware coordination

Code work is not done when edits exist; it is done when the required
validation and review state is satisfied.

Recommended fields:

- `review_required`
- `review_scope`
- `review_artifact_ids`
- `validation_requirements`
- `completion_evidence`

## Minimal message families

ACOP defines these semantic message families:

- `work_offer`
- `work_update`
- `claim_intent`
- `claim_grant`
- `claim_release`
- `blocker_update`
- `artifact_update`
- `validation_update`
- `completion_notice`
- `supersession_notice`

These MAY travel over A2A messages or be represented in indexed
artifacts/resources. The normative wire mapping for HTTP+JSON is defined
in [acop-http-binding.md](./acop-http-binding.md).

## Blocker code namespaces

ACOP reserves the following namespace prefixes for `blocker_code` values:

| Prefix             | Owned by                                  | Stability    |
| ------------------ | ----------------------------------------- | ------------ |
| (no prefix)        | ACOP core (this document)                 | stable       |
| `orchestration.`   | ACOP orchestration extension              | experimental |
| `compliance.`      | ACOP compliance extension                 | experimental |
| `x.<vendor>.`      | implementation-defined extensions         | n/a          |

Core ACOP reserves these unprefixed `blocker_code` values for the meanings
described:

| Code                              | Meaning                                                  |
| --------------------------------- | -------------------------------------------------------- |
| `seam_extraction_required`        | A code seam must be extracted before work can proceed.   |
| `host_contract_unresolved`        | The host/consumer contract is not yet stable.            |
| `branch_conflict_risk`            | Concurrent branch state would conflict with this work.   |
| `validation_environment_missing`  | A required validation environment is unavailable.        |
| `review_required`                 | Review must be obtained before transition.               |
| `dependency_work_item_pending`    | Another work item must complete first.                   |
| `external_decision_pending`       | An external (often human) decision is outstanding.       |
| `artifact_unavailable`            | A referenced artifact is missing or not yet readable.    |

Implementations MUST NOT use these unprefixed codes for meanings other
than the ones above. Implementations MAY add codes under the `x.<vendor>.`
prefix without coordination.

## Versioning policy

### `protocol_version` semantics

Every ACOP document (request body, persisted record, message) MUST carry a
`protocol_version` field of the form `acop/<major>.<minor>` (e.g.
`acop/1.0`). Servers MUST reject requests whose `protocol_version` they
do not accept with the `unsupported_protocol_version` error code.

This document defines `acop/1.0`. Future versions follow these rules:

**Additive (minor version bump, e.g. `acop/1.0` → `acop/1.1`):**

- Adding new optional fields to existing objects.
- Adding new enum values to fields whose schema declares the enum is
  extensible (the v1.0 enums for `status`, `work_kind`, `claim_status`,
  and `blackboard_entry_kind` are NOT extensible — adding a value to any
  of them is a major bump).
- Adding new error codes (clients MUST tolerate unknown error codes by
  treating them as their HTTP status's default semantics; see
  [acop-http-binding.md](./acop-http-binding.md#error-model)).
- Adding new verbs to the HTTP binding.
- Adding new namespaced extensions or new prefixes under
  [blocker code namespaces](#blocker-code-namespaces).
- Tightening recommendations from SHOULD to MUST is **not** additive; it
  is a major bump.

**Breaking (major version bump, e.g. `acop/1.0` → `acop/2.0`):**

- Removing or renaming any required field.
- Changing the meaning of an existing field.
- Adding a value to a non-extensible enum.
- Removing or renumbering an HTTP status code for an existing verb.
- Changing the semantics of an existing state transition.
- Removing an existing error code (deprecation is allowed within a major
  version, removal is not).

### Negotiation

Within a single major version, the highest minor version supported by
both client and server MUST be used:

1. The client SHOULD send the highest `protocol_version` it supports in
   every request.
2. The server, on encountering a request whose minor version is higher
   than it supports but whose major version matches, MUST either:
   a. Process the request using its own highest supported minor version
      and tag the response with that version, OR
   b. Reject with `unsupported_protocol_version` and include the highest
      version it supports in the error `details.max_supported_version`.

Across major versions, no negotiation is required: a server MAY refuse
all requests of a non-matching major version.

### Stability labels per extension

Each ACOP extension declares its own stability label independently from
core. v1.0 labels:

| Component                       | Stability    | Versioning                |
| ------------------------------- | ------------ | ------------------------- |
| ACOP core (this document)       | stable       | `acop/1.0`                |
| HTTP+JSON transport binding     | stable       | tied to core              |
| Orchestration extension         | experimental | `acop-orchestration/0.1`  |
| Compliance extension            | experimental | `acop-compliance/0.1`     |
| Read-side MCP recommendations   | experimental | tied to read-side server  |

"Experimental" means the surface MAY change in a backwards-incompatible
way at any time before its own 1.0 release. Implementations that adopt
experimental extensions SHOULD pin to an exact version.

## Conformance

A conforming ACOP implementation MUST:

1. Validate every persisted or emitted core record against
   [acop.schema.json](./acop.schema.json) with `additionalProperties:
   false` enforced at the object level.
2. Honor the [state-machine rules](#state-machines) for `WorkItem.status`
   and `Claim.claim_status`. Forbidden transitions MUST be rejected.
3. Use the reserved [blocker code](#blocker-code-namespaces) values only
   with the meanings defined above.
4. Reject requests carrying a `protocol_version` it does not accept with
   `unsupported_protocol_version`.
5. Authenticate every state-mutating request (see
   [acop-http-binding.md](./acop-http-binding.md#authentication-and-authorization))
   and scope claims to authenticated identity.

A conforming HTTP+JSON implementation MUST additionally satisfy
[acop-http-binding.md § Conformance](./acop-http-binding.md#conformance).

Implementations SHOULD execute the conformance test fixtures in
[fixtures/](./fixtures/) as part of their CI.

## Initial practical scope

ACOP v1.0 covers:

- stable work identity
- explicit status and blockers
- artifact and operation identity
- actionability
- target agent and participant agent fields
- claim / lease hooks
- repo/branch/worktree scoping
- a normative HTTP+JSON transport binding
- a normative error model
- normative state machines
- a versioning policy

It deliberately avoids:

- global scheduling policy
- model routing policy
- transport lock-in (other bindings MAY be defined alongside the HTTP
  binding)

## Core schema

The machine-readable core contract lives at:

- [acop.schema.json](./acop.schema.json)
- [acop_examples.md](./acop_examples.md)
- [acop-orchestration.md](./acop-orchestration.md)
- [acop-orchestration.schema.json](./acop-orchestration.schema.json)

That schema is intentionally narrow. It covers:

- `WorkItem`
- `Blocker`
- `Operation`
- `Artifact`
- `ValidationRequirement`
- `BlackboardEntry`
- `ClaimIntent`
- `Claim`
- `LeaseHeartbeat`

It does not encode orchestration policy. Reusable orchestration flow
semantics such as stages, lanes, gates, release conditions, and
acceptance state live in the orchestration extension.

## Producer alignment

Producers (planners, refactor engines, decomposition services) align with
ACOP by:

- keeping handoff envelopes strongly typed
- keeping blocker codes explicit and within the reserved namespaces
- keeping artifact and operation IDs stable
- publishing readiness and actionability
- not owning lease truth

## Compliance extension hook

ACOP core remains useful without compliance-heavy deployment assumptions.

Compliance-specific coordination lives in an extension/profile rather than
in core. See [acop-compliance.md](./acop-compliance.md).

## Read-side MCP alignment

The MCP read surface SHOULD:

- index ACOP-compatible work items
- expose ACOP-relevant query surfaces
- remain query/transport oriented
- not become the hidden coordination owner

## Open questions

These are explicitly out of scope for v1.0 and are tracked for a future
version:

- how much of claim / lease state should be queryable via MCP versus only
  through orchestration middleware APIs
- whether `actionability_score` should be producer-supplied,
  middleware-supplied, or both
- a full A2A binding (separate spec workstream)
- whether to formalize `review` as a first-class object rather than as an
  artifact reference
