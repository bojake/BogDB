using BogDb.Samples.FraudGraph.Console;

// ─────────────────────────────────────────────────────────────────────────────
// BogDb.Samples.FraudGraph.Console
//
// Usage:
//   fraudgraph detect-rings [--min-shared <n>]
//   fraudgraph trace <account-id>
//   fraudgraph shortest-path <from-id> <to-id>
//   fraudgraph score-all [--top <n>]
//   fraudgraph explain "<cypher>"
//   fraudgraph help
//
// Run without args to enter interactive REPL mode.
// ─────────────────────────────────────────────────────────────────────────────

var graph = new FraudGraphService();
graph.Print(Banner());

if (args.Length == 0)
{
    graph.PrintLine("[info] No command given — entering interactive REPL. Type 'help' or 'exit'.");
    graph.Repl();
    return;
}

string cmd = args[0].ToLowerInvariant();

switch (cmd)
{
    case "detect-rings":
        int minShared = GetInt(args, "--min-shared", 2);
        graph.DetectRings(minShared);
        break;

    case "trace":
        if (args.Length < 2) { graph.PrintLine("[error] Usage: trace <account-id>"); break; }
        graph.Trace(args[1]);
        break;

    case "shortest-path":
        if (args.Length < 3) { graph.PrintLine("[error] Usage: shortest-path <from-id> <to-id>"); break; }
        graph.ShortestPath(args[1], args[2]);
        break;

    case "score-all":
        int top = GetInt(args, "--top", 20);
        graph.ScoreAll(top);
        break;

    case "explain":
        if (args.Length < 2) { graph.PrintLine("[error] Usage: explain \"<cypher>\""); break; }
        string cypher = string.Join(" ", args.Skip(1));
        graph.Explain(cypher);
        break;

    case "help":
    default:
        graph.PrintHelp();
        break;
}

static int GetInt(string[] args, string flag, int defaultVal)
{
    int i = Array.IndexOf(args, flag);
    if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int v)) return v;
    return defaultVal;
}

static string Banner() => """

  ┌─────────────────────────────────────────────┐
  │  FraudGraph · BogDB Console Sample        │
  │  Financial fraud ring detection via graphs  │
  └─────────────────────────────────────────────┘

""";
