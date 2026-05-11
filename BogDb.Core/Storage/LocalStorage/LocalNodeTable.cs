using System;
using System.Collections.Generic;
using BogDb.Core.Common;
using BogDb.Core.Storage.Table;

namespace BogDb.Core.Storage.LocalStorage
{
    public class LocalNodeTable : LocalTable
    {
        private ulong _startOffset;
        private NodeGroupCollection _nodeGroups;

        public LocalNodeTable() : base()
        {
            _nodeGroups = new NodeGroupCollection();
        }

        public override bool Insert(Transaction.Transaction transaction, ref TableInsertState insertState)
        {
            return true; // Stub port logic
        }

        public override bool Update(Transaction.Transaction transaction, ref TableUpdateState updateState)
        {
            return true; // Stub port logic
        }

        public override bool Delete(Transaction.Transaction transaction, ref TableDeleteState deleteState)
        {
            return true; // Stub port logic
        }

        public override bool AddColumn(ref TableAddColumnState addColumnState)
        {
            return true; // Stub port logic
        }

        public override void Clear()
        {
            // Clear memory resources associated with the local tx footprint
            _nodeGroups = new NodeGroupCollection();
        }

        public override TableType GetTableType() => TableType.NODE;
        public override ulong GetNumTotalRows() => _nodeGroups.GetNumTotalRows();
        
        public ulong GetNumNodeGroups() => _nodeGroups.GetNumNodeGroups();
        public NodeGroup GetNodeGroup(ulong index) => _nodeGroups.GetNodeGroup(index);
        public NodeGroupCollection GetNodeGroups() => _nodeGroups;
        public ulong GetStartOffset() => _startOffset;
    }
}
