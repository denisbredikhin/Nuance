using Microsoft.Build.Evaluation;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol;
using NuGet.Versioning;
using System.Globalization;

namespace Nuance
{
    internal class ListPackageCommandRunner
    {
        private const string ProjectAssetsFile = "ProjectAssetsFile";
        private const string ProjectName = "MSBuildProjectName";

        private readonly Dictionary<PackageSource, SourceRepository> _sourceRepositoryCache;
        private readonly SourceCacheContext _sharedSourceCacheContext = new();
        private readonly Dictionary<string, IEnumerable<IPackageSearchMetadata>> _metadataMemoryCache = new(StringComparer.OrdinalIgnoreCase);

        public ListPackageCommandRunner() => _sourceRepositoryCache = [];

        public async Task<ListPackageReportModel> ExecuteCommandAsync(ListPackageArgs listPackageArgs)
        {
             // It's important not to print anything to console from below methods and sub method calls, because it'll affect both json/console outputs.
            var listPackageReportModel = new ListPackageReportModel(listPackageArgs);
            if (!File.Exists(listPackageArgs.Path))
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture,
                        Strings.ListPkg_ErrorFileNotFound,
                        listPackageArgs.Path));
            }

            PopulateSourceRepositoryCache(listPackageArgs);

            //If the given file is a solution, get the list of projects
            //If not, then it's a project, which is put in a list
            var projectsPaths = Path.GetExtension(listPackageArgs.Path).Equals(".sln", PathUtility.GetStringComparisonBasedOnOS()) || Path.GetExtension(listPackageArgs.Path).Equals(".slnx", PathUtility.GetStringComparisonBasedOnOS()) ?
                           MSBuildAPIUtility.GetProjectsFromSolution(listPackageArgs.Path).Where(File.Exists) :
                           new List<string>([listPackageArgs.Path]);

            foreach (string projectPath in projectsPaths)
                await GetProjectMetadataAsync(projectPath, listPackageReportModel, listPackageArgs);

            return listPackageReportModel;
        }

        internal void InitializeSources(ListPackageArgs args) => PopulateSourceRepositoryCache(args);

        private async Task GetProjectMetadataAsync(string projectPath, ListPackageReportModel listPackageReportModel, ListPackageArgs listPackageArgs)
        {
            //Open project to evaluate properties for the assets
            //file and the name of the project
            Project project = MSBuildAPIUtility.GetProject(projectPath);
            var projectName = project.GetPropertyValue(ProjectName);
            ListPackageProjectModel projectModel = listPackageReportModel.CreateProjectReportData(projectPath: projectPath, projectName);

            if (!MSBuildAPIUtility.IsPackageReferenceProject(project))
            {
                projectModel.AddProjectInformation(ProblemType.Error,
                    string.Format(CultureInfo.CurrentCulture, Strings.Error_NotPRProject, projectPath));
                return;
            }

            var assetsPath = project.GetPropertyValue(ProjectAssetsFile);
            if (!File.Exists(assetsPath))
            {
                projectModel.AddProjectInformation(ProblemType.Error,
                    string.Format(CultureInfo.CurrentCulture, Strings.Error_AssetsFileNotFound, projectPath));
            }
            else
            {
                var lockFileFormat = new LockFileFormat();
                LockFile assetsFile = lockFileFormat.Read(assetsPath);

                // Assets file validation
                if (assetsFile.PackageSpec != null && assetsFile.Targets is { Count: > 0 })
                {
                    // Get all the packages that are referenced in a project
                    List<FrameworkPackages> frameworks;
                    try
                    {
                        frameworks = MSBuildAPIUtility.GetResolvedVersions(project, assetsFile, listPackageArgs.IncludeTransitive);
                    }
                    catch (InvalidOperationException ex)
                    {
                        projectModel.AddProjectInformation(ProblemType.Error, ex.Message);
                        return;
                    }

                    if (frameworks.Count > 0)
                    {
                        WarnForHttpSources(listPackageArgs, projectModel);
                        var metadata = await GetPackageMetadataAsync(frameworks, listPackageArgs);
                        UpdatePackagesWithSourceMetadata(frameworks, metadata, listPackageArgs);
                        var hasAutoReference = false;
                        var projectFrameworkPackages = GetPackagesMetadata(frameworks, listPackageArgs, ref hasAutoReference);
                        projectModel.TargetFrameworkPackages = projectFrameworkPackages;
                        projectModel.AutoReferenceFound = hasAutoReference;
                    }
                }
                else
                {
                    projectModel.AddProjectInformation(ProblemType.Error,
                        string.Format(CultureInfo.CurrentCulture, Strings.ListPkg_ErrorReadingAssetsFile, assetsPath));
                }

                // Unload project
                ProjectCollection.GlobalProjectCollection.UnloadProject(project);
            }
        }

        /// <summary>
        /// Returns the metadata for list package report
        /// </summary>
        /// <param name="packages">A list of framework packages. Check <see cref="FrameworkPackages"/></param>
        /// <param name="listPackageArgs">Command line options</param>
        /// <param name="hasAutoReference">At least one discovered package is autoreference</param>
        /// <returns>The list of package metadata</returns>
        internal static List<ListPackageReportFrameworkPackage> GetPackagesMetadata(IEnumerable<FrameworkPackages> packages, ListPackageArgs listPackageArgs, ref bool hasAutoReference)
        {
            var projectFrameworkPackages = new List<ListPackageReportFrameworkPackage>();
            hasAutoReference = false;
            foreach (FrameworkPackages frameworkPackages in packages)
            {
                var targetFrameworkPackageMetadata = new ListPackageReportFrameworkPackage(frameworkPackages.Framework);
                projectFrameworkPackages.Add(targetFrameworkPackageMetadata);

                targetFrameworkPackageMetadata.TopLevelPackages = frameworkPackages.TopLevelPackages.Any()
                    ? frameworkPackages.TopLevelPackages.ToList()
                    : [];

                targetFrameworkPackageMetadata.TransitivePackages = (listPackageArgs.IncludeTransitive && frameworkPackages.TransitivePackages.Any())
                    ? frameworkPackages.TransitivePackages.ToList()
                    : [];
            }
            return projectFrameworkPackages;
        }

        private static void WarnForHttpSources(ListPackageArgs listPackageArgs, ListPackageProjectModel projectModel)
        {
            List<PackageSource> httpPackageSources = null;
            foreach (PackageSource packageSource in listPackageArgs.PackageSources)
            {
                if (packageSource.IsHttp && !packageSource.IsHttps && !packageSource.AllowInsecureConnections)
                {
                    httpPackageSources ??= new();
                    httpPackageSources.Add(packageSource);
                }
            }

            if (httpPackageSources is { Count: > 0 })
            {
                if (httpPackageSources.Count == 1)
                {
                    projectModel.AddProjectInformation(
                        ProblemType.Warning,
                        string.Format(CultureInfo.CurrentCulture,
                        Strings.Warning_HttpServerUsage,
                        "list package",
                        httpPackageSources[0]));
                }
                else
                {
                    projectModel.AddProjectInformation(
                        ProblemType.Warning,
                        string.Format(CultureInfo.CurrentCulture,
                        Strings.Warning_HttpServerUsage_MultipleSources,
                        "list package",
                            Environment.NewLine + string.Join(Environment.NewLine, httpPackageSources.Select(e => e.Name))));
                }
            }
        }

        /// <summary>
        /// Get package metadata from all sources
        /// </summary>
        /// <param name="targetFrameworks">A <see cref="FrameworkPackages"/> per project target framework</param>
        /// <param name="listPackageArgs">List command args</param>
        /// <returns>A dictionary where the key is the package id, and the value is a list of <see cref="IPackageSearchMetadata"/>.</returns>
        internal async Task<Dictionary<string, List<IPackageSearchMetadata>>> GetPackageMetadataAsync(List<FrameworkPackages> targetFrameworks, ListPackageArgs listPackageArgs)
        {
            List<string> allPackages = GetAllPackageIdentifiers(targetFrameworks, listPackageArgs.IncludeTransitive);
            var packageMetadataById = new Dictionary<string, List<IPackageSearchMetadata>>(capacity: allPackages.Count);

            int maxParallel = listPackageArgs.PackageSources.Any(s => s.IsHttp) ? 8 : (Environment.ProcessorCount / listPackageArgs.PackageSources.Count) + 1;

            await ThrottledForEachAsync(allPackages,
                async (packageId, ct) => await GetPackageVersionsAsync(packageId, listPackageArgs, ct),
                packageMetadata => { packageMetadataById[packageMetadata.Key] = packageMetadata.Value.Values.ToList(); },
                maxParallel,
                listPackageArgs.CancellationToken);

            return packageMetadataById;

            static List<string> GetAllPackageIdentifiers(List<FrameworkPackages> frameworks, bool includeTransitive)
            {
                IEnumerable<InstalledPackageReference> intermediateEnumerable = frameworks.SelectMany(f => f.TopLevelPackages);
                if (includeTransitive)
                    intermediateEnumerable = intermediateEnumerable.Concat(frameworks.SelectMany(f => f.TransitivePackages));
                return intermediateEnumerable.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
        }

        private async Task<KeyValuePair<string, Dictionary<NuGetVersion, IPackageSearchMetadata>>> GetPackageVersionsAsync(string package, ListPackageArgs listPackageArgs, CancellationToken cancellationToken)
        {
            var results = new Dictionary<NuGetVersion, IPackageSearchMetadata>();
            await ThrottledForEachAsync(listPackageArgs.PackageSources,
                async (source, ct) => await GetLatestVersionPerSourceAsync(source, listPackageArgs, package, ct),
                continuation: enumerated =>
                {
                    foreach (var metadata in enumerated)
                    {
                        if (!results.TryAdd(metadata.Identity.Version, metadata) &&
                            results[metadata.Identity.Version].Vulnerabilities == null &&
                            metadata.Vulnerabilities != null)
                        {
                            results[metadata.Identity.Version] = metadata;
                        }
                    }
                },
                maxParallel: listPackageArgs.PackageSources.Count,
                cancellationToken);
            return new KeyValuePair<string, Dictionary<NuGetVersion, IPackageSearchMetadata>>(package, results);
        }

        internal async Task<IEnumerable<IPackageSearchMetadata>> GetLatestVersionPerSourceAsync(PackageSource packageSource, ListPackageArgs listPackageArgs, string package, CancellationToken cancellationToken)
        {
            string cacheKey = packageSource.Source + "|" + (listPackageArgs.Prerelease ? "pre" : "stable") + "|" + package;
            if (_metadataMemoryCache.TryGetValue(cacheKey, out var cached))
                return cached;

            SourceRepository sourceRepository = _sourceRepositoryCache[packageSource];
            var packageMetadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(cancellationToken);

            var packages = await packageMetadataResource.GetMetadataAsync(
                package,
                includePrerelease: listPackageArgs.Prerelease,
                includeUnlisted: false,
                sourceCacheContext: _sharedSourceCacheContext,
                log: listPackageArgs.Logger,
                token: listPackageArgs.CancellationToken);

            var materialized = packages.ToList();
            _metadataMemoryCache[cacheKey] = materialized;
            return materialized;
        }

        private async Task ThrottledForEachAsync<TItem, TResult>(IList<TItem> items, Func<TItem, CancellationToken, Task<TResult>> taskFactory, Action<TResult> continuation, int maxParallel, CancellationToken cancellationToken)
        {
            int taskCount = Math.Min(items.Count, maxParallel);
            var tasks = new Task<TResult>[taskCount];
            using CancellationTokenSource faultCancelationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                int itemIndex;
                for (itemIndex = 0; itemIndex < taskCount; itemIndex++)
                    tasks[itemIndex] = taskFactory(items[itemIndex], faultCancelationTokenSource.Token);

                while (itemIndex < items.Count)
                {
                    _ = await Task.WhenAny(tasks);
                    for (int i = 0; i < tasks.Length; i++)
                    {
                        if (tasks[i].IsCompleted)
                        {
                            TResult result = await tasks[i];
                            continuation(result);
                            tasks[i] = taskFactory(items[itemIndex++], faultCancelationTokenSource.Token);
                            break;
                        }
                    }
                }

                await Task.WhenAll(tasks);
                for (int i = 0; i < tasks.Length; i++)
                {
                    TResult result = await tasks[i];
                    continuation(result);
                }
            }
            catch
            {
                faultCancelationTokenSource.Cancel();
                for (int i = 0; i < tasks.Length; i++)
                    tasks[i] ??= Task.FromResult(default(TResult));
                await Task.WhenAll(tasks);
                throw;
            }
            finally
            {
                faultCancelationTokenSource.Cancel();
            }
        }

        private void PopulateSourceRepositoryCache(ListPackageArgs listPackageArgs)
        {
            IEnumerable<Lazy<INuGetResourceProvider>> providers = Repository.Provider.GetCoreV3();
            foreach (PackageSource source in listPackageArgs.PackageSources)
            {
                var sourceRepository = Repository.CreateSource(providers, source, FeedType.Undefined);
                _sourceRepositoryCache[source] = sourceRepository;
            }
        }

        internal void UpdatePackagesWithSourceMetadata(List<FrameworkPackages> frameworks, Dictionary<string, List<IPackageSearchMetadata>> packageMetadata, ListPackageArgs listPackageArgs)
        {
            foreach (var frameworkPackages in frameworks)
            {
                foreach (var topLevelPackage in frameworkPackages.TopLevelPackages)
                {
                    if (packageMetadata.TryGetValue(topLevelPackage.Name, out var matchingPackage))
                    {
                        topLevelPackage.AllPackageMetadata = matchingPackage;
                        topLevelPackage.ResolvedPackageMetadata = matchingPackage.FirstOrDefault(v => v.Identity.Version == topLevelPackage.ResolvedPackageMetadata.Identity.Version);
                    }
                }
                foreach (var transitivePackage in frameworkPackages.TransitivePackages)
                {
                    if (packageMetadata.TryGetValue(transitivePackage.Name, out var matchingPackage))
                    {
                        transitivePackage.AllPackageMetadata = matchingPackage;
                        transitivePackage.ResolvedPackageMetadata = matchingPackage.FirstOrDefault(v => v.Identity.Version == transitivePackage.ResolvedPackageMetadata.Identity.Version);
                    }
                }
            }
        }
    }
}
