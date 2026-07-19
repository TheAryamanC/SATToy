using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.SAT.Solvers
{
    /// <summary>
    /// Wraps the built-in stochastic local search (WalkSAT-style) solver behind
    /// <see cref="ISatSolver"/> so it appears in the same picker as the external solvers.
    ///
    /// Because it mutates <see cref="Problem.Solution"/> directly it must run on the main
    /// thread: <see cref="SolveAsync"/> does its work synchronously and returns an already
    /// completed task.  Local search cannot prove unsatisfiability, so a failure to find a
    /// solution within the step cap is reported as <see cref="SatStatus.Unknown"/>.
    /// </summary>
    public class InternalSlsSolver : ISatSolver
    {
        /// <summary>Maximum number of flips before giving up.</summary>
        public int MaxSteps = 200000;

        public string DisplayName => "Internal (SLS)";

        public bool RequiresMainThread => true;

        public bool IsAvailable => true;

        public string UnavailableReason => null;

        public Task<SatResult> SolveAsync(Problem problem, CancellationToken cancellationToken)
        {
            var stopwatch = Stopwatch.StartNew();

            var steps = 0;
            while (!problem.IsSolved && steps < MaxSteps && !cancellationToken.IsCancellationRequested)
            {
                problem.StepOne();
                steps++;
            }

            SatResult result;
            if (problem.IsSolved)
            {
                var assignment = new Dictionary<Proposition, bool>(problem.PropositionCount);
                foreach (var p in problem.Propositions)
                    assignment[p] = problem.Solution[p];

                result = new SatResult
                {
                    Status = SatStatus.Satisfiable,
                    SolverName = DisplayName,
                    Assignment = assignment,
                    Seconds = stopwatch.Elapsed.TotalSeconds
                };
            }
            else
            {
                result = new SatResult
                {
                    Status = SatStatus.Unknown,
                    SolverName = DisplayName,
                    Error = $"No solution found after {steps} flips (local search cannot prove UNSAT).",
                    Seconds = stopwatch.Elapsed.TotalSeconds
                };
            }

            return Task.FromResult(result);
        }
    }
}
