using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Parser;

public abstract class DdlStatement : Statement
{
    public string TableName { get; }

    protected DdlStatement(StatementType statementType, string tableName) : base(statementType)
    {
        TableName = tableName;
    }
}

public readonly record struct DeclaredColumnDefinition(string Name, string TypeName);

public class CreateTableInfo
{
    public string TableName { get; }
    public IReadOnlyList<DeclaredColumnDefinition> PropertyDefinitions { get; }

    public CreateTableInfo(string tableName, List<DeclaredColumnDefinition> properties)
    {
        TableName = tableName;
        PropertyDefinitions = properties;
    }
}

public class CreateNodeTable : DdlStatement
{
    public CreateTableInfo Info { get; }

    public CreateNodeTable(CreateTableInfo info) : base(StatementType.CREATE_TABLE, info.TableName)
    {
        Info = info;
    }
}

public class CreateRelTable : DdlStatement
{
    public CreateTableInfo Info { get; }
    public string SrcTableName { get; }
    public string DstTableName { get; }

    public CreateRelTable(CreateTableInfo info, string srcTable, string dstTable) 
        : base(StatementType.CREATE_TABLE, info.TableName)
    {
        Info = info;
        SrcTableName = srcTable;
        DstTableName = dstTable;
    }
}

public class DropTable : DdlStatement
{
    public DropTable(string tableName) : base(StatementType.DROP, tableName) {}
}

public abstract class AlterTable : DdlStatement
{
    protected AlterTable(string tableName) : base(StatementType.ALTER, tableName) {}
}

public sealed class AlterTableAddProperty : AlterTable
{
    public string PropertyName { get; }
    public string TypeName { get; }
    public ParsedExpression? DefaultExpression { get; }

    public AlterTableAddProperty(string tableName, string propertyName, string typeName, ParsedExpression? defaultExpression)
        : base(tableName)
    {
        PropertyName = propertyName;
        TypeName = typeName;
        DefaultExpression = defaultExpression;
    }
}

public sealed class AlterTableDropProperty : AlterTable
{
    public string PropertyName { get; }

    public AlterTableDropProperty(string tableName, string propertyName)
        : base(tableName)
    {
        PropertyName = propertyName;
    }
}

public sealed class AlterTableRenameTable : AlterTable
{
    public string NewTableName { get; }

    public AlterTableRenameTable(string tableName, string newTableName)
        : base(tableName)
    {
        NewTableName = newTableName;
    }
}

public sealed class AlterTableRenameProperty : AlterTable
{
    public string OldPropertyName { get; }
    public string NewPropertyName { get; }

    public AlterTableRenameProperty(string tableName, string oldPropertyName, string newPropertyName)
        : base(tableName)
    {
        OldPropertyName = oldPropertyName;
        NewPropertyName = newPropertyName;
    }
}

public sealed class AlterTableConnectionChange : AlterTable
{
    public bool IsAdd { get; }
    public bool IgnoreIfPresentOrMissing { get; }
    public string FromTableName { get; }
    public string ToTableName { get; }

    public AlterTableConnectionChange(
        string tableName,
        bool isAdd,
        bool ignoreIfPresentOrMissing,
        string fromTableName,
        string toTableName)
        : base(tableName)
    {
        IsAdd = isAdd;
        IgnoreIfPresentOrMissing = ignoreIfPresentOrMissing;
        FromTableName = fromTableName;
        ToTableName = toTableName;
    }
}
