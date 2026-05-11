using System;
using System.IO;
using System.Text;
using BogDb.Core.Main.QueryResult;

namespace BogDb.Core.Main
{
    /// <summary>
    /// Implements an elegant Fluent API allowing method chaining for sequential Cypher evaluation pipelines natively!
    /// Example: db.BeginPipeline().Run("schema.cypher").Query("MATCH (a) RETURN a").AggregateIntoCoolReport();
    /// </summary>
    public class BogDbFluentPipeline
    {
        private readonly BogConnection _connection;
        public QueryResult.QueryResult? LastResult { get; private set; }

        public BogDbFluentPipeline(BogConnection connection)
        {
            _connection = connection;
        }

        public BogDbFluentPipeline Run(string scriptPath)
        {
            _connection.ExecuteScript(scriptPath);
            return this;
        }

        public BogDbFluentPipeline Query(string query)
        {
            LastResult = _connection.Query(query);
            return this;
        }

        public BogDbFluentPipeline AggregateIntoCoolReport()
        {
            if (LastResult == null)
            {
                Console.WriteLine("No Query Result to Aggregate!");
                return this;
            }

            Console.WriteLine("================ ACTIVE QUERY REPORT ================");
            var sb = new StringBuilder();
            
            // Iterate and format tuples elegantly!
            while (LastResult.HasNext())
            {
                var tuple = LastResult.GetNext();
                sb.AppendLine($"| {tuple.ToString()} |");
            }
            
            if (sb.Length == 0)
            {
                Console.WriteLine("0 Rows Returned.");
            }
            else
            {
                Console.Write(sb.ToString());
            }
            Console.WriteLine("=====================================================");

            return this;
        }
    }

    public static class BogDbFluentExtensions
    {
        public static BogDbFluentPipeline BeginPipeline(this BogConnection connection)
        {
            return new BogDbFluentPipeline(connection);
        }
    }
}
