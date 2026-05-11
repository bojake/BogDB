using System.Text.Json;
using BogDb.Core.Catalog;
using BogDb.Core.Main;

namespace BogDb.Mcp.Server.Services;

public sealed class BogDbSchemaToolService
{
    public object GetSchema(JsonElement arguments)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        using var database = BogDatabase.Open(databasePath);

        var nodeTables = database.Catalog.GetNodeTableEntries()
            .OfType<NodeTableCatalogEntry>()
            .Select(ToNodeTableInfo)
            .ToArray();

        var relTables = database.Catalog.GetRelTableEntries()
            .OfType<RelGroupCatalogEntry>()
            .Select(ToRelTableInfo)
            .ToArray();

        return new
        {
            nodeTables,
            relTables
        };
    }

    public object GetTables(JsonElement arguments)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        using var database = BogDatabase.Open(databasePath);

        return new
        {
            nodeTables = database.Catalog.GetNodeTableEntries().Select(entry => entry.Name).OrderBy(name => name).ToArray(),
            relTables = database.Catalog.GetRelTableEntries().Select(entry => entry.Name).OrderBy(name => name).ToArray()
        };
    }

    public object GetTableInfo(JsonElement arguments)
    {
        var databasePath = JsonArgumentReader.GetRequiredString(arguments, "databasePath");
        var tableName = JsonArgumentReader.GetRequiredString(arguments, "tableName");
        using var database = BogDatabase.Open(databasePath);

        var entry = database.Catalog.GetTableEntry(tableName);
        if (entry == null)
            throw new InvalidOperationException($"Table '{tableName}' was not found.");

        return entry switch
        {
            NodeTableCatalogEntry nodeEntry => ToNodeTableInfo(nodeEntry),
            RelGroupCatalogEntry relEntry => ToRelTableInfo(relEntry),
            _ => throw new InvalidOperationException($"Table '{tableName}' is not a supported catalog entry.")
        };
    }

    private static object ToNodeTableInfo(NodeTableCatalogEntry entry)
    {
        var properties = entry.GetProperties().ToArray();
        string? primaryKey = null;
        if (entry.PrimaryKeyPropertyID < properties.Length)
            primaryKey = properties[entry.PrimaryKeyPropertyID].Name;

        return new
        {
            name = entry.Name,
            kind = "node",
            primaryKey,
            properties = properties.Select(ToPropertyInfo).ToArray()
        };
    }

    private static object ToRelTableInfo(RelGroupCatalogEntry entry)
    {
        return new
        {
            name = entry.Name,
            kind = "relationship",
            connections = entry.GetConnections()
                .Select(connection => new { from = connection.SrcTableName, to = connection.DstTableName })
                .ToArray(),
            properties = entry.GetProperties().Select(ToPropertyInfo).ToArray()
        };
    }

    private static object ToPropertyInfo(PropertyDefinition property)
    {
        return new
        {
            name = property.Name,
            type = property.ColumnDef.DeclaredType,
            logicalType = property.ColumnDef.Type.ToString(),
            defaultExpression = property.DefaultExpressionName
        };
    }
}
