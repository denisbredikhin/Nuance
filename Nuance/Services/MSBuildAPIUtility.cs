using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using NuGet.Common;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuance
{
    internal class MSBuildAPIUtility
    {
        private const string PACKAGE_REFERENCE_TYPE_TAG = "PackageReference";
        private const string RESTORE_STYLE_TAG = "RestoreProjectStyle";
        private const string NUGET_STYLE_TAG = "NuGetProjectStyle";
        private const string ASSETS_FILE_PATH_TAG = "ProjectAssetsFile";
        private const string CollectPackageReferences = "CollectPackageReferences";
        private const string CollectCentralPackageVersions = "CollectCentralPackageVersions";


        public ILogger Logger { get; }

        public MSBuildAPIUtility(ILogger logger)
        {
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Opens an MSBuild.Evaluation.Project type from a csproj file.
        /// </summary>
        /// <param name="projectCSProjPath">CSProj file which needs to be evaluated</param>
        /// <returns>MSBuild.Evaluation.Project</returns>
        internal static Project GetProject(string projectCSProjPath)
        {
            var projectRootElement = TryOpenProjectRootElement(projectCSProjPath);
            if (projectCSProjPath == null)
            {
                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Strings.Error_MsBuildUnableToOpenProject, projectCSProjPath));
            }
            return new Project(projectRootElement);
        }

        internal static IEnumerable<string> GetProjectsFromSolution(string solutionPath)
        {
            var sln = SolutionFile.Parse(solutionPath);
            return sln.ProjectsInOrder.Select(p => p.AbsolutePath);
        }
        
        /// <summary>
        /// A simple check for some of the evaluated properties to check
        /// if the project is package reference project or not
        /// </summary>
        /// <param name="project"></param>
        /// <returns></returns>
        internal static bool IsPackageReferenceProject(Project project)
        {
            return (project.GetPropertyValue(RESTORE_STYLE_TAG) == "PackageReference" ||
                    project.GetItems(PACKAGE_REFERENCE_TYPE_TAG).Count != 0 ||
                    project.GetPropertyValue(NUGET_STYLE_TAG) == "PackageReference" ||
                    project.GetPropertyValue(ASSETS_FILE_PATH_TAG) != "");
        }

        /// <summary>
        /// Prepares the dictionary that maps frameworks to packages top-level
        /// and transitive.
        /// </summary>
        /// <param name="project">Project to get the resoled versions from</param>
        /// <param name="userInputFrameworks">A list of frameworks</param>
        /// <param name="assetsFile">Assets file for all targets and libraries</param>
        /// <param name="transitive">Include transitive packages/projects in the result</param>
        /// <returns>FrameworkPackages collection with top-level and transitive package/project
        /// references for each framework, or null on error</returns>
        internal static List<FrameworkPackages> GetResolvedVersions(Project project, LockFile assetsFile, bool transitive)
        {
            ArgumentNullException.ThrowIfNull(project);

            ArgumentNullException.ThrowIfNull(assetsFile);

            var projectPath = project.FullPath;
            var resultPackages = new List<FrameworkPackages>();
            var requestedTargetFrameworks = assetsFile.PackageSpec.TargetFrameworks;
            var requestedTargets = assetsFile.Targets;

            // Filtering the Targets to ignore TargetFramework + RID combination, only keep TargetFramework in requestedTargets.
            // So that only one section will be shown for each TFM.
            requestedTargets = requestedTargets.Where(target => target.RuntimeIdentifier == null).ToList();

            foreach (var target in requestedTargets)
            {
                // Find the tfminformation corresponding to the target to
                // get the top-level dependencies
                TargetFrameworkInformation tfmInformation;

                try
                {
                    tfmInformation = requestedTargetFrameworks.First(tfm => tfm.FrameworkName.Equals(target.TargetFramework));
                }
                catch (Exception)
                {
                    throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Strings.ListPkg_ErrorReadingAssetsFile, assetsFile.Path));
                }

                //The packages for the framework that were retrieved with GetRequestedVersions
                var frameworkDependencies = tfmInformation.Dependencies;
                var projectPackages = GetPackageReferencesFromTargets(projectPath, tfmInformation.ToString());
                var topLevelPackages = new List<InstalledPackageReference>();
                var transitivePackages = new List<InstalledPackageReference>();

                foreach (var library in target.Libraries)
                {
                    var matchingPackages = frameworkDependencies.Where(d =>
                        d.Name.Equals(library.Name, StringComparison.OrdinalIgnoreCase)).ToList();

                    var resolvedVersion = library.Version.ToString();

                    //In case we found a matching package in requestedVersions, the package will be
                    //top level.
                    if (matchingPackages.Any())
                    {
                        var topLevelPackage = matchingPackages.Single();
                        InstalledPackageReference installedPackage = default;

                        //If the package is not auto-referenced, get the version from the project file. Otherwise fall back on the assets file
                        if (!topLevelPackage.AutoReferenced)
                        {
                            try
                            { // In case proj and assets file are not in sync and some refs were deleted
                                installedPackage = projectPackages.Where(p => p.Name.Equals(topLevelPackage.Name, StringComparison.Ordinal)).First();
                            }
                            catch (Exception)
                            {
                                throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Strings.ListPkg_ErrorReadingReferenceFromProject, projectPath));
                            }
                        }
                        else
                        {
                            var projectFileVersion = topLevelPackage.LibraryRange.VersionRange.ToString();
                            installedPackage = new InstalledPackageReference(library.Name)
                            {
                                OriginalRequestedVersion = projectFileVersion
                            };
                        }

                        installedPackage.ResolvedPackageMetadata = PackageSearchMetadataBuilder
                            .FromIdentity(new PackageIdentity(library.Name, library.Version))
                            .Build();

                        installedPackage.AutoReference = topLevelPackage.AutoReferenced;

                        if (library.Type != "project")
                        {
                            var rootNode = new DependencyNode(project.GetPropertyValue("MSBuildProjectName"), NuGetVersion.Parse(project.GetPropertyValue("Version")))
                            {
                                Type = "project",
                                Children = [ new DependencyNode(installedPackage.Name, library.Version) {
                                    Type = "package"
                                }]
                            };
                            installedPackage.DependencyPath = [rootNode];
                            topLevelPackages.Add(installedPackage);
                        }
                    }
                    // If no matching packages were found, then the package is transitive,
                    // and include-transitive must be used to add the package
                    else if (transitive) // be sure to exclude "project" references here as these are irrelevant
                    {
                        var installedPackage = new InstalledPackageReference(library.Name)
                        {
                            ResolvedPackageMetadata = PackageSearchMetadataBuilder
                                .FromIdentity(new PackageIdentity(library.Name, library.Version))
                                .Build()
                        };

                        if (library.Type != "project")
                        {
                            var dependencyGraphPerFramework = DependencyGraphFinder.GetAllDependencyGraphsForTarget(
                                assetsFile,
                                library.Name,
                                [ target.Name ]);
                            var rootNode = new DependencyNode(project.GetPropertyValue("MSBuildProjectName"), NuGetVersion.Parse(project.GetPropertyValue("Version")))
                            {
                                Type = "project",
                                Children = [.. dependencyGraphPerFramework[target.Name]]
                            };
                            installedPackage.DependencyPath = [ rootNode ];
                            transitivePackages.Add(installedPackage);
                        }
                    }
                }

                var frameworkPackages = new FrameworkPackages(
                    target.TargetFramework.GetShortFolderName(),
                    topLevelPackages,
                    transitivePackages);

                resultPackages.Add(frameworkPackages);
            }

            return resultPackages;
        }

        /// <summary>
        /// Returns all package references after invoking the target CollectPackageReferences.
        /// </summary>
        /// <param name="projectPath"> Path to the project for which the package references have to be obtained.</param>
        /// <param name="framework">Framework to get reference(s) for</param>
        /// <returns>List of Items containing the package reference for the package.
        /// If the libraryDependency is null then it returns all package references</returns>
        private static IEnumerable<InstalledPackageReference> GetPackageReferencesFromTargets(string projectPath, string framework)
        {
            var globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { { "TargetFramework", framework } };
            var newProject = new ProjectInstance(projectPath, globalProperties, null);
            var logger = new BasicStringLogger();
            newProject.Build([CollectPackageReferences, CollectCentralPackageVersions], [logger], out var targetOutputs);

            // Find the first target output that matches `CollectPackageReferences`
            var matchingTargetOutputReference = targetOutputs.First(
                e => e.Key.Equals(CollectPackageReferences, StringComparison.OrdinalIgnoreCase)
            );

            // Target that matches `CollectCentralPackageVersions`. This will be used to get the versions of `GlobalPackageReference` packages
            var matchingTargetOutputVersion = targetOutputs.First(
                e => e.Key.Equals(CollectCentralPackageVersions, StringComparison.OrdinalIgnoreCase)
            );

            var referenceItems = matchingTargetOutputReference.Value.Items;
            var versionItems = matchingTargetOutputVersion.Value.Items;

            // Transform each item into an InstalledPackageReference
            var installedPackageReferences = referenceItems.Select(p =>
            {
                // Find the matching version item for the current reference item
                var versionItem = versionItems.FirstOrDefault(v =>
                    v.ItemSpec.Equals(p.ItemSpec, StringComparison.OrdinalIgnoreCase)
                );

                // Check if there is a version override
                bool isVersionOverride = !string.IsNullOrEmpty(p.GetMetadata("VersionOverride"));

                // Determine the original requested version
                // if versionOverride -> get versionOverride
                // Otherwise take the version defined in CPM versions if available
                // Otherwise take the packageReference version
                string originalRequestedVersion = isVersionOverride
                    ? p.GetMetadata("VersionOverride")
                    : (versionItem != null
                    ? versionItem.GetMetadata("Version")
                    : p.GetMetadata("Version"));

                return new InstalledPackageReference(p.ItemSpec)
                {
                    OriginalRequestedVersion = originalRequestedVersion,
                    IsVersionOverride = isVersionOverride,
                };
            });

            return installedPackageReferences;

        }

        private static ProjectRootElement TryOpenProjectRootElement(string filename)
        {
            try
            {
                // There is ProjectRootElement.TryOpen but it does not work as expected
                // I.e. it returns null for some valid projects
                return ProjectRootElement.Open(filename, ProjectCollection.GlobalProjectCollection, preserveFormatting: true);
            }
            catch (Microsoft.Build.Exceptions.InvalidProjectFileException)
            {
                return null;
            }
        }
    }
}
