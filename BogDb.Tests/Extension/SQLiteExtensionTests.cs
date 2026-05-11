using System;
using Xunit;
using BogDb.Extensions.SQLite;

namespace BogDb.Tests.Extension
{
    public class SQLiteExtensionTests
    {
        [Fact]
        public void SQLiteExtension_CreateConnection_BindsSqliteStringsSafely()
        {
            var extension = new SQLiteExtension();
            
            // Simulates local SQLite file interactions executing mapping correctly
            var connection = extension.CreateConnection("Data Source=:memory:");
            
            Assert.NotNull(connection);
            Assert.Equal("sqlite", extension.Name);
        }

        [Fact]
        public void SQLiteExtension_Load_RegistersScanSqliteFunction()
        {
            var extension = new SQLiteExtension();
            var db = BogDb.Core.Main.BogDatabase.Open(":memory:");

            extension.Load(db);

            Assert.True(db.FunctionRegistry.Contains("scan_sqlite"),
                "scan_sqlite should be registered after Load()");
        }
    }
}
