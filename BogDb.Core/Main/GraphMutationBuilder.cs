using System;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Main;

/// <summary>
/// Fluent graph mutation builder. Queue schema, node, and edge operations,
/// then commit them all in a single transaction.
///
/// Usage:
///   conn.Graph()
///       .EnsureNodeTable("Person", new() { ["id"] = STRING, ["name"] = STRING })
///       .AddNode("Person", "p1", new { name = "Alice" })
///       .AddNode("Person", "p2", new { name = "Bob" })
///       .AddEdge("KNOWS", "p1", "p2", new { since = 2020 })
///       .Commit();
/// </summary>
public sealed class GraphMutationBuilder
{
    private readonly BogConnection _connection;
    private readonly List<Action<BogConnection>> _operations = new();
    private bool _committed;

    internal GraphMutationBuilder(BogConnection connection)
    {
        _connection = connection;
    }

    // ── Schema operations ────────────────────────────────────────────────────

    /// <summary>
    /// Queues a node table creation (idempotent).
    /// </summary>
    public GraphMutationBuilder EnsureNodeTable(
        string tableName,
        Dictionary<string, LogicalTypeID>? properties = null)
    {
        _operations.Add(conn => conn.EnsureNodeTable(tableName, properties ?? new()));
        return this;
    }

    /// <summary>
    /// Queues a relationship table creation (idempotent).
    /// </summary>
    public GraphMutationBuilder EnsureRelTable(
        string tableName,
        string fromTable,
        string toTable,
        Dictionary<string, LogicalTypeID>? properties = null)
    {
        _operations.Add(conn => conn.EnsureRelTable(tableName, fromTable, toTable, properties ?? new()));
        return this;
    }

    /// <summary>
    /// Queues an index creation.
    /// </summary>
    public GraphMutationBuilder CreateIndex(string tableName, string propertyName)
    {
        _operations.Add(conn => conn.CreateIndex(tableName, propertyName));
        return this;
    }

    // ── Node operations ──────────────────────────────────────────────────────

    /// <summary>
    /// Queues a node upsert with a dictionary of properties.
    /// </summary>
    public GraphMutationBuilder AddNode(
        string tableName,
        string id,
        Dictionary<string, object> properties)
    {
        _operations.Add(conn => conn.UpsertNodeById(tableName, id, properties));
        return this;
    }

    /// <summary>
    /// Queues a node upsert with an anonymous object as properties.
    /// Example: .AddNode("Person", "p1", new { name = "Alice", age = 30 })
    /// </summary>
    public GraphMutationBuilder AddNode(string tableName, string id, object properties)
    {
        var dict = AnonymousObjectToDictionary(properties);
        _operations.Add(conn => conn.UpsertNodeById(tableName, id, dict));
        return this;
    }

    // ── Edge operations ──────────────────────────────────────────────────────

    /// <summary>
    /// Queues a relationship upsert with a dictionary of properties.
    /// </summary>
    public GraphMutationBuilder AddEdge(
        string tableName,
        string fromId,
        string toId,
        Dictionary<string, object> properties)
    {
        _operations.Add(conn => conn.UpsertRelationshipById(tableName, fromId, toId, properties));
        return this;
    }

    /// <summary>
    /// Queues a relationship upsert with an anonymous object as properties.
    /// Example: .AddEdge("KNOWS", "p1", "p2", new { since = 2020 })
    /// </summary>
    public GraphMutationBuilder AddEdge(string tableName, string fromId, string toId, object? properties = null)
    {
        var dict = properties != null ? AnonymousObjectToDictionary(properties) : new Dictionary<string, object>();
        _operations.Add(conn => conn.UpsertRelationshipById(tableName, fromId, toId, dict));
        return this;
    }

    // ── Cypher mutation ──────────────────────────────────────────────────────

    /// <summary>
    /// Queues a raw Cypher mutation statement (CREATE, SET, DELETE, MERGE, etc.).
    /// </summary>
    public GraphMutationBuilder Cypher(string statement)
    {
        _operations.Add(conn =>
        {
            var result = conn.Query(statement);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"Cypher mutation failed: {result.ErrorMessage}\nQuery: {statement}");
        });
        return this;
    }

    /// <summary>
    /// Queues a raw Cypher mutation with parameters.
    /// </summary>
    public GraphMutationBuilder Cypher(string statement, IReadOnlyDictionary<string, object?> parameters)
    {
        _operations.Add(conn =>
        {
            var result = conn.Query(statement, parameters);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"Cypher mutation failed: {result.ErrorMessage}\nQuery: {statement}");
        });
        return this;
    }

    // ── Execution ────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes all queued operations in a single write transaction and commits.
    /// </summary>
    public void Commit()
    {
        if (_committed)
            throw new InvalidOperationException("This mutation builder has already been committed.");
        if (_operations.Count == 0)
            return;

        _committed = true;
        _connection.BeginWriteTransaction();
        try
        {
            foreach (var op in _operations)
                op(_connection);
            _connection.Commit();
        }
        catch
        {
            _connection.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Returns the number of queued operations.
    /// </summary>
    public int PendingCount => _operations.Count;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Dictionary<string, object> AnonymousObjectToDictionary(object obj)
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in obj.GetType().GetProperties())
        {
            var value = prop.GetValue(obj);
            if (value != null)
                dict[prop.Name] = value;
        }
        return dict;
    }
}
