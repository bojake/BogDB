# Changelog

All notable changes to BogDB are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.4.0] - 2026-07-24

Correctness, crash, and liveness hardening from an adversarial multi-agent review of the query
engine — index/scan and traversal/transaction semantics. Eighteen confirmed defects were fixed,
each covered by a regression test in `BogDb.Tests/Adversarial/AdversarialFindingTests.cs` (the
`F##` tags below are those tests).

### Changed

Behavior-affecting corrections — review before upgrading:

- **`UNWIND` after a reading clause now preserves cardinality.** `MATCH (p) UNWIND [1,2] AS k`
  emits one row per (matched row, list element) with `p` still bound, instead of dropping the
  MATCH and returning list-only rows with unbound variables. (F04)
- **`MERGE` with an all-null key is now rejected.** `MERGE (n {email: null})` — where every named
  property evaluates to null — raises an error instead of silently matching an arbitrary existing
  node. A pattern with at least one non-null key still stores the null ones as typed nulls, and a
  keyless `MERGE (n:Label)` still matches-any. (F05)
- **`min()` / `max()` return the argument's type.** Over an `INT64` column they return a long
  (previously a double), and they now work over `STRING` and temporal columns using the same
  ordering as `ORDER BY`. (F20, F24)
- **`GROUP BY` distinguishes values by type.** Keys that merely share a string form — e.g. integer
  `1` and string `'1'` — no longer collapse into one group; grouping now uses the same equality as
  `count(DISTINCT)`. Equal numbers across numeric types (`1` and `1.0`) and multiple nulls still
  group together. (F17)
- **Disposing a connection rolls back an open transaction.** A `BogConnection` disposed with an
  uncommitted write transaction (e.g. an exception escaping a `using` block) now rolls back and
  releases the single-writer slot, instead of leaving it held so every later write fails with
  "Only one write transaction". (F21)

### Fixed

- **Cypher `CREATE` on a file-backed database now persists.** Writes issued through Cypher `CREATE`
  were written only to the graph log and lost — invisible in the same session and after reopen — in
  every transaction mode. (F02)
- **`MATCH … CREATE` creates one row per matched row** instead of a single row regardless of match
  count; the same latch also capped relationship creation. (F01)
- **Secondary-index correctness** across maintenance, planning, and scanning:
  - `STARTS WITH` on an indexed property no longer returns rows whose value changed after a `SET`.
    (F06)
  - Re-`SET` / re-upsert of an indexed row no longer produces duplicate index-scan hits. (F07)
  - `WHERE p = 'a' AND p = 'c'` on an indexed property now returns the intersection (∅), not the
    union. (F08)
  - An indexed multi-label pattern `(:A|B {p: …})` no longer drops every label but the first. (F09)
  - Building an index after a delete no longer misnumbers postings, so indexed lookups don't miss
    live nodes. (F10)
  - `ROLLBACK` of a `DELETE` now restores the node's secondary-index entries. (F11)

  These fixes preserve the index's append-only MVCC design, so a reader whose snapshot predates an
  update still resolves the node through its former value.
- **`WITH <node>, <aggregate>` keeps the node variable in scope**, so a following
  `MATCH (node)-[…]->(…)` resolves it instead of matching nothing. (F03)
- **`MERGE` after a relationship-pattern `MATCH` on the same table** no longer throws "Collection
  was modified" (the adjacency rows are snapshotted before iteration). (F23)
- **`min` / `max` / `sum` / `avg` over a `STRING` or temporal column** no longer throw; min/max
  compute the value, and sum/avg raise a clear "requires numeric values" error. (F24)
- **Extreme, infinite, or NaN `DOUBLE` values** no longer crash `DISTINCT`, `MERGE`, or
  `count(DISTINCT)` — and, latently, `ORDER BY` — via `Convert.ToDecimal` overflow. (F25)

### Notes

- **`ROLLBACK` of a `DROP TABLE` does not restore the table — by design.** BogDB's catalog DDL
  (CREATE / ALTER / DROP TABLE, index creation) is non-transactional: it mutates the catalog
  immediately and registers no undo, so a DDL statement cannot be rolled back. Do not wrap DDL in a
  transaction you might roll back. Tracked as F18 (a skipped test with the rationale in its doc
  comment); implementing transactional DDL is a separate effort and is not planned.

## [1.3.3] - 2026-07-24

### Fixed
- Consolidated the graph log behind a single shared parser, so a new record type can no longer be
  taught to one reader and not another — the root cause behind the 1.3.2 recovery defect.
- Fixed live visibility of committed graph inserts.

## [1.3.2] - 2026-07-23

### Fixed
- Graph-log record type 5 (relationship `MERGE`) was rejected by the `GraphStore` recovery reader,
  aborting recovery for any log containing a MERGE'd edge.

## [1.3.1] - 2026-07-23

### Fixed
- Index-scan-driven `DELETE` skipped nodes (stale/duplicate secondary-index hits) because it
  iterated a live posting list while that list was being mutated.

## [1.3.0] - 2026-07-23

### Added
- Vector (HNSW) and full-text (FTS) index hardening: incremental maintenance on mutation, on-disk
  persistence of the index structures, and rebuild-or-restore on reopen.
- Per-hop predicate filtering in variable-length traversal —
  `MATCH (a)-[r:REL*lo..hi (rr, nn | WHERE …)]->(b)` — pruning edges during the traversal.

[1.4.0]: https://github.com/BeyondOrdinary/BogDB/compare/v1.3.3...v1.4.0
[1.3.3]: https://github.com/BeyondOrdinary/BogDB/compare/v1.3.2...v1.3.3
[1.3.2]: https://github.com/BeyondOrdinary/BogDB/compare/v1.3.1...v1.3.2
[1.3.1]: https://github.com/BeyondOrdinary/BogDB/compare/v1.3.0...v1.3.1
[1.3.0]: https://github.com/BeyondOrdinary/BogDB/releases/tag/v1.3.0
