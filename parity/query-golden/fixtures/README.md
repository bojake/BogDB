# fixtures/

This directory holds input fixture files referenced by golden corpora.

## Fixture semantic split

The runner distinguishes two kinds of `{fixture:...}` paths:

| Kind | Declaration | Behavior |
|---|---|---|
| **Input fixture** | `-- FIXTURE: <relpath>` | File is copied from this directory into the corpus temp dir **before** any query runs |
| **Output path** | No declaration (just `{fixture:...}` token in a query) | Path resolves to the temp dir; the file does not need to pre-exist — it will be created by a query (e.g. `COPY TO`) |

There is **no `-- FIXTURE_OUT:` directive**. Omitting the declaration is all that is needed for output paths.

### Read-after-write in the same corpus run

`LOAD FROM '{fixture:out_persons.csv}'` is valid in a corpus query if an earlier query wrote that file via `COPY (…) TO '{fixture:out_persons.csv}'`. The runner processes queries in order, so the file will exist by the time the `LOAD FROM` executes.

### Dotted headers from `COPY TO`

`COPY TO` preserves projection column names exactly. That means:

- `RETURN p.id, p.name, p.age` writes the header `p.id,p.name,p.age`
- round-trip `LOAD FROM` support is stable for `RETURN *`, `count(*)`, and similar shapes that do not need to reference those dotted names directly
- if you want identifier-based column access after reload, alias before export:

```cypher
COPY (
  MATCH (p:Person)
  RETURN p.id AS id, p.name AS name, p.age AS age
) TO '{fixture:out_persons.csv}'
```

### Example

```cypher
-- FIXTURE: persons.csv        ← pre-seeded (input)
-- FIXTURE: knows.csv          ← pre-seeded (input)

-- SETUP
COPY Person FROM '{fixture:persons.csv}';

-- QUERY: export_nodes
COPY (MATCH (p:Person) RETURN p.id, p.name ORDER BY p.id)
  TO '{fixture:out_persons.csv}'    -- no declaration needed; output path

-- QUERY: reload_exported          -- requires Tier D LOAD FROM
LOAD FROM '{fixture:out_persons.csv}' RETURN *
```

## File formats supported

- `.csv`  — comma-separated values (first row = header)
- `.tsv`  — tab-separated values (first row = header)
- `.json` — newline-delimited JSON objects (one per line)

Parquet support is out of scope for the in-memory golden runner (no managed Parquet reader
is included in `BogDb.Tests`). Parquet-backed COPY corpora must be tested separately.
