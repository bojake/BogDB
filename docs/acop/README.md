# ACOP — Agentic Code Orchestration Protocol

ACOP is a vendor-neutral coordination protocol for multi-agent software
development work. It defines work-item, claim, blackboard, and orchestration
semantics that sit *above* MCP (tool/context access) and A2A
(agent-to-agent messaging).

ACOP core is at **v1.0** (stable). The orchestration and compliance
extensions remain at **v0.1** (experimental); see
[acop.md § Versioning policy](./acop.md#versioning-policy) for stability
labels.

## Documents

| File                                                           | What it defines                                            | Stability    |
| -------------------------------------------------------------- | ---------------------------------------------------------- | ------------ |
| [acop.md](./acop.md)                                           | Core protocol: work items, claims, blackboard, artifacts, state machines, conformance, versioning policy. | stable       |
| [acop.schema.json](./acop.schema.json)                         | JSON Schema for the core contract.                         | stable       |
| [acop-http-binding.md](./acop-http-binding.md)                 | Normative HTTP+JSON transport binding, error model, auth, sequence diagrams. | stable       |
| [acop-errors.schema.json](./acop-errors.schema.json)           | JSON Schema for HTTP error response bodies.                | stable       |
| [fixtures/](./fixtures/)                                       | Conformance test fixtures (one JSON file per scenario).    | stable       |
| [acop-orchestration.md](./acop-orchestration.md)               | Orchestration extension: stages, lanes, gates, acceptance. | experimental |
| [acop-orchestration.schema.json](./acop-orchestration.schema.json) | JSON Schema for orchestration flows.                       | experimental |
| [acop-orchestration-mcp.md](./acop-orchestration-mcp.md)       | Recommended MCP **read** tools for orchestration state.    | experimental |
| [acop-orchestration-cypher.md](./acop-orchestration-cypher.md) | Cypher query templates for graph-projected orchestration.  | experimental |
| [acop-compliance.md](./acop-compliance.md)                     | Compliance extension: requirements, evidence, exceptions.  | experimental |
| [acop-compliance.schema.json](./acop-compliance.schema.json)   | JSON Schema for the compliance extension.                  | experimental |
| [acop_examples.md](./acop_examples.md)                         | Worked examples of the core and extension shapes.          | stable       |

## Layering

ACOP layers on top of existing standards:

- **MCP** — tools, resources, and context exposure
- **A2A** — inter-agent communication and delegation
- **ACOP** — code-work coordination semantics layered on top

ACOP does not replace MCP or A2A; it specifies the contract those transports
carry for work coordination.

## Reference implementation

The reference write-side MCP server for ACOP coordination lives in
[`BogDb.Acop.Mcp.Server`](../../BogDb.Acop.Mcp.Server). It exposes ACOP
claim/complete/work-item/blackboard tools over stdio JSON-RPC and delegates
the actual coordination authority to a pluggable backend adapter. The
[HttpAcopBackend](../../BogDb.Acop.Mcp.Server/Adapters/HttpAcopBackend.cs)
adapter implements the [HTTP+JSON binding](./acop-http-binding.md).

The read-side MCP surface (orchestration state, blocked work, gate status)
is provided by [`BogDb.Mcp.Server`](../../BogDb.Mcp.Server) and reads from
the graph projection of coordination state.

## Schema host

The schema `$id` URIs in this repo point at
`https://acop.dev/schemas/v1.0/...` (and `v0.1/...` for the experimental
extensions). That host is a placeholder; if you depend on `$id` resolution,
mirror the schemas locally until they are published.
