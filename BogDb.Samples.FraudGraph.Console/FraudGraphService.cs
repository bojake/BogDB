using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BogDb.Core.Common;
using BogDb.Core.Main;

namespace BogDb.Samples.FraudGraph.Console;

// ── Domain POCOs ──────────────────────────────────────────────────────────────

public record Account(string Id, string Name, string Email, string Status, double Balance);
public record Transaction(string Id, string FromId, string ToId, double Amount,
    string Category, string Date, bool IsFlagged);
public record Identifier(string Id, string Value, string Kind);
public record Rule(string Id, string Name, string Description);

public record RingResult(string AccountId, string Name, string Status,
    int SharedIds, int TxCount, double TotalExposure);

public record TraceHop(string NodeLabel, string NodeId, string NodeName,
    string RelType, int Depth);

public record PathHop(string AccountId, string AccountName, string Status,
    string? RelType, double? Amount);

public record RiskScore(int Rank, string AccountId, string Name, string Status,
    double Score, int SharedIdCount, int FlaggedTxCount, int TxVelocity);

public record FraudQueryResult(bool IsSuccess, string Error,
    List<string> Columns, List<Dictionary<string, object?>> Rows, long ElapsedMs);

// ── Console Output Helpers ────────────────────────────────────────────────────

public enum Color { Default, Cyan, Yellow, Red, Green, Gray, White, Magenta }

// ── Service ───────────────────────────────────────────────────────────────────

/// <summary>
/// Singleton service that owns the BogDB in-memory fraud graph.
///
/// Pattern:
///   1. Schema via EnsureNodeTable / EnsureRelTable
///   2. Seed via UpsertNodeById / UpsertRelationshipById
///   3. Query via conn.Query() wrapped in Execute()
/// </summary>
public sealed class FraudGraphService
{
    private readonly BogDatabase _db;
    private readonly BogConnection _conn;
    private int _seedSummaryAccounts, _seedSummaryTxns, _seedSummaryIds;

    public FraudGraphService()
    {
        _db   = BogDatabase.CreateInMemory();
        _conn = new BogConnection(_db);
        SetupSchema();
        SeedData();
        PrintSeedSummary();
    }

    // ── Output helpers ────────────────────────────────────────────────────────

    public void Print(string text, Color color = Color.Default)
    {
        if (color != Color.Default)
        {
            System.Console.ForegroundColor = color switch
            {
                Color.Cyan    => ConsoleColor.Cyan,
                Color.Yellow  => ConsoleColor.Yellow,
                Color.Red     => ConsoleColor.Red,
                Color.Green   => ConsoleColor.Green,
                Color.Gray    => ConsoleColor.DarkGray,
                Color.White   => ConsoleColor.White,
                Color.Magenta => ConsoleColor.Magenta,
                _             => ConsoleColor.Gray,
            };
        }
        System.Console.Write(text);
        if (color != Color.Default) System.Console.ResetColor();
    }

    public void PrintLine(string text = "", Color color = Color.Default)
    {
        Print(text, color);
        System.Console.WriteLine();
    }

    private void PrintHeader(string title)
    {
        PrintLine();
        var bar = new string('─', title.Length + 4);
        PrintLine($"  ┌{bar}┐", Color.Cyan);
        PrintLine($"  │  {title}  │", Color.Cyan);
        PrintLine($"  └{bar}┘", Color.Cyan);
        PrintLine();
    }

    internal void PrintTable(List<string> cols, List<Dictionary<string, object?>> rows,
        int? maxRows = null)
    {
        if (rows.Count == 0) { PrintLine("  (no rows)", Color.Gray); return; }

        // Compute column widths
        var widths = cols.Select(c => c.Length).ToList();
        foreach (var row in rows.Take(maxRows ?? rows.Count))
            for (int i = 0; i < cols.Count; i++)
                widths[i] = Math.Max(widths[i], row.GetValueOrDefault(cols[i])?.ToString()?.Length ?? 0);

        // Header
        var header = string.Join(" │ ", cols.Select((c, i) => c.PadRight(widths[i])));
        var div    = string.Join("─┼─", widths.Select(w => new string('─', w)));

        PrintLine($"  ┌─{string.Join("─┬─", widths.Select(w => new string('─', w)))}─┐", Color.Gray);
        Print("  │ ", Color.Gray);
        for (int i = 0; i < cols.Count; i++)
        {
            Print(cols[i].PadRight(widths[i]), Color.Cyan);
            if (i < cols.Count - 1) Print(" │ ", Color.Gray);
        }
        PrintLine(" │", Color.Gray);
        PrintLine($"  ├─{div}─┤", Color.Gray);

        int shown = 0;
        foreach (var row in rows)
        {
            if (maxRows.HasValue && shown >= maxRows.Value) break;
            Print("  │ ", Color.Gray);
            for (int i = 0; i < cols.Count; i++)
            {
                var val = row.GetValueOrDefault(cols[i])?.ToString() ?? "";
                Print(val.PadRight(widths[i]), Color.White);
                if (i < cols.Count - 1) Print(" │ ", Color.Gray);
            }
            PrintLine(" │", Color.Gray);
            shown++;
        }

        PrintLine($"  └─{string.Join("─┴─", widths.Select(w => new string('─', w)))}─┘", Color.Gray);
        if (maxRows.HasValue && rows.Count > maxRows.Value)
            PrintLine($"  … {rows.Count - maxRows} more rows", Color.Gray);
    }

