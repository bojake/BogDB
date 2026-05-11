using System;
using System.IO;
using BogDb.Core.Parser;

namespace BogDb.Core.Processor.Operator;

/// <summary>
/// Executes extension lifecycle statements once and produces no output tuples.
/// </summary>
public sealed class PhysicalExtensionStatement : PhysicalOperator
{
    private readonly Main.BogDatabase _database;
    private readonly ExtensionStatement _statement;
    private bool _executed;

    public PhysicalExtensionStatement(Main.BogDatabase database, ExtensionStatement statement, uint id)
        : base(PhysicalOperatorType.STANDALONE_CALL, id)
    {
        _database = database;
        _statement = statement;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_executed)
            return false;

        _executed = true;
        switch (_statement.Command)
        {
            case ExtensionCommand.LOAD:
            case ExtensionCommand.INSTALL:
                if (LooksLikeExtensionPath(_statement.ExtensionNameOrPath))
                {
                    var path = _statement.ExtensionNameOrPath;
                    var name = GetExtensionNameFromPath(path);
                    _database.ExtensionManager.LoadExtension(path, name);
                }
                else
                {
                    _database.ExtensionManager.LoadExtension(_statement.ExtensionNameOrPath);
                }

                break;
            case ExtensionCommand.UPDATE:
                _database.ExtensionManager.ReloadExtension(_statement.ExtensionNameOrPath);
                break;
            case ExtensionCommand.UNINSTALL:
                _database.ExtensionManager.UnloadExtension(_statement.ExtensionNameOrPath);
                break;
            default:
                throw new NotSupportedException($"Unsupported extension command: {_statement.Command}");
        }

        return false;
    }

    private static bool LooksLikeExtensionPath(string target)
        => target.Contains(Path.DirectorySeparatorChar) ||
           target.Contains(Path.AltDirectorySeparatorChar) ||
           target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);

    private static string GetExtensionNameFromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        const string prefix = "BogDb.Extensions.";
        return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fileName[prefix.Length..]
            : fileName;
    }
}
