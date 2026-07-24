using System;
using System.Collections.Generic;
using System.IO;
using BogDb.Core.Main;
using Xunit;

namespace BogDb.Tests.Adversarial;

/// <summary>
/// Regression suite for the defects surfaced by the 2026-07-24 adversarial multi-agent review and then
/// reproduced by execution. Every test asserts the CORRECT behavior, so it is RED while the defect is
/// present and turns GREEN when the defect is fixed — this file is the living scoreboard for the audit.
///
/// All tests carry <c>[Trait("Category", "AdversarialFinding")]</c>. To run a green gate that excludes the
/// still-open defects:  <c>dotnet test --filter "Category!=AdversarialFinding"</c>. To run only these:
/// <c>dotnet test --filter "Category=AdversarialFinding"</c>.
///
/// Status legend in each test's doc comment:
///   CONFIRMED — reproduced by execution on 2026-07-24 (fails today).
///   FIXED     — defect addressed; test now green (annotated with the fixing commit/area).
/// </summary>
[Trait("Category", "AdversarialFinding")]
public class AdversarialFindingTests
{
    private static BogConnection Mem(out BogDatabase db)
    {
        db = BogDatabase.CreateInMemory();
        return new BogConnection(db);
    }

    private static void Ok(BogDb.Core.Main.QueryResult.QueryResult r) => Assert.True(r.IsSuccess, r.ErrorMessage);

    private static int Count(BogConnection conn, string q)
    {
        var r = conn.Query(q);
        Assert.True(r.IsSuccess, "query failed: " + r.ErrorMessage);
        var n = 0;
        while (r.HasNext()) { r.GetNext(); n++; }
        return n;
    }

    // ============================================================================================
    // Domain A — lost writes through ordinary Cypher (highest severity: silent data loss)
    // ============================================================================================

    /// <summary>F01 (CONFIRMED) — multi-row projection CREATE writes only one row.
    /// Root: PhysicalInsert pull loop is single-shot (_hasExecuted latch); the child produces N rows
    /// but CREATE fires once. Verify by: 3 people, one Tag per person, expect 3 Tags.</summary>
    [Fact]
    public void F01_MatchThenCreate_CreatesOneRowPerMatchedRow()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE Person(id INT64 PRIMARY KEY)"));
            Ok(conn.Query("CREATE NODE TABLE Tag(id INT64 PRIMARY KEY)"));
            for (var i = 1; i <= 3; i++) Ok(conn.Query($"CREATE (:Person {{id:{i}}})"));

            Ok(conn.Query("MATCH (p:Person) CREATE (:Tag {id:p.id})"));

