using Nuance;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using System;

class Program
{
    static async Task Main(string[] args)
    {
        VulnerabilityAnalyzingService.Initialize();


        var result = await VulnerabilityAnalyzingService.GetVulnerabilityAnalysis(args[0]);

        foreach (var dep in result.VulnerablePackages.OrderBy(d => d.PackageName).ThenBy(d => d.PackageVersion))
        {
            Console.WriteLine($"{dep.PackageName} {dep.PackageVersion}: {dep.Vulnerabilities.First().AdvisoryUrl}");
            /*Console.WriteLine("Reasons: ");
            foreach (var dependency in dep.InterestingDependencyPaths)
            {
                Console.WriteLine(dependency);
            }
            Console.WriteLine();*/
        }

        Console.WriteLine();
        Console.WriteLine("Top level packages with vulnerable paths: ");
        Console.WriteLine();

        var actions = new HashSet<Tuple<TopLevelDependencyWithVulnerabilitiesInfo, TopLevelDependencyWithVulnerabilitiesInfo>>();

        foreach (var vulInfo in result.TopLevelDependencyWithVulnerabilitiesInfo)
        {
            PrintVulnerabilities(null, vulInfo, actions);
            Console.WriteLine("=============================================================================================");
        }

        Console.WriteLine();
        Console.WriteLine("Actions to do: ");
        foreach (var g in actions.GroupBy(a => a.Item1?.PackageName, a => a.Item2).OrderBy(g => g.Key)) {
            if (g.Key==null)
                Console.WriteLine("Update the following dependencies in your solution:");
            else 
                Console.WriteLine($"Contact author of package {g.Key} and ask him to update the following dependencies:");
            foreach (var dep in g.AsEnumerable().GroupBy(dep => dep.PackageName))
                Console.WriteLine($"  - {dep.Key} to version {dep.First().BestUpdateCandidate.Identity.Version}");
        }

    }

    private static void PrintVulnerabilities(TopLevelDependencyWithVulnerabilitiesInfo? parentInfo, TopLevelDependencyWithVulnerabilitiesInfo dependencyInfo, HashSet<Tuple<TopLevelDependencyWithVulnerabilitiesInfo, TopLevelDependencyWithVulnerabilitiesInfo>> actions, int indentLevel = 0)
    {
        var indent = new string(' ', indentLevel * 2);

        Console.WriteLine($"{indent}{dependencyInfo.PackageName} {dependencyInfo.PackageVersion}");
        Console.WriteLine($"{indent}It is responsible for the following vulnerabilities: ");

        foreach (var realVul in dependencyInfo.RelatedVulnerabilities)
        {
            Console.WriteLine($"{indent}  - {realVul.PackageName} {realVul.PackageVersion}: {string.Join(", ", realVul.Vulnerabilities.Select(v => v.AdvisoryUrl))}");
        }

        if (dependencyInfo.BestUpdateCandidate != null)
        {
            Console.WriteLine($"{indent}It is recommended to update it to the version: {dependencyInfo.BestUpdateCandidate.Identity.Version}");

            if (dependencyInfo.BestUpdateCandidateRelatedVulnerabilities.Count == 0)
            {
                actions.Add(new Tuple<TopLevelDependencyWithVulnerabilitiesInfo, TopLevelDependencyWithVulnerabilitiesInfo>(parentInfo, dependencyInfo));
                Console.WriteLine($"{indent}This will solve all the vulnerabilities!");
            }
            else
            {
                if (dependencyInfo.BestUpdateCandidateSolvedVulnerabilities.Count > 0)
                {
                    Console.WriteLine($"{indent}This will solve the following vulnerabilities:");
                    foreach (var solvedVuln in dependencyInfo.BestUpdateCandidateSolvedVulnerabilities)
                    {
                        Console.WriteLine($"{indent}  - {solvedVuln.PackageName} {solvedVuln.PackageVersion}: {string.Join(", ", solvedVuln.Vulnerabilities.Select(v => v.AdvisoryUrl))}");
                    }
                }
                if (dependencyInfo.BestUpdateCandidateNotSolvedVulnerabilities.Count > 0)
                {
                    Console.WriteLine($"{indent}But the following vulnerabilities are not solved:");
                    foreach (var nonSolvedVuln in dependencyInfo.BestUpdateCandidateNotSolvedVulnerabilities)
                    {
                        Console.WriteLine($"{indent}  - {nonSolvedVuln.PackageName} {nonSolvedVuln.PackageVersion}: {string.Join(", ", nonSolvedVuln.Vulnerabilities.Select(v => v.AdvisoryUrl))}");
                    }
                }
                if (dependencyInfo.BestUpdateCandidateNewVulnerabilities.Count > 0)
                {
                    Console.WriteLine($"{indent}And it will bring some new vulnerabilities!:");
                    foreach (var newVul in dependencyInfo.BestUpdateCandidateNewVulnerabilities)
                    {
                        Console.WriteLine($"{indent}  - {newVul.PackageName} {newVul.PackageVersion}: {string.Join(", ", newVul.Vulnerabilities.Select(v => v.AdvisoryUrl))}");
                    }
                }
            }
        }
        else
        {
            Console.WriteLine($"{indent}Unfortunately, there is no update for this dependency that will solve any vulnerability");
        }

        if (dependencyInfo.ChildVulnerabilities.Count > 0)
        {
            Console.WriteLine($"{indent}Child packages that are responsible for vulnerabilities:");
            foreach (var child in dependencyInfo.ChildVulnerabilities)
            {
                PrintVulnerabilities(dependencyInfo, child, actions, indentLevel + 1);
            }
        }
    }

}