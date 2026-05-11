using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Samples.LLMBenchmarks.Blazor.Services;

/// <summary>
/// Singleton service that owns the BogDB in-memory graph database,
/// seeds it once on construction, and exposes typed query helpers.
/// </summary>
public sealed class BenchmarkGraphService
{
    private readonly BogDatabase _db;
    private readonly BogConnection _conn;

    public BenchmarkGraphService()
    {
        _db   = BogDatabase.CreateInMemory();
        _conn = new BogConnection(_db);
        SetupSchema();
        SeedData();
    }

    // ── Public query helpers ──────────────────────────────────────────────────

    /// <summary>Execute an arbitrary Cypher string and return typed rows.</summary>
    public QueryResponse Execute(string cypher)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r  = _conn.Query(cypher);
        sw.Stop();
        if (!r.IsSuccess)
            return new QueryResponse { IsSuccess = false, Error = r.ErrorMessage, ElapsedMs = sw.ElapsedMilliseconds };

        var cols = r.ColumnNames.ToList();
        var rows = new List<Dictionary<string, object?>>();
        while (r.HasNext())
        {
            var row = r.GetNext();
            rows.Add(row.GetAsDictionary()
                .ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
        }
        return new QueryResponse { Columns = cols, Rows = rows, ElapsedMs = sw.ElapsedMilliseconds };
    }

    // ── Pre-built data for the Home dashboard charts ──────────────────────────

