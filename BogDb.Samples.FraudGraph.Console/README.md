# BogDb.Samples.FraudGraph.Console

**Theme:** Financial transaction graph — fraud ring detection via structural pattern matching  
**Stack:** .NET 9 Console · BogDB in-memory graph · coloured terminal output

---

## What this sample demonstrates

A headless console application that loads a BogDB property graph of 150 financial accounts, 800+ transactions, 60 device/IP/phone identifiers, and 3 planted fraud rings. It provides five command-line tools for fraud investigation — each backed by a graph query that would require impractical multi-level self-joins in SQL.

| Entity | Count | Details |
|---|---|---|
| `Account` | 150 | 120 legitimate (CLEAN), 25 in 3 fraud rings (FLAGGED/CONFIRMED_FRAUD) |
| `Transaction` | 820+ | Legit: salary, utilities, retail; Ring: structuring, rapid round-trips |
| `Identifier` | 60+ | IP addresses, device fingerprints, phone numbers |
| `Rule` | 5 | Velocity, Structuring, Rapid Round-Trip, New Account, Money Mule |

---

## The graph-native moment

Fraud rings share identifiers (IP, device, phone) across multiple accounts. The detection query — impossible in SQL without recursive CTEs — is a single Cypher pattern:

```cypher
-- Find accounts sharing ≥ 2 identifiers with a flagged account
MATCH (flagged:Account)-[:USED]->(id:Identifier)<-[:USED]-(suspect:Account)
WHERE (flagged.status = 'FLAGGED' OR flagged.status = 'CONFIRMED_FRAUD')
  AND suspect <> flagged
WITH suspect, COUNT(DISTINCT id) AS shared_ids
WHERE shared_ids >= 2
MATCH (suspect)-[:MADE]->(t:Transaction)
RETURN suspect.id, suspect.name, shared_ids,
       COUNT(t) AS tx_count, SUM(t.amount) AS total_exposure
ORDER BY shared_ids DESC, total_exposure DESC
```

The `(flagged)-[:USED]->(id)<-[:USED]-(suspect)` diamond pattern finds all accounts sharing any identifier node — without needing a self-join table or any intermediate materialisation.

---

## Graph schema

```
(Account)-[:MADE]->(Transaction)-[:TO]->(Account)   ← money movement
(Account)-[:USED]->(Identifier)                      ← shared device/IP/phone
(Account)-[:LINKED_TO {reason}]->(Account)            ← explicit association
(Transaction)-[:FLAGGED_BY {score}]->(Rule)           ← rule engine hits
```

---

## Fraud ring seed data

Three rings are intentionally planted:

| Ring | Size | Status | Pattern |
|---|---|---|---|
| Ring 1 | 8 members | FLAGGED | Structuring below $10k threshold |
| Ring 2 | 10 members | CONFIRMED_FRAUD | Rapid round-trips + shared IP |
| Ring 3 | 7 members | FLAGGED | Shared device fingerprint + phone |

Each ring: all members share the same IP, device, and phone `Identifier` nodes. Structuring transactions are $9,800–$9,950 (below the $10k reporting threshold). Round-trip transactions: member A sends to member B, then B sends back to A within the same time window.

---

## Commands

### `detect-rings [--min-shared N]`
Find all accounts sharing ≥ N identifiers with a flagged/confirmed-fraud account, ranked by shared-ID count and total transaction exposure.

```
$ dotnet run -- detect-rings --min-shared 2

  ● [CONFIRMED_FRAUD  ]  acc-ring2-00  Ring2 Member A      shared_ids=3  txns=30  exposure=$87,432.10
  ● [FLAGGED          ]  acc-ring1-00  Ring1 Member A      shared_ids=3  txns=24  exposure=$72,150.00
  ...
```

**Key Cypher:** `COUNT(DISTINCT id)` guard + `WITH … WHERE count >= N` (graph HAVING equivalent).

---

### `trace <account-id>`
Print the 2-hop neighbourhood of any account: connected accounts, shared identifiers, and outgoing transactions.

```
$ dotnet run -- trace acc-ring2-00
```

**Key Cypher:** `MATCH (a)-[r:MADE|LINKED_TO*1..2]-(other)` — multi-type variable-length path.

---

### `shortest-path <from-id> <to-id>`
Follow-the-money shortest path chain between any two accounts.

```
$ dotnet run -- shortest-path acc-0000 acc-ring1-03
```

**Key Cypher:** `shortestPath((a)-[:MADE|TO|LINKED_TO*]-(b))` — multi-relationship-type shortest path.

---

### `score-all [--top N]`
Composite risk score for every account. Formula: `(shared_id_count × 30) + (flagged_tx_count × 15) + tx_velocity_bonus`.

```
$ dotnet run -- score-all --top 15
```

**Key Cypher:** `OPTIONAL MATCH` chains, `COUNT(DISTINCT …)`, `CASE WHEN`, composite scoring in a single query pass.

---

### `explain "<cypher>"`
Run any Cypher and pretty-print results as a table. Doubles as an embedded REPL.

```
$ dotnet run -- explain "MATCH (a:Account) RETURN a.name, a.status ORDER BY a.status LIMIT 10"
```

---

### Interactive REPL
Run without arguments to enter an interactive prompt:

```
$ dotnet run

  fraudgraph> detect-rings
  fraudgraph> trace acc-ring2-00
  fraudgraph> score-all --top 10
  fraudgraph> exit
```

---

## Running the sample

```bash
cd BogDb.Samples.FraudGraph.Console
dotnet run                              # interactive REPL
dotnet run -- detect-rings              # one-shot
dotnet run -- detect-rings --min-shared 3
dotnet run -- trace acc-ring2-00
dotnet run -- shortest-path acc-0000 acc-ring1-03
dotnet run -- score-all --top 20
dotnet run -- explain "MATCH (a:Account) RETURN a.status, COUNT(*) ORDER BY COUNT(*) DESC"
```

The graph seeds in ~100ms and is ready immediately.

---

## Key APIs demonstrated

| API | Used in |
|---|---|
| `BogDatabase.CreateInMemory()` | Constructor |
| `EnsureNodeTable` | `Account`, `Transaction`, `Identifier`, `Rule` |
| `EnsureRelTable` | `MADE`, `TO`, `USED`, `LINKED_TO`, `FLAGGED_BY` |
| `UpsertNodeById` | Seed — 150 accounts, 820+ transactions, 60+ identifiers |
| `UpsertRelationshipById` | All 1,200+ edges with optional properties |
| `BeginWriteTransaction` / `Commit` | Schema + seed in two separate transactions |
| `conn.Query(cypher)` | All commands via `Execute()` |
| `(a)-[:USED]->(id)<-[:USED]-(b)` | Diamond identifier-sharing pattern |
| `COUNT(DISTINCT id)` + `WITH … WHERE` | Graph-level HAVING equivalent |
| `MATCH (a)-[:MADE\|LINKED_TO*1..2]-(b)` | Multi-type variable-length traversal |
| `shortestPath((a)-[:MADE\|TO\|LINKED_TO*]-(b))` | Multi-type shortest money path |
| `OPTIONAL MATCH` chains | Composite risk scoring without NULL errors |
| `CASE WHEN … THEN … END` | Velocity bonus in score formula |
| `FLAGGED_BY {score}` | Weighted rule confidence on relationship |

---

## Why console (not Blazor)?

This sample keeps the focus entirely on BogDB query patterns — no UI framework code dilutes the demonstration. It acts as the reference for embedding BogDB in a backend service, headless microservice, or batch fraud pipeline, complementing the visual Blazor samples.
