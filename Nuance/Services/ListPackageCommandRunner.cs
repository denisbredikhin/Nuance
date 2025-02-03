using Microsoft.Build.Evaluation;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;
using NuGet.Protocol;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Packaging;
using System.Text.Json;

namespace Nuance
{
    internal class ListPackageCommandRunner
    {
        private const string ProjectAssetsFile = "ProjectAssetsFile";
        private const string ProjectName = "MSBuildProjectName";
        private readonly Dictionary<PackageSource, SourceRepository> _sourceRepositoryCache;

        public ListPackageCommandRunner()
        {
            _sourceRepositoryCache = [];
        }

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
            var projectsPaths = Path.GetExtension(listPackageArgs.Path).Equals(".sln", PathUtility.GetStringComparisonBasedOnOS()) ?
                           MSBuildAPIUtility.GetProjectsFromSolution(listPackageArgs.Path).Where(f => File.Exists(f)) :
                           new List<string>([listPackageArgs.Path]);

            foreach (string projectPath in projectsPaths)
                await GetProjectMetadataAsync(projectPath, listPackageReportModel, listPackageArgs);

            return listPackageReportModel;
        }

        private async Task GetProjectMetadataAsync(
            string projectPath,
            ListPackageReportModel listPackageReportModel,
            ListPackageArgs listPackageArgs)
        {
            //Open project to evaluate properties for the assets
            //file and the name of the project
            Project project = MSBuildAPIUtility.GetProject(projectPath);
            var projectName = project.GetPropertyValue(ProjectName);
            ListPackageProjectModel projectModel = listPackageReportModel.CreateProjectReportData(projectPath: projectPath, projectName);

            if (!MSBuildAPIUtility.IsPackageReferenceProject(project))
            {
                projectModel.AddProjectInformation(problemType: ProblemType.Error,
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
                if (assetsFile.PackageSpec != null &&
                    assetsFile.Targets != null &&
                    assetsFile.Targets.Count != 0)
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
        internal static List<ListPackageReportFrameworkPackage> GetPackagesMetadata(
            IEnumerable<FrameworkPackages> packages,
            ListPackageArgs listPackageArgs,
            ref bool hasAutoReference)
        {
            var projectFrameworkPackages = new List<ListPackageReportFrameworkPackage>();

            hasAutoReference = false;
            foreach (FrameworkPackages frameworkPackages in packages)
            {
                string frameWork = frameworkPackages.Framework;
                ListPackageReportFrameworkPackage targetFrameworkPackageMetadata = new ListPackageReportFrameworkPackage(frameWork);
                projectFrameworkPackages.Add(targetFrameworkPackageMetadata);
                var frameworkTopLevelPackages = frameworkPackages.TopLevelPackages;
                var frameworkTransitivePackages = frameworkPackages.TransitivePackages;

                // If no packages exist for this framework, print the
                // appropriate message
                var tableHasAutoReference = false;
                // Print top-level packages
                if (frameworkTopLevelPackages.Any())
                {

                    targetFrameworkPackageMetadata.TopLevelPackages = frameworkTopLevelPackages.ToList();
                }
                else
                    targetFrameworkPackageMetadata.TopLevelPackages = [];

                // Print transitive packages
                if (listPackageArgs.IncludeTransitive && frameworkTransitivePackages.Any())
                {
                    targetFrameworkPackageMetadata.TransitivePackages = frameworkTransitivePackages.ToList();
                }
                else
                    targetFrameworkPackageMetadata.TransitivePackages = [];
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
                    if (httpPackageSources == null)
                    {
                        httpPackageSources = new();
                    }
                    httpPackageSources.Add(packageSource);
                }
            }

            if (httpPackageSources != null && httpPackageSources.Count != 0)
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
        private async Task<Dictionary<string, List<IPackageSearchMetadata>>> GetPackageMetadataAsync(
            List<FrameworkPackages> targetFrameworks,
            ListPackageArgs listPackageArgs)
        {
            List<string> allPackages = GetAllPackageIdentifiers(targetFrameworks, listPackageArgs.IncludeTransitive);
            var packageMetadataById = new Dictionary<string, List<IPackageSearchMetadata>>(capacity: allPackages.Count);

            int maxParallel = listPackageArgs.PackageSources.Any(s => s.IsHttp)
                ? 8 // Try to be nice to HTTP package sources
                : (Environment.ProcessorCount / listPackageArgs.PackageSources.Count) + 1;

            await ThrottledForEachAsync(allPackages,
                async (packageId, cancellationToken) => await GetPackageVersionsAsync(packageId, listPackageArgs, cancellationToken),
                packageMetadata => {
                    packageMetadataById[packageMetadata.Key] = packageMetadata.Value.Values.ToList();
                    /*var existingMethadata = packageMetadataById[packageMetadata.Key];
                    if (existingMethadata == null)
                        packageMetadataById.Add(packageMetadata.Key, packageMetadata.Value);
                    else {
                        foreach (var kvp in packageMetadata.Value) {
                            var existingVersionInfo = existingMethadata[kvp.Key];
                            if (existingVersionInfo==null)
                                existingMethadata.Add(kvp.Key, kvp.Value);
                            else if (existingVersionInfo.Vulnerabilities == null && kvp.Value.Vulnerabilities != null)
                                existingMethadata[kvp.Key] = kvp.Value;
                        }
                    }*/
                },
                maxParallel,
                listPackageArgs.CancellationToken);

            return packageMetadataById;

            static List<string> GetAllPackageIdentifiers(List<FrameworkPackages> frameworks, bool includeTransitive)
            {
                IEnumerable<InstalledPackageReference> intermediateEnumerable = frameworks.SelectMany(f => f.TopLevelPackages);
                if (includeTransitive)
                {
                    intermediateEnumerable = intermediateEnumerable.Concat(frameworks.SelectMany(f => f.TransitivePackages));
                }
                List<string> allPackages = intermediateEnumerable.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return allPackages;
            }
        }

        /// <summary>Run a throttled iteration of a list that performs async work, with a "single threaded" collection of results.</summary>
        /// <remarks>
        /// <para>The continuation delegate is called sequentially, so results can be safely added to non-synchronized collections.</para>
        /// <para>If any task factory invocation throws, or any task faults, the cancellation token will be triggered and the iteration will end early.</para>
        /// </remarks>
        /// <typeparam name="TItem">The item type for the input list</typeparam>
        /// <typeparam name="TResult">The result type of the async work</typeparam>
        /// <param name="items">The input list to iterate</param>
        /// <param name="taskFactory">Delegate to start async work.</param>
        /// <param name="continuation">Delegate with result of async work. Will not be called concurrently.</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <param name="maxParallel">The maximum number of tasks to allow running in parallel.</param>
        /// <returns>A task that can be awaited to wait for completion of the iteration.</returns>
        private async Task ThrottledForEachAsync<TItem, TResult>(
            IList<TItem> items,
            Func<TItem, CancellationToken, Task<TResult>> taskFactory,
            Action<TResult> continuation,
            int maxParallel,
            CancellationToken cancellationToken)
        {
            int taskCount = Math.Min(items.Count, maxParallel);
            var tasks = new Task<TResult>[taskCount];

            using CancellationTokenSource faultCancelationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                // ramp up throttling (fill task array)
                int itemIndex;
                for (itemIndex = 0; itemIndex < taskCount; itemIndex++)
                {
                    tasks[itemIndex] = taskFactory(items[itemIndex], faultCancelationTokenSource.Token);
                }

                // throttling steady state (max parallel tasks running, more input items waiting to queue)
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

                // ramp down throttling (no more inputs waiting to start, just need to wait for last tasks to finish)
                await Task.WhenAll(tasks);
                for (int i = 0; i < tasks.Length; i++)
                {
                    TResult result = await tasks[i];
                    continuation(result);
                }
            }
            catch
            {
                // Don't leave un-awaited tasks. Request cancellation, then wait for tasks to finish.
                faultCancelationTokenSource.Cancel();

                // Make sure none of the tasks are null (factory exception during ramp-up)
                for (int i = 0; i < tasks.Length; i++)
                {
                    if (tasks[i] is null)
                    {
                        tasks[i] = Task.FromResult(default(TResult));
                    }
                }

                await Task.WhenAll(tasks);
                throw;
            }
            finally
            {
                faultCancelationTokenSource.Cancel();
            }
        }

        /// <summary>
        /// Pre-populate _sourceRepositoryCache so source repository can be reused between different calls.
        /// </summary>
        /// <param name="listPackageArgs">List args for the token and source provider</param>
        private void PopulateSourceRepositoryCache(ListPackageArgs listPackageArgs)
        {
            IEnumerable<Lazy<INuGetResourceProvider>> providers = Repository.Provider.GetCoreV3();
            IEnumerable<PackageSource> sources = listPackageArgs.PackageSources;
            foreach (PackageSource source in sources)
            {
                SourceRepository sourceRepository = Repository.CreateSource(providers, source, FeedType.Undefined);
                _sourceRepositoryCache[source] = sourceRepository;
            }
        }

        /// <summary>
        /// Get last versions for every package from the unique packages
        /// </summary>
        /// <param name="frameworks"> List of <see cref="FrameworkPackages"/>.</param>
        /// <param name="packageMetadata">Package metadata from package sources</param>
        /// <param name="listPackageArgs">Arguments for list package to get the right latest version</param>
        internal void UpdatePackagesWithSourceMetadata(
            List<FrameworkPackages> frameworks,
            Dictionary<string, List<IPackageSearchMetadata>> packageMetadata,
            ListPackageArgs listPackageArgs)
        {
            foreach (var frameworkPackages in frameworks)
            {
                foreach (var topLevelPackage in frameworkPackages.TopLevelPackages)
                {
                    if (packageMetadata.TryGetValue(topLevelPackage.Name, out List<IPackageSearchMetadata> matchingPackage))
                    {
                        topLevelPackage.AllPackageMetadata = matchingPackage;
                        // Get latest metadata *only* if this is a report requiring "outdated" metadata
                        /*if (listPackageArgs.ReportType == ReportType.Outdated && matchingPackage.Count > 0)
                        {
                            var latestVersion = matchingPackage.Where(newVersion => MeetsConstraints(newVersion.Identity.Version, topLevelPackage, listPackageArgs)).Max(i => i.Identity.Version);

                            if (latestVersion is not null)
                            {
                                topLevelPackage.LatestPackageMetadata = matchingPackage.First(p => p.Identity.Version == latestVersion);
                                topLevelPackage.UpdateLevel = GetUpdateLevel(topLevelPackage.ResolvedPackageMetadata.Identity.Version, topLevelPackage.LatestPackageMetadata.Identity.Version);
                            }
                            else // no latest version available with the given constraints
                            {
                                topLevelPackage.LatestPackageMetadata = null;
                                topLevelPackage.UpdateLevel = UpdateLevel.NoUpdate;
                            }
                        }

                        var matchingPackagesWithDeprecationMetadata = await Task.WhenAll(
                            matchingPackage.Select(async v => new { SearchMetadata = v, DeprecationMetadata = await v.GetDeprecationMetadataAsync() }));

                        // Update resolved version with additional metadata information returned by the server.
                        var resolvedVersionFromServer = matchingPackagesWithDeprecationMetadata
                            .Where(v => v.SearchMetadata.Identity.Version == topLevelPackage.ResolvedPackageMetadata.Identity.Version &&
                                    (v.DeprecationMetadata != null || v.SearchMetadata?.Vulnerabilities != null))
                            .FirstOrDefault();

                        if (resolvedVersionFromServer != null)
                        {
                            topLevelPackage.ResolvedPackageMetadata = resolvedVersionFromServer.SearchMetadata;
                        }*/
                        topLevelPackage.ResolvedPackageMetadata = matchingPackage.FirstOrDefault(v => v.Identity.Version == topLevelPackage.ResolvedPackageMetadata.Identity.Version);
                    }
                }

                foreach (var transitivePackage in frameworkPackages.TransitivePackages)
                {
                    if (packageMetadata.TryGetValue(transitivePackage.Name, out List<IPackageSearchMetadata> matchingPackage))
                    {
                        transitivePackage.AllPackageMetadata = matchingPackage;
                        // Get latest metadata *only* if this is a report requiring "outdated" metadata
                        /*if (listPackageArgs.ReportType == ReportType.Outdated && matchingPackage.Count > 0)
                        {
                            var latestVersion = matchingPackage.Where(newVersion => MeetsConstraints(newVersion.Identity.Version, transitivePackage, listPackageArgs)).Max(i => i.Identity.Version);

                            transitivePackage.LatestPackageMetadata = matchingPackage.First(p => p.Identity.Version == latestVersion);
                            transitivePackage.UpdateLevel = GetUpdateLevel(transitivePackage.ResolvedPackageMetadata.Identity.Version, transitivePackage.LatestPackageMetadata.Identity.Version);
                        }

                        var matchingPackagesWithDeprecationMetadata = await Task.WhenAll(
                            matchingPackage.Select(async v => new { SearchMetadata = v, DeprecationMetadata = await v.GetDeprecationMetadataAsync() }));

                        // Update resolved version with additional metadata information returned by the server.
                        var resolvedVersionFromServer = matchingPackagesWithDeprecationMetadata
                            .Where(v => v.SearchMetadata.Identity.Version == transitivePackage.ResolvedPackageMetadata.Identity.Version &&
                                    (v.DeprecationMetadata != null || v.SearchMetadata?.Vulnerabilities != null))
                            .FirstOrDefault();

                        if (resolvedVersionFromServer != null)
                        {
                            transitivePackage.ResolvedPackageMetadata = resolvedVersionFromServer.SearchMetadata;
                        }*/
                        transitivePackage.ResolvedPackageMetadata = matchingPackage.FirstOrDefault(v => v.Identity.Version == transitivePackage.ResolvedPackageMetadata.Identity.Version);
                    }
                }
            }
        }

        /// <summary>
        /// Prepares the calls to sources for latest versions and updates
        /// the list of tasks with the requests
        /// </summary>
        /// <param name="package">The package to get the latest version for</param>
        /// <param name="listPackageArgs">List args for the token and source provider></param>
        /// <param name="cancellationToken"></param>
        /// <returns>A list of tasks for all latest versions for packages from all sources</returns>
        private async Task<KeyValuePair<string, Dictionary<NuGetVersion, IPackageSearchMetadata>>> GetPackageVersionsAsync(
            string package,
            ListPackageArgs listPackageArgs,
            CancellationToken cancellationToken)
        {
            var results = new Dictionary<NuGetVersion, IPackageSearchMetadata>();
            var sources = listPackageArgs.PackageSources;

            await ThrottledForEachAsync(sources,
                async (source, innerCancellationToken) => await GetLatestVersionPerSourceAsync(source, listPackageArgs, package, innerCancellationToken),
                continuation: (result) => {
                    foreach (var methadata in result)
                    {
                        if (!results.TryAdd(methadata.Identity.Version, methadata) && 
                            results[methadata.Identity.Version].Vulnerabilities == null && 
                            methadata.Vulnerabilities != null) 
                            results[methadata.Identity.Version] = methadata;
                    }
                },
                maxParallel: listPackageArgs.PackageSources.Count,
                cancellationToken);

            return new KeyValuePair<string, Dictionary<NuGetVersion, IPackageSearchMetadata>>(package, results);
        }

        /// <summary>
        /// Gets the highest version of a package from a specific source
        /// </summary>
        /// <param name="packageSource">The source to look for packages at</param>
        /// <param name="listPackageArgs">The list args for the cancellation token</param>
        /// <param name="package">Package to look for updates for</param>
        /// <param name="cancellationToken"></param>
        /// <returns>An updated package with the highest version at a single source</returns>
        private async Task<IEnumerable<IPackageSearchMetadata>> GetLatestVersionPerSourceAsync(
            PackageSource packageSource,
            ListPackageArgs listPackageArgs,
            string package,
            CancellationToken cancellationToken)
        {
            //var cacheKey = $"{packageSource.Source}-{package}";
            //var cachedData = LoadFromCache(cacheKey);

            //if (cachedData != null)
           // {
            //    return cachedData;
           // }

            SourceRepository sourceRepository = _sourceRepositoryCache[packageSource];
            var packageMetadataResource = await sourceRepository.GetResourceAsync<PackageMetadataResource>(cancellationToken);

            using var sourceCacheContext = new SourceCacheContext();
            var packages =
                await packageMetadataResource.GetMetadataAsync(
                    package,
                    includePrerelease: listPackageArgs.Prerelease,
                    includeUnlisted: false,
                    sourceCacheContext: sourceCacheContext,
                    log: listPackageArgs.Logger,
                    token: listPackageArgs.CancellationToken);

            //SaveToCache(cacheKey, packages);

            return packages;
        }

        private const string CacheFilePath = "nuget_cache.json";
        private readonly TimeSpan CacheDuration = TimeSpan.FromDays(1);
        private readonly object _cacheLock = new();

        /*private IEnumerable<IPackageSearchMetadata>? LoadFromCache(string key)
        {
            if (!File.Exists(CacheFilePath))
                return null;

            Dictionary<string, (DateTime, List<IPackageSearchMetadata>)>? cache = null;
            lock (_cacheLock)
            {
                using var stream = new FileStream(CacheFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                cache = JsonSerializer.Deserialize<Dictionary<string, (DateTime, List<IPackageSearchMetadata>)>>(stream);
            }

            if (cache != null && cache.TryGetValue(key, out var entry))
            {
                if (entry.Item1 > DateTime.UtcNow)
                {
                    return entry.Item2;
                }
            }
            return null;
        }

        private void SaveToCache(string key, IEnumerable<IPackageSearchMetadata> packages)
        {
            var cache = new Dictionary<string, (DateTime, List<PackageSearchMetadataRegistration>)>();
            lock (_cacheLock)
            {
                if (File.Exists(CacheFilePath))
                {
                    var existingContent = File.ReadAllText(CacheFilePath);
                    cache = JsonSerializer.Deserialize<Dictionary<string, (DateTime, List<PackageSearchMetadataRegistration>)>>(existingContent) ?? [];
                }

                cache[key] = (DateTime.UtcNow.Add(CacheDuration), packages.ToList());

                using var writeStream = new FileStream(CacheFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                JsonSerializer.Serialize(writeStream, cache);
            }
        }*/

    }
}
