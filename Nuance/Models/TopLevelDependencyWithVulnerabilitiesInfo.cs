using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuance
{
    public class TopLevelDependencyWithVulnerabilitiesInfo : PackageInfo
    {
        public List<VulnerablePackageInfo> RelatedVulnerabilities { get; set; } = [];

        internal DependencyNode Node { get; set; }

        public string TargetFramework { get; set; }

        public IPackageSearchMetadata? BestUpdateCandidate { get; set; }

        public List<VulnerablePackageInfo> BestUpdateCandidateRelatedVulnerabilities { get; set; } = [];

        public List<VulnerablePackageInfo> BestUpdateCandidateSolvedVulnerabilities { get; set; } = [];

        public List<VulnerablePackageInfo> BestUpdateCandidateNewVulnerabilities { get; set; } = [];

        public List<VulnerablePackageInfo> BestUpdateCandidateNotSolvedVulnerabilities { get; set; } = [];

        public List<TopLevelDependencyWithVulnerabilitiesInfo> ChildVulnerabilities { get; set; } = [];

        public TopLevelDependencyWithVulnerabilitiesInfo(DependencyNode node)
        { 
            this.Node = node;
            this.PackageVersion = node.Version;
            this.PackageName = node.Id;
        }

        public TopLevelDependencyWithVulnerabilitiesInfo(PackageDependency dependency)
        {
            this.PackageVersion = dependency.VersionRange.MinVersion;
            this.PackageName = dependency.Id;
        }
    }
}
