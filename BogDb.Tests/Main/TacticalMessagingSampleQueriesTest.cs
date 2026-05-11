using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using BogDb.Samples.TacticalMessaging.Blazor.Services;

namespace BogDb.Tests.Main;

/// <summary>
/// Exercises the TacticalMessaging sample: verifies that all showcase Cypher queries
/// run successfully and that core analytics (dashboard stats, blast radius) produce
/// nontrivial results against the seeded sample data.
/// </summary>
public class TacticalMessagingSampleQueriesTest : IDisposable
{
    private readonly TacticalMessagingGraphService _service;
    private readonly SqliteConnection _sharedConnection;

    public TacticalMessagingSampleQueriesTest()
    {
        // Use a shared in-memory SQLite connection so data persists across contexts
        _sharedConnection = new SqliteConnection("DataSource=:memory:");
        _sharedConnection.Open();

        var factory = new SharedConnectionDbContextFactory(_sharedConnection);

        // Seed EF data
        using (var initCtx = factory.CreateDbContext())
        {
            initCtx.Database.EnsureCreated();
            TacticalMessagingSeedData.Seed(initCtx);
        }

        // Build graph service
        _service = new TacticalMessagingGraphService(factory);
        _service.WarmUp();
    }

    [Fact]
    public void DashboardStats_HasNonzeroData()
    {
        var stats = _service.GetDashboardStats();
        Assert.True(stats.StandardCount > 0, "Expected at least one standard");
        Assert.True(stats.MessageTypeCount > 0, "Expected at least one message type");
        Assert.True(stats.ComponentCount > 0, "Expected at least one component");
        Assert.True(stats.SubsystemCount > 0, "Expected at least one subsystem");
        Assert.True(stats.PlatformCount > 0, "Expected at least one platform");
        Assert.True(stats.BaselineCount > 0, "Expected at least one baseline");
        Assert.True(stats.ChangeEventCount > 0, "Expected at least one change event");
    }

    [Theory]
    [MemberData(nameof(ShowcaseQueryData))]
    public void ShowcaseQuery_ExecutesSuccessfully(string title, string tag, string cypher)
    {
        var result = _service.Execute(cypher);
        Assert.True(result.IsSuccess,
            $"Showcase query '{title}' [{tag}] failed: {result.Error}\nQuery:\n{cypher}");
        Assert.True(result.Columns.Count > 0,
            $"Showcase query '{title}' returned no columns");
        // Showcase queries are designed against seed data — they must return rows
        Assert.True(result.Rows.Count > 0,
            $"Showcase query '{title}' [{tag}] returned 0 rows — likely broken query or missing seed data");
    }

    [Fact]
    public void BlastRadius_CHG001_ReturnsNonEmptyChain()
    {
        var rows = _service.GetBlastRadius("CHG-001");
        Assert.True(rows.Count > 0,
            "Expected blast radius from CHG-001 to produce at least one impact row");
    }

    [Fact]
    public void CertificationChain_TCOMP001_ReturnsResults()
    {
        var rows = _service.GetCertificationChain("TCOMP-001");
        Assert.True(rows.Count > 0,
            "Expected certification chain for TCOMP-001 to produce at least one row");
    }

    [Fact]
    public void Baselines_HasMultiple()
    {
        var baselines = _service.GetBaselines();
        Assert.True(baselines.Count >= 2,
            $"Expected at least 2 baselines for drift comparison, got {baselines.Count}");
    }

    [Fact]
    public void BaselineDrift_FirstToLast_Computes()
    {
        var baselines = _service.GetBaselines();
        if (baselines.Count < 2) return;
        var drift = _service.GetBaselineDrift(baselines[0].ExtId, baselines[^1].ExtId);
        Assert.NotNull(drift);
    }

    [Fact]
    public void ConsensusReport_CompletesSuccessfully()
    {
        var consensus = _service.GetConsensusReport();
        // May be null if < 2 baselines, but should not throw
        if (consensus != null)
        {
            Assert.True(consensus.ScopeCount > 0, "Expected nonzero scope count");
        }
    }

    [Fact]
    public void ChangeEvents_HasEntries()
    {
        var events = _service.GetChangeEvents();
        Assert.True(events.Count > 0, "Expected at least one change event");
    }

    [Fact]
    public void FieldOverlap_ReturnsResults()
    {
        var rows = _service.GetFieldOverlap();
        // Fields shared across multiple standards — should exist in our seed data
        Assert.True(rows.Count > 0,
            "Expected at least one field shared across multiple standards");
    }

    [Fact]
    public void UnverifiedRequirements_DoesNotThrow()
    {
        // This exercises the EF Core path — should return a list without errors
        var rows = _service.GetUnverifiedRequirements();
        Assert.NotNull(rows);
    }

    public static IEnumerable<object[]> ShowcaseQueryData()
    {
        foreach (var q in TacticalMessagingGraphService.ShowcaseQueries)
            yield return [q.Title, q.Tag, q.Cypher];
    }

    public void Dispose()
    {
        _service.Dispose();
        _sharedConnection.Dispose();
    }

    /// <summary>Minimal IDbContextFactory using a shared in-memory SQLite connection.</summary>
    private sealed class SharedConnectionDbContextFactory : IDbContextFactory<TacticalMessagingDbContext>
    {
        private readonly SqliteConnection _conn;
        public SharedConnectionDbContextFactory(SqliteConnection conn) => _conn = conn;

        public TacticalMessagingDbContext CreateDbContext()
        {
            var options = new DbContextOptionsBuilder<TacticalMessagingDbContext>()
                .UseSqlite(_conn)
                .Options;
            return new TacticalMessagingDbContext(options);
        }
    }
}
