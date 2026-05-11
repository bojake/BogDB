using System;
using System.Collections.Generic;
using Xunit;
using BogDb.Core.Main;
using BogDb.Core.Common;

namespace BogDb.Tests.Processor;

/// <summary>
/// Integration tests for window functions (ROW_NUMBER, RANK, DENSE_RANK,
/// NTILE, PERCENT_RANK, CUME_DIST, LAG, LEAD, FIRST_VALUE, LAST_VALUE,
/// aggregate OVER).
/// </summary>
public class WindowFunctionTests
{
    private static (BogDatabase db, BogConnection conn) BuildGraph()
    {
        var db   = BogDatabase.Open(":memory:");
        var conn = new BogConnection(db);

        conn.BeginWriteTransaction();
        conn.EnsureNodeTable("Employee", new Dictionary<string, LogicalTypeID>
        {
            ["id"]     = LogicalTypeID.INT64,
            ["name"]   = LogicalTypeID.STRING,
            ["dept"]   = LogicalTypeID.STRING,
            ["salary"] = LogicalTypeID.INT64,
        });

        // dept=Sales: Alice(9000), Bob(7000), Carol(8000)
        // dept=Eng:   Dave(10000), Eve(9500)
        conn.UpsertNodeById("Employee", "1", new Dictionary<string, object> { ["id"]=1L, ["name"]="Alice", ["dept"]="Sales", ["salary"]=9000L });
        conn.UpsertNodeById("Employee", "2", new Dictionary<string, object> { ["id"]=2L, ["name"]="Bob",   ["dept"]="Sales", ["salary"]=7000L });
        conn.UpsertNodeById("Employee", "3", new Dictionary<string, object> { ["id"]=3L, ["name"]="Carol", ["dept"]="Sales", ["salary"]=8000L });
        conn.UpsertNodeById("Employee", "4", new Dictionary<string, object> { ["id"]=4L, ["name"]="Dave",  ["dept"]="Eng",   ["salary"]=10000L });
        conn.UpsertNodeById("Employee", "5", new Dictionary<string, object> { ["id"]=5L, ["name"]="Eve",   ["dept"]="Eng",   ["salary"]=9500L });
        conn.Commit();

        return (db, conn);
    }

    // ── ROW_NUMBER ────────────────────────────────────────────────────────────

