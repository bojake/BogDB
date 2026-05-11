-- corpus-copy-and-reader.cypher
-- G-013: COPY FROM / COPY TO / LOAD FROM node and relationship CSV pipeline.
--
-- Scope: Tier B (COPY FROM) + Tier C (COPY TO) + Tier D (LOAD FROM).
--
-- Fixture files (resolved from parity/query-golden/fixtures/):
--   persons.csv  — header: id,name,age   — 4 rows (input)
--   knows.csv    — header: from_id,to_id,since — 4 rows (input)
--
-- Output paths ({fixture:out_*.csv}) are NOT declared with -- FIXTURE:.
-- They are created during corpus execution by COPY TO queries.
-- LOAD FROM queries that follow in the same corpus run read those files naturally.
--
-- CSV format (confirmed from CopyTo.cs + grammar):
--   - COPY TO output: UTF-8, header = RETURN projection column names, RFC 4180 escaping
--   - LOAD FROM input: reads header row as column names; type inference without WITH HEADERS
--   - WITH HEADERS (col TYPE, ...): pins column types for deterministic access
--
-- LOAD FROM column name note:
--   out_persons.csv written by Tier C has headers "p.id,p.name,p.age" (dotted).
--   LOAD FROM corpus queries over that file use RETURN count(*) only, because
--   using dotted names in RETURN/WHERE may conflict with property access syntax.
--   See tracker open question on dotted-header column names in expression position.
--
-- Status:
--   Tier B: live success-mode. 43/43 golden diff green.
--   Tier C: live success-mode. Re-blessed March 27 2026.
--   Tier D: corpus queries added March 27 2026; golden snapshot set to expected-error
--           (isSuccess: false) until LOAD FROM engine lands. Re-bless after engine.

-- FIXTURE: persons.csv
-- FIXTURE: knows.csv

-- SCHEMA
NODE Person  id:INT64  name:STRING  age:INT64
REL  Knows   FROM:Person  TO:Person  since:INT64

-- SETUP
COPY Person FROM '{fixture:persons.csv}';

-- QUERY: node_count_after_copy
-- Verify all 4 rows from persons.csv were loaded
MATCH (p:Person) RETURN count(p) AS n

-- QUERY: node_id_readback
-- Verify primary key values are correct and sortable
MATCH (p:Person) RETURN p.id ORDER BY p.id

-- QUERY: node_name_readback
-- Verify string column values loaded correctly
MATCH (p:Person) RETURN p.name ORDER BY p.id

-- QUERY: node_age_readback
-- Verify integer column values loaded correctly
MATCH (p:Person) RETURN p.age ORDER BY p.id

-- QUERY: node_full_row_readback
-- Verify all columns together for a specific row
MATCH (p:Person) WHERE p.id = 1 RETURN p.id, p.name, p.age

-- QUERY: node_filter_after_copy
-- Verify WHERE predicates work over COPY-loaded data
MATCH (p:Person) WHERE p.age > 28 RETURN p.name ORDER BY p.name

-- QUERY: rel_copy_and_count
-- Load relationships from knows.csv and verify count
COPY Knows FROM '{fixture:knows.csv}';
MATCH ()-[k:Knows]->() RETURN count(k) AS n

-- QUERY: rel_traversal_readback
-- Verify rel endpoints resolve correctly (src -> dst)
MATCH (a:Person)-[k:Knows]->(b:Person) RETURN a.id, b.id ORDER BY a.id, b.id

-- QUERY: rel_property_readback
-- Verify rel property (since) loaded correctly
MATCH (a:Person)-[k:Knows]->(b:Person) RETURN a.id, b.id, k.since ORDER BY a.id, b.id

-- QUERY: rel_filter_by_property
-- Verify filtering on a COPY-loaded rel property
MATCH (a:Person)-[k:Knows]->(b:Person) WHERE k.since >= 2021 RETURN a.id, b.id ORDER BY a.id, b.id

-- QUERY: rel_degree_via_copy
-- Verify out-degree aggregation over COPY-loaded graph
MATCH (p:Person) OPTIONAL MATCH (p)-[:Knows]->(other:Person)
RETURN p.id, count(other) AS out_degree ORDER BY p.id

-- ── Tier C: COPY TO ─────────────────────────────────────────────────────────
-- These COPY TO queries also produce the output files consumed by Tier D below.
-- Execution order matters: Tier C runs first, Tier D reads its output files.

