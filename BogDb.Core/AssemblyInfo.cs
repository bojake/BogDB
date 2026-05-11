using System.Runtime.CompilerServices;

// Expose internals to BogDb.Tests so test-code can access NodeTables, NodeTableData, EdgeKey et al.
[assembly: InternalsVisibleTo("BogDb.Tests")]
