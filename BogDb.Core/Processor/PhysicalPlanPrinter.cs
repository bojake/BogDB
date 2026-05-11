using System.Text;
using BogDb.Core.Processor.Operator;

namespace BogDb.Core.Processor;

internal static class PhysicalPlanPrinter
{
    public static string Print(PhysicalPlan plan)
    {
        if (plan.LastOperator == null)
            return "<empty plan>";

        var sb = new StringBuilder();
        AppendOperator(sb, plan.LastOperator, 0);
        return sb.ToString().TrimEnd();
    }

    private static void AppendOperator(StringBuilder sb, PhysicalOperator op, int depth)
    {
        sb.Append(' ', depth * 2);
        sb.Append(op.OperatorType.ToString());
        sb.AppendLine();

        foreach (var child in op.Children)
            AppendOperator(sb, child, depth + 1);
    }
}
