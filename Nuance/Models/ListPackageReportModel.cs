using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuance
{
    /// <summary>
    /// Calculated solution/projects data model for list report
    /// </summary>
    internal class ListPackageReportModel
    {
        internal ListPackageArgs ListPackageArgs { get; }
        internal List<ListPackageProjectModel> Projects { get; } = new();

        private ListPackageReportModel()
        { }

        internal ListPackageReportModel(ListPackageArgs listPackageArgs)
        {
            ListPackageArgs = listPackageArgs;
        }

        internal ListPackageProjectModel CreateProjectReportData(string projectPath, string projectName)
        {
            var projectModel = new ListPackageProjectModel(projectPath, projectName);
            Projects.Add(projectModel);
            return projectModel;
        }
    }
}