            Assert.Equal(3, Count(conn, "MATCH (t:Tag) RETURN t.id"));
        }
    }

    /// <summary>F01b (CONFIRMED) — the same single-shot latch also caps relationship creation.
    /// MATCH producing N pairs then CREATE an edge yields one edge.</summary>
    [Fact]
    public void F01b_MatchThenCreateRelationship_CreatesOneEdgePerMatchedPair()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE Person(id INT64 PRIMARY KEY)"));
            Ok(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)"));
            for (var i = 1; i <= 3; i++) Ok(conn.Query($"CREATE (:Person {{id:{i}}})"));

            // Cartesian self-join minus self-loops would be 6 pairs; a hub-and-spoke is simpler to count:
            // person 1 KNOWS everyone (ids 2,3) -> 2 edges expected.
            Ok(conn.Query("MATCH (a:Person {id:1}), (b:Person) WHERE b.id <> 1 CREATE (a)-[:KNOWS]->(b)"));

            Assert.Equal(2, Count(conn, "MATCH (:Person {id:1})-[:KNOWS]->(b:Person) RETURN b.id"));
        }
    }

    /// <summary>F02 (CONFIRMED, severe) — Cypher CREATE on a file-backed DB is silently lost.
    /// Root: PhysicalInsert gates the in-memory table Upsert on IsInMemory; on disk it writes only the
    /// graph log, but the scan reads NodeTables. Lost in every tx mode, same-session and after reopen.
    /// (The UpsertNode API persists correctly — that is why nothing else hit this.)</summary>
    [Fact]
    public void F02_FileBackedCypherCreate_IsVisibleAndPersists()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bogdb-F02-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            using (var db = BogDatabase.Open(path))
            using (var conn = new BogConnection(db))
            {
                Ok(conn.Query("CREATE NODE TABLE Person(id INT64 PRIMARY KEY, name STRING)"));
                Ok(conn.Query("CREATE (:Person {id:1, name:'Alice'})"));
                Assert.Equal(1, Count(conn, "MATCH (p:Person) RETURN p.id"));   // same session
            }
            using (var db = BogDatabase.Open(path))
            using (var conn = new BogConnection(db))
                Assert.Equal(1, Count(conn, "MATCH (p:Person) RETURN p.id"));   // after reopen
        }
        finally { try { Directory.Delete(path, true); } catch { } }
    }

    // ============================================================================================
    // Domain B — silent wrong results (no error, wrong answer)
    // ============================================================================================

    /// <summary>F03 (CONFIRMED) — a join above a WITH-aggregate can never match on a carried node var.
    /// Root: PhysicalAggregate nulls the shared CurrentVariableIds after draining; the downstream
    /// ValueHashJoin hashes null and never matches.</summary>
    [Fact]
    public void F03_WithAggregateThenMatchOnCarriedNode_StillMatches()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE Person(id INT64 PRIMARY KEY)"));
            Ok(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)"));
            Ok(conn.Query("CREATE (:Person {id:1})"));
            Ok(conn.Query("CREATE (:Person {id:2})"));
            Ok(conn.Query("MATCH (a:Person {id:1}),(b:Person {id:2}) CREATE (a)-[:KNOWS]->(b)"));

            Assert.Equal(1, Count(conn,
                "MATCH (a:Person) WITH a, count(*) AS c MATCH (a)-[:KNOWS]->(b) RETURN a.id, b.id"));
        }
    }

    /// <summary>F04 (CONFIRMED) — UNWIND after MATCH drops the MATCH bindings.
    /// Root: PhysicalUnwind never pulls Children[0]; it evaluates its list once as a source. A blessed
    /// tck.golden.json already froze the wrong (MATCH-less) output.</summary>
    [Fact]
    public void F04_MatchThenUnwind_KeepsMatchBindings()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE Person(id INT64 PRIMARY KEY, name STRING)"));
            Ok(conn.Query("CREATE (:Person {id:1, name:'Alice'})"));
            Ok(conn.Query("CREATE (:Person {id:2, name:'Bob'})"));

            var r = conn.Query("MATCH (p:Person) UNWIND [1,2] AS k RETURN p.name AS name, k");
            Ok(r);
            var rows = new List<(string?, long)>();
            while (r.HasNext()) { var row = r.GetNext(); rows.Add((row.GetString("name"), row.GetInt64("k"))); }

            Assert.Equal(4, rows.Count);                        // 2 people x 2 list items
            Assert.DoesNotContain(rows, t => t.Item1 is null);  // MATCH binding preserved
        }
    }

    /// <summary>F05 (CONFIRMED) — MERGE with a null key silently matches an arbitrary existing node.
    /// Root: null key is dropped from the expected-property map; MatchesProperties then returns true
    /// vacuously for the first scanned row.</summary>
    [Fact]
    public void F05_MergeNullKey_DoesNotMatchUnrelatedNode()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE P(id INT64 PRIMARY KEY, email STRING)"));
            Ok(conn.Query("CREATE (:P {id:1, email:'real@x.com'})"));

            var r = conn.Query("MERGE (n:P {email:$e}) RETURN n.id AS id",
                new Dictionary<string, object?> { ["e"] = null });
            if (r.IsSuccess && r.HasNext())
            {
                var matched = r.GetNext().GetValue("id");
                Assert.False(matched is long l && l == 1L, "MERGE null key matched unrelated node id=1");
            }
        }
    }

    /// <summary>F06 (CONFIRMED) — STARTS WITH index scan returns rows whose value no longer matches.
    /// Root: prefix scans skip the equality re-validation that equality scans run, and SET only Puts the
    /// new key without removing the old one, so the stale key survives with no residual filter.</summary>
    [Fact]
    public void F06_StartsWithIndex_AfterSet_NoStaleRow()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE C(id INT64 PRIMARY KEY, name STRING)"));
            Ok(conn.Query("CREATE (:C {id:1, name:'Newark'})"));
            Ok(conn.Query("CALL create_index('C','name') RETURN *"));
            Ok(conn.Query("MATCH (c:C {id:1}) SET c.name = 'Springfield'"));

            Assert.Equal(0, Count(conn, "MATCH (c:C) WHERE c.name STARTS WITH 'New' RETURN c.id"));
        }
    }

    /// <summary>F07 (CONFIRMED) — re-upsert/SET of a non-unique indexed row duplicates it in scans.
    /// Root: posting-list Put dedups only against the tail element; a touched offset that is not the tail
    /// gets appended again, so the scan (which has no seen-set) emits the node twice.</summary>
    [Fact]
    public void F07_NonUniqueIndex_ReSet_NoDuplicateHit()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE C(id INT64 PRIMARY KEY, city STRING)"));
            Ok(conn.Query("CREATE (:C {id:1, city:'NYC'})"));
            Ok(conn.Query("CREATE (:C {id:2, city:'NYC'})"));
            Ok(conn.Query("CALL create_index('C','city') RETURN *"));
            Ok(conn.Query("MATCH (c:C {id:1}) SET c.city = 'NYC'"));   // re-set the same value

            Assert.Equal(2, Count(conn, "MATCH (c:C) WHERE c.city = 'NYC' RETURN c.id"));
        }
    }

    /// <summary>F08 (CONFIRMED) — AND of two predicates on one indexed property executes as OR.
    /// Root: the planner flattens conjuncts/disjuncts into one untagged list and chains same-property
    /// index lookups with LogicalUnionAll, retaining no residual filter.</summary>
    [Fact]
    public void F08_AndOnSameIndexedProperty_Intersects()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE U(id INT64 PRIMARY KEY, tag STRING)"));
            Ok(conn.Query("CREATE (:U {id:1, tag:'a'})"));
            Ok(conn.Query("CREATE (:U {id:2, tag:'c'})"));
            Ok(conn.Query("CALL create_index('U','tag') RETURN *"));

            Assert.Equal(0, Count(conn, "MATCH (u:U) WHERE u.tag = 'a' AND u.tag = 'c' RETURN u.id"));
        }
    }

    /// <summary>F09 (CONFIRMED) — an indexed multi-label node pattern drops all labels but the first.
    /// Root: the index-backed plan uses TableNames[0] with no label-count guard, called before the
    /// multi-label UnionAll branch. The same file guards this correctly in three sibling methods.</summary>
    [Fact]
    public void F09_MultiLabelIndexedPattern_KeepsAllLabels()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE Person(id INT64 PRIMARY KEY, name STRING)"));
            Ok(conn.Query("CREATE NODE TABLE Company(id INT64 PRIMARY KEY, name STRING)"));
            Ok(conn.Query("CREATE (:Person {id:1, name:'Acme'})"));
            Ok(conn.Query("CREATE (:Company {id:2, name:'Acme'})"));
            Ok(conn.Query("CALL create_index('Person','name') RETURN *"));

            Assert.Equal(2, Count(conn, "MATCH (n:Person|Company) WHERE n.name = 'Acme' RETURN n.name"));
        }
    }

    /// <summary>F10 (CONFIRMED) — create_index after a delete numbers postings by visible-row position
    /// while lookups resolve raw physical row index, so indexed lookups miss live nodes.
    /// Root: NodePropertyIndex.Rebuild does a dense offset++ over tombstone-skipping EnumerateRows,
    /// but TryGetByOffset resolves through the physical row-key index.</summary>
    [Fact]
    public void F10_RebuildAfterDelete_IndexAgreesWithFullScan()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE A(id INT64 PRIMARY KEY, wing STRING)"));
            for (var i = 1; i <= 4; i++)
                Ok(conn.Query($"CREATE (:A {{id:{i}, wing:'{(i % 2 == 0 ? "east" : "west")}'}})"));
            Ok(conn.Query("MATCH (a:A {id:1}) DELETE a"));           // tombstone before the index build
            Ok(conn.Query("CALL create_index('A','wing') RETURN *"));

            Assert.Equal(2, Count(conn, "MATCH (a:A) WHERE a.wing = 'east' RETURN a.id")); // ids 2,4
        }
    }

    /// <summary>F11 (CONFIRMED) — ROLLBACK of a DELETE leaves secondary-index entries stripped.
    /// Root: RemoveNodeFromIndexes mutates the index inline with no tx.TrackVersionedAction, so rollback
    /// replays only the row undo; the core index is the outlier vs the extension indexes.</summary>
    [Fact]
    public void F11_RollbackDelete_RestoresIndexEntries()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE A(id INT64 PRIMARY KEY, wing STRING)"));
            Ok(conn.Query("CREATE (:A {id:1, wing:'east'})"));
            Ok(conn.Query("CREATE (:A {id:2, wing:'east'})"));
            Ok(conn.Query("CALL create_index('A','wing') RETURN *"));

            conn.BeginWriteTransaction();
            Ok(conn.Query("MATCH (a:A) WHERE a.wing='east' DELETE a"));
            conn.Rollback();

            Assert.Equal(2, Count(conn, "MATCH (a:A) WHERE a.wing = 'east' RETURN a.id"));
        }
    }

    /// <summary>F17 (CONFIRMED, exotic) — GROUP BY collapses type-distinct keys sharing a string form,
    /// while count(DISTINCT) keeps them separate — the engine contradicts itself.
    /// Root: the group key is a ToBogDbString concatenation (long 1 -> "1", string '1' -> "1").</summary>
    [Fact]
    public void F17_GroupBy_KeepsTypeDistinctKeysSeparate()
    {
        using var conn = Mem(out var db); using (db)
        {
            var r = conn.Query("UNWIND [1, '1'] AS v RETURN v, count(*) AS c");
            Ok(r);
            var groups = 0;
            while (r.HasNext()) { r.GetNext(); groups++; }
            Assert.Equal(2, groups);
        }
    }

    /// <summary>F20 (CONFIRMED) — max()/min() over INT64 returns a DOUBLE-typed value though the column
    /// is declared INT64. Root: the aggregate accumulators are double[]; long promotion is gated to the
    /// count family only, so a consumer unboxing (long) throws.</summary>
    [Fact]
    public void F20_MaxOverInt64_ReturnsInt64TypedValue()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE N(id INT64 PRIMARY KEY, age INT64)"));
            foreach (var a in new[] { 20, 35, 28 }) Ok(conn.Query($"CREATE (:N {{id:{a}, age:{a}}})"));

            var r = conn.Query("MATCH (n:N) RETURN max(n.age) AS m");
            Ok(r);
            Assert.True(r.HasNext());
            var row = r.GetNext();
            if (r.ColumnTypes[0].ToString().Contains("INT64"))
                Assert.IsType<long>(row.GetValue(0));
        }
    }

    // ============================================================================================
    // Domain C — liveness and caught crashes
    // ============================================================================================

    /// <summary>F21 (CONFIRMED) — disposing a connection with an open write tx never rolls back and
    /// permanently blocks all future writes. Root: Dispose nulls the field and disposes the client
    /// context but never Rollback()s; ClearTransactionNoLock only runs from Commit/Rollback; no reaper.</summary>
    [Fact]
    public void F21_DisposeWithOpenWriteTx_DoesNotBlockFutureWrites()
    {
        using var db = BogDatabase.CreateInMemory();
        using (var conn1 = new BogConnection(db))
        {
            Ok(conn1.Query("CREATE NODE TABLE P(id INT64 PRIMARY KEY)"));
            conn1.BeginWriteTransaction();
            conn1.Query("CREATE (:P {id:1})");
            // no commit/rollback — e.g. an exception escaping a `using` block
        }

        using var conn2 = new BogConnection(db);
        var res = conn2.Query("CREATE (:P {id:2})");
        Assert.True(res.IsSuccess, "write blocked after a disposed abandoned write tx: " + res.ErrorMessage);
    }

    /// <summary>F23 (CONFIRMED) — MERGE after a rel-pattern MATCH on the same rel table throws mid-scan.
    /// Root: ExpandRel hands out a lazy enumerator over the live _outAdj[srcId] list, and the MERGE's
    /// Insert appends to that same list. Same shape as the already-fixed TryLookupAll live-list bug.</summary>
    [Fact]
    public void F23_MergeAfterRelMatch_SameRelTable_NoCrash()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE Person(id STRING PRIMARY KEY)"));
            Ok(conn.Query("CREATE REL TABLE KNOWS(FROM Person TO Person)"));
            Ok(conn.Query("CREATE (:Person {id:'a'})"));
            Ok(conn.Query("CREATE (:Person {id:'b'})"));
            Ok(conn.Query("MATCH (a:Person {id:'a'}),(b:Person {id:'b'}) CREATE (a)-[:KNOWS]->(b)"));

            var res = conn.Query("MATCH (a:Person)-[:KNOWS]->(b:Person) MERGE (a)-[:KNOWS]->(c:Person {id:'c'})");
            Assert.True(res.IsSuccess, "MERGE after rel MATCH crashed: " + res.ErrorMessage);
        }
    }

    /// <summary>F24 (CONFIRMED) — max/min/sum/avg over a STRING (or temporal) column throws.
    /// Root: the numeric-aggregate branch unconditionally Convert.ToDouble's the value; the binder
    /// declares min/max return type = arg type, so it is a bind-valid query. ORDER BY name works.</summary>
    [Fact]
    public void F24_MaxOverStringColumn_Works()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE N(id INT64 PRIMARY KEY, name STRING)"));
            foreach (var n in new[] { "Alice", "Bob", "Carol" })
                Ok(conn.Query($"CREATE (:N {{id:{n.Length}, name:'{n}'}})"));

            var r = conn.Query("MATCH (n:N) RETURN max(n.name) AS m");
            Assert.True(r.IsSuccess, "max() over STRING failed: " + r.ErrorMessage);
            Assert.True(r.HasNext());
            Assert.Equal("Carol", r.GetNext().GetString("m"));
        }
    }

    /// <summary>F25 (CONFIRMED) — an extreme/Inf/NaN double crashes the structural comparer behind
    /// DISTINCT / MERGE / count(DISTINCT). Root: TryGetNumericValue does an unguarded Convert.ToDecimal
    /// which overflows above ~7.9e28. Caught at the connection boundary -> error result, not a crash.</summary>
    [Fact]
    public void F25_ExtremeDouble_Distinct_Works()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE M(id INT64 PRIMARY KEY, v DOUBLE)"));
            Ok(conn.Query("CREATE (:M {id:1, v:1.0e300})"));
            Ok(conn.Query("CREATE (:M {id:2, v:2.0e300})"));

            var r = conn.Query("MATCH (n:M) RETURN DISTINCT n.v AS v");
            Assert.True(r.IsSuccess, "DISTINCT over extreme double failed: " + r.ErrorMessage);
        }
    }

    // ============================================================================================
    // Domain D — data-loss on non-transactional DDL
    // ============================================================================================

    /// <summary>F18 (CONFIRMED) — ROLLBACK of a DROP TABLE does not restore the table; the rows and the
    /// catalog entry are gone (the follow-up MATCH errors "table does not exist"). The review flagged
    /// catalog DDL as non-transactional (no UndoRecordType.CATALOG_ENTRY registrations) and dissented
    /// on whether that is intended — so this test may resolve to a documented limitation rather than a
    /// fix, but the data-loss behavior is real either way.</summary>
    [Fact]
    public void F18_RollbackDropTable_RestoresTableAndRows()
    {
        using var conn = Mem(out var db); using (db)
        {
            Ok(conn.Query("CREATE NODE TABLE Person(id INT64 PRIMARY KEY)"));
            Ok(conn.Query("CREATE (:Person {id:1})"));

            conn.BeginWriteTransaction();
            Ok(conn.Query("DROP TABLE Person"));
            conn.Rollback();

            Assert.Equal(1, Count(conn, "MATCH (p:Person) RETURN p.id"));
        }
    }

    // Note: F26 (sequences are process-global static) could not be exercised — `CREATE SEQUENCE` is
    // parsed but "not yet implemented in the execution engine", so there is no query path to a
    // user-created sequence. The static-dictionary concern from the review remains unverified.
}
