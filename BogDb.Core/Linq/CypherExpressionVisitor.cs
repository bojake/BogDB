using System;
using System.Linq.Expressions;
using System.Text;

namespace BogDb.Core.Linq
{
    public class CypherExpressionVisitor : ExpressionVisitor
    {
        private readonly string _label;
        private readonly StringBuilder _whereBuilder = new StringBuilder();
        private int _skip = 0;
        private int _limit = 0;
        private string _orderBy = "";
        
        // Simple alias mapping (e.g., 'x' inside where(x => x.Age))
        private string _alias = "n"; 

        public CypherExpressionVisitor(string label)
        {
            _label = label;
        }

        public string GetCypherQuery()
        {
            var sb = new StringBuilder();
            sb.Append($"MATCH ({_alias}");
            if (!string.IsNullOrEmpty(_label)) sb.Append($":{_label}");
            sb.Append(")");

            if (_whereBuilder.Length > 0)
            {
                sb.Append($" WHERE {_whereBuilder.ToString()}");
            }

            sb.Append($" RETURN {_alias}");

            if (!string.IsNullOrEmpty(_orderBy))
            {
                sb.Append($" ORDER BY {_orderBy}");
            }

            if (_skip > 0) sb.Append($" SKIP {_skip}");
            if (_limit > 0) sb.Append($" LIMIT {_limit}");

            return sb.ToString();
        }

        protected override Expression VisitMethodCall(MethodCallExpression node)
        {
            if (node.Method.DeclaringType == typeof(System.Linq.Queryable))
            {
                if (node.Method.Name == "Where")
                {
                    Visit(node.Arguments[0]); // Visit the structural source implicitly 
                    if (_whereBuilder.Length > 0) _whereBuilder.Append(" AND ");
                    Visit(node.Arguments[1]); // Evaluate lambda body cleanly
                    return node;
                }
                else if (node.Method.Name == "Skip")
                {
                    Visit(node.Arguments[0]);
                    _skip = (int)((ConstantExpression)node.Arguments[1]).Value!;
                    return node;
                }
                else if (node.Method.Name == "Take")
                {
                    Visit(node.Arguments[0]);
                    _limit = (int)((ConstantExpression)node.Arguments[1]).Value!;
                    return node;
                }
                else if (node.Method.Name == "OrderBy" || node.Method.Name == "OrderByDescending")
                {
                    Visit(node.Arguments[0]);
                    var lambda = (LambdaExpression)((UnaryExpression)node.Arguments[1]).Operand;
                    var memberAccess = (MemberExpression)lambda.Body;
                    _orderBy = $"{_alias}.{memberAccess.Member.Name}";
                    if (node.Method.Name == "OrderByDescending") _orderBy += " DESC";
                    return node;
                }
                // Handle complex mappings...
            }
            return base.VisitMethodCall(node);
        }

        protected override Expression VisitLambda<T>(Expression<T> node)
        {
            _alias = node.Parameters[0].Name ?? "n";
            return Visit(node.Body);
        }

        protected override Expression VisitBinary(BinaryExpression node)
        {
            sbPush("(");
            Visit(node.Left);

            switch (node.NodeType)
            {
                case ExpressionType.Equal: sbPush(" = "); break;
                case ExpressionType.NotEqual: sbPush(" <> "); break;
                case ExpressionType.GreaterThan: sbPush(" > "); break;
                case ExpressionType.GreaterThanOrEqual: sbPush(" >= "); break;
                case ExpressionType.LessThan: sbPush(" < "); break;
                case ExpressionType.LessThanOrEqual: sbPush(" <= "); break;
                case ExpressionType.AndAlso: sbPush(" AND "); break;
                case ExpressionType.OrElse: sbPush(" OR "); break;
            }

            Visit(node.Right);
            sbPush(")");
            return node;
        }

        protected override Expression VisitMember(MemberExpression node)
        {
            if (node.Expression != null && node.Expression.NodeType == ExpressionType.Parameter)
            {
                sbPush($"{_alias}.{node.Member.Name}");
                return node;
            }
            return base.VisitMember(node);
        }

        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is IQueryable) return node;
            
            if (node.Value == null) sbPush("null");
            else if (node.Value is string s) sbPush($"'{s}'");
            else sbPush(node.Value.ToString()!);
            return node;
        }

        private void sbPush(string value)
        {
            _whereBuilder.Append(value);
        }
    }
}
