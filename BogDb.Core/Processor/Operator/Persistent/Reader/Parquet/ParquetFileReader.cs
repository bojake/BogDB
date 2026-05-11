using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Parquet;
using Parquet.Schema;

namespace BogDb.Core.Processor.Operator.Persistent.Reader.Parquet;

/// <summary>
/// Reads a Parquet file and yields rows as string arrays,
/// matching the interface contract of CSVReader.ReadAllRows().
/// C++ parity: src/processor/operator/persistent/reader/parquet/parquet_reader.cpp
/// </summary>
internal sealed class ParquetFileReader : IDisposable
{
    private readonly string _filePath;
    private readonly Stream _stream;
    private readonly ParquetReader _reader;
    private readonly DataField[] _dataFields;

    private ParquetFileReader(string filePath, Stream stream, ParquetReader reader)
    {
        _filePath = filePath;
        _stream = stream;
        _reader = reader;
        _dataFields = reader.Schema.GetDataFields();
    }

    public static ParquetFileReader Open(string filePath)
    {
        var stream = File.OpenRead(filePath);
        try
        {
            var reader = ParquetReader.CreateAsync(stream).GetAwaiter().GetResult();
            return new ParquetFileReader(filePath, stream, reader);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Returns the column names from the Parquet schema.
    /// </summary>
    public string[] GetColumnNames()
    {
        var names = new string[_dataFields.Length];
        for (int i = 0; i < _dataFields.Length; i++)
            names[i] = _dataFields[i].Name;
        return names;
    }

    /// <summary>
    /// Returns the number of data columns in the schema.
    /// </summary>
    public int ColumnCount => _dataFields.Length;

    /// <summary>
    /// Yields all rows in the Parquet file as string arrays,
    /// one array per row, matching the CSV reader contract.
    /// </summary>
    public IEnumerable<string[]> ReadAllRows(int expectedColumnCount)
    {
        var colCount = Math.Min(expectedColumnCount, _dataFields.Length);

        for (int rgIdx = 0; rgIdx < _reader.RowGroupCount; rgIdx++)
        {
            using var rowGroupReader = _reader.OpenRowGroupReader(rgIdx);

            // Read all columns for this row group
            var columns = new Array[colCount];
            for (int c = 0; c < colCount; c++)
            {
                columns[c] = ReadColumn(rowGroupReader, _dataFields[c]);
            }

            if (colCount == 0) continue;
            var rowCount = (int)rowGroupReader.RowCount;

            // Yield rows
            for (int r = 0; r < rowCount; r++)
            {
                var row = new string[expectedColumnCount];
                for (int c = 0; c < colCount; c++)
                {
                    var value = columns[c].GetValue(r);
                    row[c] = value?.ToString() ?? "";
                }
                // Fill remaining with empty strings if Parquet has fewer columns
                for (int c = colCount; c < expectedColumnCount; c++)
                    row[c] = "";

                yield return row;
            }
        }
    }

    public void Dispose()
    {
        _reader.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _stream.Dispose();
    }

    private static Array ReadColumn(ParquetRowGroupReader rowGroupReader, DataField field)
    {
        var rowCount = checked((int)rowGroupReader.RowCount);
        if (field.ClrType == typeof(string))
        {
            var values = new string[rowCount];
            rowGroupReader.ReadAsync(field, values.AsMemory(), null, default)
                .GetAwaiter().GetResult();
            return values;
        }

        var valuesType = field.ClrNullableIfHasNullsType;
        var nullableType = Nullable.GetUnderlyingType(valuesType);
        var methodName = nullableType == null
            ? nameof(ReadStructColumn)
            : nameof(ReadNullableStructColumn);
        var typeArgument = nullableType ?? valuesType;
        var method = typeof(ParquetFileReader)
            .GetMethod(methodName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(ParquetFileReader), methodName);

        return (Array)method
            .MakeGenericMethod(typeArgument)
            .Invoke(null, new object[] { rowGroupReader, field, rowCount })!;
    }

    private static Array ReadStructColumn<T>(ParquetRowGroupReader rowGroupReader, DataField field, int rowCount)
        where T : struct
    {
        var values = new T[rowCount];
        rowGroupReader.ReadAsync(field, values.AsMemory(), null, default)
            .GetAwaiter().GetResult();
        return values;
    }

    private static Array ReadNullableStructColumn<T>(ParquetRowGroupReader rowGroupReader, DataField field, int rowCount)
        where T : struct
    {
        var values = new T?[rowCount];
        rowGroupReader.ReadAsync(field, values.AsMemory(), null, default)
            .GetAwaiter().GetResult();
        return values;
    }
}
