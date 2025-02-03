using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuance
{
    internal class ReportProblem
    {
        internal string Project { get; private set; }
        internal string Text { get; private set; }
        internal ProblemType ProblemType { get; }

        private ReportProblem()
        { }

        public ReportProblem(ProblemType problemType, string project, string text)
        {
            ProblemType = problemType;
            Project = project;
            Text = text;
        }
    }
}
