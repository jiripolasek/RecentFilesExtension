// ------------------------------------------------------------
// 
// Copyright (c) Jiří Polášek. All rights reserved.
// 
// ------------------------------------------------------------

using System.Collections;

namespace JPSoftworks.RecentFilesExtension.Model;

/// <summary>
/// A specialized collection for managing recent files with efficient lookups and duplicate detection.
/// </summary>
internal sealed class RecentFileCollection : IList<IRecentFile>
{
    private readonly List<IRecentFile> _items = [];
    private readonly Dictionary<string, IRecentFile> _itemsByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _targetPaths = new(StringComparer.OrdinalIgnoreCase);

    public int Count => this._items.Count;

    public bool IsReadOnly => false;

    public IRecentFile this[int index]
    {
        get => this._items[index];
        set
        {
            var oldItem = this._items[index];
            this.RemoveFromLookups(oldItem);
            this._items[index] = value;
            this.AddToLookups(value);
        }
    }

    /// <summary>
    /// Tries to get an existing item by its shortcut file path.
    /// </summary>
    public bool TryGetByPath(string fullPath, out IRecentFile? item)
    {
        return this._itemsByPath.TryGetValue(fullPath, out item);
    }

    /// <summary>
    /// Checks if a target path already exists in the collection.
    /// </summary>
    public bool ContainsTarget(string targetPath)
    {
        var key = this.GetTargetKey(targetPath);
        return this._targetPaths.Contains(key);
    }

    /// <summary>
    /// Clears all items and internal collections.
    /// </summary>
    public void Clear()
    {
        this._items.Clear();
        this._itemsByPath.Clear();
        this._targetPaths.Clear();
    }

    /// <summary>
    /// Adds an item if the target path doesn't already exist.
    /// </summary>
    /// <returns>True if the item was added, false if target already exists.</returns>
    public bool TryAdd(IRecentFile item)
    {
        var targetKey = this.GetTargetKey(item.TargetPath);
        if (this._targetPaths.Contains(targetKey))
            return false;

        this.Add(item);
        return true;
    }

    /// <summary>
    /// Replaces the entire collection with new items, maintaining internal collections.
    /// </summary>
    public void ReplaceWith(IEnumerable<IRecentFile> newItems)
    {
        this.Clear();
        foreach (var item in newItems)
        {
            this.Add(item);
        }
    }

    /// <summary>
    /// Creates a snapshot copy of the current items as a regular list.
    /// </summary>
    public List<IRecentFile> ToList()
    {
        return [.. this._items];
    }

    public void Add(IRecentFile item)
    {
        this._items.Add(item);
        this.AddToLookups(item);
    }

    public bool Remove(IRecentFile item)
    {
        if (this._items.Remove(item))
        {
            this.RemoveFromLookups(item);
            return true;
        }
        return false;
    }

    public void RemoveAt(int index)
    {
        var item = this._items[index];
        this._items.RemoveAt(index);
        this.RemoveFromLookups(item);
    }

    public void Insert(int index, IRecentFile item)
    {
        this._items.Insert(index, item);
        this.AddToLookups(item);
    }

    public int IndexOf(IRecentFile item) => this._items.IndexOf(item);

    public bool Contains(IRecentFile item) => this._items.Contains(item);

    public void CopyTo(IRecentFile[] array, int arrayIndex) => this._items.CopyTo(array, arrayIndex);

    public IEnumerator<IRecentFile> GetEnumerator() => this._items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    private void AddToLookups(IRecentFile item)
    {
        this._itemsByPath[item.FullPath] = item;
        var targetKey = this.GetTargetKey(item.TargetPath);
        this._targetPaths.Add(targetKey);
    }

    private void RemoveFromLookups(IRecentFile item)
    {
        this._itemsByPath.Remove(item.FullPath);
        var targetKey = this.GetTargetKey(item.TargetPath);
        this._targetPaths.Remove(targetKey);
    }

    private string GetTargetKey(string targetPath)
    {
        return targetPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) ||
               targetPath.StartsWith(@"\\?\UNC", StringComparison.OrdinalIgnoreCase)
            ? targetPath
            : targetPath.ToLowerInvariant();
    }
}