
namespace Nuance
{
    internal enum ProblemType
    {
        Warning,
        Error // Any report problem with this type make application to return 1 instead of 0, for example if asset file is missing for 1 of the projects.
    }
}