    public void PrintHelp()
    {
        PrintHeader("FraudGraph · Commands");
        var cmds = new[]
        {
            ("detect-rings [--min-shared N]",  "Find fraud rings sharing ≥ N identifiers (default: 2)"),
            ("trace <account-id>",              "Print the 2-hop neighbourhood of an account"),
            ("shortest-path <from> <to>",       "Follow-the-money shortest path between two accounts"),
            ("score-all [--top N]",             "Risk-score all accounts and rank by composite score"),
            ("explain \"<cypher>\"",            "Run any Cypher and pretty-print results"),
            ("help",                            "Show this help"),
        };
        foreach (var (cmd, desc) in cmds)
        {
            Print($"  {cmd,-42}", Color.Yellow);
            PrintLine($"  {desc}", Color.Gray);
        }
        PrintLine();
    }

    // ── REPL ──────────────────────────────────────────────────────────────────

    public void Repl()
    {
        PrintHelp();
        while (true)
        {
            Print("fraudgraph> ", Color.Cyan);
            var line = System.Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line is "exit" or "quit" or "q") break;

            var parts = SplitArgs(line);
            if (parts.Length == 0) continue;

            switch (parts[0].ToLowerInvariant())
            {
                case "detect-rings":   DetectRings(GetInt(parts, "--min-shared", 2)); break;
                case "trace":
                    if (parts.Length < 2) PrintLine("[error] Usage: trace <account-id>", Color.Red);
                    else Trace(parts[1]);
                    break;
                case "shortest-path":
                    if (parts.Length < 3) PrintLine("[error] Usage: shortest-path <from> <to>", Color.Red);
                    else ShortestPath(parts[1], parts[2]);
                    break;
                case "score-all":      ScoreAll(GetInt(parts, "--top", 20)); break;
                case "explain":
                    if (parts.Length < 2) PrintLine("[error] Usage: explain \"<cypher>\"", Color.Red);
                    else Explain(string.Join(" ", parts.Skip(1)));
                    break;
                case "help":           PrintHelp(); break;
                default:
                    PrintLine($"[error] Unknown command '{parts[0]}'. Type 'help' for usage.", Color.Red);
                    break;
            }
        }
        PrintLine("Goodbye.", Color.Gray);
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Core fraud ring detection: find accounts that share ≥ minShared identifiers
    /// with a flagged/confirmed-fraud account, then show their transaction exposure.
    /// </summary>
    public void DetectRings(int minShared = 2)
    {
        PrintHeader($"Fraud Ring Detection  (min shared identifiers: {minShared})");

        // Step 1 — Accounts sharing identifiers with a flagged account
        var ringQ = Execute(
            "MATCH (flagged:Account)-[:USED]->(id:Identifier)<-[:USED]-(suspect:Account) " +
            "WHERE (flagged.status = 'FLAGGED' OR flagged.status = 'CONFIRMED_FRAUD') " +
            "  AND suspect <> flagged " +
            "WITH suspect, COUNT(DISTINCT id) AS shared_ids " +
            $"WHERE shared_ids >= {minShared} " +
            "MATCH (suspect)-[:MADE]->(t:Transaction) " +
            "RETURN suspect.id AS account_id, suspect.name AS name, " +
            "       suspect.status AS status, shared_ids, " +
            "       COUNT(t) AS tx_count, SUM(t.amount) AS total_exposure " +
            "ORDER BY shared_ids DESC, total_exposure DESC");

        if (!ringQ.IsSuccess)
        {
            PrintLine($"  [error] {ringQ.Error}", Color.Red);
            return;
        }

        if (ringQ.Rows.Count == 0)
        {
            PrintLine("  No suspects found. Try lowering --min-shared.", Color.Yellow);
            return;
        }

        PrintLine($"  Found {ringQ.Rows.Count} suspect account(s):", Color.Yellow);
        PrintLine();

        // Colour status
        foreach (var row in ringQ.Rows)
        {
            var status = row["status"]?.ToString() ?? "";
            var color  = status == "CONFIRMED_FRAUD" ? Color.Red :
                         status == "FLAGGED"          ? Color.Yellow : Color.Green;

            Print($"  ● [{status,-16}] ", color);
            Print($"{row["account_id"],-10} ", Color.Cyan);
            Print($"{row["name"],-28}", Color.White);
            Print($"  shared_ids={row["shared_ids"]}  ", Color.Gray);
            Print($"txns={row["tx_count"]}  ", Color.Gray);
            PrintLine($"exposure=${Convert.ToDouble(row["total_exposure"]):N2}", Color.Magenta);
        }

        PrintLine();

        // Step 2 — Show which identifiers they share
        var idQ = Execute(
            "MATCH (flagged:Account)-[:USED]->(id:Identifier)<-[:USED]-(suspect:Account) " +
            "WHERE (flagged.status = 'FLAGGED' OR flagged.status = 'CONFIRMED_FRAUD') " +
            "  AND suspect <> flagged " +
            "WITH suspect, id " +
            "RETURN suspect.id AS account_id, id.kind AS id_kind, id.value AS id_value " +
            "ORDER BY account_id, id_kind");

        if (idQ.IsSuccess && idQ.Rows.Count > 0)
        {
            PrintLine("  Shared identifiers:", Color.Cyan);
            PrintTable(["account_id", "id_kind", "id_value"], idQ.Rows, 30);
        }
    }

