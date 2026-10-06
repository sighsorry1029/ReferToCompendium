using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ReferToCompendium
{
    /// <summary>Client layout independent of the current game's available compendium entries.</summary>
    public sealed class LayoutState
    {
        private readonly object gate = new object();
        private readonly List<string> order = new List<string>();
        private readonly HashSet<string> collapsed = new HashSet<string>(StringComparer.Ordinal);

        public List<string> GetOrderedIds(IEnumerable<string?> availableIds)
        {
            List<string> available = DistinctIds(availableIds);
            lock (gate)
            {
                Reconcile(available);
                var present = new HashSet<string>(available, StringComparer.Ordinal);
                return order.FindAll(present.Contains);
            }
        }

        public bool IsCollapsed(string? id)
        {
            lock (gate) return id != null && collapsed.Contains(id);
        }

        public bool SetCollapsed(string? id, bool value)
        {
            if (id == null || id.Length == 0) return false;
            lock (gate)
            {
                if (!value) return collapsed.Remove(id);
                if (!order.Contains(id)) order.Add(id);
                return collapsed.Add(id);
            }
        }

        public bool Move(string? id, int delta, IEnumerable<string?> availableIds)
        {
            List<string> available = DistinctIds(availableIds);
            lock (gate)
            {
                Reconcile(available);
                if (delta == 0 || id == null || id.Length == 0) return false;
                var present = new HashSet<string>(available, StringComparer.Ordinal);
                if (!present.Contains(id)) return false;
                bool bucket = collapsed.Contains(id);
                var slots = new List<int>();
                for (int i = 0; i < order.Count; ++i)
                    if (present.Contains(order[i]) && collapsed.Contains(order[i]) == bucket) slots.Add(i);
                int source = slots.FindIndex(index => order[index] == id);
                if (source < 0) return false;
                int target = (int)Math.Max(0L, Math.Min(slots.Count - 1L, (long)source + delta));
                if (source == target) return false;
                // Reorder only visible slots in this bucket. Absent mods keep their places.
                int direction = target > source ? 1 : -1;
                for (int i = source; i != target; i += direction) order[slots[i]] = order[slots[i + direction]];
                order[slots[target]] = id;
                return true;
            }
        }

        public void Reset(IEnumerable<string?> availableIds)
        {
            List<string> available = DistinctIds(availableIds);
            lock (gate)
            {
                Reconcile(available);
                var present = new HashSet<string>(available, StringComparer.Ordinal);
                int next = 0;
                for (int i = 0; i < order.Count; ++i)
                    if (present.Contains(order[i])) order[i] = available[next++];
                foreach (string id in available) collapsed.Remove(id);
            }
        }

        internal List<LayoutEntry> Snapshot()
        {
            lock (gate)
            {
                var entries = new List<LayoutEntry>(order.Count);
                foreach (string id in order) entries.Add(new LayoutEntry { Id = id, Collapsed = collapsed.Contains(id) });
                return entries;
            }
        }

        internal static LayoutState FromEntries(List<LayoutEntry> entries)
        {
            var state = new LayoutState();
            foreach (LayoutEntry entry in entries)
            {
                state.order.Add(entry.Id);
                if (entry.Collapsed) state.collapsed.Add(entry.Id);
            }
            return state;
        }

        private void Reconcile(List<string> available)
        {
            var known = new HashSet<string>(order, StringComparer.Ordinal);
            foreach (string id in available) if (known.Add(id)) order.Add(id);
        }

        private static List<string> DistinctIds(IEnumerable<string?> ids)
        {
            if (ids == null) throw new ArgumentNullException(nameof(ids));
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string? id in ids) if (id != null && id.Length != 0 && seen.Add(id)) result.Add(id);
            return result;
        }
    }

    [DataContract]
    internal sealed class LayoutEntry
    {
        [DataMember(Name = "id", Order = 0, IsRequired = true)] public string Id = string.Empty;
        [DataMember(Name = "collapsed", Order = 1)] public bool Collapsed;
    }
}
