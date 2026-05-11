using BogDb.Core.Common;

namespace BogDb.Core.Parser;

public enum ExtensionCommand : byte
{
    LOAD,
    INSTALL,
    UNINSTALL,
    UPDATE
}

public sealed class ExtensionStatement : Statement
{
    public ExtensionCommand Command { get; }
    public string ExtensionNameOrPath { get; }
    public bool ForceInstall { get; }
    public string? RepositoryPath { get; }

    public ExtensionStatement(
        ExtensionCommand command,
        string extensionNameOrPath,
        bool forceInstall = false,
        string? repositoryPath = null)
        : base(StatementType.EXTENSION)
    {
        Command = command;
        ExtensionNameOrPath = extensionNameOrPath;
        ForceInstall = forceInstall;
        RepositoryPath = repositoryPath;
    }
}
