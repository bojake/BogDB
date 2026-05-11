using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Main.QueryResult;

namespace BogDb.Core.Main;

/// <summary>
/// Fluent Cypher query builder. Chain parameters and execution options,
/// then materialize results via <see cref="Execute"/>, <see cref="AsEnumerable"/>,
/// <see cref="ForEach"/>, or <see cref="Scalar{T}"/>.
///
/// Usage:
///   conn.Cypher("MATCH (p:Person) WHERE p.age > $minAge RETURN p.name, p.age")
///       .Param("minAge", 30)
///       .Execute()
///       .ForEach(row => Console.WriteLine(row.GetString("p.name")));
/// </summary>
public sealed class CypherFluentQuery
{
    private readonly BogConnection _connection;
    private readonly string _cypher;
    private Dictionary<string, object?>? _parameters;

    internal CypherFluentQuery(BogConnection connection, string cypher)
    {
        _connection = connection;
        _cypher = cypher;
    }

    /// <summary>
    /// Adds a named parameter binding.
    /// </summary>
    public CypherFluentQuery Param(string name, object? value)
    {
        _parameters ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        _parameters[name] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple parameter bindings from a dictionary.
    /// </summary>
    public CypherFluentQuery Params(IReadOnlyDictionary<string, object?> parameters)
    {
        _parameters ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in parameters)
            _parameters[key] = value;
        return this;
    }

    /// <summary>
    /// Adds multiple parameter bindings from an anonymous object.
    /// Example: .Params(new { minAge = 30, country = "US" })
    /// </summary>
    public CypherFluentQuery Params(object anonymousObject)
    {
        _parameters ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in anonymousObject.GetType().GetProperties())
            _parameters[prop.Name] = prop.GetValue(anonymousObject);
        return this;
    }

    /// <summary>
    /// Executes the query and returns the full <see cref="QueryResult.QueryResult"/>.
    /// </summary>
    public QueryResult.QueryResult Execute()
    {
        return _parameters is { Count: > 0 }
            ? _connection.Query(_cypher, _parameters)
            : _connection.Query(_cypher);
    }

    /// <summary>
    /// Executes the query, throws if failed, and returns an enumerable of rows.
    /// Enables LINQ-to-objects on the result.
    /// </summary>
    public IEnumerable<BogRow> AsEnumerable()
    {
        return Execute().ThrowIfFailed();
    }

    /// <summary>
    /// Executes the query, throws if failed, and returns all rows as a list.
    /// </summary>
    public List<BogRow> ToList()
    {
        return Execute().ThrowIfFailed().ToRowList();
    }

    /// <summary>
    /// Executes the query, throws if failed, and runs <paramref name="action"/> on each row.
    /// </summary>
    public CypherFluentQuery ForEach(Action<BogRow> action)
    {
        Execute().ThrowIfFailed().ForEach(action);
        return this;
    }

    /// <summary>
    /// Executes the query and projects each row to <typeparamref name="T"/>.
    /// </summary>
    public List<T> Select<T>(Func<BogRow, T> selector)
    {
        return Execute().ThrowIfFailed().Select(selector);
    }

    /// <summary>
    /// Executes the query and returns the scalar value from the first column of the first row.
    /// Useful for COUNT(*), SUM(), etc.
    /// </summary>
    public T Scalar<T>()
    {
        return Execute().ThrowIfFailed().Scalar<T>();
    }

    /// <summary>
    /// Executes the query and returns the first row, or null if empty.
    /// </summary>
    public BogRow? FirstOrDefault()
    {
        return Execute().ThrowIfFailed().FirstOrDefault();
    }

    /// <summary>
    /// Executes the query and returns the row count.
    /// </summary>
    public int Count()
    {
        return Execute().ThrowIfFailed().Rows.Count;
    }
}
