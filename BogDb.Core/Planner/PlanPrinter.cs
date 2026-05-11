using System.Text;
using BogDb.Core.Planner.Operator;

namespace BogDb.Core.Planner;

internal static class PlanPrinter
{
    public static string Print(LogicalPlan plan)
    {
        if (plan.LastOperator == null)
            return "<empty plan>";

        var sb = new StringBuilder();
        AppendOperator(sb, plan.LastOperator, 0);
        return sb.ToString().TrimEnd();
    }

    private static void AppendOperator(StringBuilder sb, LogicalOperator op, int depth)
    {
        sb.Append(' ', depth * 2);
        sb.Append(GetOperatorName(op.OperatorType));

        var expressions = op.GetExpressionsForPrinting();
        if (!string.IsNullOrWhiteSpace(expressions))
        {
            sb.Append(" ");
            sb.Append(expressions);
        }
        sb.AppendLine();

        foreach (var child in op.Children)
            AppendOperator(sb, child, depth + 1);
    }

    private static string GetOperatorName(LogicalOperatorType operatorType)
    {
        var name = operatorType.ToString();
        if (name.StartsWith("LOGICAL_", System.StringComparison.Ordinal))
            name = name["LOGICAL_".Length..];
        return name;
    }
}
