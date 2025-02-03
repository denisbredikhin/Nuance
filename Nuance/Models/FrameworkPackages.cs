namespace Nuance
{
    internal class FrameworkPackages
    {
        public string Framework { get; }
        public IEnumerable<InstalledPackageReference> TopLevelPackages { get; set; }
        public IEnumerable<InstalledPackageReference> TransitivePackages { get; set; }

        /// <summary>
        /// A constructor that takes a framework name, and
        /// initializes the top-level and transitive package
        /// lists
        /// </summary>
        /// <param name="framework">Framework name</param>
        public FrameworkPackages(string framework) : this(framework, new List<InstalledPackageReference>(), new List<InstalledPackageReference>())
        {

        }

        /// <summary>
        /// A constructor that takes a framework name, a list
        /// of top-level packages, and a list of transitive
        /// packages
        /// </summary>
        /// <param name="framework">Framework name that we have packages for</param>
        /// <param name="topLevelPackages">Top-level packages. Shouldn't be null</param>
        /// <param name="transitivePackages">Transitive packages. Shouldn't be null</param>
        public FrameworkPackages(string framework,
            IEnumerable<InstalledPackageReference> topLevelPackages,
            IEnumerable<InstalledPackageReference> transitivePackages)
        {
            Framework = framework ?? throw new ArgumentNullException(nameof(framework));
            TopLevelPackages = topLevelPackages ?? throw new ArgumentNullException(nameof(topLevelPackages));
            TransitivePackages = transitivePackages ?? throw new ArgumentNullException(nameof(transitivePackages));
        }
    }
}