    /// <summary>
    /// Trace the 2-hop neighbourhood of a given account — all connected accounts,
    /// transactions and identifiers within 2 hops.
    /// </summary>
    public void Trace(string accountId)
    {
        PrintHeader($"Neighbourhood Trace  ·  {accountId}");

        // Confirm node exists
        var chk = Execute($"MATCH (a:Account {{id: '{accountId}'}}) RETURN a.name AS name, a.status AS status");
        if (!chk.IsSuccess || chk.Rows.Count == 0)
        {
            PrintLine($"  Account '{accountId}' not found.", Color.Red);
            PrintLine("  Use 'explain \"MATCH (a:Account) RETURN a.id, a.name LIMIT 20\"' to list accounts.", Color.Gray);
            return;
        }

        var accName   = chk.Rows[0]["name"]?.ToString() ?? accountId;
        var accStatus = chk.Rows[0]["status"]?.ToString() ?? "?";
        var sc = accStatus == "CONFIRMED_FRAUD" ? Color.Red : accStatus == "FLAGGED" ? Color.Yellow : Color.Green;

        Print($"  Account: "); Print($"{accName} ({accountId})", Color.White);
        Print($"  Status: "); PrintLine($"{accStatus}", sc);
        PrintLine();

        // Connected accounts within 2 hops via MADE/TO/LINKED_TO
        var txHops = Execute(
            $"MATCH (src:Account {{id: '{accountId}'}})-[r:MADE|LINKED_TO*1..2]-(other:Account) " +
            "WHERE other <> src " +
            "RETURN DISTINCT other.id AS other_id, other.name AS other_name, " +
            "       other.status AS other_status, COUNT(r) AS connection_count " +
            "ORDER BY connection_count DESC LIMIT 20");

        PrintLine("  Connected accounts (via MADE/LINKED_TO ≤ 2 hops):", Color.Cyan);
        if (txHops.IsSuccess && txHops.Rows.Count > 0)
            PrintTable(["other_id", "other_name", "other_status", "connection_count"], txHops.Rows, 20);
        else
            PrintLine("  (none)", Color.Gray);

        // Shared identifiers
        var idents = Execute(
            $"MATCH (src:Account {{id: '{accountId}'}})-[:USED]->(id:Identifier)<-[:USED]-(peer:Account) " +
            "WHERE peer <> src " +
            "RETURN id.kind AS kind, id.value AS value, peer.id AS peer_id, " +
            "       peer.name AS peer_name, peer.status AS peer_status " +
            "ORDER BY kind");

        PrintLine();
        PrintLine("  Shared identifiers with other accounts:", Color.Cyan);
        if (idents.IsSuccess && idents.Rows.Count > 0)
            PrintTable(["kind", "value", "peer_id", "peer_name", "peer_status"], idents.Rows, 20);
        else
            PrintLine("  (this account shares no identifiers with others)", Color.Gray);

        // Direct transactions
        var txns = Execute(
            $"MATCH (src:Account {{id: '{accountId}'}})-[:MADE]->(t:Transaction)-[:TO]->(dest:Account) " +
            "RETURN t.id AS tx_id, dest.id AS to_account, dest.name AS to_name, " +
            "       t.amount AS amount, t.category AS category, " +
            "       t.is_flagged AS flagged, t.date AS date " +
            "ORDER BY t.date DESC LIMIT 15");

        PrintLine();
        PrintLine("  Outgoing transactions (most recent):", Color.Cyan);
        if (txns.IsSuccess && txns.Rows.Count > 0)
            PrintTable(["tx_id", "to_account", "to_name", "amount", "category", "flagged", "date"], txns.Rows, 15);
        else
            PrintLine("  (no outgoing transactions)", Color.Gray);
    }

