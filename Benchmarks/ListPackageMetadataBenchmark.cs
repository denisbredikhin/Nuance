using BenchmarkDotNet.Attributes;
using NuGet.Configuration;
using NuGet.Common;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Nuance.Benchmarks;

[MemoryDiagnoser]
public class ListPackageMetadataBenchmark
{
    private Nuance.ListPackageCommandRunner _runner = null!;
    private Nuance.ListPackageArgs _args = null!;
    private List<Nuance.FrameworkPackages> _frameworks = null!;

    [GlobalSetup]
    public void Setup()
    {
        Nuance.VulnerabilityAnalyzingService.Initialize();
        var settings = Settings.LoadDefaultSettings(System.IO.Directory.GetCurrentDirectory());
        var sources = PackageSourceProvider.LoadPackageSources(settings).Where(s => s.IsEnabled).ToList();
        _runner = new Nuance.ListPackageCommandRunner();
        var dummyPath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "Nuance.csproj");
        _args = new Nuance.ListPackageArgs(dummyPath, sources, includeTransitive: false, prerelease: false, highestPatch: false, highestMinor: false, NullLogger.Instance, CancellationToken.None);
        _runner.InitializeSources(_args);
        var topPackages = new List<Nuance.InstalledPackageReference>
        {
            CreateStub("Newtonsoft.Json"),
            CreateStub("Serilog"),
            CreateStub("NUnit"),
            CreateStub("FluentAssertions"),
            CreateStub("Dapper")
        };
        _frameworks = new List<Nuance.FrameworkPackages>
        {
            new Nuance.FrameworkPackages("net8.0", topPackages, new List<Nuance.InstalledPackageReference>())
        };
    }

    private static Nuance.InstalledPackageReference CreateStub(string id)
    {
        var ipr = new Nuance.InstalledPackageReference(id)
        {
            ResolvedPackageMetadata = NuGet.Protocol.Core.Types.PackageSearchMetadataBuilder
                .FromIdentity(new PackageIdentity(id, new NuGetVersion(0,0,0)))
                .Build(),
            AllPackageMetadata = new List<NuGet.Protocol.Core.Types.IPackageSearchMetadata>()
        };
        return ipr;
    }

    [Benchmark]
    public Dictionary<string, List<NuGet.Protocol.Core.Types.IPackageSearchMetadata>> FetchMetadata()
    {
        return _runner.GetPackageMetadataAsync(_frameworks, _args).GetAwaiter().GetResult();
    }
}
