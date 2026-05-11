# Query Golden Diff Harness

Semantic parity testing: run the same query corpus against BogDB (C#) and compare results against frozen golden snapshots committed to source control.

## How It Works

```text
parity/query-golden/
|- corpus-*.cypher         <- query corpus files (schema + DML + named queries)
|- golden/                 <- frozen golden snapshots (one JSON file per corpus)
|  |- basic.golden.json
|  |- agg.golden.json
|  |- ...
|  \- with-chaining.golden.json
\- run-golden.ps1          <- orchestration script
```

The C# infrastructure lives in `BogDb.Tests/Golden/`:

- `GoldenTestRunner`: parses corpus files, executes against an in-memory `BogDatabase`, and normalizes results
- `GoldenDiffTests`: xUnit `[Theory]` that diffs live output against frozen golden files
- `GoldenBlessCommand`: regenerates golden files from current engine output

## Corpus Format

```text
-- SCHEMA
NODE <TableName>  <col>:<type>  ...
REL  <RelType>    FROM:<FromTable>  TO:<ToTable>  [<prop>:<type> ...]

-- SETUP
<DML Cypher statements; one per line, ending with ';'>

-- QUERY: <name>
<single deterministic RETURN statement>
```

`SCHEMA` declarations are routed to `EnsureNodeTable` / `EnsureRelTable`.
`SETUP` and `QUERY` statements are executed through `BogConnection.Query()`.

Supported schema types: `INT64`, `INT32`, `INT16`, `INT8`, `UINT64`, `UINT32`, `UINT16`, `UINT8`, `DOUBLE`, `FLOAT`, `BOOL`, `STRING`, `DATE`, `TIMESTAMP`, `TIMESTAMP_MS`, `TIMESTAMP_NS`, `TIMESTAMP_SEC`.

## Running the Diff Tests

```sh
# Run all golden diff assertions

dotnet test BogDb.Tests --filter "FullyQualifiedName~GoldenDiffTests"

# Or use the wrapper
cd parity/query-golden
./run-golden.ps1
```

If a required golden file is missing, the diff test fails. It does not silently pass.

## Re-Blessing Golden Files

Run this when intentional behavior changes. Review the git diff in `golden/` before committing.

```sh
dotnet test BogDb.Tests --filter "FullyQualifiedName~GoldenBlessCommand"
# OR:
./run-golden.ps1 -Bless
```

By default, the bless report is written to `parity/reports/query-golden/bless-report.md`.
The wrapper also supports overrides:

```sh
./run-golden.ps1 -CorpusDir parity/query-golden -OutputDir parity/reports/query-golden
```

## Adding New Queries

1. Edit an existing `corpus-*.cypher` file, or create a new one.
2. Add entries to `GoldenDiffTests.CorpusFiles` and `GoldenBlessCommand.Corpora`.
3. Run bless to generate the golden file.
4. Commit both the corpus and the golden file.

## Corpus Coverage

| File | Schema | Queries |
|---|---|---|
| `corpus-basic.cypher` | Person (4 nodes) + KNOWS (3 rels) | 8 - scan, filter, one-hop, two-hop |
| `corpus-agg.cypher` | Employee (6 nodes) | 8 - COUNT, SUM, AVG, MIN, MAX, HAVING, DISTINCT |
| `corpus-recursive.cypher` | Node (5 nodes) + EDGE (5 rels, chain + shortcut) | 6 - `*1..N` variable-length path patterns |
| `corpus-filter.cypher` | Item (6 nodes) | 8 - arithmetic, string ops, boolean logic |
| `corpus-projection.cypher` | Record (10 nodes) | 10 - ORDER BY, SKIP, LIMIT, WITH, aliases, `UNION`, `UNION ALL` |
| `corpus-multitable.cypher` | Person + Company + City, with WORKS_AT and LOCATED_IN | 8 - cross-table scans, typed traversals, chained joins |
| `corpus-multitable-advanced.cypher` | Person + Company + City, with WORKS_AT, ADVISES, and LOCATED_IN | 8 - incoming traversals, mixed-rel matches, endpoint filters, denser multi-table chains |
| `corpus-paths.cypher` | Company graph path-value queries over recursive PARTNERS_WITH traversal | 8 - `length(p)`, `nodes(p)`, `rels(p)`, `is_acyclic(p)`, `is_trail(p)`, path node extraction |
| `corpus-multitable-recursive.cypher` | Person + Company + City, with WORKS_AT, ADVISES, LOCATED_IN, and recursive PARTNERS_WITH chains | 8 - variable-length partner traversal, recursive incoming/undirected company reachability, person-to-city recursion through company chains |
| `corpus-multitable-undirected.cypher` | Person + Company + City, with WORKS_AT, ADVISES, LOCATED_IN, and PARTNERS_WITH | 8 - undirected typed traversals, undirected mixed-rel chains, company-company partner links, denser mixed-shape counts |
| `corpus-nulls.cypher` | Person (4 nodes) | 8 - explicit `NULL`, omitted properties, `IS NULL`, `coalesce`, null-sensitive aggregates |
| `corpus-types.cypher` | Metric (4 nodes) | 8 - typed schema columns, typed `CAST(... AS TYPE)` writes, numeric predicates, arithmetic, aggregates |
| `corpus-types-extended.cypher` | Event (3 nodes) | 8 - `UINT32`/`UINT64` schema columns, `DATE`/`TIMESTAMP` writes and reads, date extractors, epoch conversion, mixed numeric comparison |
| `corpus-types-nested.cypher` | nested scalar/evaluation-only queries | 11 - `LIST`/`STRUCT`/`MAP` literals, struct literal extraction/keys, quantifiers, `UNWIND`, key extraction, nested collection access |
| `corpus-functions.cypher` | scalar/evaluation-only queries | 8 - `coalesce`, `ifnull`, `nullif`, `typeof`, `printf`, base64, hex, bit/octet length |
| `corpus-functions-advanced.cypher` | scalar/evaluation-only queries | 8 - list/map/struct functions plus `date_add`/`date_diff`/`timestamp_add` behavior snapshots |
| `corpus-functions-json-vector.cypher` | vector + JSON table-function queries | 8 - vector similarity/distance/cross-product plus `LOAD FROM` JSON array/object/NDJSON snapshots |
| `corpus-functions-vector-advanced.cypher` | vector/array evaluation-only queries | 8 - L1/squared/cosine distance, zero-vector normalize, array aggregate helpers, push/pop, unique, reverse |
| `corpus-temporal.cypher` | temporal scalar/evaluation-only queries | 8 - `date`, `timestamp`, `make_date`, `make_timestamp`, `date_part`, `date_trunc`, epoch conversion |
| `corpus-errors.cypher` | Person (1 node) | 12 - deterministic parser/binder/planner/runtime failure snapshots including unknown rels, non-constant LIMIT, type mismatch, and runtime math errors |
| `corpus-transactional.cypher` | Person (1 seed node) + KNOWS rel | 28 - begin/commit/rollback, insert/delete/update visibility, rel mutation visibility |
| `corpus-transactional-graph.cypher` | Person + Company graph with WORKS_AT and PARTNERS_WITH | 17 - multi-table graph visibility inside tx, rollback of rel property update/delete, committed graph traversal visibility |
| `corpus-path-endpoints.cypher` | City (5 nodes) + ROUTE (5 rels, dist/mode) | 8 - endpoint ID extraction, pop filter on dest node, mode filter on rels, variable-depth node counts |
| `corpus-errors-extended.cypher` | Item (2 nodes) | 12 - unknown function, invalid regex, integer divide-by-zero, log/sqrt of illegal values, nested aggregate, aggregate in WHERE |
| `corpus-string-functions.cypher` | scalar/evaluation-only | 10 - trim/pad/repeat/reverse, substring/left/right, replace/concat, upper/lower, split, regex_extract, strpos |
| `corpus-with-chaining.cypher` | Sale (8 nodes) | 8 - computed column pipe, aggregation staging, HAVING-equivalent, double aggregate, WITH+LIMIT, WITH rename, chained filter+count, MAX per region |
| `corpus-scalar-macro.cypher` | scalar/evaluation-only queries | 6 - `CREATE MACRO` plus required/default/zero-arg invocation |
| `corpus-subquery.cypher` | Person (3 nodes) + KNOWS (1 rel) | 8 - correlated `EXISTS` / `COUNT` subqueries, nested inner filters, and grouped aggregates |
| `corpus-merge.cypher` | Person + Company + City with WORKS_AT and LOCATED_IN | 19 - single-node `MERGE`, single-relationship `MERGE`, connected and disconnected graph `MERGE`, `ON CREATE`/`ON MATCH`, typed `NULL` property snapshots, duplicate-PK failure snapshots, storage-backed same-endpoint parallel edges, and post-`MATCH` relationship `SET`/`DELETE` targeting |
| `corpus-explain.cypher` | Person | 2 - EXPLAIN syntax and EXPLAIN LOGICAL query plans |
| `corpus-hint.cypher` | Person | 1 - Index and join order hint statements |
| `corpus-ldbc.cypher` | Person + Message + Tag... | 4 - LDBC schema and structural workload queries |
| `corpus-lsqb.cypher` | Country + Message + Tag... | 4 - LSQB schema and complex topological join forms |
| `corpus-parquet.cypher` | Dummy | 2 - COPY TO and LOAD FROM parquet |
| `corpus-reader.cypher` | none | 2 - CSV loader and absent file snapshots |
| `corpus-rel-group.cypher` | A + B | 1 - CREATE REL TABLE GROUP definitions |
| `corpus-tck.cypher` | Node + REL | 6 - TCK structurally typed and binding assertions |
| `corpus-user-defined-types.cypher` | none | 2 - CREATE TYPE and casting to structural subsets |

**Total: 714 named queries across 52 corpora**

## Current Coverage Notes

The current corpus now covers the major supported query, function, reader/export, GDS, and parity-category lanes. The remaining gaps are mostly depth and long-tail semantic permutations rather than missing top-level categories.

- typed storage/DML coverage now exists for core scalar widths plus first unsigned/temporal schema slices
  `corpus-types.cypher` exercises `INT16`, `INT32`, `FLOAT`, `DOUBLE`, and `BOOL` columns through query-driven `SET ... CAST(... AS TYPE)` writes plus numeric predicates, arithmetic, and aggregate reads. `corpus-types-extended.cypher` adds `UINT32`, `UINT64`, `DATE`, and `TIMESTAMP` schema coverage.
- nested collection/query coverage now exists, but it is still literal/function-based rather than full nested schema parity
  `corpus-types-nested.cypher` freezes `LIST`/`STRUCT`/`MAP` literals, quantifier truth-table behavior, `UNWIND` over nested values, key extraction, and nested collection access. Catalog-level nested type metadata and storage-backed nested schema coverage are still thin.
- null and missing-property semantics now have direct golden coverage
  `corpus-nulls.cypher` covers explicit `NULL`, omitted properties, `IS NULL` / `IS NOT NULL`, `coalesce`, and aggregate null handling. Broader null-sensitive function coverage is still thin.
- direct scalar, collection, temporal, vector, and JSON table-function coverage now exists
  `corpus-functions.cypher`, `corpus-functions-advanced.cypher`, `corpus-functions-json-vector.cypher`, `corpus-functions-vector-advanced.cypher`, and `corpus-temporal.cypher` freeze deterministic slices of scalar formatting/encoding, list/map/struct evaluation, vector similarity/distance math, array aggregation/mutation helpers, and JSON `LOAD FROM` behavior across array/object/NDJSON inputs. Broader json scalar/window coverage is still thin.
- relationship-pattern coverage is broader but still incomplete
  `corpus-multitable-advanced.cypher`, `corpus-multitable-undirected.cypher`, and `corpus-multitable-recursive.cypher` now cover incoming traversals, undirected typed traversals, recursive partner-chain traversal, company-company partner links, and mixed relationship-type matches across the same endpoints. `corpus-paths.cypher` adds explicit recursive path-value semantics (`length(p)`, `nodes(p)`, `rels(p)`, `is_acyclic(p)`, `is_trail(p)`) over recursive bindings. More complex mixed-shape graph queries are still thin.
- multi-table graph behavior is broader but still not exhaustive
  `corpus-multitable.cypher`, `corpus-multitable-advanced.cypher`, `corpus-multitable-undirected.cypher`, `corpus-multitable-recursive.cypher`, and `corpus-paths.cypher` now cover outgoing, incoming, undirected, and variable-length traversals, mixed rel-type expansion, endpoint filtering, company-company partner links, recursive path-value projection, and denser company/city chains. Broader multi-label selection and larger mixed schemas are still thin.
- error-path coverage is present but still narrow
  `corpus-errors.cypher` now captures deterministic parser, binder, planner, and runtime failures, including unknown labels/properties/variables/relationship tables, non-constant `SKIP`/`LIMIT`, DISTINCT+ORDER constraints, mixed-type comparison errors, and runtime math failures. Failed-query goldens now diff on exact error message text, but broader planner/execution failures are still not broadly covered.
- write/readback coverage is now broader but still incomplete
  `corpus-transactional.cypher` now snapshots insert/delete/update visibility plus relationship mutation across BEGIN/COMMIT/ROLLBACK. `corpus-transactional-graph.cypher` extends that into multi-table graph traversal visibility, rollback of relationship property updates and partner-edge deletes, and committed chain visibility across `Person -> Company -> Company`. Broader mixed read/write workflows and denser rel-mutation cases are still uncovered.
- expression/function coverage is still selective
  The corpus now covers basic arithmetic, core scalar formatting/encoding, temporal constructors/extractors, list/map/struct helpers, broader vector/array helpers (similarity, distance, normalize, aggregate/mutation helpers), JSON table-function loading, simple string predicates, and core aggregates, but not broader json scalar/window function behavior.

Recommended next backlog slices:

1. extend nested coverage from literal/function paths into schema-backed `LIST`/`STRUCT`/`MAP` workflows once richer catalog typing exists
2. expand function coverage further into json scalar paths and broader aggregation/window families
3. add more complex mixed-shape graph pattern coverage beyond the current directed/incoming/undirected/recursive multi-table slices
4. integer/float edge cases: overflow, `NaN`, `Infinity`, `CAST` between incompatible types

## Validation Notes

The exact full-suite count changes over time as the repository evolves. Do not hardcode historical totals in this README.

The stable checks for this harness are:

- `GoldenBlessCommand` regenerates all registered golden files
- `GoldenDiffTests` executes one theory case per registered corpus
- failed-query snapshots are diffed on exact `ErrorMessage`, not just success/failure state
- `run-golden.ps1` routes corpus/report directory overrides through environment variables consumed by the test code

## Implementation Notes

- freeze-and-compare: golden files are the source of truth. No live C++ binary is required.
- canonical row sort: rows are lexicographically sorted before comparison, so queries without `ORDER BY` still produce stable golden files.
- `NULL` is serialized as `"<null>"` in golden files.
- float precision: `double` uses `G17`; `float` uses `G9`.
- in-memory only: tests open `BogDatabase.Open(":memory:")`; no on-disk state.
- schema routing: `NODE` / `REL` declarations go through `EnsureNodeTable` / `EnsureRelTable` because `BogConnection.Query()` does not handle `CREATE NODE TABLE` syntax.
