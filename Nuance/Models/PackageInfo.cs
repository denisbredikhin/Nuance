using NuGet.Versioning;


namespace Nuance
{
    public class PackageInfo : IEquatable<PackageInfo>
    {
        public string PackageName { get; set; }
        public NuGetVersion PackageVersion { get; set; }

        public override bool Equals(object? obj)
        {
            if (obj is PackageInfo other)
            {
                return Equals(other);
            }
            return false;
        }

        public bool Equals(PackageInfo? other)
        {
            if (other == null) return false;
            return string.Equals(PackageName, other.PackageName, StringComparison.Ordinal) &&
                   PackageVersion==other.PackageVersion;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(PackageName, PackageVersion);
        }
    }
}
