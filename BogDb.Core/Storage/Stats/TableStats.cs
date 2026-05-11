using System;
using System.Collections.Generic;
using BogDb.Core.Common;

namespace BogDb.Core.Storage.Stats
{
    public class TableStats
    {
        private ulong _cardinality;
        private List<ColumnStats> _columnStats;

        public TableStats(List<PhysicalTypeID> dataTypes)
        {
            _cardinality = 0;
            _columnStats = new List<ColumnStats>(dataTypes.Count);
            
            foreach (var dataType in dataTypes)
            {
                _columnStats.Add(new ColumnStats(dataType));
            }
        }

        public TableStats(TableStats other)
        {
            _cardinality = other._cardinality;
            _columnStats = new List<ColumnStats>(other._columnStats.Count);
            foreach(var stat in other._columnStats)
            {
                 _columnStats.Add(new ColumnStats(stat));
            }
        }

        public void IncrementCardinality(ulong increment)
        {
            _cardinality += increment;
        }

        public void Merge(TableStats other)
        {
            List<uint> columnIDs = new List<uint>(_columnStats.Count);
            for (uint i = 0; i < (uint)_columnStats.Count; i++)
            {
                columnIDs.Add(i);
            }
            Merge(columnIDs, other);
        }

        public void Merge(List<uint> columnIDs, TableStats other)
        {
            if (columnIDs.Count != other._columnStats.Count)
                throw new ArgumentException("columnIDs count must match source column stats count.", nameof(columnIDs));

            _cardinality += other._cardinality;
            
            for (int i = 0; i < columnIDs.Count; i++)
            {
                var columnID = columnIDs[i];
                if (columnID >= _columnStats.Count)
                    throw new ArgumentOutOfRangeException(nameof(columnIDs), $"columnID {columnID} is out of range.");
                _columnStats[(int)columnID].Merge(other._columnStats[i]);
            }
        }

        public ulong GetTableCard() => _cardinality;

        public ulong GetNumDistinctValues(uint columnID)
        {
            return _columnStats[(int)columnID].GetNumDistinctValues();
        }

        public void Update(List<ValueVector> vectors, uint numColumns = uint.MaxValue)
        {
            List<uint> dummyColumnIDs = new List<uint>(vectors.Count);
            for (uint i = 0; i < (uint)vectors.Count; i++)
            {
                dummyColumnIDs.Add(i);
            }
            Update(dummyColumnIDs, vectors, numColumns);
        }

        public void Update(List<uint> columnIDs, List<ValueVector> vectors, uint numColumns = uint.MaxValue)
        {
            if (vectors.Count == 0)
                return;
            if (columnIDs.Count != vectors.Count)
                throw new ArgumentException("columnIDs count must match vectors count.", nameof(columnIDs));

            uint numColumnsToUpdate = Math.Min(numColumns, (uint)vectors.Count);

            for (int i = 0; i < numColumnsToUpdate; i++)
            {
                var columnID = columnIDs[i];
                if (columnID >= _columnStats.Count)
                    throw new ArgumentOutOfRangeException(nameof(columnIDs), $"columnID {columnID} is out of range.");
                _columnStats[(int)columnID].Update(vectors[i]);
            }

            ulong numValues = vectors[0].State.GetSelVector().GetSelSize();
            for (int i = 1; i < numColumnsToUpdate; i++)
            {
                if (vectors[i].State.GetSelVector().GetSelSize() != numValues)
                    throw new InvalidOperationException("All vectors must have matching selection sizes.");
            }
            IncrementCardinality(numValues);
        }

        public ColumnStats AddNewColumn(PhysicalTypeID dataType)
        {
            var stat = new ColumnStats(dataType);
            _columnStats.Add(stat);
            return stat;
        }
    }
}
