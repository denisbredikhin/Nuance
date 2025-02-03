
namespace Nuance
{
    internal class ListPackageReportFrameworkPackage
    {
        internal string Framework { get; set; }
        internal List<InstalledPackageReference> TopLevelPackages { get; set; }
        internal List<InstalledPackageReference> TransitivePackages { get; set; }
        public ListPackageReportFrameworkPackage(string frameWork)
        {
            Framework = frameWork;
        }
    }
}
