using System.Collections;
using System.Collections.Concurrent;
using BogDb.Core.Common;
using BogDb.Core.Extraction;
using BogDb.Core.Main;
using Microsoft.EntityFrameworkCore;

namespace BogDb.Samples.TacticalMessaging.Blazor.Services;

// ── View models ───────────────────────────────────────────────────────────────

public record DashboardStats(
    int StandardCount, int EditionCount, int FamilyCount, int MessageTypeCount,
    int FieldCount, int ProfileCount, int ComponentCount, int SubsystemCount,
    int PlatformCount, int RequirementCount, int TestCaseCount,
    int PassingTests, int FailingTests, int BaselineCount, int ChangeEventCount);

public record BaselineInfo(string ExtId, string Label, string SnapshotDate, bool Sealed, int EntryCount);

public record BlastRadiusRow(string FieldName, string ComponentName, string SubsystemName,
    string CapabilityName, string RequirementId, string TestName, string Verdict, string CertPackage);

public record CertChainRow(string ComponentName, string RequirementId, string ReqStatement,
    string TestName, string TestStatus, string Verdict, string CertPackage, string CertStatus);

public record PlatformExposureRow(string PlatformId, string PlatformType,
    string SubsystemName, string ComponentName, string ProfileName);

public record UnverifiedReqRow(string ReqId, string Statement, string Priority, string ComponentName);

public record FieldOverlapRow(string FieldName, int StandardCount, string Standards);

public record BaselineDriftSummary(
    string FromLabel, string ToLabel,
    int AddedNodes, int RemovedNodes, int AddedRels, int RemovedRels,
    string OverviewLabel,
    List<TopDeltaRow> TopGaining, List<TopDeltaRow> TopDeclining);

public record TopDeltaRow(string Key, int AddedCount, int RemovedCount, int NetDelta);

public record ConsensusResult(string ConsensusLabel, int ScopeCount,
    int AddedNodes, int RemovedNodes, int AddedRels, int RemovedRels,
    List<SelectionFamilyRow> Families);

public record SelectionFamilyRow(string Signature, List<string> ScopeKeys,
    List<string> SelectedNodeTables, List<string> SelectedRelTypes, int ScopeCount);

public record QueryResponse(bool IsSuccess, string Error,
    List<string> Columns, List<Dictionary<string, object?>> Rows, long ElapsedMs);

public record ShowcaseQuery(string Title, string Tag, string Cypher);

public record ChangeEventInfo(string ExtId, string Description, string Severity, string AffectingStandard);

// ── Graph Service ─────────────────────────────────────────────────────────────

public sealed class TacticalMessagingGraphService : IDisposable
{
    private readonly IDbContextFactory<TacticalMessagingDbContext> _factory;
    private readonly BogDatabase _db;
    private readonly BogConnection _conn;

    private readonly ConcurrentDictionary<string, GraphShard> _shards = new();
    private readonly ConcurrentDictionary<string, GraphShardLocalRuntime> _runtimes = new();
    private List<BaselineInfo> _baselines = [];

