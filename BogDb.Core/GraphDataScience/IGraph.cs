using System;
using System.Collections.Generic;

namespace BogDb.Core.GraphDataScience;

// ── Node ID ────────────────────────────────────────────────────────────────────

/// <summary>Identifies a node by its table-qualified storage offset.</summary>
public readonly struct NodeId : IEquatable<NodeId>
{
    public readonly ulong  Offset;   // 0-based position within the node table
    public readonly uint   TableId;  // catalog table identifier

    public NodeId(ulong offset, uint tableId = 0)
    {
        Offset  = offset;
        TableId = tableId;
    }

    public bool Equals(NodeId other) => Offset == other.Offset && TableId == other.TableId;
    public override bool Equals(object? obj) => obj is NodeId n && Equals(n);
    public override int GetHashCode() => HashCode.Combine(Offset, TableId);
    public override string ToString() => $"{TableId}:{Offset}";
    public static bool operator ==(NodeId a, NodeId b) => a.Equals(b);
    public static bool operator !=(NodeId a, NodeId b) => !a.Equals(b);
}

// ── Edge ──────────────────────────────────────────────────────────────────────

/// <summary>A directed edge between two nodes with optional weight property.</summary>
public sealed class GdsEdge
{
    public NodeId Source { get; }
    public NodeId Target { get; }
    /// <summary>Numeric edge weight (1.0 for unweighted graphs).</summary>
    public double Weight { get; }
    /// <summary>Full property bag, if the caller requested properties.</summary>
    public IReadOnlyDictionary<string, object?>? Properties { get; }

    public GdsEdge(NodeId source, NodeId target, double weight = 1.0,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        Source     = source;
        Target     = target;
        Weight     = weight;
        Properties = properties;
    }
}

// ── IGraph ────────────────────────────────────────────────────────────────────

/// <summary>
/// Runtime graph view consumed by GDS algorithms.
/// C++ parity: <c>src/include/graph/graph.h</c> — <c>Graph</c> base class.
///
/// A single IGraph instance is created per GDS function invocation and is
/// valid only for the duration of that call. Implementations must be safe for
/// sequential (single-threaded) use; parallelism is the caller's responsibility.
/// </summary>
public interface IGraph
{
    // ── Node access ──────────────────────────────────────────────────────────

    /// <summary>Total number of nodes across all node tables in this graph view.</summary>
    long NodeCount { get; }

    /// <summary>Enumerate all node IDs in the graph.</summary>
    IEnumerable<NodeId> AllNodes();

    /// <summary>Returns the property bag for the node, or null if not found.</summary>
    IReadOnlyDictionary<string, object?>? GetNodeProperties(NodeId id);

    // ── Adjacency access ─────────────────────────────────────────────────────

    /// <summary>Enumerate outgoing edges from <paramref name="src"/>.</summary>
    IEnumerable<GdsEdge> GetOutEdges(NodeId src);

    /// <summary>Enumerate incoming edges to <paramref name="dst"/>.</summary>
    IEnumerable<GdsEdge> GetInEdges(NodeId dst);

    /// <summary>Enumerate both outgoing and incoming edges for <paramref name="n"/>.</summary>
    IEnumerable<GdsEdge> GetBothEdges(NodeId n);

    /// <summary>Out-degree of a node (number of outgoing edges).</summary>
    long OutDegree(NodeId n);

    /// <summary>In-degree of a node (number of incoming edges).</summary>
    long InDegree(NodeId n);

    // ── Graph-level ──────────────────────────────────────────────────────────

    /// <summary>Name of the optional weight property on relationship tuples; null = unweighted.</summary>
    string? WeightProperty { get; }
}

// ── GdsResult ─────────────────────────────────────────────────────────────────

/// <summary>A single output row produced by a GDS algorithm.</summary>
public sealed class GdsResultRow
{
    /// <summary>The node this result row belongs to.</summary>
    public NodeId NodeId { get; }
    /// <summary>Named output values (algorithm-dependent: score, component, distance, rank, …).</summary>
    public IReadOnlyDictionary<string, object?> Values { get; }

    public GdsResultRow(NodeId nodeId, IReadOnlyDictionary<string, object?> values)
    {
        NodeId = nodeId;
        Values = values;
    }
}
