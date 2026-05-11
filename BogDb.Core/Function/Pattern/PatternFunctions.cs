using System;
using System.Collections.Generic;
using System.Linq;
using BogDb.Core.Common;

namespace BogDb.Core.Function.Pattern;

/// <summary>
/// Pattern / schema scalar functions: id(), label(), start_node(), end_node(), cost().
/// In BogDB, nodes and rels are Dictionary&lt;string, object&gt; property bags.
/// The special keys _id, _label, _src, _dst, _cost hold structural metadata.
///
/// C++ parity: src/function/pattern/ and src/function/internal_id/
/// </summary>
internal static class PatternFunctions
{
    internal static void Register(IDictionary<string, Func<object?[], object?>> r)
    {
        // ── id(n) / id(r) — internal identifier of a node or relationship ─────
        r["id"] = r["internal_id"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            // String node-id passed directly (e.g. "Person:0")
            if (a[0] is string s) return (object?)s;
            // Property bag — look for _id, id, or _internalId keys
            return DictGet(a[0], "_id", "id", "_internalId", InternalKey.Id);
        };

        // ── label(n) — table name / label of a node or relationship ───────────
        r["label"] = r["labels"] = a =>
        {
            if (a.Length < 1 || a[0] == null) return null;
            var lbl = DictGet(a[0], "_label", "_table", "label", InternalKey.Label);
            if (lbl != null) return lbl;
            // Derive from id string "TableName:offset"
            if (a[0] is string sid)
            {
                var colon = sid.LastIndexOf(':');
                return colon > 0 ? (object?)sid[..colon] : (object?)sid;
            }
            return null;
        };

        // ── type(r) — relationship type (alias for label on rels) ─────────────
        r["type"] = r["relationship_type"] = a => r["label"](a);

        // ── start_node(r) / end_node(r) — endpoint nodes of a relationship ────
        r["start_node"] = r["startNode"] = a =>
        {
            if (a.Length < 1) return null;
            return DictGet(a[0], "_src", "_source", "src", InternalKey.Src);
        };

        r["end_node"] = r["endNode"] = a =>
        {
            if (a.Length < 1) return null;
            return DictGet(a[0], "_dst", "_dest", "dst", InternalKey.Dst);
        };

        // ── cost(r) — weighted cost property (custom BogDb extension) ─────────
        r["cost"] = a =>
        {
            if (a.Length < 1) return null;
            return DictGet(a[0], "_cost", "cost", "weight");
        };

        // ── tableID(n), offsetID(n) — raw internal ID components ─────────────
        r["tableid"] = r["table_id"] = a =>
        {
            var id = TypeCoercionHelper.ToBogDbString(r["id"](a));
            if (id == null) return null;
            var colon = id.IndexOf(':');
            return colon < 0 ? null : (object?)id[(colon + 1)..];
        };

        r["offsetid"] = r["offset_id"] = a =>
        {
            var id = TypeCoercionHelper.ToBogDbString(r["id"](a));
            if (id == null) return null;
            var colon = id.IndexOf(':');
            return colon < 0 ? null : (object?)id[(colon + 1)..];
        };

        // ── node_offset(n) — 0-based row offset within a table ────────────────
        r["node_offset"] = r["nodeoffset"] = a =>
        {
            var id = TypeCoercionHelper.ToBogDbString(r["id"](a));
            if (id == null) return null;
            var colon = id.LastIndexOf(':');
            if (colon < 0 || !long.TryParse(id[(colon + 1)..], out var offset)) return null;
            return (object?)offset;
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static object? DictGet(object? value, params string[] keys)
    {
        if (value == null) return null;
        if (value is Dictionary<string, object?> sd)
            foreach (var k in keys) if (sd.TryGetValue(k, out var v)) return v;
        if (value is Dictionary<string, object> sod)
            foreach (var k in keys) if (sod.TryGetValue(k, out var v)) return v;
        return null;
    }

    private static class InternalKey
    {
        public const string Id    = "_id";
        public const string Label = "_label";
        public const string Src   = "_src";
        public const string Dst   = "_dst";
    }
}
