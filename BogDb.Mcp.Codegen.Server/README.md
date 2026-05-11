# BogDb MCP Codegen Server

`BogDb.Mcp.Codegen.Server` is a specialized MCP server for code-intelligence agents (Codex, Claude, etc.).  
It layers **semantic, task-oriented tools** on top of a purpose-built code-intelligence graph schema inside BogDB.

## Why a separate server?

The base `BogDb.Mcp.Server` provides generic database tools (`bogdb_query`, `bogdb_schema`, etc.) that work against any BogDb database. A codegen agent needs more — it needs to ask questions like:

- *"Where is `UserService.CreateUser` defined?"*
- *"What calls this function across all repos?"*
- *"If I change this protobuf schema, what services break?"*
- *"Who owns the payments service?"*
- *"What code is behind the `enable_v2_payments` feature flag?"*

These require a **pre-defined graph schema** with nodes for repos, packages, modules, files, symbols, services, APIs, owners, etc., and **semantic tools** that generate the right Cypher internally.

## Architecture

```
┌────────────────────────────────────────┐
│         Codegen Agent (Codex)          │
└─────────────┬──────────────────────────┘
              │  MCP (stdio)
┌─────────────▼──────────────────────────┐
│    BogDb.Mcp.Codegen.Server             │
│  ┌───────────────────────────────┐     │
│  │  11 codegen read tools        │     │
│  │  1 ingestion tool             │     │
│  └──────────┬────────────────────┘     │
│  ┌──────────▼────────────────────┐     │
│  │  4 base BogDb tools            │     │
│  │  (bogdb_query, schema, etc.)   │     │
│  └───────────────────────────────┘     │
└─────────────┬──────────────────────────┘
              │
┌─────────────▼──────────────────────────┐
│       BogDB (codegen schema)         │
└────────────────────────────────────────┘
```

## Tool Catalog

### Base BogDb Tools (pass-through)

| Tool | Description |
|------|-------------|
| `bogdb_query` | Read-only Cypher query against any BogDb database (escape hatch) |
| `bogdb_schema` | Compact schema summary |
| `bogdb_tables` | List node/rel tables |
| `bogdb_table_info` | Detailed metadata for one table |

### Codegen Read Tools

| Tool | Description | Example Question |
|------|-------------|------------------|
| `codegen_find_symbol` | Find symbol definitions by name, kind, repo, file | *"Where is `CreateUser` defined?"* |
| `codegen_callers` | Transitive callers of a symbol | *"Who calls this function?"* |
| `codegen_callees` | Transitive dependencies of a symbol | *"What does this function depend on?"* |
| `codegen_impact_analysis` | Blast radius of changing a symbol | *"If I change this type, what breaks?"* |
| `codegen_file_context` | Full context for a file (hierarchy, symbols, owners) | *"What's in this file?"* |
| `codegen_dependency_tree` | Transitive package dependencies | *"What does @acme/core pull in?"* |
| `codegen_api_consumers` | Services/consumers of an API endpoint | *"Who depends on POST /users?"* |
| `codegen_ownership` | Owner team for repo/service/package | *"Who owns the payments service?"* |
| `codegen_schema_status` | Graph health check (table counts, schema version) | *"Is the graph ready?"* |
| `codegen_search_docs` | Search arch docs and runbooks | *"Is there a design doc for auth?"* |
| `codegen_feature_coverage` | Code paths gated by a feature flag | *"What's behind enable_v2?"* |

### Ingestion Tools

| Tool | Description |
|------|-------------|
| `codegen_ingest_repo` | Scan a local repo and populate the graph (Repo → Package → Module → File → Symbol) |

## Graph Schema

### Node Tables (15)

`Repo`, `Package`, `Module`, `File`, `Symbol`, `Service`, `ApiEndpoint`, `DataSchema`, `Consumer`, `Owner`, `DeployUnit`, `FeatureFlag`, `Migration`, `Runbook`, `ArchDoc`

### Relationship Tables (16+)

`CONTAINS_PACKAGE`, `CONTAINS_MODULE`, `CONTAINS_FILE`, `DEFINES_SYMBOL`, `REFERENCES_SYMBOL`, `DEPENDS_ON`, `EXPOSES_API`, `GOVERNED_BY_SCHEMA`, `CONSUMES_API`, `REPO_OWNED_BY`, `SERVICE_OWNED_BY`, `PACKAGE_OWNED_BY`, `DEPLOYED_AS`, `GATED_BY`, `HAS_MIGRATION`, `SERVICE_DOCUMENTED_IN`, `SERVICE_ARCH_DOC`, `SYMBOL_DOCUMENTED_IN`, `API_DOCUMENTED_IN`, `LINKED_TO`

## Run Locally

```bash
# In-memory database (default)
dotnet run --project BogDb.Mcp.Codegen.Server/BogDb.Mcp.Codegen.Server.csproj

# Persistent database
CODEGEN_DB_PATH=/path/to/codegen.bogdb dotnet run --project BogDb.Mcp.Codegen.Server/BogDb.Mcp.Codegen.Server.csproj
```

## MCP Client Configuration

### Claude / Codex (stdio)

```json
{
  "mcpServers": {
    "bogdb-codegen": {
      "command": "dotnet",
      "args": ["run", "--project", "/path/to/BogDb.Mcp.Codegen.Server/BogDb.Mcp.Codegen.Server.csproj"],
      "env": {
        "CODEGEN_DB_PATH": "/path/to/codegen.bogdb"
      }
    }
  }
}
```

## Typical Agent Workflow

1. **Check graph status**: `codegen_schema_status` → verify schema is initialized
2. **Ingest repos**: `codegen_ingest_repo({repoPath: "/path/to/myrepo"})` → populate the graph
3. **Query**: Use semantic tools (`codegen_find_symbol`, `codegen_callers`, etc.) for code intelligence
4. **Escape hatch**: Use `bogdb_query` for ad-hoc Cypher when semantic tools aren't enough

## Supported Languages (Ingestion)

| Language | Package Manifest | Symbol Extraction |
|----------|-----------------|-------------------|
| C# | `.csproj` | Classes, records, structs, interfaces, enums, methods |
| TypeScript/JS | `package.json` | Classes, interfaces, types, enums, functions, constants |
| Python | `pyproject.toml`, `setup.py` | Classes, functions |
| Java | `pom.xml` | Classes, interfaces, enums, records, methods |

> **Note:** Day-1 extraction uses convention-based regex patterns. The interface is designed for future tree-sitter or LSP-based extraction upgrades.
