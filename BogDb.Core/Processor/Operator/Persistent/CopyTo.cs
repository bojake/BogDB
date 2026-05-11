using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BogDb.Core.Common.FileSystem;

namespace BogDb.Core.Processor.Operator.Persistent;

/// <summary>
/// Physical COPY TO sink for projected query results.
/// </summary>
public sealed class CopyTo : PhysicalOperator
{
    private readonly string _filePath;
    private readonly IReadOnlyList<string> _columnNames;
    private readonly Main.BogDatabase? _database;
    private readonly PhysicalOperator _child;
    private bool _executed;

    public CopyTo(
        string filePath,
        IReadOnlyList<string> columnNames,
        Main.BogDatabase? database,
        PhysicalOperator child,
        uint id)
        : base(PhysicalOperatorType.COPY_TO, child, id)
    {
        _filePath = filePath;
        _columnNames = columnNames;
        _database = database;
        _child = child;
    }

    public override bool GetNextTuple(ExecutionContext context)
    {
        if (_executed)
            return false;

        ExecuteCopy(context);
        _executed = true;
        return false;
    }

    private void ExecuteCopy(ExecutionContext context)
    {
        var output = new StringBuilder();
        if (_columnNames.Count > 0)
            output.AppendLine(string.Join(",", EscapeColumns(_columnNames)));

        while (_child.GetNextTuple(context))
        {
            var values = GetCurrentRowValues(context);
            var serialized = new string[values.Length];
            for (var i = 0; i < values.Length; i++)
                serialized[i] = EscapeCsv(FormatValue(values[i]));
            output.AppendLine(string.Join(",", serialized));
        }

        WriteOutput(output.ToString());
    }

    private void WriteOutput(string content)
    {
        if (TryWriteToRegisteredFileSystem(content))
            return;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(_filePath, content, Encoding.UTF8);
    }

    private bool TryWriteToRegisteredFileSystem(string content)
    {
        if (_database == null)
            return false;

        var schemeSeparator = _filePath.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator <= 0)
            return false;

        var scheme = _filePath[..schemeSeparator];
        if (!_database.TryGetFileSystem(scheme, out var fileSystem))
            return false;

        var directory = GetDirectoryPath(_filePath, schemeSeparator);
        if (!string.IsNullOrEmpty(directory))
            fileSystem.CreateDirectory(directory);

        var bytes = Encoding.UTF8.GetBytes(content);
        using var fileInfo = fileSystem.OpenFile(
            _filePath,
            FileFlags.Write | FileFlags.CreateIfMissing | FileFlags.Truncate);
        fileInfo.Write(bytes, 0);
        fileInfo.Truncate(bytes.Length);
        fileInfo.Sync();
        return true;
    }

    private static string? GetDirectoryPath(string filePath, int schemeSeparator)
    {
        var lastSlash = filePath.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash <= schemeSeparator + 2)
            return null;

        return filePath[..lastSlash];
    }

    private object?[] GetCurrentRowValues(ExecutionContext context)
    {
        if (context.CurrentProjectionRow != null)
            return Array.ConvertAll(context.CurrentProjectionRow, value => value);

        if (_columnNames.Count > 0 && context.CurrentScalarBindings != null)
        {
            var values = new object?[_columnNames.Count];
            for (var i = 0; i < _columnNames.Count; i++)
                values[i] = context.CurrentScalarBindings.TryGetValue(_columnNames[i], out var value) ? value : null;
            return values;
        }

        return Array.Empty<object?>();
    }

    private static IEnumerable<string> EscapeColumns(IReadOnlyList<string> columnNames)
    {
        for (var i = 0; i < columnNames.Count; i++)
            yield return EscapeCsv(columnNames[i]);
    }

    private static string FormatValue(object? value)
    {
        if (value == null || value == DBNull.Value)
            return string.Empty;

        return value switch
        {
            bool b => b ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string EscapeCsv(string value)
    {
        if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            return value;

        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