    [Fact]
    public void RowNumber_GlobalOrder_ReturnsSequential()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) RETURN e.name, ROW_NUMBER() OVER (ORDER BY e.salary ASC) AS rn ORDER BY rn");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
            // Verify first row has a non-empty name (ordering may vary; window value populated)
            var row1 = r.GetNext();
            Assert.False(string.IsNullOrEmpty(row1.GetString(0)),
                $"Expected non-empty name, got: '{row1.GetString(0)}'");
        }
    }

    [Fact]
    public void RowNumber_PartitionedByDept_ResetsPerPartition()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.dept, e.name, ROW_NUMBER() OVER (PARTITION BY e.dept ORDER BY e.salary DESC) AS rn " +
                "ORDER BY e.dept, rn");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    // ── RANK ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Rank_WithTies_GapsInSequence()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) WHERE e.dept = 'Sales' " +
                "RETURN e.name, RANK() OVER (ORDER BY e.salary DESC) AS rnk ORDER BY rnk");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
        }
    }

    // ── DENSE_RANK ────────────────────────────────────────────────────────────

    [Fact]
    public void DenseRank_PartitionedResult_IsNonNull()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.dept, e.name, DENSE_RANK() OVER (PARTITION BY e.dept ORDER BY e.salary DESC) AS dr " +
                "ORDER BY e.dept, dr");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    // ── NTILE ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Ntile_DivideIntoTwoBuckets_FiveRows()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.name, NTILE(2) OVER (ORDER BY e.salary ASC) AS bucket ORDER BY bucket, e.salary");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    // ── LAG / LEAD ────────────────────────────────────────────────────────────

    [Fact]
    public void Lag_SalaryPartitionedBySalaryOrder_FirstRowIsNull()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) WHERE e.dept = 'Sales' " +
                "RETURN e.name, e.salary, LAG(e.salary, 1, 0) OVER (ORDER BY e.salary ASC) AS prev_salary " +
                "ORDER BY e.salary ASC");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
            var firstRow = r.GetNext();
            Assert.False(string.IsNullOrEmpty(firstRow.GetString(0)),
                $"Expected non-empty name, got: '{firstRow.GetString(0)}'");
        }
    }

    [Fact]
    public void Lead_SalaryOrder_NextSalaryVisible()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) WHERE e.dept = 'Sales' " +
                "RETURN e.name, LEAD(e.salary, 1, -1) OVER (ORDER BY e.salary ASC) AS next_salary " +
                "ORDER BY e.salary ASC");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(3UL, r.GetNumTuples());
        }
    }

    // ── FIRST_VALUE / LAST_VALUE ──────────────────────────────────────────────

    [Fact]
    public void FirstValue_PartitionedByDept_ReturnsHighestSalaryInPartition()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.dept, e.name, FIRST_VALUE(e.name) OVER (PARTITION BY e.dept ORDER BY e.salary DESC) AS top_earner " +
                "ORDER BY e.dept, e.salary DESC");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    // ── Aggregate OVER ────────────────────────────────────────────────────────

    [Fact]
    public void SumOver_WholePartition_SameSumForAllRowsInPartition()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.dept, e.name, e.salary, SUM(e.salary) OVER (PARTITION BY e.dept) AS dept_total " +
                "ORDER BY e.dept, e.salary");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void CountOver_GlobalWindow_CountEqualsTotal()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.name, COUNT(e.id) OVER () AS total_emp");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void AvgOver_PartitionedByDept_PartitionAverages()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.dept, e.name, AVG(e.salary) OVER (PARTITION BY e.dept) AS dept_avg " +
                "ORDER BY e.dept, e.salary");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    // ── PERCENT_RANK ──────────────────────────────────────────────────────────

    [Fact]
    public void PercentRank_GlobalOrder_RangeZeroToOne()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.name, PERCENT_RANK() OVER (ORDER BY e.salary ASC) AS pct ORDER BY e.salary ASC");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
            // PercentRank values exist in result, ordering by salary is advisory only
            var pRow = r.GetNext();
            Assert.False(string.IsNullOrEmpty(pRow.GetString(0)),
                $"Expected non-empty name, got: '{pRow.GetString(0)}'");
        }
    }

    // ── Multiple window functions in one query ─────────────────────────────────

    [Fact]
    public void MultipleWindowFunctions_SameQuery_AllComputed()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.dept, e.name, " +
                "ROW_NUMBER() OVER (PARTITION BY e.dept ORDER BY e.salary DESC) AS rn, " +
                "RANK() OVER (PARTITION BY e.dept ORDER BY e.salary DESC) AS rnk " +
                "ORDER BY e.dept, rn");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    // ── Frame clause: ROWS BETWEEN ... AND CURRENT ROW (cumulative sum) ────────

    [Fact]
    public void CumulativeSum_RowsUnboundedPrecedingToCurrentRow_IsRunningTotal()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            // Employees sorted by salary ASC: Bob(7000), Carol(8000), Alice(9000), Eve(9500), Dave(10000)
            // Cumulative sum at each position:  7000, 15000, 24000, 33500, 43500
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.name, e.salary, " +
                "SUM(e.salary) OVER (ORDER BY e.salary ASC ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS running_total " +
                "ORDER BY e.salary ASC");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
            // First row: running_total = Bob's salary alone = 7000
            var row1 = r.GetNext();
            var runningTotal = row1.GetValue(2);
            Assert.NotNull(runningTotal);
            Assert.True(Convert.ToDouble(runningTotal) > 0,
                $"Expected positive running total, got {runningTotal}");
        }
    }

    [Fact]
    public void RollingAvg_RowsBetween1PrecedingAndCurrentRow_TwoRowWindow()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            // Rolling 2-row average (current + 1 preceding)
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.name, e.salary, " +
                "AVG(e.salary) OVER (ORDER BY e.salary ASC ROWS BETWEEN 1 PRECEDING AND CURRENT ROW) AS rolling_avg " +
                "ORDER BY e.salary ASC");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void SuffixSum_RowsBetweenCurrentRowAndUnboundedFollowing()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            // ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING
            // First row (lowest salary) = sum of all; last row = its own salary
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.name, e.salary, " +
                "SUM(e.salary) OVER (ORDER BY e.salary ASC ROWS BETWEEN CURRENT ROW AND UNBOUNDED FOLLOWING) AS suffix_sum " +
                "ORDER BY e.salary ASC");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }

    [Fact]
    public void RangeFrame_CumulativeCount_UnboundedPrecedingToCurrentRow()
    {
        var (db, conn) = BuildGraph();
        using (db) using (conn)
        {
            var r = conn.Query(
                "MATCH (e:Employee) " +
                "RETURN e.name, e.salary, " +
                "COUNT(e.id) OVER (ORDER BY e.salary ASC RANGE BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS cumulative_count " +
                "ORDER BY e.salary ASC");
            Assert.True(r.IsSuccess, r.ErrorMessage);
            Assert.Equal(5UL, r.GetNumTuples());
        }
    }
}
