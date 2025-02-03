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