    public TacticalMessagingGraphService(IDbContextFactory<TacticalMessagingDbContext> factory)
    {
        _factory = factory;
        _db = BogDatabase.CreateInMemory();
        _conn = new BogConnection(_db);
        SetupSchema();
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public void WarmUp()
    {
        using var ctx = _factory.CreateDbContext();
        IngestAllData(ctx);

        var baselines = ctx.Baselines.OrderBy(b => b.SnapshotDate).ToList();
        var entries = ctx.BaselineEntries.ToList();

        _baselines = baselines.Select(b => new BaselineInfo(
            b.ExtId, b.Label, b.SnapshotDate, b.Sealed,
            entries.Count(e => e.BaselineId == b.Id))).ToList();

        foreach (var bl in baselines)
        {
            var blEntries = entries.Where(e => e.BaselineId == bl.Id).ToList();
            var shard = BuildShardForBaseline(ctx, bl, blEntries);
            _shards[bl.ExtId] = shard;
            _runtimes[bl.ExtId] = GraphShardLocalRuntime.LoadExtract(shard);
        }
    }

    public void Dispose() { _conn.Dispose(); _db.Dispose(); }

    // ── Public analytics ──────────────────────────────────────────────────────

    public DashboardStats GetDashboardStats()
    {
        using var ctx = _factory.CreateDbContext();
        return new DashboardStats(
            ctx.Standards.Count(), ctx.Editions.Count(), ctx.Families.Count(),
            ctx.MessageTypes.Count(), ctx.MessageFields.Count(), ctx.Profiles.Count(),
            ctx.Components.Count(), ctx.Subsystems.Count(), ctx.Platforms.Count(),
            ctx.Requirements.Count(), ctx.TestCases.Count(),
            ctx.TestCases.Count(t => t.Status == "PASSING"),
            ctx.TestCases.Count(t => t.Status == "FAILING"),
            ctx.Baselines.Count(), ctx.ChangeEvents.Count());
    }

    public List<BaselineInfo> GetBaselines() => _baselines;

    public List<ChangeEventInfo> GetChangeEvents()
    {
        using var ctx = _factory.CreateDbContext();
        return ctx.ChangeEvents.Select(c => new ChangeEventInfo(
            c.ExtId, c.Description, c.Severity, c.AffectingStandard)).ToList();
    }

    public List<BlastRadiusRow> GetBlastRadius(string changeEventExtId)
    {
        var cypher = @"
            MATCH (chg:ChangeEvent {id: $id})-[:AFFECTS]->(mf:MessageField)
            MATCH (tc:TranslatorComponent)-[:USES_FIELD]->(mf)
            MATCH (ss:Subsystem)-[:DEPENDS_ON]->(tc)
            MATCH (ss)-[:ENABLES]->(cap:MissionCapability)
            MATCH (tc)-[:SATISFIES]->(req:Requirement)-[:VERIFIED_BY]->(t:TestCase)
            MATCH (t)-[:PRODUCES]->(ea:EvidenceArtifact)-[:SUPPORTS]->(cp:CertificationPackage)
            RETURN DISTINCT mf.field_name, tc.name, ss.name, cap.name,
                   req.id, t.name, ea.verdict, cp.name
            ORDER BY cap.name, tc.name";
        // BogDb doesn't support $params in current C# API — inline the value
        cypher = cypher.Replace("$id", $"'{changeEventExtId}'");
        var r = _conn.Query(cypher);
        var rows = new List<BlastRadiusRow>();
        if (!r.IsSuccess) return rows;
        while (r.HasNext())
        {
            var row = r.GetNext().GetAsDictionary();
            rows.Add(new BlastRadiusRow(
                row.GetValueOrDefault("mf.field_name")?.ToString() ?? "",
                row.GetValueOrDefault("tc.name")?.ToString() ?? "",
                row.GetValueOrDefault("ss.name")?.ToString() ?? "",
                row.GetValueOrDefault("cap.name")?.ToString() ?? "",
                row.GetValueOrDefault("req.id")?.ToString() ?? "",
                row.GetValueOrDefault("t.name")?.ToString() ?? "",
                row.GetValueOrDefault("ea.verdict")?.ToString() ?? "",
                row.GetValueOrDefault("cp.name")?.ToString() ?? ""));
        }
        return rows;
    }

    public List<CertChainRow> GetCertificationChain(string componentExtId)
    {
        var cypher = $@"
            MATCH (tc:TranslatorComponent {{id: '{componentExtId}'}})-[:SATISFIES]->(req:Requirement)
                  -[:VERIFIED_BY]->(t:TestCase)-[:PRODUCES]->(ea:EvidenceArtifact)
                  -[:SUPPORTS]->(cp:CertificationPackage)
            RETURN tc.name, req.id, req.statement, t.name, t.status, ea.verdict, cp.name, cp.status
            ORDER BY req.id";
        var r = _conn.Query(cypher);
        var rows = new List<CertChainRow>();
        if (!r.IsSuccess) return rows;
        while (r.HasNext())
        {
            var row = r.GetNext().GetAsDictionary();
            rows.Add(new CertChainRow(
                row.GetValueOrDefault("tc.name")?.ToString() ?? "",
                row.GetValueOrDefault("req.id")?.ToString() ?? "",
                row.GetValueOrDefault("req.statement")?.ToString() ?? "",
                row.GetValueOrDefault("t.name")?.ToString() ?? "",
                row.GetValueOrDefault("t.status")?.ToString() ?? "",
                row.GetValueOrDefault("ea.verdict")?.ToString() ?? "",
                row.GetValueOrDefault("cp.name")?.ToString() ?? "",
                row.GetValueOrDefault("cp.status")?.ToString() ?? ""));
        }
        return rows;
    }

    public List<PlatformExposureRow> GetAffectedPlatforms(string editionExtId)
    {
        var cypher = $@"
            MATCH (se:StandardEdition {{id: '{editionExtId}'}})-[:DEFINES_FAMILY]->(fam:MessageFamily)-[:HAS_TYPE]->(mt:MessageType)
            MATCH (p:Profile)-[:CONSTRAINS]->(mt)
            MATCH (tc:TranslatorComponent)-[:IMPLEMENTS]->(p)
            MATCH (ss:Subsystem)-[:DEPENDS_ON]->(tc)
            MATCH (ss)-[:ON_PLATFORM]->(plt:Platform)
            RETURN DISTINCT plt.id, plt.platform_type, ss.name, tc.name, p.name
            ORDER BY plt.platform_type, plt.id";
        var r = _conn.Query(cypher);
        var rows = new List<PlatformExposureRow>();
        if (!r.IsSuccess) return rows;
        while (r.HasNext())
        {
            var row = r.GetNext().GetAsDictionary();
            rows.Add(new PlatformExposureRow(
                row.GetValueOrDefault("plt.id")?.ToString() ?? "",
                row.GetValueOrDefault("plt.platform_type")?.ToString() ?? "",
                row.GetValueOrDefault("ss.name")?.ToString() ?? "",
                row.GetValueOrDefault("tc.name")?.ToString() ?? "",
                row.GetValueOrDefault("p.name")?.ToString() ?? ""));
        }
        return rows;
    }

    public List<UnverifiedReqRow> GetUnverifiedRequirements()
    {
        // Separate queries to avoid OPTIONAL MATCH issues
        using var ctx = _factory.CreateDbContext();
        var compReqs = ctx.ComponentRequirements
            .Include(cr => cr.Component).Include(cr => cr.Requirement)
            .ToList();
        var reqTests = ctx.RequirementTests
            .Include(rt => rt.TestCase)
            .ToList();
        var testEvidence = ctx.TestEvidences
            .Include(te => te.Evidence)
            .ToList();

        var rows = new List<UnverifiedReqRow>();
        foreach (var cr in compReqs)
        {
            var testLinks = reqTests.Where(rt => rt.RequirementId == cr.RequirementId).ToList();
            bool hasPass = testLinks.Any(rt =>
            {
                var evs = testEvidence.Where(te => te.TestCaseId == rt.TestCaseId);
                return evs.Any(e => e.Evidence.Verdict == "PASS");
            });
            if (!hasPass)
                rows.Add(new UnverifiedReqRow(cr.Requirement.ExtId, cr.Requirement.Statement,
                    cr.Requirement.Priority, cr.Component.Name));
        }
        return rows.OrderBy(r => r.Priority).ToList();
    }

    public List<FieldOverlapRow> GetFieldOverlap()
    {
        var cypher = @"
            MATCH (s:Standard)-[:HAS_EDITION]->(se:StandardEdition)-[:DEFINES_FAMILY]->(mf_fam:MessageFamily)
                  -[:HAS_TYPE]->(mt:MessageType)-[:HAS_FIELD]->(f:MessageField)
            WITH f.field_name AS fname, collect(DISTINCT s.alias) AS stds
            WHERE size(stds) >= 2
            RETURN fname, size(stds) AS std_count, stds
            ORDER BY std_count DESC, fname";
        var r = _conn.Query(cypher);
        var rows = new List<FieldOverlapRow>();
        if (!r.IsSuccess) return rows;
        while (r.HasNext())
        {
            var row = r.GetNext().GetAsDictionary();
            var stdList = FormatListValue(row.GetValueOrDefault("stds"));
            rows.Add(new FieldOverlapRow(
                row.GetValueOrDefault("fname")?.ToString() ?? "",
                int.TryParse(row.GetValueOrDefault("std_count")?.ToString(), out var c) ? c : 0,
                stdList));
        }
        return rows;
    }

    private static string FormatListValue(object? value)
    {
        if (value is null)
            return string.Empty;

        if (value is string s)
            return s;

        if (value is IEnumerable items)
        {
            var values = new List<string>();
            foreach (var item in items)
            {
                if (item is not null)
                    values.Add(item.ToString() ?? string.Empty);
            }

            return string.Join(", ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
        }

        return value.ToString() ?? string.Empty;
    }

    public BaselineDriftSummary? GetBaselineDrift(string fromExtId, string toExtId)
    {
        if (!_runtimes.TryGetValue(toExtId, out var toRt)) return null;
        if (!_shards.TryGetValue(fromExtId, out var fromShard)) return null;
        var fromBl = _baselines.FirstOrDefault(b => b.ExtId == fromExtId);
        var toBl = _baselines.FirstOrDefault(b => b.ExtId == toExtId);
        if (fromBl is null || toBl is null) return null;

        var overview = toRt.BuildChangeReviewOverview(fromShard);
        var gaining = toRt.TopGainingNodeTables(fromShard, new GraphShardTopNSpec { Limit = 10 });
        var declining = toRt.TopDecliningNodeTables(fromShard, new GraphShardTopNSpec { Limit = 10 });

        return new BaselineDriftSummary(
            fromBl.Label, toBl.Label,
            overview.AddedNodeCount, overview.RemovedNodeCount,
            overview.AddedRelCount, overview.RemovedRelCount,
            overview.SummaryLabel,
            gaining.Rows.Select(r => new TopDeltaRow(r.TableName, r.AddedCount, r.RemovedCount, r.NetDelta)).ToList(),
            declining.Rows.Select(r => new TopDeltaRow(r.TableName, r.AddedCount, r.RemovedCount, r.NetDelta)).ToList());
    }

    public ConsensusResult? GetConsensusReport()
    {
        var labels = _baselines.Select(b => b.ExtId).ToList();
        if (labels.Count < 2) return null;

        var reviews = new Dictionary<string, GraphShardMultiGroupChangeReview>();
        for (var i = 1; i < labels.Count; i++)
        {
            var fromLabel = labels[i - 1];
            var toLabel = labels[i];
            if (!_shards.TryGetValue(fromLabel, out var fromShard)) continue;
            if (!_runtimes.TryGetValue(toLabel, out var rt)) continue;

            reviews[$"{fromLabel}->{toLabel}"] = rt.BuildMultiGroupChangeReview(fromShard,
                nodeTables: ["TranslatorComponent", "Profile", "Requirement", "Standard"],
                relTypes: ["IMPLEMENTS", "SATISFIES", "DEPENDS_ON", "CONTAINS"]);
        }
        if (reviews.Count == 0) return null;

        var finalToLabel = labels[^1];
        if (!_runtimes.TryGetValue(finalToLabel, out var lastRt)) return null;

        var consensus = lastRt.BuildMultiGroupChangeReviewConsensus(reviews);
        var families = lastRt.BuildMultiGroupChangeReviewSelectionFamilies(reviews);

        return new ConsensusResult(
            consensus.CommonSummary.SummaryLabel,
            consensus.ScopeCount,
            consensus.CommonSummary.AddedNodeCount,
            consensus.CommonSummary.RemovedNodeCount,
            consensus.CommonSummary.AddedRelCount,
            consensus.CommonSummary.RemovedRelCount,
            families.Families.Select(f => new SelectionFamilyRow(
                f.Signature, f.Keys, f.SelectedNodeTableKeys,
                f.SelectedRelationshipTypeKeys, f.ScopeCount)).ToList());
    }

    public QueryResponse Execute(string cypher)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = _conn.Query(cypher);
        sw.Stop();
        if (!r.IsSuccess)
            return new QueryResponse(false, r.ErrorMessage ?? "Query failed", [], [], sw.ElapsedMilliseconds);
        var cols = r.ColumnNames.ToList();
        var rows = new List<Dictionary<string, object?>>();
        while (r.HasNext())
        {
            var row = r.GetNext();
            rows.Add(row.GetAsDictionary().ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
        }
        return new QueryResponse(true, string.Empty, cols, rows, sw.ElapsedMilliseconds);
    }

    public static readonly List<ShowcaseQuery> ShowcaseQueries =
    [
        new("Blast radius from J3.2 TRACK_NUMBER change", "impact",
            "MATCH (chg:ChangeEvent {id: 'CHG-001'})-[:AFFECTS]->(mf:MessageField)\nMATCH (tc:TranslatorComponent)-[:USES_FIELD]->(mf)\nMATCH (ss:Subsystem)-[:DEPENDS_ON]->(tc)\nMATCH (ss)-[:ENABLES]->(cap:MissionCapability)\nRETURN mf.field_name, tc.name, ss.name, cap.name\nORDER BY cap.name"),
        new("Full certification chain for TCOMP-001", "cert",
            "MATCH (tc:TranslatorComponent {id: 'TCOMP-001'})-[:SATISFIES]->(req:Requirement)\n      -[:VERIFIED_BY]->(t:TestCase)-[:PRODUCES]->(ea:EvidenceArtifact)\n      -[:SUPPORTS]->(cp:CertificationPackage)\nRETURN tc.name, req.id, t.name, ea.verdict, cp.name"),
        new("Platforms affected by Link 16 Rev C", "platform",
            "MATCH (se:StandardEdition {id: 'SE-L16-C'})-[:DEFINES_FAMILY]->(fam:MessageFamily)-[:HAS_TYPE]->(mt:MessageType)\nMATCH (p:Profile)-[:CONSTRAINS]->(mt)\nMATCH (tc:TranslatorComponent)-[:IMPLEMENTS]->(p)\nMATCH (ss:Subsystem)-[:DEPENDS_ON]->(tc)\nMATCH (ss)-[:ON_PLATFORM]->(plt:Platform)\nRETURN DISTINCT plt.id, plt.platform_type, ss.name, tc.name"),
        new("Cross-standard field overlap", "interop",
            "MATCH (s:Standard)-[:HAS_EDITION]->(se:StandardEdition)-[:DEFINES_FAMILY]->(fam:MessageFamily)\n      -[:HAS_TYPE]->(mt:MessageType)-[:HAS_FIELD]->(f:MessageField)\nWITH f.field_name AS fname, collect(DISTINCT s.alias) AS stds\nWHERE size(stds) >= 2\nRETURN fname, size(stds) AS std_count, stds\nORDER BY std_count DESC"),
        new("All translator components by status", "inventory",
            "MATCH (tc:TranslatorComponent)\nRETURN tc.id, tc.name, tc.version, tc.language, tc.status\nORDER BY tc.status, tc.name"),
        new("Message types per standard", "schema",
            "MATCH (s:Standard)-[:HAS_EDITION]->(se:StandardEdition)-[:DEFINES_FAMILY]->(fam:MessageFamily)\n      -[:HAS_TYPE]->(mt:MessageType)\nRETURN s.alias, count(mt) AS msg_count\nORDER BY msg_count DESC"),
        new("Subsystems and their enabled capabilities", "capability",
            "MATCH (ss:Subsystem)-[:ENABLES]->(cap:MissionCapability)\nRETURN ss.name, ss.domain, collect(cap.name) AS capabilities\nORDER BY ss.name"),
        new("Tests with non-PASS evidence", "gaps",
            "MATCH (t:TestCase)-[:PRODUCES]->(ea:EvidenceArtifact)\nWHERE ea.verdict <> 'PASS'\nRETURN t.name, t.status, ea.verdict, ea.artifact_type\nORDER BY ea.verdict, t.name"),
    ];

    // ── Schema ────────────────────────────────────────────────────────────────

    private void SetupSchema()
    {
        _conn.BeginWriteTransaction();

        // Node tables
        _conn.EnsureNodeTable("Standard", new() {
            ["id"] = LogicalTypeID.STRING, ["name"] = LogicalTypeID.STRING,
            ["alias"] = LogicalTypeID.STRING, ["status"] = LogicalTypeID.STRING,
            ["governing_body"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("StandardEdition", new() {
            ["id"] = LogicalTypeID.STRING, ["standard_id"] = LogicalTypeID.STRING,
            ["edition_label"] = LogicalTypeID.STRING, ["effective_date"] = LogicalTypeID.STRING,
            ["deprecated"] = LogicalTypeID.BOOL });
        _conn.EnsureNodeTable("MessageFamily", new() {
            ["id"] = LogicalTypeID.STRING, ["edition_id"] = LogicalTypeID.STRING,
            ["family_designator"] = LogicalTypeID.STRING, ["description"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("MessageType", new() {
            ["id"] = LogicalTypeID.STRING, ["family_id"] = LogicalTypeID.STRING,
            ["type_designator"] = LogicalTypeID.STRING, ["description"] = LogicalTypeID.STRING,
            ["direction"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("MessageField", new() {
            ["id"] = LogicalTypeID.STRING, ["message_type_id"] = LogicalTypeID.STRING,
            ["field_name"] = LogicalTypeID.STRING, ["data_category"] = LogicalTypeID.STRING,
            ["mandatory"] = LogicalTypeID.BOOL });
        _conn.EnsureNodeTable("Profile", new() {
            ["id"] = LogicalTypeID.STRING, ["name"] = LogicalTypeID.STRING,
            ["message_type_id"] = LogicalTypeID.STRING, ["platform_class"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("TranslatorComponent", new() {
            ["id"] = LogicalTypeID.STRING, ["name"] = LogicalTypeID.STRING,
            ["version"] = LogicalTypeID.STRING, ["language"] = LogicalTypeID.STRING,
            ["status"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("InterfaceContract", new() {
            ["id"] = LogicalTypeID.STRING, ["name"] = LogicalTypeID.STRING,
            ["protocol"] = LogicalTypeID.STRING, ["version"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("Subsystem", new() {
            ["id"] = LogicalTypeID.STRING, ["name"] = LogicalTypeID.STRING,
            ["domain"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("Platform", new() {
            ["id"] = LogicalTypeID.STRING, ["platform_type"] = LogicalTypeID.STRING,
            ["synthetic"] = LogicalTypeID.BOOL });
        _conn.EnsureNodeTable("Requirement", new() {
            ["id"] = LogicalTypeID.STRING, ["statement"] = LogicalTypeID.STRING,
            ["type"] = LogicalTypeID.STRING, ["priority"] = LogicalTypeID.STRING,
            ["allocating_standard"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("TestCase", new() {
            ["id"] = LogicalTypeID.STRING, ["name"] = LogicalTypeID.STRING,
            ["method"] = LogicalTypeID.STRING, ["pass_criteria"] = LogicalTypeID.STRING,
            ["status"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("EvidenceArtifact", new() {
            ["id"] = LogicalTypeID.STRING, ["artifact_type"] = LogicalTypeID.STRING,
            ["date_produced"] = LogicalTypeID.STRING, ["verdict"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("CertificationPackage", new() {
            ["id"] = LogicalTypeID.STRING, ["name"] = LogicalTypeID.STRING,
            ["certification_authority"] = LogicalTypeID.STRING, ["status"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("MissionCapability", new() {
            ["id"] = LogicalTypeID.STRING, ["name"] = LogicalTypeID.STRING,
            ["category"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("Baseline", new() {
            ["id"] = LogicalTypeID.STRING, ["label"] = LogicalTypeID.STRING,
            ["snapshot_date"] = LogicalTypeID.STRING, ["sealed"] = LogicalTypeID.BOOL });
        _conn.EnsureNodeTable("ChangeEvent", new() {
            ["id"] = LogicalTypeID.STRING, ["description"] = LogicalTypeID.STRING,
            ["severity"] = LogicalTypeID.STRING, ["affecting_standard"] = LogicalTypeID.STRING });

        // Rel tables
        _conn.EnsureRelTable("HAS_EDITION",    "Standard",            "StandardEdition",      new());
        _conn.EnsureRelTable("DEFINES_FAMILY", "StandardEdition",     "MessageFamily",         new());
        _conn.EnsureRelTable("HAS_TYPE",       "MessageFamily",       "MessageType",           new());
        _conn.EnsureRelTable("HAS_FIELD",      "MessageType",         "MessageField",          new() { ["mandatory"] = LogicalTypeID.BOOL });
        _conn.EnsureRelTable("CONSTRAINS",     "Profile",             "MessageType",           new());
        _conn.EnsureRelTable("IMPLEMENTS",     "TranslatorComponent", "Profile",               new() { ["implementation_version"] = LogicalTypeID.STRING });
        _conn.EnsureRelTable("USES_FIELD",     "TranslatorComponent", "MessageField",          new() { ["access"] = LogicalTypeID.STRING });
        _conn.EnsureRelTable("DEPENDS_ON",     "Subsystem",           "TranslatorComponent",   new() { ["dependency_type"] = LogicalTypeID.STRING });
        _conn.EnsureRelTable("ON_PLATFORM",    "Subsystem",           "Platform",              new() { ["deployment_date"] = LogicalTypeID.STRING });
        _conn.EnsureRelTable("SATISFIES",      "TranslatorComponent", "Requirement",           new());
        _conn.EnsureRelTable("VERIFIED_BY",    "Requirement",         "TestCase",              new());
        _conn.EnsureRelTable("PRODUCES",       "TestCase",            "EvidenceArtifact",      new() { ["run_date"] = LogicalTypeID.STRING });
        _conn.EnsureRelTable("SUPPORTS",       "EvidenceArtifact",    "CertificationPackage",  new());
        _conn.EnsureRelTable("ENABLES",        "Subsystem",           "MissionCapability",     new());
        _conn.EnsureRelTable("CONTAINS",       "Baseline",            "TranslatorComponent",   new());
        _conn.EnsureRelTable("AFFECTS",        "ChangeEvent",         "MessageField",          new() { ["severity"] = LogicalTypeID.STRING });

        _conn.Commit();
    }

    // ── Ingest ────────────────────────────────────────────────────────────────

    private void IngestAllData(TacticalMessagingDbContext ctx)
    {
        _conn.BeginWriteTransaction();

        foreach (var s in ctx.Standards)
            _conn.UpsertNodeById("Standard", s.ExtId, new() {
                ["id"] = s.ExtId, ["name"] = s.Name, ["alias"] = s.Alias,
                ["status"] = s.Status, ["governing_body"] = s.GoverningBody });

        foreach (var e in ctx.Editions.Include(e => e.Standard))
        {
            _conn.UpsertNodeById("StandardEdition", e.ExtId, new() {
                ["id"] = e.ExtId, ["standard_id"] = e.Standard.ExtId,
                ["edition_label"] = e.EditionLabel, ["effective_date"] = e.EffectiveDate,
                ["deprecated"] = e.Deprecated });
            _conn.UpsertRelationshipById("HAS_EDITION", e.Standard.ExtId, e.ExtId, new());
        }

        foreach (var f in ctx.Families.Include(f => f.Edition))
        {
            _conn.UpsertNodeById("MessageFamily", f.ExtId, new() {
                ["id"] = f.ExtId, ["edition_id"] = f.Edition.ExtId,
                ["family_designator"] = f.FamilyDesignator, ["description"] = f.Description });
            _conn.UpsertRelationshipById("DEFINES_FAMILY", f.Edition.ExtId, f.ExtId, new());
        }

        foreach (var mt in ctx.MessageTypes.Include(mt => mt.Family))
        {
            _conn.UpsertNodeById("MessageType", mt.ExtId, new() {
                ["id"] = mt.ExtId, ["family_id"] = mt.Family.ExtId,
                ["type_designator"] = mt.TypeDesignator, ["description"] = mt.Description,
                ["direction"] = mt.Direction });
            _conn.UpsertRelationshipById("HAS_TYPE", mt.Family.ExtId, mt.ExtId, new());
        }

        foreach (var mf in ctx.MessageFields.Include(mf => mf.MessageType))
        {
            _conn.UpsertNodeById("MessageField", mf.ExtId, new() {
                ["id"] = mf.ExtId, ["message_type_id"] = mf.MessageType.ExtId,
                ["field_name"] = mf.FieldName, ["data_category"] = mf.DataCategory,
                ["mandatory"] = mf.Mandatory });
            _conn.UpsertRelationshipById("HAS_FIELD", mf.MessageType.ExtId, mf.ExtId,
                new() { ["mandatory"] = mf.Mandatory });
        }

        foreach (var p in ctx.Profiles.Include(p => p.MessageType))
        {
            _conn.UpsertNodeById("Profile", p.ExtId, new() {
                ["id"] = p.ExtId, ["name"] = p.Name,
                ["message_type_id"] = p.MessageType.ExtId, ["platform_class"] = p.PlatformClass });
            _conn.UpsertRelationshipById("CONSTRAINS", p.ExtId, p.MessageType.ExtId, new());
        }

        foreach (var tc in ctx.Components)
            _conn.UpsertNodeById("TranslatorComponent", tc.ExtId, new() {
                ["id"] = tc.ExtId, ["name"] = tc.Name, ["version"] = tc.Version,
                ["language"] = tc.Language, ["status"] = tc.Status });

        foreach (var ic in ctx.Contracts)
            _conn.UpsertNodeById("InterfaceContract", ic.ExtId, new() {
                ["id"] = ic.ExtId, ["name"] = ic.Name,
                ["protocol"] = ic.Protocol, ["version"] = ic.Version });

        foreach (var ss in ctx.Subsystems)
            _conn.UpsertNodeById("Subsystem", ss.ExtId, new() {
                ["id"] = ss.ExtId, ["name"] = ss.Name, ["domain"] = ss.Domain });

        foreach (var plt in ctx.Platforms)
            _conn.UpsertNodeById("Platform", plt.ExtId, new() {
                ["id"] = plt.ExtId, ["platform_type"] = plt.PlatformType,
                ["synthetic"] = plt.Synthetic });

        foreach (var req in ctx.Requirements)
            _conn.UpsertNodeById("Requirement", req.ExtId, new() {
                ["id"] = req.ExtId, ["statement"] = req.Statement, ["type"] = req.Type,
                ["priority"] = req.Priority, ["allocating_standard"] = req.AllocatingStandard });

        foreach (var tc in ctx.TestCases)
            _conn.UpsertNodeById("TestCase", tc.ExtId, new() {
                ["id"] = tc.ExtId, ["name"] = tc.Name, ["method"] = tc.Method,
                ["pass_criteria"] = tc.PassCriteria, ["status"] = tc.Status });

        foreach (var ea in ctx.EvidenceArtifacts)
            _conn.UpsertNodeById("EvidenceArtifact", ea.ExtId, new() {
                ["id"] = ea.ExtId, ["artifact_type"] = ea.ArtifactType,
                ["date_produced"] = ea.DateProduced, ["verdict"] = ea.Verdict });

        foreach (var cp in ctx.CertificationPackages)
            _conn.UpsertNodeById("CertificationPackage", cp.ExtId, new() {
                ["id"] = cp.ExtId, ["name"] = cp.Name,
                ["certification_authority"] = cp.CertificationAuthority, ["status"] = cp.Status });

        foreach (var mc in ctx.MissionCapabilities)
            _conn.UpsertNodeById("MissionCapability", mc.ExtId, new() {
                ["id"] = mc.ExtId, ["name"] = mc.Name, ["category"] = mc.Category });

        foreach (var bl in ctx.Baselines)
            _conn.UpsertNodeById("Baseline", bl.ExtId, new() {
                ["id"] = bl.ExtId, ["label"] = bl.Label,
                ["snapshot_date"] = bl.SnapshotDate, ["sealed"] = bl.Sealed });

        foreach (var chg in ctx.ChangeEvents)
            _conn.UpsertNodeById("ChangeEvent", chg.ExtId, new() {
                ["id"] = chg.ExtId, ["description"] = chg.Description,
                ["severity"] = chg.Severity, ["affecting_standard"] = chg.AffectingStandard });

        // Relationship ingest
        foreach (var cp in ctx.ComponentProfiles.Include(cp => cp.Component).Include(cp => cp.Profile))
            _conn.UpsertRelationshipById("IMPLEMENTS", cp.Component.ExtId, cp.Profile.ExtId,
                new() { ["implementation_version"] = cp.ImplementationVersion });

        foreach (var cfu in ctx.ComponentFieldUsages.Include(c => c.Component).Include(c => c.Field))
            _conn.UpsertRelationshipById("USES_FIELD", cfu.Component.ExtId, cfu.Field.ExtId,
                new() { ["access"] = cfu.Access });

        foreach (var sc in ctx.SubsystemComponents.Include(s => s.Subsystem).Include(s => s.Component))
            _conn.UpsertRelationshipById("DEPENDS_ON", sc.Subsystem.ExtId, sc.Component.ExtId,
                new() { ["dependency_type"] = sc.DependencyType });

        foreach (var sp in ctx.SubsystemPlatforms.Include(s => s.Subsystem).Include(s => s.Platform))
            _conn.UpsertRelationshipById("ON_PLATFORM", sp.Subsystem.ExtId, sp.Platform.ExtId,
                new() { ["deployment_date"] = sp.DeploymentDate });

        foreach (var cr in ctx.ComponentRequirements.Include(c => c.Component).Include(c => c.Requirement))
            _conn.UpsertRelationshipById("SATISFIES", cr.Component.ExtId, cr.Requirement.ExtId, new());

        foreach (var rt in ctx.RequirementTests.Include(r => r.Requirement).Include(r => r.TestCase))
            _conn.UpsertRelationshipById("VERIFIED_BY", rt.Requirement.ExtId, rt.TestCase.ExtId, new());

        foreach (var te in ctx.TestEvidences.Include(t => t.TestCase).Include(t => t.Evidence))
            _conn.UpsertRelationshipById("PRODUCES", te.TestCase.ExtId, te.Evidence.ExtId,
                new() { ["run_date"] = te.RunDate });

        foreach (var ec in ctx.EvidenceCertifications.Include(e => e.Evidence).Include(e => e.Package))
            _conn.UpsertRelationshipById("SUPPORTS", ec.Evidence.ExtId, ec.Package.ExtId, new());

        foreach (var sc in ctx.SubsystemCapabilities.Include(s => s.Subsystem).Include(s => s.Capability))
            _conn.UpsertRelationshipById("ENABLES", sc.Subsystem.ExtId, sc.Capability.ExtId, new());

        // CONTAINS: baseline → components (for shard drift)
        foreach (var be in ctx.BaselineEntries.Include(b => b.Baseline).Where(b => b.EntityType == "TranslatorComponent"))
            _conn.UpsertRelationshipById("CONTAINS", be.Baseline.ExtId, be.EntityExtId, new());

        // AFFECTS: change → field
        foreach (var ct in ctx.ChangeEventTargets.Include(c => c.ChangeEvent).Where(c => c.TargetType == "MessageField"))
            _conn.UpsertRelationshipById("AFFECTS", ct.ChangeEvent.ExtId, ct.TargetExtId,
                new() { ["severity"] = ct.Severity });

        _conn.Commit();
    }

    // ── Shard builder ─────────────────────────────────────────────────────────

    private GraphShard BuildShardForBaseline(TacticalMessagingDbContext ctx, EfBaseline bl, List<EfBaselineEntry> entries)
    {
        var nodeTables = new Dictionary<string, NodeShardTable>();
        var relTables = new Dictionary<string, RelShardTable>();
        var outgoing = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal);
        var incoming = new Dictionary<string, List<ShardEdgeRef>>(StringComparer.Ordinal);

        void EnsureAdj(string id) { outgoing.TryAdd(id, []); incoming.TryAdd(id, []); }
        void AddEdge(string relId, string relType, string srcId, string dstId)
        {
            EnsureAdj(srcId); EnsureAdj(dstId);
            outgoing[srcId].Add(new ShardEdgeRef { RelId = relId, RelType = relType, NeighborNodeId = dstId, Direction = "out" });
            incoming[dstId].Add(new ShardEdgeRef { RelId = relId, RelType = relType, NeighborNodeId = srcId, Direction = "in" });
        }

        var compExtIds = new HashSet<string>(entries.Where(e => e.EntityType == "TranslatorComponent").Select(e => e.EntityExtId));
        var stdExtIds = new HashSet<string>(entries.Where(e => e.EntityType == "Standard").Select(e => e.EntityExtId));
        var profExtIds = new HashSet<string>(entries.Where(e => e.EntityType == "Profile").Select(e => e.EntityExtId));
        var reqExtIds = new HashSet<string>(entries.Where(e => e.EntityType == "Requirement").Select(e => e.EntityExtId));

        // Components
        var compRows = new List<NodeShardRow>();
        foreach (var tc in ctx.Components.Where(c => compExtIds.Contains(c.ExtId)))
        {
            EnsureAdj(tc.ExtId);
            compRows.Add(new NodeShardRow { ExternalId = tc.ExtId,
                Properties = new() { ["id"] = tc.ExtId, ["name"] = tc.Name, ["version"] = tc.Version, ["status"] = tc.Status } });
        }
        nodeTables["TranslatorComponent"] = new NodeShardTable { TableName = "TranslatorComponent", Rows = compRows };

        // Profiles
        var profRows = new List<NodeShardRow>();
        foreach (var p in ctx.Profiles.Where(p => profExtIds.Contains(p.ExtId)))
        {
            EnsureAdj(p.ExtId);
            profRows.Add(new NodeShardRow { ExternalId = p.ExtId,
                Properties = new() { ["id"] = p.ExtId, ["name"] = p.Name, ["platform_class"] = p.PlatformClass } });
        }
        nodeTables["Profile"] = new NodeShardTable { TableName = "Profile", Rows = profRows };

        // Requirements
        var reqRows = new List<NodeShardRow>();
        foreach (var r in ctx.Requirements.Where(r => reqExtIds.Contains(r.ExtId)))
        {
            EnsureAdj(r.ExtId);
            reqRows.Add(new NodeShardRow { ExternalId = r.ExtId,
                Properties = new() { ["id"] = r.ExtId, ["statement"] = r.Statement, ["priority"] = r.Priority } });
        }
        nodeTables["Requirement"] = new NodeShardTable { TableName = "Requirement", Rows = reqRows };

        // Standards
        var stdRows = new List<NodeShardRow>();
        foreach (var s in ctx.Standards.Where(s => stdExtIds.Contains(s.ExtId)))
        {
            EnsureAdj(s.ExtId);
            stdRows.Add(new NodeShardRow { ExternalId = s.ExtId,
                Properties = new() { ["id"] = s.ExtId, ["name"] = s.Name, ["alias"] = s.Alias, ["status"] = s.Status } });
        }
        nodeTables["Standard"] = new NodeShardTable { TableName = "Standard", Rows = stdRows };

        // Baseline node (needed as source for CONTAINS relationships)
        EnsureAdj(bl.ExtId);
        nodeTables["Baseline"] = new NodeShardTable { TableName = "Baseline", Rows =
        [
            new NodeShardRow { ExternalId = bl.ExtId,
                Properties = new() { ["id"] = bl.ExtId, ["label"] = bl.Label, ["snapshot_date"] = bl.SnapshotDate } }
        ] };

        // Subsystem nodes (needed as source for DEPENDS_ON relationships)
        var ssRows = new List<NodeShardRow>();
        var ssEntities = ctx.SubsystemComponents.Include(s => s.Subsystem).Include(s => s.Component)
            .Where(sc => compExtIds.Contains(sc.Component.ExtId))
            .Select(sc => sc.Subsystem).ToList().DistinctBy(s => s.ExtId);
        foreach (var ss in ssEntities)
        {
            EnsureAdj(ss.ExtId);
            ssRows.Add(new NodeShardRow { ExternalId = ss.ExtId,
                Properties = new() { ["id"] = ss.ExtId, ["name"] = ss.Name, ["domain"] = ss.Domain } });
        }
        nodeTables["Subsystem"] = new NodeShardTable { TableName = "Subsystem", Rows = ssRows };

        // Relationships in this baseline scope
        var implementsRels = new List<RelShardRow>();
        foreach (var cp in ctx.ComponentProfiles.Include(c => c.Component).Include(c => c.Profile)
            .Where(cp => compExtIds.Contains(cp.Component.ExtId) && profExtIds.Contains(cp.Profile.ExtId)))
        {
            var relId = $"impl-{cp.Component.ExtId}-{cp.Profile.ExtId}";
            implementsRels.Add(new RelShardRow { RelId = relId, SourceNodeId = cp.Component.ExtId, TargetNodeId = cp.Profile.ExtId, Properties = new() });
            AddEdge(relId, "IMPLEMENTS", cp.Component.ExtId, cp.Profile.ExtId);
        }
        if (implementsRels.Count > 0) relTables["IMPLEMENTS"] = new RelShardTable { RelType = "IMPLEMENTS", Rows = implementsRels };

        var satisfiesRels = new List<RelShardRow>();
        foreach (var cr in ctx.ComponentRequirements.Include(c => c.Component).Include(c => c.Requirement)
            .Where(cr => compExtIds.Contains(cr.Component.ExtId) && reqExtIds.Contains(cr.Requirement.ExtId)))
        {
            var relId = $"sat-{cr.Component.ExtId}-{cr.Requirement.ExtId}";
            satisfiesRels.Add(new RelShardRow { RelId = relId, SourceNodeId = cr.Component.ExtId, TargetNodeId = cr.Requirement.ExtId, Properties = new() });
            AddEdge(relId, "SATISFIES", cr.Component.ExtId, cr.Requirement.ExtId);
        }
        if (satisfiesRels.Count > 0) relTables["SATISFIES"] = new RelShardTable { RelType = "SATISFIES", Rows = satisfiesRels };

        var dependsOnRels = new List<RelShardRow>();
        foreach (var sc in ctx.SubsystemComponents.Include(s => s.Subsystem).Include(s => s.Component)
            .Where(sc => compExtIds.Contains(sc.Component.ExtId)))
        {
            EnsureAdj(sc.Subsystem.ExtId);
            var relId = $"dep-{sc.Subsystem.ExtId}-{sc.Component.ExtId}";
            dependsOnRels.Add(new RelShardRow { RelId = relId, SourceNodeId = sc.Subsystem.ExtId, TargetNodeId = sc.Component.ExtId, Properties = new() });
            AddEdge(relId, "DEPENDS_ON", sc.Subsystem.ExtId, sc.Component.ExtId);
        }
        if (dependsOnRels.Count > 0) relTables["DEPENDS_ON"] = new RelShardTable { RelType = "DEPENDS_ON", Rows = dependsOnRels };

        var containsRels = new List<RelShardRow>();
        foreach (var ext in compExtIds)
        {
            var relId = $"contains-{bl.ExtId}-{ext}";
            EnsureAdj(bl.ExtId);
            containsRels.Add(new RelShardRow { RelId = relId, SourceNodeId = bl.ExtId, TargetNodeId = ext, Properties = new() });
            AddEdge(relId, "CONTAINS", bl.ExtId, ext);
        }
        if (containsRels.Count > 0) relTables["CONTAINS"] = new RelShardTable { RelType = "CONTAINS", Rows = containsRels };

        return GraphShardNormalizer.Normalize(new GraphShard
        {
            FormatVersion = GraphShard.CurrentFormatVersion,
            ExtractorVersion = GraphShard.CurrentExtractorVersion,
            GraphVersionToken = $"tactical-messaging-{bl.ExtId}",
            ExtractedAtUtc = DateTime.UtcNow.ToString("O"),
            ExtractionPolicy = "ef-baseline-snapshot",
            IsComplete = true,
            NodeTables = nodeTables,
            RelTables = relTables,
            Adjacency = new GraphShardAdjacency { Outgoing = outgoing, Incoming = incoming },
            SeedProvenance = new GraphShardSeedProvenance { RequestedCount = entries.Count, IncludedCount = entries.Count },
            Boundary = new GraphShardBoundary(),
            Options = new GraphShardExtractionOptions
            {
                IncludeOutgoing = true, IncludeIncoming = true,
                IncludeNodeProperties = true, IncludeRelProperties = true,
                IncludeAdjacency = true
            },
            Metadata = new() { ["baselineLabel"] = bl.Label }
        });
    }
}
