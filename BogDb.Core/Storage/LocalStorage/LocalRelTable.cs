using System;
using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Storage.Table;

namespace BogDb.Core.Storage.LocalStorage
{
    public enum RelDataDirection
    {
        FWD = 0,
        BWD = 1
    }

    public struct DirectedCSRIndex
    {
        public RelDataDirection Direction;
        public Dictionary<ulong, List<ulong>> Index;
        
        public bool IsEmpty() => Index == null || Index.Count == 0;
        public void Clear() => Index?.Clear();
    }

    public class LocalRelTable : LocalTable
    {
        private List<DirectedCSRIndex> _directedIndices;
        private NodeGroup _localNodeGroup;

        public LocalRelTable() : base()
        {
            _directedIndices = new List<DirectedCSRIndex>
            {
                new DirectedCSRIndex { Direction = RelDataDirection.FWD, Index = new Dictionary<ulong, List<ulong>>() },
                new DirectedCSRIndex { Direction = RelDataDirection.BWD, Index = new Dictionary<ulong, List<ulong>>() }
            };
            _localNodeGroup = new NodeGroup();
        }

        public override bool Insert(Transaction.Transaction transaction, ref TableInsertState insertState)
        {
            return true; // Stub
        }

        public override bool Update(Transaction.Transaction transaction, ref TableUpdateState updateState)
        {
            return true; // Stub
        }

        public override bool Delete(Transaction.Transaction transaction, ref TableDeleteState deleteState)
        {
            return true; // Stub
        }

        public override bool AddColumn(ref TableAddColumnState addColumnState)
        {
            return true; // Stub
        }

        public override void Clear()
        {
            _localNodeGroup = new NodeGroup();
            foreach (var index in _directedIndices)
            {
                index.Clear();
            }
        }

        public override TableType GetTableType() => TableType.REL;
        public override ulong GetNumTotalRows() => _localNodeGroup.GetNumRows();

        public bool IsEmpty() => _directedIndices[0].IsEmpty();
        public Dictionary<ulong, List<ulong>> GetCSRIndex(RelDataDirection direction)
        {
            return _directedIndices[(int)direction].Index;
        }
        public NodeGroup GetLocalNodeGroup() => _localNodeGroup;
    }
}
