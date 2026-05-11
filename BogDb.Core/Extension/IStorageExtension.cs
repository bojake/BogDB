namespace BogDb.Core.Extension;

public sealed class AttachedTableInfo
{
    public string Name { get; }
    public IReadOnlyList<(string Name, string Type)> Columns { get; }

    public AttachedTableInfo(string name, IReadOnlyList<(string Name, string Type)> columns)
    {
        Name = name;
        Columns = columns;
    }
}

public sealed class AttachedDatabaseHandle
{
    public string Alias { get; }
    public string DbType { get; }
    public string Path { get; }
    public string ExtensionName { get; }
    public IReadOnlyDictionary<string, AttachedTableInfo> Tables { get; }

    public AttachedDatabaseHandle(
        string alias,
        string dbType,
        string path,
        string extensionName,
        IReadOnlyDictionary<string, AttachedTableInfo> tables)
    {
        Alias = alias;
        DbType = dbType;
        Path = path;
        ExtensionName = extensionName;
        Tables = tables;
    }
}

/// <summary>
/// Executable contract for read-oriented foreign storage extensions.
/// The first slice supports ATTACH discovery plus row scans through LOAD FROM.
/// </summary>
public interface IStorageExtension
{
    string Name { get; }
    bool CanHandle(string dbType);
    AttachedDatabaseHandle Attach(
        Main.BogDatabase database,
        string alias,
        string path,
        IReadOnlyDictionary<string, object?> options);
    IEnumerable<Dictionary<string, object?>> Scan(
        AttachedDatabaseHandle attachedDatabase,
        string? tableName);
}
