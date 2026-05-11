using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BogDb.Core.Common.FileSystem;
using BogDb.Core.Main;

namespace BogDb.Core.Processor.Operator.Persistent.Reader.CSV;

internal static class CsvFileAccess
{
    public static CSVReader OpenReader(BogDatabase database, string filePath)
    {
        if (TryReadAllTextFromRegisteredFileSystem(database, filePath, out var content))
            return new CSVReader(content, treatAsContent: true);

        return new CSVReader(filePath);
    }

    public static List<string> ReadCsvHeader(BogDatabase database, string filePath)
    {
        if (TryReadAllTextFromRegisteredFileSystem(database, filePath, out var content))
            return ReadCsvHeaderFromContent(filePath, content);

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"COPY source file '{filePath}' does not exist.", filePath);

        var headerLine = File.ReadLines(filePath).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidOperationException($"COPY source file '{filePath}' is empty.");

        return ParseHeader(filePath, headerLine);
    }

    private static List<string> ReadCsvHeaderFromContent(string filePath, string content)
    {
        using var reader = new StringReader(content);
        var headerLine = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(headerLine))
            throw new InvalidOperationException($"COPY source file '{filePath}' is empty.");

        return ParseHeader(filePath, headerLine);
    }

    private static List<string> ParseHeader(string filePath, string headerLine)
    {
        var columns = headerLine
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(column => column.Trim())
            .ToList();

        if (columns.Count == 0 || columns.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException($"COPY source file '{filePath}' has an invalid header row.");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in columns)
        {
            if (!seen.Add(column))
                throw new InvalidOperationException(
                    $"COPY source file '{filePath}' contains duplicate column '{column}'.");
        }

        return columns;
    }

    private static bool TryReadAllTextFromRegisteredFileSystem(
        BogDatabase database,
        string filePath,
        out string content)
    {
        content = string.Empty;
        var schemeSeparator = filePath.IndexOf("://", StringComparison.Ordinal);
        if (schemeSeparator <= 0)
            return false;

        var scheme = filePath[..schemeSeparator];
        if (!database.TryGetFileSystem(scheme, out var fileSystem))
            return false;

        using var fileInfo = fileSystem.OpenFile(filePath, FileFlags.Read);
        var fileSize = checked((int)fileInfo.GetFileSize());
        var buffer = new byte[fileSize];
        fileInfo.Read(buffer, 0);
        content = Encoding.UTF8.GetString(buffer);
        return true;
    }
}
