using System.Collections.Generic;

namespace Assets.SAT.Solvers
{
    /// <summary>
    /// The outcome of a solve attempt.
    /// </summary>
    public enum SatStatus
    {
        /// <summary>A satisfying assignment was found (see <see cref="SatResult.Assignment"/>).</summary>
        Satisfiable,

        /// <summary>The problem was proven to have no solution.</summary>
        Unsatisfiable,

        /// <summary>The solver stopped without deciding (e.g. timeout or step cap).</summary>
        Unknown,

        /// <summary>Something went wrong (missing binary, parse failure, exception, ...).</summary>
        Error
    }

    /// <summary>
    /// Result returned by an <see cref="ISatSolver"/>.
    /// This is a plain data object so it can be created safely on a background thread
    /// and consumed later on Unity's main thread.
    /// </summary>
    public class SatResult
    {
        /// <summary>What happened.</summary>
        public SatStatus Status;

        /// <summary>
        /// When <see cref="Status"/> is <see cref="SatStatus.Satisfiable"/>, this maps each
        /// proposition to its truth value.  Null otherwise.
        /// Only "real" propositions appear here; any auxiliary CNF variables are ignored.
        /// </summary>
        public Dictionary<Proposition, bool> Assignment;

        /// <summary>Human readable name of the solver that produced this result.</summary>
        public string SolverName;

        /// <summary>Wall-clock time the solve took, in seconds.</summary>
        public double Seconds;

        /// <summary>Raw text the solver produced (stdout or result file). Useful for debugging.</summary>
        public string RawOutput;

        /// <summary>Populated when <see cref="Status"/> is <see cref="SatStatus.Error"/>.</summary>
        public string Error;

        public static SatResult FromError(string solverName, string message) =>
            new SatResult { Status = SatStatus.Error, SolverName = solverName, Error = message };
    }
}
