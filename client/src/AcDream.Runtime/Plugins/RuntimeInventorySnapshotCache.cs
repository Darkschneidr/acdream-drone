using AcDream.Core.Items;
using AcDream.Plugin.Abstractions;

namespace AcDream.Runtime.Plugins;

/// <summary>
/// Retains immutable inventory results while callers validate the current values.
/// Direct property edits need no notification to invalidate a result.
/// </summary>
internal sealed class RuntimeInventorySnapshotCache
{
    internal sealed record Entry(ClientObjectHeader? Header, PluginInventoryItem Item);

    private readonly Dictionary<uint, Entry> _items = new();
    private readonly HashSet<uint> _seen = new();
    private readonly List<uint> _removed = new();
    private IReadOnlyList<PluginInventoryItem> _snapshot = Array.Empty<PluginInventoryItem>();
    private bool _changed;

    internal void Begin() => _seen.Clear();

    internal Entry? Find(uint id) => _items.GetValueOrDefault(id);

    internal void Add(ClientObjectHeader? header, in PluginInventoryItem item)
    {
        _seen.Add(item.ObjectId);
        if (_items.TryGetValue(item.ObjectId, out Entry? previous)
            && previous.Item == item && previous.Header == header)
            return;
        _items[item.ObjectId] = new Entry(header, item);
        _changed = true;
    }

    internal IReadOnlyList<PluginInventoryItem> Complete()
    {
        _removed.Clear();
        foreach (uint id in _items.Keys)
            if (!_seen.Contains(id))
                _removed.Add(id);
        foreach (uint id in _removed)
        {
            _items.Remove(id);
            _changed = true;
        }
        if (!_changed)
            return _snapshot;

        var items = new PluginInventoryItem[_items.Count];
        int index = 0;
        foreach (Entry entry in _items.Values)
            items[index++] = entry.Item;
        Array.Sort(items, static (left, right) =>
        {
            int name = string.CompareOrdinal(left.Name, right.Name);
            return name != 0 ? name : left.ObjectId.CompareTo(right.ObjectId);
        });
        _snapshot = Array.AsReadOnly(items);
        _changed = false;
        return _snapshot;
    }

    internal void Clear()
    {
        _items.Clear();
        _seen.Clear();
        _removed.Clear();
        _snapshot = Array.Empty<PluginInventoryItem>();
        _changed = false;
    }

    internal static IReadOnlyList<uint> CaptureSpells(
        IReadOnlyList<uint> current, IReadOnlyList<uint>? previous)
    {
        if (current.Count == 0)
            return Array.Empty<uint>();
        if (previous is not null && current.Count == previous.Count)
        {
            int index = 0;
            while (index < current.Count && current[index] == previous[index])
                index++;
            if (index == current.Count)
                return previous;
        }
        var copy = new uint[current.Count];
        for (int index = 0; index < current.Count; index++)
            copy[index] = current[index];
        return Array.AsReadOnly(copy);
    }
}
