using NuGet.Protocol;

namespace Nuance
{
    public class VulnerablePackageInfo : PackageInfo
    {
        public PackageVulnerabilityMetadata[] Vulnerabilities { get; set; }
    }
    public class RealVulnerablePackageInfo : VulnerablePackageInfo
    {
        public DependencyNode[] DependencyPaths { get; set; }

        public DependencyNode[] InterestingDependencyPaths { get; set; }

        public string TargetFramework { get; set; }
    }
}
