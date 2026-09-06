using System.Collections.Generic;

namespace HomeworkCentral.Api.Assessment;

/// <summary>
/// Managed fallback for <c>rust/hc-cache</c> when <c>libhc_kernels</c> is
/// missing the <c>hc_lru_*</c> exports. Eviction is never FIFO: delete the
/// least recent (rightmost) address, then insert the new item at the left.
/// After A is reused, <c>D &gt;&gt; [A,B,C] -&gt; [D,A,C]</c>. Runtime
/// prefers <see cref="HostLru"/> (Rust).
/// </summary>
public sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int capacity;
    private readonly Dictionary<TKey, LinkedListNode<Entry>> map;
    private readonly LinkedList<Entry> order;
    private readonly object gate = new();

    public LruCache(int capacity)
    {
        this.capacity = capacity < 0 ? 0 : capacity;
        map = [];
        order = new LinkedList<Entry>();
    }

    public int Capacity => capacity;

    public int Count
    {
        get
        {
            lock (gate)
                return map.Count;
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (gate)
        {
            if (!map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                value = default!;
                return false;
            }

            order.Remove(node);
            order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
    }

    public void Put(TKey key, TValue value)
    {
        if (capacity == 0)
            return;

        lock (gate)
        {
            if (map.TryGetValue(key, out LinkedListNode<Entry>? existing))
            {
                existing.Value = new Entry(key, value);
                order.Remove(existing);
                order.AddFirst(existing);
                return;
            }

            if (map.Count == capacity)
                EvictLeastImportant();

            LinkedListNode<Entry> node = order.AddFirst(new Entry(key, value));
            map[key] = node;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            map.Clear();
            order.Clear();
        }
    }

    /// <summary>Keys from most recent (left) to least recent (right).</summary>
    public List<TKey> KeysMruToLru()
    {
        lock (gate)
        {
            List<TKey> keys = new(map.Count);
            for (LinkedListNode<Entry>? cursor = order.First; cursor is not null; cursor = cursor.Next)
                keys.Add(cursor.Value.Key);
            return keys;
        }
    }

    private void EvictLeastImportant()
    {
        LinkedListNode<Entry>? tail = order.Last;
        if (tail is null)
            return;

        map.Remove(tail.Value.Key);
        order.RemoveLast();
    }

    private readonly record struct Entry(TKey Key, TValue Value);
}