    /// <summary>
    /// Find the shortest path between two accounts — "follow the money" chain.
    /// Traverses MADE → Transaction → TO edges.
    /// </summary>
    public void ShortestPath(string fromId, string toId)
    {
        PrintHeader($"Shortest Money Path  ·  {fromId} → {toId}");

        // Validate both exist
        var check = Execute($"""
            MATCH (a:Account) WHERE a.id = '{fromId}' OR a.id = '{toId}'
            RETURN a.id AS id, a.name AS name, a.status AS status
            """);

        if (!check.IsSuccess || check.Rows.Count < 2)
        {
            PrintLine($"  One or both account IDs not found.", Color.Red);
            return;
        }

        foreach (var r in check.Rows)
        {
            var sc = (r["status"]?.ToString() ?? "") == "CONFIRMED_FRAUD" ? Color.Red :
                     (r["status"]?.ToString() ?? "") == "FLAGGED" ? Color.Yellow : Color.Green;
            Print($"  {r["id"],-10} "); PrintLine($"{r["name"],-28} [{r["status"]}]", sc);
        }
        PrintLine();

        // Use shortestPath — traverses any relationship type
        var pathQ = Execute(
            $"MATCH (a:Account {{id: '{fromId}'}}), (b:Account {{id: '{toId}'}}) " +
            "MATCH path = shortestPath((a)-[:MADE|TO|LINKED_TO*]-(b)) " +
            "RETURN length(path) AS hops, [n IN nodes(path) | n.id] AS node_ids");

        if (!pathQ.IsSuccess || pathQ.Rows.Count == 0)
        {
            PrintLine("  No path found between these accounts.", Color.Yellow);
            PrintLine("  They may be in disconnected components of the graph.", Color.Gray);
            return;
        }

        var hops    = pathQ.Rows[0]["hops"];
        var nodeIds = pathQ.Rows[0]["node_ids"]?.ToString() ?? "";

        PrintLine($"  Path found in {hops} hop(s):", Color.Green);
        PrintLine();

        // Fetch account details for each node in path
        var pathAccQ = Execute($"""
            MATCH (a:Account) WHERE a.id = '{fromId}' OR a.id = '{toId}'
            RETURN a.id AS id, a.name AS name, a.status AS status
            """);

        // Render hop chain
        var ids = nodeIds.Trim('[', ']').Split(',').Select(s => s.Trim().Trim('\'', '"')).ToList();
        for (int i = 0; i < ids.Count; i++)
        {
            var nid   = ids[i];
            var aRow  = pathAccQ.Rows.FirstOrDefault(r => r["id"]?.ToString() == nid);
            var name  = aRow?["name"]?.ToString() ?? nid;
            var status= aRow?["status"]?.ToString() ?? "";
            var sc    = status == "CONFIRMED_FRAUD" ? Color.Red :
                        status == "FLAGGED" ? Color.Yellow : Color.Cyan;

            Print($"    [{i}] ", Color.Gray);
            Print($"{nid,-12}", sc);
            PrintLine($"{name}  {(string.IsNullOrEmpty(status) ? "" : $"[{status}]")}", Color.Gray);

            if (i < ids.Count - 1)
                PrintLine($"         ↓", Color.Gray);
        }
        PrintLine();
    }