-- QUERY: copy_to_nodes_creates_no_result_rows
-- Node export: COPY TO returns 0 tuples, file written as side effect
-- Produces: {fixture:out_persons.csv} (header: p.id,p.name,p.age, 4 rows)
COPY (MATCH (p:Person) RETURN p.id, p.name, p.age ORDER BY p.id) TO '{fixture:out_persons.csv}'

-- QUERY: copy_to_filtered_creates_no_result_rows
-- Filtered export: WHERE clause applies before write, still returns 0 tuples
-- Produces: {fixture:out_persons_filtered.csv} (header: p.id,p.name, 2 rows: Alice, Charlie)
COPY (MATCH (p:Person) WHERE p.age > 28 RETURN p.id, p.name ORDER BY p.id) TO '{fixture:out_persons_filtered.csv}'

-- QUERY: copy_to_rel_export_creates_no_result_rows
-- Relationship traversal export: 4 edges written to CSV, 0 result rows
-- Produces: {fixture:out_knows.csv} (header: a.id,b.id,k.since, 4 rows)
COPY (MATCH (a:Person)-[k:Knows]->(b:Person) RETURN a.id, b.id, k.since ORDER BY a.id, b.id) TO '{fixture:out_knows.csv}'

-- QUERY: copy_to_aggregation_export
-- Aggregation export: degree summary, 0 result rows
COPY (MATCH (p:Person) OPTIONAL MATCH (p)-[:Knows]->(other:Person) RETURN p.id, count(other) AS out_degree ORDER BY p.id) TO '{fixture:out_degrees.csv}'

-- QUERY: readback_after_copy_to_unchanged
-- Graph must be unchanged after COPY TO (export is read-only)
MATCH (p:Person) RETURN count(p) AS n

-- ── Tier D: LOAD FROM ───────────────────────────────────────────────────────
-- Syntax: LOAD FROM 'path' RETURN *
-- Optional: LOAD WITH HEADERS (col TYPE, ...) FROM 'path' [WHERE ...] RETURN ...
-- LOAD FROM is a reading clause (not a statement); it requires RETURN.
--
-- These queries are expected-error until the Tier D engine lands.
-- Re-bless after LOAD FROM is implemented.

-- QUERY: load_from_declared_input_row_count
-- Basic LOAD FROM a declared input fixture: should return 4 rows.
-- Uses WITH HEADERS to pin types for deterministic access.
LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{fixture:persons.csv}' RETURN count(*) AS n

-- QUERY: load_from_declared_input_ordered_ids
-- Verify CSV id values come back sorted correctly (typed access)
LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{fixture:persons.csv}' RETURN id ORDER BY id

-- QUERY: load_from_declared_input_names
-- Verify string column values (name) are returned correctly
LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{fixture:persons.csv}' RETURN name ORDER BY id

-- QUERY: load_from_with_where_filter
-- WHERE predicate filters rows post-scan: age > 28 → Alice (30), Charlie (35)
LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{fixture:persons.csv}' WHERE age > 28 RETURN id, name ORDER BY id

-- QUERY: load_from_round_trip_row_count
-- Round-trip: reads out_persons.csv produced by copy_to_nodes_creates_no_result_rows.
-- Uses count(*) only — avoids dotted column name ambiguity (header is "p.id,p.name,p.age").
-- Expected: 4 rows (same as the node table).
LOAD FROM '{fixture:out_persons.csv}' RETURN count(*) AS n

-- QUERY: load_from_filtered_round_trip_row_count
-- Round-trip: reads out_persons_filtered.csv (2 rows: Alice, Charlie).
-- Uses count(*) only.
LOAD FROM '{fixture:out_persons_filtered.csv}' RETURN count(*) AS n

-- QUERY: copy_to_complex_projection
COPY (MATCH (p:Person) RETURN p.name AS name, p.age * 2 AS double_age, substring(p.name, 1, 3) AS tag ORDER BY p.id) TO '{fixture:out_complex.csv}'

-- QUERY: load_from_complex_projection
LOAD FROM '{fixture:out_complex.csv}' RETURN count(*) AS n

-- QUERY: load_from_skip_limit
LOAD WITH HEADERS (id INT64, name STRING, age INT64) FROM '{fixture:persons.csv}' RETURN id, name ORDER BY id SKIP 1 LIMIT 2

-- QUERY: load_from_inline_structural_cast
LOAD WITH HEADERS (id STRING, name STRING, age STRING) FROM '{fixture:persons.csv}' WHERE cast(id, 'INT64') > 2 RETURN cast(age, 'INT64') * 2 AS doubleAge ORDER BY id
