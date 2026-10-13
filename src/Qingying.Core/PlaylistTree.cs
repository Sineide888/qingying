namespace Qingying.Core;

public enum PlaylistDropKind
{
    Before,
    After,
    Into
}

public static class PlaylistTree
{
    public const int MaxDepth = 6;

    public static PlaylistNode? CreateFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return null;
        }

        var full = Path.GetFullPath(folderPath);
        var node = new PlaylistNode(new DirectoryInfo(full).Name, null, full);
        Fill(node, full, 0);
        return CountFiles(node) == 0 ? null : node;
    }

    public static int CountFiles(PlaylistNode node)
    {
        if (!node.IsFolder)
        {
            return 1;
        }

        var count = 0;
        foreach (var child in node.Children)
        {
            count += CountFiles(child);
        }

        return count;
    }

    public static int CountFiles(IEnumerable<PlaylistNode> roots)
    {
        var count = 0;
        foreach (var root in roots)
        {
            count += CountFiles(root);
        }

        return count;
    }

    public static List<PlaylistNode> Flatten(IEnumerable<PlaylistNode> roots)
    {
        var files = new List<PlaylistNode>();
        foreach (var root in roots)
        {
            Walk(root, files);
        }

        return files;
    }

    public static bool ContainsFile(IEnumerable<PlaylistNode> roots, string fullPath)
    {
        return Flatten(roots).Any(node => string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase));
    }

    public static bool ContainsNode(PlaylistNode root, PlaylistNode target)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        foreach (var child in root.Children)
        {
            if (ContainsNode(child, target))
            {
                return true;
            }
        }

        return false;
    }

    public static bool Remove(IList<PlaylistNode> nodes, PlaylistNode target)
    {
        if (nodes.Remove(target))
        {
            return true;
        }

        foreach (var node in nodes)
        {
            if (Remove(node.Children, target))
            {
                return true;
            }
        }

        return false;
    }

    public static List<PlaylistNode> Visible(IEnumerable<PlaylistNode> roots)
    {
        var list = new List<PlaylistNode>();
        foreach (var root in roots)
        {
            AppendVisible(root, list);
        }

        return list;
    }

    public static IEnumerable<PlaylistNode> Walk(IEnumerable<PlaylistNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Walk(root.Children))
            {
                yield return child;
            }
        }
    }

    public static List<PlaylistNode> TopMost(IEnumerable<PlaylistNode> nodes)
    {
        var list = nodes.ToList();
        return list.Where(node => !list.Any(other => !ReferenceEquals(other, node) && ContainsNode(other, node))).ToList();
    }

    public static bool CanMove(IList<PlaylistNode> roots, IReadOnlyList<PlaylistNode> moving, PlaylistNode? target, PlaylistDropKind kind)
    {
        return Plan(roots, moving, target, kind) is not null;
    }

    public static bool TryMove(IList<PlaylistNode> roots, IReadOnlyList<PlaylistNode> moving, PlaylistNode? target, PlaylistDropKind kind)
    {
        var plan = Plan(roots, moving, target, kind);
        if (plan is null)
        {
            return false;
        }

        var removedBefore = 0;
        foreach (var node in plan.Nodes)
        {
            if (TryLocate(roots, node, out var source, out var sourceIndex)
                && ReferenceEquals(source, plan.Destination)
                && sourceIndex < plan.Index)
            {
                removedBefore++;
            }
        }

        foreach (var node in plan.Nodes)
        {
            Remove(roots, node);
        }

        var index = Math.Clamp(plan.Index - removedBefore, 0, plan.Destination.Count);
        for (var i = 0; i < plan.Nodes.Count; i++)
        {
            plan.Destination.Insert(index + i, plan.Nodes[i]);
        }

        return true;
    }

    public static IEnumerable<PlaylistNode> Ancestors(IEnumerable<PlaylistNode> roots, PlaylistNode target)
    {
        var chain = new List<PlaylistNode>();
        return Find(roots, target, chain) ? chain : [];
    }

    public static PlaylistSnapshot ToSnapshot(PlaylistNode node)
    {
        return new PlaylistSnapshot
        {
            Name = node.Name,
            Path = node.FullPath,
            FolderPath = node.FolderPath,
            Expanded = node.IsExpanded,
            Children = node.Children.Select(ToSnapshot).ToList()
        };
    }

    public static PlaylistNode? FromSnapshot(PlaylistSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.Path))
        {
            var folder = new PlaylistNode(snapshot.Name, null, snapshot.FolderPath) { IsExpanded = snapshot.Expanded };
            foreach (var child in snapshot.Children)
            {
                var restored = FromSnapshot(child);
                if (restored is not null)
                {
                    folder.Children.Add(restored);
                }
            }

            return CountFiles(folder) == 0 ? null : folder;
        }

        var path = snapshot.Path;
        if (!File.Exists(path))
        {
            return null;
        }

        return new PlaylistNode(Path.GetFileName(path), path, null);
    }

    private sealed record MovePlan(IList<PlaylistNode> Destination, int Index, List<PlaylistNode> Nodes);

    private static MovePlan? Plan(IList<PlaylistNode> roots, IReadOnlyList<PlaylistNode> moving, PlaylistNode? target, PlaylistDropKind kind)
    {
        var order = Walk(roots).ToList();
        var nodes = TopMost(moving)
            .OrderBy(node => order.IndexOf(node))
            .Where(node => order.Contains(node))
            .ToList();
        if (nodes.Count == 0)
        {
            return null;
        }

        if (target is not null && nodes.Any(node => ReferenceEquals(node, target) || ContainsNode(node, target)))
        {
            return null;
        }

        if (target is null)
        {
            return new MovePlan(roots, roots.Count, nodes);
        }

        if (!TryLocate(roots, target, out var parent, out var targetIndex))
        {
            return null;
        }

        if (kind == PlaylistDropKind.Into)
        {
            return target.IsFolder ? new MovePlan(target.Children, target.Children.Count, nodes) : null;
        }

        var index = targetIndex + (kind == PlaylistDropKind.After ? 1 : 0);
        return new MovePlan(parent, index, nodes);
    }

    private static bool TryLocate(IList<PlaylistNode> nodes, PlaylistNode target, out IList<PlaylistNode> parent, out int index)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            if (ReferenceEquals(nodes[i], target))
            {
                parent = nodes;
                index = i;
                return true;
            }

            if (TryLocate(nodes[i].Children, target, out parent, out index))
            {
                return true;
            }
        }

        parent = nodes;
        index = -1;
        return false;
    }

    private static void AppendVisible(PlaylistNode node, List<PlaylistNode> list)
    {
        list.Add(node);
        if (!node.IsFolder || !node.IsExpanded)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            AppendVisible(child, list);
        }
    }

    private static bool Find(IEnumerable<PlaylistNode> nodes, PlaylistNode target, List<PlaylistNode> chain)
    {
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node, target))
            {
                return true;
            }

            chain.Add(node);
            if (Find(node.Children, target, chain))
            {
                return true;
            }

            chain.RemoveAt(chain.Count - 1);
        }

        return false;
    }

    private static void Walk(PlaylistNode node, List<PlaylistNode> files)
    {
        if (!node.IsFolder)
        {
            files.Add(node);
            return;
        }

        foreach (var child in node.Children)
        {
            Walk(child, files);
        }
    }

    private static void Fill(PlaylistNode parent, string directory, int depth)
    {
        if (depth >= MaxDepth)
        {
            return;
        }

        IEnumerable<string> directories;
        IEnumerable<string> files;
        try
        {
            directories = Directory.EnumerateDirectories(directory)
                .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
            files = Directory.EnumerateFiles(directory)
                .Where(MediaKinds.IsMedia)
                .OrderBy(path => Path.GetFileName(path), StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var childDirectory in directories)
        {
            var child = new PlaylistNode(Path.GetFileName(childDirectory), null, Path.GetFullPath(childDirectory));
            Fill(child, childDirectory, depth + 1);
            if (CountFiles(child) > 0)
            {
                parent.Children.Add(child);
            }
        }

        foreach (var file in files)
        {
            parent.Children.Add(new PlaylistNode(Path.GetFileName(file), Path.GetFullPath(file), null));
        }
    }
}
