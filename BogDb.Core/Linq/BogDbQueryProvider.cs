using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using BogDb.Core.Main;

namespace BogDb.Core.Linq
{
    public class BogDbQueryProvider : IQueryProvider
    {
        private readonly BogConnection _connection;
        private readonly string _label;

        public BogDbQueryProvider(BogConnection connection, string label)
        {
            _connection = connection;
            _label = label;
        }

        public IQueryable CreateQuery(Expression expression)
        {
            var elementType = expression.Type.GetGenericArguments()[0];
            var queryableType = typeof(BogDbQueryable<>).MakeGenericType(elementType);
            return (IQueryable)Activator.CreateInstance(queryableType, this, expression)!;
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new BogDbQueryable<TElement>(this, expression);
        }

        public object? Execute(Expression expression)
        {
            return Execute<object>(expression);
        }

        public TResult Execute<TResult>(Expression expression)
        {
            bool isEnumerable = typeof(TResult).Name == "IEnumerable`1";
            var visitor = new CypherExpressionVisitor(_label);
            visitor.Visit(expression);
            string cypherQuery = visitor.GetCypherQuery();

            var result = _connection.Query(cypherQuery);
            
            // Map flat BogRow properties logically into generic T mappings gracefully natively
            Type listType = typeof(List<>).MakeGenericType(isEnumerable ? typeof(TResult).GetGenericArguments()[0] : typeof(TResult));
            var list = (System.Collections.IList)Activator.CreateInstance(listType)!;

            while (result.HasNext())
            {
                var dict = result.GetNext().GetAsDictionary();
                // Simple generic object creation via JSON serialization or explicit dict parsing
                // Assuming dict represents properties for type T:
                string json = System.Text.Json.JsonSerializer.Serialize(dict);
                var obj = System.Text.Json.JsonSerializer.Deserialize(json, isEnumerable ? typeof(TResult).GetGenericArguments()[0] : typeof(TResult));
                list.Add(obj);
            }

            if (!isEnumerable)
            {
                if (list.Count > 0) return (TResult)list[0]!;
                return default!;
            }

            return (TResult)list;
        }
    }
}