    public ChartData GetOrgAvgScores()
    {
        var r = Execute(@"
            MATCH (m:Model)-[:MADE_BY]->(o:Organisation)
            MATCH (m)-[s:SCORED_ON]->(b:Benchmark)
            WITH o.name AS org, avg(s.score) AS avg_score
            RETURN org, avg_score
            ORDER BY avg_score DESC");
        return ToChartData(r, "org", "avg_score");
    }

    public ChartData GetVfmIndex()
    {
        var r = Execute(@"
            MATCH (m:Model)-[s:SCORED_ON]->(b:Benchmark)
            WHERE m.cost_per_1m_in > 0
            WITH m.name AS model, m.cost_per_1m_in AS cost, avg(s.score) AS avg_score
            RETURN model, cost, avg_score, avg_score / cost AS vfm
            ORDER BY vfm DESC");
        return ToChartData(r, "model", "vfm");
    }

    public ChartData GetArenaElo()
    {
        var r = Execute(@"
            MATCH (m:Model)
            RETURN m.name AS model, m.arena_elo AS elo
            ORDER BY elo DESC");
        return ToChartData(r, "model", "elo");
    }

    public ChartData GetCategoryAvgs()
    {
        var r = Execute(@"
            MATCH (m:Model)-[s:SCORED_ON]->(b:Benchmark)
            WITH b.category AS cat, avg(s.score) AS avg_score
            RETURN cat, avg_score
            ORDER BY avg_score DESC");
        return ToChartData(r, "cat", "avg_score");
    }

    /// Returns per-model scores across all benchmarks for the radar chart.
    public RadarData GetTopModelsRadar(int topN = 6)
    {
        var topR = Execute(@"
            MATCH (m:Model)-[s:SCORED_ON]->(b:Benchmark)
            WITH m.name AS model, avg(s.score) AS avg_score
            RETURN model, avg_score
            ORDER BY avg_score DESC
            LIMIT 6");
        var topModels = topR.Rows.Select(r => r["model"]?.ToString() ?? "").ToList();

        var scoresR = Execute(@"
            MATCH (m:Model)-[s:SCORED_ON]->(b:Benchmark)
            RETURN m.name AS model, b.name AS benchmark, s.score AS score
            ORDER BY b.name, m.name");

        var benchmarks = scoresR.Rows.Select(r => r["benchmark"]?.ToString() ?? "")
            .Distinct().OrderBy(x => x).ToList();

        var datasets = topModels.Select(model =>
        {
            var scores = benchmarks.Select(bench =>
            {
                var row = scoresR.Rows.FirstOrDefault(r =>
                    r["model"]?.ToString() == model && r["benchmark"]?.ToString() == bench);
                return row != null ? Convert.ToDouble(row["score"] ?? 0) : 0.0;
            }).ToList();
            return new RadarDataset { Label = model, Data = scores };
        }).ToList();

        return new RadarData { Labels = benchmarks, Datasets = datasets };
    }

    // ── Pre-built showcase queries (the library) ──────────────────────────────

    public static List<ShowcaseQuery> GetShowcaseQueries() => new()
    {
        new("Q1 · All Models & Organisations",
            "Basic MATCH — traverse MADE_BY edges to join Model → Organisation.",
            "graph-traversal",
            @"MATCH (m:Model)-[:MADE_BY]->(o:Organisation)
RETURN m.name AS model, o.name AS org, m.params_b AS params_b,
       m.open_source AS open_source, m.arena_elo AS elo
ORDER BY m.arena_elo DESC"),

        new("Q2 · Open-Source Models Only",
            "WHERE filter on a boolean property to isolate open-weight models.",
            "filter",
            @"MATCH (m:Model)-[:MADE_BY]->(o:Organisation)
WHERE m.open_source = true
RETURN m.name AS model, o.name AS org,
       m.params_b AS params_b, m.cost_per_1m_in AS cost_usd_1m
ORDER BY m.arena_elo DESC"),

        new("Q3 · Avg Score by Organisation",
            "GROUP BY with AVG — BogDB implicit grouping semantics.",
            "aggregation",
            @"MATCH (m:Model)-[:MADE_BY]->(o:Organisation)
MATCH (m)-[r:SCORED_ON]->(b:Benchmark)
WITH o.name AS org, avg(r.score) AS avg_score, count(r.score) AS n
RETURN org, round(avg_score * 100) / 100.0 AS avg_score, n
ORDER BY avg_score DESC"),

        new("Q4 · Stats by Benchmark Category",
            "GROUP BY on a related-node property with MIN/MAX/AVG aggregates.",
            "aggregation",
            @"MATCH (m:Model)-[r:SCORED_ON]->(b:Benchmark)
WITH b.category AS category, avg(r.score) AS avg_score,
     min(r.score) AS min_score, max(r.score) AS max_score
RETURN category, avg_score, min_score, max_score
ORDER BY avg_score DESC"),

        new("Q5 · Model Ranking per Benchmark",
            "RANK() OVER (PARTITION BY …) — window functions.",
            "window",
            @"MATCH (m:Model)-[r:SCORED_ON]->(b:Benchmark)
RETURN b.name AS benchmark, m.name AS model, r.score AS score,
       RANK() OVER (PARTITION BY b.name ORDER BY r.score DESC) AS rnk
ORDER BY b.name, rnk"),

        new("Q6 · Top 5 Performers",
            "ORDER BY + LIMIT triggers BogDB's O(n·log K) TopK optimizer.",
            "topk",
            @"MATCH (m:Model)-[r:SCORED_ON]->(b:Benchmark)
WITH m.name AS model, avg(r.score) AS avg_score
RETURN model, round(avg_score*100)/100.0 AS avg_score
ORDER BY avg_score DESC
LIMIT 5"),

        new("Q7 · Who Beats OpenAI on MATH?",
            "Traverse BEATS edges with WHERE on organisation name.",
            "graph-traversal",
            @"MATCH (challenger:Model)-[b:BEATS]->(victim:Model)-[:MADE_BY]->(o:Organisation)
WHERE o.name = 'OpenAI' AND b.on_benchmark = 'math'
RETURN challenger.name AS challenger, victim.name AS victim,
       b.on_benchmark AS benchmark,
       round(b.by_margin*10)/10.0 AS margin
ORDER BY b.by_margin DESC"),

        new("Q8 · Value-For-Money Index",
            "Multi-stage WITH — perf/cost ratio across all benchmarks.",
            "aggregation",
            @"MATCH (m:Model)-[r:SCORED_ON]->(b:Benchmark)
WHERE m.cost_per_1m_in > 0
WITH m.name AS model, m.cost_per_1m_in AS cost_usd,
     avg(r.score) AS avg_score
RETURN model, cost_usd, avg_score,
       avg_score / cost_usd AS perf_per_dollar
ORDER BY avg_score / cost_usd DESC"),

        new("Q9 · DeepSeek-V3 Scorecard",
            "Per-model scorecard across all benchmarks.",
            "filter",
            @"MATCH (m:Model)-[r:SCORED_ON]->(b:Benchmark)
WHERE m.name = 'DeepSeek-V3'
RETURN b.name AS benchmark, b.category AS category,
       r.score AS score, r.method AS method
ORDER BY r.score DESC"),

        new("Q10 · Budget Champions",
            "Multi-condition filter: cost < $1/M and MMLU ≥ 85%.",
            "filter",
            @"MATCH (m:Model)-[r:SCORED_ON]->(b:Benchmark)
WHERE b.name = 'MMLU' AND r.score >= 85.0 AND m.cost_per_1m_in < 1.0
RETURN m.name AS model, m.cost_per_1m_in AS cost_usd_1m,
       r.score AS mmlu_score, m.params_b AS params_b
ORDER BY m.cost_per_1m_in ASC"),
    };

    // ── Stats helpers ─────────────────────────────────────────────────────────

    public DashboardStats GetStats()
    {
        var modelCount = (long)(Execute("MATCH (m:Model) RETURN count(m) AS n").Rows[0]["n"] ?? 0L);
        var benchCount = (long)(Execute("MATCH (b:Benchmark) RETURN count(b) AS n").Rows[0]["n"] ?? 0L);
        var scoreCount = (long)(Execute("MATCH (m:Model)-[s:SCORED_ON]->(b:Benchmark) WITH count(s.score) AS n RETURN n").Rows[0]["n"] ?? 0L);
        var orgCount   = (long)(Execute("MATCH (o:Organisation) RETURN count(o) AS n").Rows[0]["n"] ?? 0L);
        return new DashboardStats
        {
            Models = (int)modelCount, Benchmarks = (int)benchCount,
            Scores = (int)scoreCount, Organisations = (int)orgCount
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static ChartData ToChartData(QueryResponse r, string labelCol, string valueCol) => new()
    {
        Labels = r.Rows.Select(row => row[labelCol]?.ToString() ?? "").ToList(),
        Values = r.Rows.Select(row => Convert.ToDouble(row[valueCol] ?? 0)).ToList(),
    };

    // ── Schema + Seed ─────────────────────────────────────────────────────────

    private void SetupSchema()
    {
        _conn.BeginWriteTransaction();
        _conn.EnsureNodeTable("Organisation", new Dictionary<string, LogicalTypeID>
            { ["name"] = LogicalTypeID.STRING, ["country"] = LogicalTypeID.STRING, ["tier"] = LogicalTypeID.STRING });
        _conn.EnsureNodeTable("Model", new Dictionary<string, LogicalTypeID>
        {
            ["name"] = LogicalTypeID.STRING, ["version"] = LogicalTypeID.STRING,
            ["params_b"] = LogicalTypeID.DOUBLE, ["context_k"] = LogicalTypeID.INT64,
            ["open_source"] = LogicalTypeID.BOOL, ["release_year"] = LogicalTypeID.INT64,
            ["cost_per_1m_in"] = LogicalTypeID.DOUBLE, ["arena_elo"] = LogicalTypeID.INT64,
        });
        _conn.EnsureNodeTable("Benchmark", new Dictionary<string, LogicalTypeID>
            { ["name"] = LogicalTypeID.STRING, ["category"] = LogicalTypeID.STRING, ["max_score"] = LogicalTypeID.DOUBLE });
        _conn.EnsureRelTable("MADE_BY", "Model", "Organisation", new Dictionary<string, LogicalTypeID>());
        _conn.EnsureRelTable("BEATS",   "Model", "Model",        new Dictionary<string, LogicalTypeID>
            { ["on_benchmark"] = LogicalTypeID.STRING, ["by_margin"] = LogicalTypeID.DOUBLE });
        _conn.EnsureRelTable("SCORED_ON", "Model", "Benchmark",  new Dictionary<string, LogicalTypeID>
            { ["score"] = LogicalTypeID.DOUBLE, ["method"] = LogicalTypeID.STRING });
        _conn.Commit();
    }

    private void SeedData()
    {
        _conn.BeginWriteTransaction();

        // Orgs
        foreach (var (id, name, country, tier) in new (string, string, string, string)[]
        {
            ("openai","OpenAI","USA","Big-Tech"), ("anthropic","Anthropic","USA","Startup"),
            ("google","Google DeepMind","USA","Big-Tech"), ("meta","Meta AI","USA","Big-Tech"),
            ("mistral","Mistral AI","France","Startup"), ("deepseek","DeepSeek","China","Startup"),
            ("microsoft","Microsoft","USA","Big-Tech"), ("alibaba","Alibaba/Qwen","China","Big-Tech"),
        })
            _conn.UpsertNodeById("Organisation", id, new() { ["name"]=name, ["country"]=country, ["tier"]=tier });

        // Benchmarks
        foreach (var (id, name, cat) in new (string, string, string)[]
        {
            ("mmlu","MMLU","Knowledge"), ("humaneval","HumanEval","Code"),
            ("gsm8k","GSM8K","Math"), ("math","MATH","Math"),
            ("bbh","BBH","Reasoning"), ("arc_c","ARC-Challenge","Knowledge"), ("hellaswag","HellaSwag","Reasoning"),
        })
            _conn.UpsertNodeById("Benchmark", id, new() { ["name"]=name, ["category"]=cat, ["max_score"]=100.0 });

        // Models
        foreach (var m in new (string id, string name, string ver, double p, int ctx, bool oss, int yr, double cost, int elo, string org)[]
        {
            ("gpt4o","GPT-4o","2024-11",200,128,false,2024,5.00,1312,"openai"),
            ("gpt41","GPT-4.1","2025-04",200,1024,false,2025,2.00,1350,"openai"),
            ("claude35","Claude 3.5 Sonnet","2024-10",175,200,false,2024,3.00,1268,"anthropic"),
            ("claude37","Claude 3.7 Sonnet","2025-02",175,200,false,2025,3.00,1336,"anthropic"),
            ("gemini15","Gemini 1.5 Pro","2024-05",175,2048,false,2024,3.50,1265,"google"),
            ("gemini25","Gemini 2.5 Pro","2025-03",175,1048,false,2025,7.00,1380,"google"),
            ("llama33","Llama 3.3 70B","2024-12",70,128,true,2024,0.59,1224,"meta"),
            ("llama4","Llama 4 Scout","2025-04",109,10240,true,2025,0.17,1254,"meta"),
            ("mistral2","Mistral Large 2","2024-07",123,128,false,2024,3.00,1185,"mistral"),
            ("deepseek3","DeepSeek-V3","2024-12",671,128,true,2024,0.27,1221,"deepseek"),
            ("phi4","Phi-4","2024-12",14,16,true,2024,0.07,1090,"microsoft"),
            ("qwen25","Qwen2.5 72B","2024-09",72,128,true,2024,0.40,1248,"alibaba"),
        })
        {
            _conn.UpsertNodeById("Model", m.id, new()
            {
                ["name"]=m.name, ["version"]=m.ver, ["params_b"]=m.p,
                ["context_k"]=(long)m.ctx, ["open_source"]=m.oss,
                ["release_year"]=(long)m.yr, ["cost_per_1m_in"]=m.cost, ["arena_elo"]=(long)m.elo,
            });
            _conn.UpsertRelationshipById("MADE_BY", m.id, m.org, new());
        }

        // Scores (model_id, benchmark_id, score, method)
        foreach (var (mid, bid, score, method) in new (string, string, double, string)[]
        {
            ("gpt4o","mmlu",88.7,"5-shot"),("gpt41","mmlu",90.5,"5-shot"),("claude35","mmlu",88.7,"5-shot"),
            ("claude37","mmlu",90.3,"5-shot"),("gemini15","mmlu",85.9,"5-shot"),("gemini25","mmlu",92.5,"5-shot"),
            ("llama33","mmlu",86.0,"5-shot"),("llama4","mmlu",88.5,"5-shot"),("mistral2","mmlu",84.0,"5-shot"),
            ("deepseek3","mmlu",87.1,"5-shot"),("phi4","mmlu",84.8,"5-shot"),("qwen25","mmlu",86.0,"5-shot"),
            ("gpt4o","humaneval",90.2,"0-shot"),("gpt41","humaneval",93.6,"0-shot"),
            ("claude35","humaneval",92.0,"0-shot"),("claude37","humaneval",94.5,"0-shot"),
            ("gemini15","humaneval",90.2,"0-shot"),("gemini25","humaneval",97.0,"0-shot"),
            ("llama33","humaneval",88.4,"0-shot"),("llama4","humaneval",92.3,"0-shot"),
            ("mistral2","humaneval",92.1,"0-shot"),("deepseek3","humaneval",89.3,"0-shot"),
            ("phi4","humaneval",82.6,"0-shot"),("qwen25","humaneval",87.2,"0-shot"),
            ("gpt4o","gsm8k",97.0,"CoT"),("gpt41","gsm8k",98.5,"CoT"),("claude35","gsm8k",96.4,"CoT"),
            ("claude37","gsm8k",97.8,"CoT"),("gemini15","gsm8k",90.8,"CoT"),("gemini25","gsm8k",97.0,"CoT"),
            ("llama33","gsm8k",95.1,"CoT"),("llama4","gsm8k",96.5,"CoT"),("mistral2","gsm8k",93.0,"CoT"),
            ("deepseek3","gsm8k",97.0,"CoT"),("phi4","gsm8k",95.5,"CoT"),("qwen25","gsm8k",95.0,"CoT"),
            ("gpt4o","math",76.6,"4-shot"),("gpt41","math",81.2,"4-shot"),("claude35","math",71.1,"4-shot"),
            ("claude37","math",78.2,"4-shot"),("gemini15","math",67.7,"4-shot"),("gemini25","math",86.0,"4-shot"),
            ("llama33","math",77.0,"4-shot"),("llama4","math",80.4,"4-shot"),("mistral2","math",68.0,"4-shot"),
            ("deepseek3","math",87.5,"4-shot"),("phi4","math",80.4,"4-shot"),("qwen25","math",83.1,"4-shot"),
            ("gpt4o","bbh",87.0,"3-shot"),("gpt41","bbh",89.5,"3-shot"),("claude35","bbh",93.1,"3-shot"),
            ("claude37","bbh",95.0,"3-shot"),("gemini15","bbh",89.2,"3-shot"),("gemini25","bbh",92.8,"3-shot"),
            ("llama33","bbh",81.5,"3-shot"),("llama4","bbh",85.0,"3-shot"),("mistral2","bbh",78.2,"3-shot"),
            ("deepseek3","bbh",83.0,"3-shot"),("phi4","bbh",76.6,"3-shot"),("qwen25","bbh",79.5,"3-shot"),
            ("gpt4o","arc_c",96.4,"0-shot"),("gpt41","arc_c",97.0,"0-shot"),("claude35","arc_c",93.2,"0-shot"),
            ("claude37","arc_c",94.5,"0-shot"),("gemini15","arc_c",92.0,"0-shot"),("gemini25","arc_c",95.0,"0-shot"),
            ("llama33","arc_c",92.9,"0-shot"),("llama4","arc_c",94.0,"0-shot"),("mistral2","arc_c",91.5,"0-shot"),
            ("deepseek3","arc_c",93.0,"0-shot"),("phi4","arc_c",88.6,"0-shot"),("qwen25","arc_c",91.5,"0-shot"),
            ("gpt4o","hellaswag",95.3,"0-shot"),("gpt41","hellaswag",96.0,"0-shot"),
            ("claude35","hellaswag",92.0,"0-shot"),("claude37","hellaswag",93.5,"0-shot"),
            ("gemini15","hellaswag",92.5,"0-shot"),("gemini25","hellaswag",94.5,"0-shot"),
            ("llama33","hellaswag",93.2,"0-shot"),("llama4","hellaswag",94.5,"0-shot"),
            ("mistral2","hellaswag",88.0,"0-shot"),("deepseek3","hellaswag",90.0,"0-shot"),
            ("phi4","hellaswag",84.5,"0-shot"),("qwen25","hellaswag",87.0,"0-shot"),
        })
            _conn.UpsertRelationshipById("SCORED_ON", mid, bid, new() { ["score"]=score, ["method"]=method });

        // BEATS edges
        foreach (var (a, b, bench, margin) in new (string, string, string, double)[]
        {
            ("deepseek3","gpt4o","math",87.5-76.6), ("deepseek3","claude35","math",87.5-71.1),
            ("gemini25","gpt4o","humaneval",97.0-90.2), ("claude37","gpt4o","bbh",95.0-87.0),
            ("gpt4o","mistral2","mmlu",88.7-84.0), ("llama33","mistral2","math",77.0-68.0),
            ("qwen25","mistral2","math",83.1-68.0), ("phi4","llama33","math",80.4-77.0),
        })
            _conn.UpsertRelationshipById("BEATS", a, b,
                new() { ["on_benchmark"]=bench, ["by_margin"]=Math.Round(margin,1) });

        _conn.Commit();
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class QueryResponse
{
    public bool IsSuccess { get; set; } = true;
    public string? Error { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public long ElapsedMs { get; set; }
}

public class ChartData
{
    public List<string> Labels { get; set; } = new();
    public List<double> Values { get; set; } = new();
}

public class RadarData
{
    public List<string> Labels { get; set; } = new();
    public List<RadarDataset> Datasets { get; set; } = new();
}

public class RadarDataset
{
    public string Label { get; set; } = "";
    public List<double> Data { get; set; } = new();
}

public class DashboardStats
{
    public int Models { get; set; }
    public int Benchmarks { get; set; }
    public int Scores { get; set; }
    public int Organisations { get; set; }
}

public record ShowcaseQuery(string Title, string Description, string Tag, string Cypher);
