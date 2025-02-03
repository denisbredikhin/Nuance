using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuance
{
    /// <summary>
    /// Represents a node in the package dependency graph.
    /// </summary>
    public class DependencyNode
    {
        public string Id { get; set; }
        public NuGetVersion Version { get; set; }

        public string? Type { get; set; }
        public HashSet<DependencyNode> Children { get; set; }

        public DependencyNode(string id, NuGetVersion version)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Children = new HashSet<DependencyNode>(new DependencyNodeComparer());
        }

        public override int GetHashCode()
        {
            var hashCodeCombiner = new HashCodeCombiner();
            hashCodeCombiner.AddObject(Id);
            hashCodeCombiner.AddObject(Version);
            hashCodeCombiner.AddUnorderedSequence(Children);
            return hashCodeCombiner.CombinedHash;
        }

        public override string ToString()
        {
            var paths = new List<string>();
            BuildPaths(this, [], paths);
            return string.Join(Environment.NewLine, paths);
        }

        private void BuildPaths(DependencyNode node, List<string> currentPath, List<string> paths)
        {
            if (node == null) return;

            currentPath.Add($"{node.Id} ({node.Type},{node.Version})");

            if (node.Children == null || node.Children.Count == 0)
            {
                paths.Add(string.Join(" -> ", currentPath));
            }
            else
            {
                foreach (var child in node.Children)
                {
                    BuildPaths(child, new List<string>(currentPath), paths);
                }
            }
        }
    }

    internal class DependencyNodeComparer : IEqualityComparer<DependencyNode>
    {
        public bool Equals(DependencyNode? x, DependencyNode? y)
        {
            if (x == null || y == null)
                return false;

            return string.Equals(x.Id, y.Id, StringComparison.CurrentCultureIgnoreCase);
        }

        public int GetHashCode(DependencyNode obj)
        {
            return obj.Id.GetHashCode();
        }
    }
}

