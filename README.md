# Nuance

**Nuance** is a command-line tool designed to identify and recommend updates for vulnerable NuGet packages in .NET projects. It analyzes both direct and transitive dependencies, providing suggestions for updates to mitigate security vulnerabilities.

## Installation

Simply download the latest release and place it in a directory accessible from the command line.

## Usage

Run the tool by providing the path to your solution file:

```
Nuance path_to_solution.sln
```

### Example Output

```
Microsoft.Extensions.Caching.Memory 6.0.1: https://github.com/advisories/GHSA-qj66-m88j-hmgj
Microsoft.IdentityModel.JsonWebTokens 6.33.0: https://github.com/advisories/GHSA-59j7-ghrg-fj52
System.Formats.Asn1 6.0.0: https://github.com/advisories/GHSA-447r-wph3-92pm
...

Top level packages with vulnerable paths:

CoreWCF.Primitives 1.6.0
It is responsible for the following vulnerabilities:
  - Microsoft.Extensions.Caching.Memory 6.0.1: https://github.com/advisories/GHSA-qj66-m88j-hmgj
  - System.Formats.Asn1 6.0.0: https://github.com/advisories/GHSA-447r-wph3-92pm
  - System.Security.Cryptography.Pkcs 6.0.1: https://github.com/advisories/GHSA-555c-2p6r-68mm
...
```

## Features

- Detects vulnerable NuGet packages in .NET solutions.
- Provides links to relevant security advisories.
- Suggests package updates to resolve known vulnerabilities.
- Analyzes both direct and transitive dependencies.

## How is Nuance better than `dotnet list package --vulnerable`?

While `dotnet list package --vulnerable` provides a list of vulnerable dependencies, **Nuance** offers several key advantages:

1. **Consolidated Dependency Analysis:**
   - `dotnet list package --vulnerable` generates reports separately for each project in a solution. This can lead to a lot of redundant information if multiple projects share dependencies, making it harder to analyze the real impact.
   - **Nuance** consolidates dependencies across all projects, reducing noise and making it easier to see which vulnerabilities actually need attention.

2. **Actionable Recommendations:**
   - `dotnet list package --vulnerable` can list either only direct dependencies or a combined list of direct and transitive dependencies. However, it does not provide guidance on what to do next.
   - **Nuance** analyzes vulnerabilities at every dependency level:
     - If there are known fixes available, it suggests the correct versions to update to.
     - If no fixes are available, it advises which upstream dependency maintainers should be contacted to request an update, helping developers take the necessary steps to resolve security issues proactively.


## Requirements

- .NET 8 projects (other versions are not currently supported).

## Status

This project is currently **experimental** and is **not ready for production use**. It has been tested only on .NET 8 projects. Contributions and feedback are welcome to improve functionality and compatibility with other versions.

## Limitations

- Limited to analyzing .NET 8 projects.
- Some vulnerabilities may not have available updates.
- Experimental; results may not be 100% accurate.

## Contributing

Contributions are welcome! Feel free to submit issues and pull requests to help improve the tool.

## License

This project is licensed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).

The project contains portions of code from **NuGet.Client**, which is distributed under the [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0).

## Disclaimer

Use at your own risk. The authors are not responsible for any issues arising from the use of this tool.

---

For more information, visit the GitHub repository.