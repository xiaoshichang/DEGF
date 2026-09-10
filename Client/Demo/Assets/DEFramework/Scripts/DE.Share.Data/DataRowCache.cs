using System.Collections.Generic;

namespace DE.Share.Data
{
    internal sealed class DataRowCache<TRow> where TRow : DataRow
    {
        private readonly Dictionary<DataTableKey, TRow> _Rows = new Dictionary<DataTableKey, TRow>();
        private readonly Dictionary<DataTableKey, LinkedListNode<DataTableKey>> _Nodes;
        private readonly LinkedList<DataTableKey> _Recency;
        private readonly int _Capacity;

        internal DataRowCache(DataCachePolicy policy, int capacity)
        {
            _Capacity = capacity;
            if (policy == DataCachePolicy.Lru)
            {
                _Nodes = new Dictionary<DataTableKey, LinkedListNode<DataTableKey>>();
                _Recency = new LinkedList<DataTableKey>();
            }
        }

        internal bool TryGetRow(DataTableKey key, out TRow row)
        {
            if (!_Rows.TryGetValue(key, out row))
            {
                return false;
            }
            Touch(key);
            return true;
        }

        internal void AddRows(Dictionary<DataTableKey, TRow> rows, DataTableKey? requestedKey)
        {
            foreach (var pair in rows)
            {
                // Keep the identity and access history of rows that are already cached.
                if (_Rows.ContainsKey(pair.Key))
                {
                    continue;
                }
                _Rows.Add(pair.Key, pair.Value);
                if (_Recency != null)
                {
                    _Nodes.Add(pair.Key, _Recency.AddLast(pair.Key));
                }
            }
            if (requestedKey.HasValue && _Rows.ContainsKey(requestedKey.Value))
            {
                Touch(requestedKey.Value);
            }
            if (_Recency != null)
            {
                while (_Rows.Count > _Capacity)
                {
                    var oldest = _Recency.Last;
                    _Rows.Remove(oldest.Value);
                    _Nodes.Remove(oldest.Value);
                    _Recency.RemoveLast();
                }
            }
        }

        private void Touch(DataTableKey key)
        {
            if (_Recency != null)
            {
                var node = _Nodes[key];
                _Recency.Remove(node);
                _Recency.AddFirst(node);
            }
        }
    }
}
