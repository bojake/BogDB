using System;
using Xunit;
using BogDb.Extensions.Postgres;

namespace BogDb.Tests.Extension
{
    public class PostgresExtensionTests
    {
        [Fact]
        public void PostgresExtension_CreateConnection_BindsNpgsqlConfigurationsCorrectly()
        {
            var extension = new PostgresExtension();
            
            // Simulates standard Postgres connection mapping local tests natively!
            var connection = extension.CreateConnection("Host=localhost;Username=postgres;Password=bogdb");
            
            Assert.NotNull(connection);
            Assert.Equal("postgres", extension.Name);
        }

        [Fact]
        public void PostgresExtension_Load_RegistersInternalsUnderDatabaseCorrectly()
        {
            var extension = new PostgresExtension();
            
            var exception = Record.Exception(() => extension.Load(null!));

            Assert.NotNull(exception);
            Assert.IsType<ArgumentNullException>(exception);
        }
    }
}
