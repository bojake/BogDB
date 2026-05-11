# SQL → Cypher · BogDB Sample

**WS35** — An interactive Blazor Server application teaching graph query (Cypher) to developers who already know SQL. Running at port **5090**.

## What was built

| Page | Route | Purpose |
|------|-------|---------|
| Home & Schema | `/` | Stat cards · node/rel schema explorer · getting-started CTAs |
| SQL → Cypher Lessons | `/lessons` | 20 side-by-side SQL/Cypher lessons, live editable Cypher, "Try it!" prompts |
| Query Lab | `/querylab` | Free-form Cypher editor · 12 domain quick-start queries |
| Concept Guide | `/concepts` | 8 reference cards explaining the SQL→graph mental model shifts |

## Running

```bash
dotnet run --project BogDb.Samples.SqlToCypher.Blazor/BogDb.Samples.SqlToCypher.Blazor.csproj
# Open http://localhost:5090
```

## Graph domains

All data is synthetic, seeded in-memory at startup via `SqlToCypherGraphService`.

| Domain | Node labels | Relationship types |
|--------|-------------|-------------------|
| **E-commerce** | Customer, Product, Order, Category | PLACED, CONTAINS, BELONGS_TO |
| **Employees** | Employee, Department, Project | WORKS_IN, MANAGES, ASSIGNED_TO |
| **Movies** | Movie, Person, Genre | ACTED_IN, DIRECTED, IN_GENRE |

## Lesson catalogue

20 lessons covering the full spectrum from basic to advanced:

| # | Title | SQL concept | Cypher concept |
|---|-------|-------------|----------------|
| 1 | Basic SELECT | `SELECT * FROM` | `MATCH (n:Label) RETURN` |
| 2 | Filter | `WHERE col = val` | `WHERE n.prop = val` |
| 3 | Projection | `SELECT col, col` | `RETURN n.prop, n.prop` |
| 4 | Aliases | `SELECT x AS y` | `RETURN n.x AS y` |
| 5 | Sort & Limit | `ORDER BY … LIMIT` | identical syntax |
| 6 | Count | `COUNT(*)` | `COUNT(*)` |
| 7 | Sum & Avg | `SUM / AVG / MIN / MAX` | identical syntax |
| 8 | Group By | `GROUP BY col` | `WITH col, COUNT(*) AS n` |
| 9 | One-Hop Join | `INNER JOIN ON fk` | `MATCH (a)-[:REL]->(b)` |
| 10 | Multi-Hop Join | multiple JOINs | chained MATCH pattern |
| 11 | Self Join | self-referencing JOIN | `(a)-[:MANAGES]->(b:Employee)` |
| 12 | Transitive Hops | `WITH RECURSIVE` CTE | `[:R*1..N]` |
| 13 | Shortest Path | Dijkstra / extension | `shortestPath((a)-[*]-(b))` |
| 14 | Distinct | `SELECT DISTINCT` | `RETURN DISTINCT` |
| 15 | Exists Check | `WHERE EXISTS (SELECT …)` | `WHERE EXISTS { MATCH … }` |
| 16 | Collect | `array_agg / STRING_AGG` | `COLLECT(expr)` |
| 17 | Anti-Join | `LEFT JOIN … IS NULL` | `WHERE NOT (a)-[:R]->()` |
| 18 | Edge Properties | junction table with attrs | `[r:REL {prop}]` |
| 19 | Variable-Length Path | no clean equivalent | `[:R*min..max]` |
| 20 | Graph Algorithms | no equivalent | `CALL pagerank() YIELD …` |

Each lesson includes:
- Read-only **SQL pane** (purple)
- Editable **Cypher pane** (amber) — modify and run live against the graph
- Plain-English **explanation paragraph**
- **Key differences** callout (3–4 bullet points)
- **Try it!** prompt suggesting a small experiment

## What was verified

| Check | Result |
|-------|--------|
| `dotnet build` | ✅ 0 errors, 0 warnings (net10.0) |
| Home page — stat cards + schema tables | ✅ |
| 20 lessons with category chip filter | ✅ |
| Lesson detail — SQL + Cypher panes + explanation | ✅ |
| Run Cypher — real query execution + results table | ✅ |
| Query Lab — default query + quick-start load | ✅ |
| Concept Guide — 8 reference cards | ✅ |

## Architecture

```
SqlToCypherGraphService (singleton)
  ├── BogDatabase.CreateInMemory()
  ├── SetupSchema()     — EnsureNodeTable / EnsureRelTable (10 node types, 9 rel types)
  ├── SeedData()        — UpsertNodeById / UpsertRelationshipById
  ├── Execute(cypher)   — conn.Query() → columns + rows + elapsed ms
  ├── GetSchemaInfo()   — node & rel table metadata for the Home page
  └── GetLessons()      — static List<Lesson> with 20 SQL/Cypher lesson pairs
```

APIs used: `BogDatabase.CreateInMemory()`, `EnsureNodeTable`, `EnsureRelTable`, `UpsertNodeById`, `UpsertRelationshipById`, `BogConnection.Query()`.
