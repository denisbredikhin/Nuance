using NuGet.Protocol.Core.Types;
using System.Diagnostics;


namespace Nuance
{
    /// <summary>
    /// A class to simplify holding all of the information
    /// about a package reference when using list
    /// </summary>
    [DebuggerDisplay("{Name} {OriginalRequestedVersion}")]
    internal class InstalledPackageReference
    {
        internal string Name { get; }
        internal string OriginalRequestedVersion { get; set; }
        internal IPackageSearchMetadata ResolvedPackageMetadata { get; set; }
        internal IPackageSearchMetadata LatestPackageMetadata { get; set; }

        internal List<IPackageSearchMetadata> AllPackageMetadata { get; set; }
        internal bool AutoReference { get; set; }
        internal bool IsVersionOverride { get; set; }

        internal List<DependencyNode>? DependencyPath { get; set; }

        /// <summary>
        /// A constructor that takes a name of a package
        /// </summary>
        /// <param name="name">The name of the package</param>
        public InstalledPackageReference(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }
    }
}
