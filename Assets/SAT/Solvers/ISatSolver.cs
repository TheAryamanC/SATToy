using System.Threading;
using System.Threading.Tasks;

namespace Assets.SAT.Solvers
{
    /// <summary>
    /// Common interface for every SAT solver that can be offered in the UI,
    /// whether it is the built-in stochastic local search or an external
    /// process such as MiniSat or Kissat.
    /// </summary>
    public interface ISatSolver
    {
        /// <summary>Name shown to the user in the solver picker.</summary>
        string DisplayName { get; }

        /// <summary>
        /// True if this solver must run on Unity's main thread (e.g. the internal
        /// SLS solver mutates the <see cref="Problem"/> directly).  When true the UI
        /// calls <see cref="SolveAsync"/> and expects the returned task to already be
        /// complete (the work happens synchronously before the task is returned).
        /// External process solvers return false so they can run on a worker thread.
        /// </summary>
        bool RequiresMainThread { get; }

        /// <summary>
        /// True if this solver can actually be used on the current platform right now
        /// (for external solvers this means the executable exists).  Solvers that are
        /// unavailable are still listed but cannot be launched.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// If <see cref="IsAvailable"/> is false, a short explanation the UI can show.
        /// </summary>
        string UnavailableReason { get; }

        /// <summary>
        /// Attempt to solve the problem.
        /// For process based solvers the work runs on a worker thread and only reads the
        /// (immutable) problem structure; it never mutates <see cref="Problem.Solution"/>.
        /// The caller is responsible for applying the returned assignment on the main thread.
        /// </summary>
        Task<SatResult> SolveAsync(Problem problem, CancellationToken cancellationToken);
    }
}
