using System.Collections.Generic;

namespace Assets.SAT.Solvers
{
    /// <summary>
    /// Central list of the SAT solvers offered to the user.
    /// Add a new external solver by adding another <see cref="SolverDescriptor"/> here.
    ///
    /// Must be called from Unity's main thread because <see cref="ProcessSatSolver"/> reads
    /// Unity <c>Application</c> paths in its constructor.
    /// </summary>
    public static class SolverRegistry
    {
        public static List<ISatSolver> BuildSolvers()
        {
            return new List<ISatSolver>
            {
                new InternalSlsSolver(),

                new ProcessSatSolver(new SolverDescriptor
                {
                    DisplayName = "MiniSat",
                    ExecutableBaseName = "minisat",
                    ArgumentsTemplate = "\"{cnf}\" \"{out}\"",
                    OutputFormat = SolverOutputFormat.MinisatResultFile,
                    ReadResultFile = true
                }),

                new ProcessSatSolver(new SolverDescriptor
                {
                    DisplayName = "Kissat",
                    ExecutableBaseName = "kissat",
                    ArgumentsTemplate = "\"{cnf}\"",
                    OutputFormat = SolverOutputFormat.DimacsCompetition,
                    ReadResultFile = false
                })
            };
        }
    }
}
