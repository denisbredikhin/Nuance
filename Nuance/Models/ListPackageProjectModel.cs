using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuance
{
    /// <summary>
    /// Calculated project data model for list report
    /// </summary>
    internal class ListPackageProjectModel
    {
        internal List<ReportProblem> ProjectProblems { get; } = new();
        internal string ProjectPath { get; private set; }
        // Calculated project model data for each targetframeworks
        internal List<ListPackageReportFrameworkPackage> TargetFrameworkPackages { get; set; }
        internal string ProjectName { get; private set; }
        internal bool AutoReferenceFound { get; set; }

        public ListPackageProjectModel(string projectPath, string projectName)
        {
            ProjectPath = projectPath;
            ProjectName = projectName;
        }

        // For testing purposes only
        internal ListPackageProjectModel(string projectPath)
            : this(projectPath, null) { }

        internal void AddProjectInformation(ProblemType problemType, string message)
        {
            ProjectProblems.Add(new ReportProblem(project: ProjectPath, text: message, problemType: problemType));
        }
    }
}