    /// <summary>
    /// Compute a composite risk score per account and rank them.
    /// Score = (shared_id_count × 30) + (flagged_tx_count × 15) + (tx_velocity_score)
    /// </summary>
    public void ScoreAll(int top = 20)
    {
        PrintHeader($"Account Risk Scores  ·  Top {top}");

        var scoreQ = Execute("""
            MATCH (a:Account)
            OPTIONAL MATCH (a)-[:USED]->(id:Identifier)<-[:USED]-(peer:Account)
              WHERE (peer.status = 'FLAGGED' OR peer.status = 'CONFIRMED_FRAUD') AND peer <> a
            WITH a, COUNT(DISTINCT id) AS shared_id_count
            OPTIONAL MATCH (a)-[:MADE]->(t:Transaction) WHERE t.is_flagged = true
            WITH a, shared_id_count, COUNT(t) AS flagged_tx_count
            OPTIONAL MATCH (a)-[:MADE]->(all_t:Transaction)
            WITH a, shared_id_count, flagged_tx_count, COUNT(all_t) AS total_txns
            WITH a,
                 shared_id_count,
                 flagged_tx_count,
                 total_txns,
                 (shared_id_count * 30 + flagged_tx_count * 15 + CASE WHEN total_txns > 10 THEN 20 ELSE total_txns * 2 END) AS risk_score
            WHERE risk_score > 0 OR a.status <> 'CLEAN'
            RETURN a.id AS account_id, a.name AS name, a.status AS status,
                   risk_score, shared_id_count, flagged_tx_count, total_txns
            ORDER BY risk_score DESC
            """);

        if (!scoreQ.IsSuccess)
        {
            PrintLine($"  [error] {scoreQ.Error}", Color.Red);
            return;
        }

        PrintLine($"  {scoreQ.Rows.Count} accounts with non-zero risk:", Color.Yellow);
        PrintLine();

        int rank = 1;
        foreach (var row in scoreQ.Rows.Take(top))
        {
            var status = row["status"]?.ToString() ?? "";
            var score  = Convert.ToDouble(row["risk_score"] ?? 0.0);

            var medal = rank == 1 ? "★" : rank == 2 ? "◆" : rank == 3 ? "▲" : " ";
            var sc    = status == "CONFIRMED_FRAUD" ? Color.Red :
                        status == "FLAGGED"          ? Color.Yellow :
                        score  > 50                 ? Color.Magenta : Color.White;

            Print($"  {medal} #{rank,-3}", score > 50 ? Color.Red : Color.Gray);
            Print($"  {row["account_id"],-12}", Color.Cyan);
            Print($"{row["name"],-28}", Color.White);
            Print($"  [{status,-16}] ", sc);
            Print($"score={score,6:F0}  ", Color.Magenta);
            Print($"shared_ids={row["shared_id_count"]}  ", Color.Gray);
            PrintLine($"flagged_txns={row["flagged_tx_count"]}", Color.Gray);
            rank++;
        }

        PrintLine();
        PrintLine("  Score formula:  (shared_id_count × 30) + (flagged_tx_count × 15) + tx_velocity_bonus", Color.Gray);
    }

    /// <summary>
    /// Run arbitrary Cypher — pretty-print results as a table.
    /// </summary>
    public void Explain(string cypher)
    {
        PrintHeader("Cypher Query");
        PrintLine($"  {cypher}", Color.Cyan);
        PrintLine();

        var r = Execute(cypher);

        if (!r.IsSuccess)
        {
            PrintLine($"  [error] {r.Error}", Color.Red);
            return;
        }

        PrintLine($"  {r.Rows.Count} row(s)  ·  {r.ElapsedMs} ms", Color.Gray);
        PrintLine();
        PrintTable(r.Columns, r.Rows);
        PrintLine();
        PrintLine("  💡 Tip: This query uses the fluent API under the hood — conn.Cypher(\"...\").AsEnumerable()", Color.Gray);
    }

    // ── Internal Execute wrapper (now uses fluent API) ────────────────────────

    public FraudQueryResult Execute(string cypher)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r  = _conn.Cypher(cypher).Execute();
        sw.Stop();

        if (!r.IsSuccess)
            return new FraudQueryResult(false, r.ErrorMessage ?? "Query failed",
                [], [], sw.ElapsedMilliseconds);

