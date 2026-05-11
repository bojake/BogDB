using System;
using BogDb.Core.Extension;
using BogDb.Core.Main;
using DuckDB.NET.Data;

namespace BogDb.Extensions.DuckDB
{
    public class DuckDBExtension : IExtension
    {
        public string Name => "duckdb";

        public void Load(BogDatabase database)
        {
            ArgumentNullException.ThrowIfNull(database);

            var scanDuckDb = new ScanDuckDbTableFunction();
            database.FunctionRegistry.Register(scanDuckDb);
            database.StandaloneTableFunctionRegistry.Register(scanDuckDb);
            database.RegisterStorageExtension("duckdb", new DuckDbStorageExtension());
        }

        // Simulates mapping DuckDB vectors to BogDb ValueVectors natively!
        public DuckDBConnection CreateConnection(string connectionString)
        {
            return new DuckDBConnection(connectionString);
        }
    }
}