        // QueryResult is now IEnumerable<BogRow> — no more HasNext()/GetNext() loops
        var cols = r.ColumnNames.ToList();
        var rows = r.Select(row =>
            row.GetAsDictionary().ToDictionary(kv => kv.Key, kv => (object?)kv.Value));
        return new FraudQueryResult(true, string.Empty, cols, rows, sw.ElapsedMilliseconds);
    }

    // ── Schema (fluent Graph builder) ──────────────────────────────────────────

    private void SetupSchema()
    {
        _conn.Graph()
            .EnsureNodeTable("Account", new()
            {
                ["id"]      = LogicalTypeID.STRING,
                ["name"]    = LogicalTypeID.STRING,
                ["email"]   = LogicalTypeID.STRING,
                ["status"]  = LogicalTypeID.STRING,
                ["balance"] = LogicalTypeID.DOUBLE,
            })
            .EnsureNodeTable("Transaction", new()
            {
                ["id"]         = LogicalTypeID.STRING,
                ["amount"]     = LogicalTypeID.DOUBLE,
                ["category"]   = LogicalTypeID.STRING,
                ["date"]       = LogicalTypeID.STRING,
                ["is_flagged"] = LogicalTypeID.BOOL,
            })
            .EnsureNodeTable("Identifier", new()
            {
                ["id"]    = LogicalTypeID.STRING,
                ["value"] = LogicalTypeID.STRING,
                ["kind"]  = LogicalTypeID.STRING,   // ip, device, phone
            })
            .EnsureNodeTable("Rule", new()
            {
                ["id"]          = LogicalTypeID.STRING,
                ["name"]        = LogicalTypeID.STRING,
                ["description"] = LogicalTypeID.STRING,
            })
            .EnsureRelTable("MADE",       "Account",     "Transaction")
            .EnsureRelTable("TO",         "Transaction", "Account")
            .EnsureRelTable("USED",       "Account",     "Identifier")
            .EnsureRelTable("LINKED_TO",  "Account",     "Account",
                new() { ["reason"] = LogicalTypeID.STRING })
            .EnsureRelTable("FLAGGED_BY", "Transaction", "Rule",
                new() { ["score"] = LogicalTypeID.DOUBLE })
            .Commit();
    }

    // ── Seed data ─────────────────────────────────────────────────────────────

    private void SeedData()
    {
        _conn.BeginWriteTransaction();
        SeedRules();
        SeedIdentifiers();
        SeedAccounts();
        SeedLegitTransactions();
        SeedFraudRings();
        _conn.Commit();
    }

    private void PrintSeedSummary()
    {
        PrintLine($"  Graph loaded: {_seedSummaryAccounts} accounts · {_seedSummaryTxns} transactions " +
                  $"· {_seedSummaryIds} identifiers", Color.Gray);
        PrintLine();
    }

    // ── Rules ───────────────────────────────────────────────

    private void SeedRules()
    {
        void R(string id, string name, string desc) =>
            _conn.UpsertNodeById("Rule", id, new() { ["id"]=id, ["name"]=name, ["description"]=desc });

        R("rule-vel",  "Velocity",          "More than 5 transactions within 24 hours");
        R("rule-str",  "Structuring",       "Multiple transactions just below reporting threshold ($10k)");
        R("rule-rt",   "Rapid Round-Trip",  "Amount sent and returned within 48 hours");
        R("rule-new",  "New Account",       "Account less than 30 days old making large transfers");
        R("rule-mule", "Money Mule Signal", "Account receives then immediately forwards large sums");
    }

    // ── Identifiers ─────────────────────────────────────────

    private static readonly string[] _ips = [
        "192.168.1.1","192.168.1.2","10.0.0.1","10.0.0.2","172.16.0.1","172.16.0.2",
        "203.0.113.5","203.0.113.6","198.51.100.7","198.51.100.8",
        "192.0.2.10","192.0.2.11","192.0.2.12","192.0.2.13","192.0.2.14",
        "10.10.10.1","10.10.10.2","10.10.10.3","10.10.10.4","10.10.10.5",
    ];

    private static readonly string[] _devices = [
        "DEVICE-AABB","DEVICE-CCDD","DEVICE-EEFF","DEVICE-0011","DEVICE-2233",
        "DEVICE-4455","DEVICE-6677","DEVICE-8899","DEVICE-AABB2","DEVICE-CCDD2",
        "DEVICE-RING1A","DEVICE-RING1B","DEVICE-RING2A","DEVICE-RING2B","DEVICE-RING3A",
    ];

    private static readonly string[] _phones = [
        "+1-555-0001","+1-555-0002","+1-555-0003","+1-555-0004","+1-555-0005",
        "+1-555-0006","+1-555-0007","+1-555-0008","+1-555-0009","+1-555-0010",
        "+1-555-RING1","+1-555-RING2","+1-555-RING3",
    ];

    private void SeedIdentifiers()
    {
        int n = 0;
        foreach (var ip in _ips)
        {
            var id = $"id-ip-{n++}";
            _conn.UpsertNodeById("Identifier", id, new() { ["id"]=id, ["value"]=ip, ["kind"]="ip" });
        }
        foreach (var dev in _devices)
        {
            var id = $"id-dev-{n++}";
            _conn.UpsertNodeById("Identifier", id, new() { ["id"]=id, ["value"]=dev, ["kind"]="device" });
        }
        foreach (var ph in _phones)
        {
            var id = $"id-ph-{n++}";
            _conn.UpsertNodeById("Identifier", id, new() { ["id"]=id, ["value"]=ph, ["kind"]="phone" });
        }
        _seedSummaryIds = n;
    }

    // ── Legitimate accounts ──────────────────────────────────

    private static readonly string[] _legitFirsts = [
        "Alice","Bob","Carol","David","Eve","Frank","Grace","Henry","Iris","Jack",
        "Karen","Leo","Mia","Nathan","Olivia","Paul","Quinn","Rachel","Steve","Tina",
        "Uma","Victor","Wendy","Xavier","Yolanda","Aaron","Beth","Chad","Dana","Erik",
        "Faye","Glen","Hana","Ian","Jane","Kyle","Lisa","Mike","Nora","Otto",
        "Pam","Raj","Sara","Tom","Una","Vera","Walt","Xena","Yuki","Zack",
        "Abby","Ben","Cleo","Dan","Ella","Fred","Gina","Hal","Ivy","John",
        "Kim","Luke","Mona","Ned","Opal","Pete","Quin","Rose","Sean","Tess",
        "Ugo","Vince","Willa","Xiu","Yasmin","Zoe",
    ];

    private static readonly string[] _legitLasts = [
        "Smith","Jones","Williams","Brown","Davies","Evans","Wilson","Taylor","Thomas","Johnson",
        "Roberts","Walker","White","Hall","Martin","Thompson","Garcia","Martinez","Robinson","Clark",
        "Rodriguez","Lewis","Lee","Walker","Allen","Young","Hernandez","King","Wright","Lopez",
        "Hill","Scott","Green","Adams","Baker","Nelson","Carter","Mitchell","Perez","Roberts",
    ];

    private readonly List<string> _legitAccountIds = [];

    private void SeedAccounts()
    {
        var rng = new Random(7);
        int n   = 0;

        // 120 legitimate CLEAN accounts
        for (int i = 0; i < 120; i++)
        {
            var id    = $"acc-{n:D4}";
            var first = _legitFirsts[i % _legitFirsts.Length];
            var last  = _legitLasts[(i * 3 + 7) % _legitLasts.Length];
            var name  = $"{first} {last}";
            var email = $"{first.ToLower()}.{last.ToLower()}{i}@example.com";
            double bal = 1000 + rng.NextDouble() * 49000;

            _conn.UpsertNodeById("Account", id, new()
            {
                ["id"]="id",["id"]=id, ["name"]=name, ["email"]=email,
                ["status"]="CLEAN", ["balance"]=Math.Round(bal, 2),
            });

            // Assign 1-2 identifiers to legitimate accounts (not fraud ring identifiers)
            int ipIdx  = rng.Next(10);          // first 10 IPs are legit
            int devIdx = rng.Next(10);          // first 10 devices are legit
            _conn.UpsertRelationshipById("USED", id, $"id-ip-{ipIdx}",  new());
            _conn.UpsertRelationshipById("USED", id, $"id-dev-{devIdx + 20}", new());

            _legitAccountIds.Add(id);
            n++;
        }

        _seedSummaryAccounts = n; // ring accounts added later
    }

    // ── Legitimate transactions ──────────────────────────────

    private static readonly string[] _categories = [
        "SALARY","UTILITIES","RENT","GROCERY","RETAIL","INSURANCE",
        "SUBSCRIPTION","MEDICAL","TAX_PAYMENT","INVESTMENT",
    ];

    private int _txCounter;

    private void SeedLegitTransactions()
    {
        var rng  = new Random(13);
        int year = 2024;

        for (int i = 0; i < 120; i++)
        {
            var fromId = _legitAccountIds[i];
            // 3-8 transactions each
            int count = 3 + rng.Next(6);
            for (int j = 0; j < count; j++)
            {
                var txId   = $"tx-{_txCounter++:D5}";
                var toIdx  = rng.Next(_legitAccountIds.Count);
                var toId   = _legitAccountIds[toIdx];
                if (toId == fromId) continue;

                double amt = 50 + rng.NextDouble() * 4950;
                var cat    = _categories[rng.Next(_categories.Length)];
                var month  = 1 + rng.Next(12);
                var day    = 1 + rng.Next(28);
                var date   = $"{year}-{month:D2}-{day:D2}";
                bool flag  = false;

                _conn.UpsertNodeById("Transaction", txId, new()
                {
                    ["id"]=txId, ["amount"]=Math.Round(amt,2),
                    ["category"]=cat, ["date"]=date, ["is_flagged"]=flag,
                });
                _conn.UpsertRelationshipById("MADE", fromId, txId, new());
                _conn.UpsertRelationshipById("TO",   txId, toId, new());
            }
        }
        _seedSummaryTxns = _txCounter;
    }

    // ── Fraud rings ──────────────────────────────────────────

    private void SeedFraudRings()
    {
        SeedRing("ring1", 8,  "FLAGGED",          startOffset: 20, sharedIpBase: 10, sharedDevBase: 30, sharedPhBase: 40);
        SeedRing("ring2", 10, "CONFIRMED_FRAUD",   startOffset: 21, sharedIpBase: 12, sharedDevBase: 32, sharedPhBase: 41);
        SeedRing("ring3", 7,  "FLAGGED",           startOffset: 22, sharedIpBase: 14, sharedDevBase: 34, sharedPhBase: 42);
    }

    private void SeedRing(string ringId, int size, string status,
        int startOffset, int sharedIpBase, int sharedDevBase, int sharedPhBase)
    {
        var rng      = new Random(ringId.GetHashCode());
        var ringAccs = new List<string>();

        // Create ring accounts
        for (int i = 0; i < size; i++)
        {
            var id    = $"acc-{ringId}-{i:D2}";
            var name  = $"{ringId.ToUpper().Replace("ring", "Ring")} Member {(char)('A' + i)}";
            var email = $"{ringId}.member{i}@tempmail.io";
            var st    = (i == 0) ? status : (rng.NextDouble() < 0.4 ? status : "CLEAN");

            _conn.UpsertNodeById("Account", id, new()
            {
                ["id"]=id, ["name"]=name, ["email"]=email,
                ["status"]=st, ["balance"]=Math.Round(500 + rng.NextDouble() * 9500, 2),
            });

            // Each ring member shares the ring's IP, device, phone
            _conn.UpsertRelationshipById("USED", id, $"id-ip-{sharedIpBase}",  new());
            _conn.UpsertRelationshipById("USED", id, $"id-dev-{sharedDevBase + 20}", new());
            _conn.UpsertRelationshipById("USED", id, $"id-ph-{sharedPhBase + 40}",  new());

            // Each also has a unique identifier
            int extras = _seedSummaryIds + ringAccs.Count;
            var extraId = $"id-extra-{ringId}-{i}";
            _conn.UpsertNodeById("Identifier", extraId, new()
            {
                ["id"]=extraId,
                ["value"]=$"192.168.{startOffset}.{i}",
                ["kind"]="ip",
            });
            _conn.UpsertRelationshipById("USED", id, extraId, new());

            ringAccs.Add(id);
        }

        // LINKED_TO the first 3 in a chain
        for (int i = 0; i < Math.Min(3, size - 1); i++)
            _conn.UpsertRelationshipById("LINKED_TO", ringAccs[i], ringAccs[i + 1],
                new() { ["reason"]="joint_account" });

        // Rapid round-trip transactions inside the ring
        for (int i = 0; i < size; i++)
        {
            int next = (i + 1) % size;
            // Structuring pattern: multiple transactions just below $9,950 threshold
            for (int s = 0; s < 3; s++)
            {
                var txId = $"tx-{_txCounter++:D5}";
                double amt = 9800 + rng.NextDouble() * 149;  // $9,800–$9,950
                _conn.UpsertNodeById("Transaction", txId, new()
                {
                    ["id"]=txId, ["amount"]=Math.Round(amt, 2),
                    ["category"]="TRANSFER",
                    ["date"]=$"2024-{1 + (s % 12):D2}-{1 + rng.Next(28):D2}",
                    ["is_flagged"]=true,
                });
                _conn.UpsertRelationshipById("MADE",       ringAccs[i], txId, new());
                _conn.UpsertRelationshipById("TO",         txId, ringAccs[next], new());
                _conn.UpsertRelationshipById("FLAGGED_BY", txId, "rule-str",
                    new() { ["score"]=0.85 + rng.NextDouble() * 0.15 });
            }

            // Rapid round-trip: send then receive back
            var rtId = $"tx-{_txCounter++:D5}";
            double rtAmt = 5000 + rng.NextDouble() * 20000;
            _conn.UpsertNodeById("Transaction", rtId, new()
            {
                ["id"]=rtId, ["amount"]=Math.Round(rtAmt, 2),
                ["category"]="TRANSFER",
                ["date"]="2024-03-15",
                ["is_flagged"]=true,
            });
            _conn.UpsertRelationshipById("MADE",       ringAccs[next], rtId, new());
            _conn.UpsertRelationshipById("TO",         rtId, ringAccs[i], new());
            _conn.UpsertRelationshipById("FLAGGED_BY", rtId, "rule-rt",
                new() { ["score"]=0.9 });
        }

        _seedSummaryAccounts += size;
        _seedSummaryTxns      = _txCounter;
    }

    // ── Utilities ─────────────────────────────────────────────────────────────

    private static string[] SplitArgs(string line)
    {
        // Respect quoted strings
        var parts  = new List<string>();
        var cur    = new StringBuilder();
        bool inQ   = false;
        foreach (char c in line)
        {
            if (c == '"') { inQ = !inQ; }
            else if (c == ' ' && !inQ) { if (cur.Length > 0) { parts.Add(cur.ToString()); cur.Clear(); } }
            else cur.Append(c);
        }
        if (cur.Length > 0) parts.Add(cur.ToString());
        return [.. parts];
    }

    private static int GetInt(string[] args, string flag, int def)
    {
        int i = Array.IndexOf(args, flag);
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v)) return v;
        return def;
    }
}
