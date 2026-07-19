using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Assets.SAT
{
    /// <summary>
    /// Represents a SAT problem
    /// </summary>
    public class Problem
    {
        /// <summary>
        /// Make a problem from the constraints specified in the file
        /// The format of the file is:
        /// - 1 line per constraint
        /// - ! means not, | means or
        /// - proposition names can be anything not including those characters
        /// </summary>
        /// <param name="path">Path to the file to load</param>
        public Problem(string path)
        {
            Constraints.AddRange(File.ReadAllLines(path).Where(s => !s.StartsWith("#")).Select(constraintExp => Constraint.FromExpression(constraintExp, this)));
            TrueLiteralCounts = new int[Constraints.Count];

            Solution = new TruthAssignment(this);

            for (var i = 0; i < Constraints.Count; i++)
                TrueLiteralCounts[i] = Solution.TrueLiteralCount(Constraints[i]);

            UnsatisfiedConstraints.AddRange(Constraints.Where(Unsatisfied));
        }

        /// <summary>
        /// The truth assignment we are trying to make into a solution.
        /// This starts completely random is then is gradually changed into
        /// a solution by "flipping" the values of specific propositions
        /// </summary>
        public TruthAssignment Solution;

        /// <summary>
        /// Percent of the time we do a random walk step rather than a greedy one.
        /// 0   = pure greedy
        /// 100 = pure random walk
        /// </summary>
        public int NoiseLevel = 10;

        #region Proposition information
        /// <summary>
        /// The Proposition object within this problem with the specified name.
        /// Creates a new proposition object if necessary.
        /// </summary>
        public Proposition this[string name]
        {
            get
            {
                if (propositionTable.TryGetValue(name, out var result))
                    return result;
                return propositionTable[name] = new Proposition(name, propositionTable.Count);
            }
        }

        /// <summary>
        /// Hash table mapping names (string) to the Proposition objects with that name
        /// </summary>
        private readonly Dictionary<string, Proposition> propositionTable = new Dictionary<string, Proposition>();

        /// <summary>
        /// Enumeration of all the propositions in the problem
        /// </summary>
        public IEnumerable<Proposition> Propositions => propositionTable.Select(pair => pair.Value);

        /// <summary>
        /// Total number of propositions in the problem.
        /// Note this is the number of propositions, not the number of disjuncts in constraints.
        /// If a Proposition appears in 3 constraints, it's only counted once here.
        /// </summary>
        public int PropositionCount => propositionTable.Count;

        /// <summary>
        /// True if the current value of Solution is in fact a solution.
        /// If it's false, then we need to work on it some more.
        /// </summary>
        public bool IsSolved => UnsatisfiedConstraints.Count == 0;
        #endregion

        #region constraint information
        /// <summary>
        /// Constraints in the SAT problem.
        /// </summary>
        public readonly List<Constraint> Constraints = new List<Constraint>();

        /// <summary>
        /// List of constraints whose number of literals is not in its required range,
        /// i.e. MinTrueLiterals-MaxTrueLiterals.  The solver needs to get the number
        /// of true literals for each constraint into the right range.
        /// </summary>
        public readonly List<Constraint> UnsatisfiedConstraints = new List<Constraint>();

        /// <summary>
        /// Number of literals in each constraint that are true, indexed by the Index field of the constraint.
        /// So to find out how many literals are true in c, look at TrueLiteralCounts[c.Index].
        /// </summary>
        public int[] TrueLiteralCounts;

        /// <summary>
        /// Number of literals that are true within the constraint given the current TruthAssignment
        /// </summary>
        /// <param name="c"></param>
        /// <returns></returns>
        public int CurrentTrueLiterals(Constraint c) => TrueLiteralCounts[c.Index];

        /// <summary>
        /// True if the specified constraint is currently satisfied
        /// (i.e. if it's true in the current truth assignment)
        /// </summary>
        public bool Satisfied(Constraint c) => CurrentTrueLiterals(c) >= c.MinTrueLiterals && CurrentTrueLiterals(c) <= c.MaxTrueLiterals;

        /// <summary>
        /// True if the specified constraint is currently unsatisfied
        /// (i.e. false in the current truth assignment).
        /// </summary>
        public bool Unsatisfied(Constraint c) => !Satisfied(c);

        /// <summary>
        /// Checks that the TrueLiteralCounts array and UnsatisfiedConstraints list are correct.
        /// Use this to look for bugs in your implementation of Flip.
        /// </summary>
        public void CheckConsistency()
        {
            for (var i = 0; i < Constraints.Count; i++)
                if (TrueLiteralCounts[i] != Solution.TrueLiteralCount(Constraints[i]))
                    throw new Exception($"True literal count incorrect for constraint {i}");
            foreach (var c in Constraints)
            {
                var present = UnsatisfiedConstraints.IndexOf(c) >= 0;
                if (Satisfied(c))
                {
                    if (present)
                        throw new Exception($"constraint \"{c}\" appears in UnsatisfiedConstraints but is satisfied.  Last flip was {lastFlip}");
                }
                else if (!present)
                    throw new Exception($"constraint \"{c}\" is unsatisfied but does not appear in the UnsatisfiedConstraints list.  Last flip was {lastFlip}");
            }
        }
        #endregion

        #region Solver
        /// <summary>
        /// Pick one variable to flip and flip it by calling Flip, below.
        /// </summary>
        /// <returns>True if all constraints are satisfied.</returns>
        public bool StepOne()
        {
            // If already satisfied, we’re done.
            if (UnsatisfiedConstraints.Count == 0)
                return true;

            // Pick a random unsatisfied constraint
            var randomConstraint = UnsatisfiedConstraints.RandomElement();
            var randomLiteral = randomConstraint.RandomLiteral;

            // With probability NoiseLevel: pure random walk
            if (Random.Percent(NoiseLevel))
            {
                Flip(randomLiteral);
            }
            else
            {
                // Greedy: find the literal whose flip gives the best Δ
                Literal bestLiteral = null;
                int bestDelta = int.MinValue;
                foreach (var lit in randomConstraint.Literals)
                {
                    int delta = SatisfiedConstraintDelta(lit.Proposition);
                    if (delta > bestDelta)
                    {
                        bestDelta = delta;
                        bestLiteral = lit;
                    }
                }

                // If flipping bestLiteral improves (# newly satisfied – # newly broken) > 0, do it.
                // Otherwise fall back to a random walk on this clause.
                if (bestDelta > 1)
                    Flip(bestLiteral);
                else
                    Flip(randomLiteral);
            }

            return UnsatisfiedConstraints.Count == 0;
        }

        private Literal lastFlip;
        /// <summary>
        /// Flip the value of the specified literal.
        /// Call Solution.Flip to do the actual flipping.  But make sure to update
        /// TrueLiteralCounts and UnsatisfiedConstraints, accordingly
        /// </summary>
        void Flip(Literal l)
        {
            lastFlip = l;
            var p = l.Proposition;
            bool oldValue = Solution[p];
            // Finally flip the assignment
            Solution.Flip(p);

            // Update all constraints where p appears positively
            foreach (var c in p.PositiveConstraints)
            {
                bool before = Satisfied(c);
                TrueLiteralCounts[c.Index] += oldValue ? -1 : +1;
                bool after = Satisfied(c);
                if (before != after)
                {
                    if (before) UnsatisfiedConstraints.Add(c);
                    else UnsatisfiedConstraints.Remove(c);
                }
            }

            // Update all constraints where p appears negated
            foreach (var c in p.NegativeConstraints)
            {
                bool before = Satisfied(c);
                // flipping p from old→!old makes ¬p go from (old? false→true) or (old? true→false)
                TrueLiteralCounts[c.Index] += oldValue ? +1 : -1;
                bool after = Satisfied(c);
                if (before != after)
                {
                    if (before) UnsatisfiedConstraints.Add(c);
                    else UnsatisfiedConstraints.Remove(c);
                }
            }
        }

        /// <summary>
        /// Return the net increase or decrease in satisfied constraints if we were to flip this proposition
        /// </summary>
        int SatisfiedConstraintDelta(Proposition p)
        {
            bool oldValue = Solution[p];
            int delta = 0;

            // Positive occurrences
            foreach (var c in p.PositiveConstraints)
            {
                int curr = CurrentTrueLiterals(c);
                int next = curr + (oldValue ? -1 : +1);
                bool wasSat = (curr >= c.MinTrueLiterals && curr <= c.MaxTrueLiterals);
                bool nowSat = (next >= c.MinTrueLiterals && next <= c.MaxTrueLiterals);
                if (!wasSat && nowSat) delta++;
                else if (wasSat && !nowSat) delta--;
            }

            // Negative occurrences
            foreach (var c in p.NegativeConstraints)
            {
                int curr = CurrentTrueLiterals(c);
                // ¬p is true exactly when p is false, so flipping p toggles its contribution
                int next = curr + (oldValue ? +1 : -1);
                bool wasSat = (curr >= c.MinTrueLiterals && curr <= c.MaxTrueLiterals);
                bool nowSat = (next >= c.MinTrueLiterals && next <= c.MaxTrueLiterals);
                if (!wasSat && nowSat) delta++;
                else if (wasSat && !nowSat) delta--;
            }

            return delta;
        }

        /// <summary>
        /// Brute-force enumeration of all possible assignments. Writes every satisfying
        /// assignment (i.e. solution) as one line in the specified text file.
        /// Format: "Solution #k: Name1=1 Name2=0 Name3=1" (1=true, 0=false)
        /// Throws an exception if there are too many variables to enumerate with 64-bit masks.
        /// </summary>
        /// <param name="outPath">Path to output .txt file (will be overwritten)</param>
        public void EnumerateAllSolutions(string outPath)
        {
            var n = PropositionCount;
            if (n == 0)
                throw new Exception("No propositions in problem.");

            if (n >= 64)
                throw new Exception("Brute-force enumeration requires fewer than 64 propositions. Use a different approach for n >= 64.");

            // Order propositions by Index to have stable, predictable ordering in output
            var props = Propositions.OrderBy(p => p.Index).ToArray();

            ulong total = 1UL << n; // safe because we checked n < 64

            using (var sw = new StreamWriter(outPath, false, System.Text.Encoding.UTF8))
            {
                sw.WriteLine($"# Problem has {n} propositions. Enumerating {total} assignments.");
                sw.WriteLine($"# Output lines: each satisfying assignment listed as: Name=1 (true) Name=0 (false)");
                ulong solutionCount = 0;
                for (ulong mask = 0; mask < total; mask++)
                {
                    // Create a deterministic assignment (all false) and set according to mask
                    Solution = new TruthAssignment(this, false);

                    // Set each proposition according to mask bit
                    foreach (var p in props)
                    {
                        bool val = (((mask >> p.Index) & 1UL) == 1UL);
                        Solution[p] = val;
                    }

                    // Recompute TrueLiteralCounts and UnsatisfiedConstraints for this assignment
                    for (int i = 0; i < Constraints.Count; i++)
                        TrueLiteralCounts[i] = Solution.TrueLiteralCount(Constraints[i]);

                    UnsatisfiedConstraints.Clear();
                    foreach (var c in Constraints)
                        if (Unsatisfied(c))
                            UnsatisfiedConstraints.Add(c);

                    // If satisfied, write it
                    if (UnsatisfiedConstraints.Count == 0)
                    {
                        solutionCount++;
                        // Build a compact printable representation
                        var parts = props.Select(p => $"{p.Name}={(Solution[p] ? "1" : "0")}");
                        sw.WriteLine($"Solution #{solutionCount}: {string.Join(" ", parts)}");
                    }
                }

                sw.WriteLine($"# Done. Found {solutionCount} satisfying assignment(s).");
            }
        }

        /// <summary>
        /// Overwrite the current <see cref="Solution"/> with an externally computed assignment
        /// (e.g. produced by MiniSat or Kissat) and rebuild the derived tables
        /// (<see cref="TrueLiteralCounts"/> and <see cref="UnsatisfiedConstraints"/>).
        /// Propositions absent from the dictionary default to false.
        /// After calling this, inspect <see cref="IsSolved"/> to verify the assignment really
        /// satisfies the original constraints (this is the safety net that catches any bug in
        /// the CNF/cardinality encoding).
        /// </summary>
        public void ApplyAssignment(IDictionary<Proposition, bool> assignment)
        {
            Solution = new TruthAssignment(this, false);
            foreach (var p in Propositions)
                if (assignment != null && assignment.TryGetValue(p, out var value))
                    Solution[p] = value;

            for (var i = 0; i < Constraints.Count; i++)
                TrueLiteralCounts[i] = Solution.TrueLiteralCount(Constraints[i]);

            UnsatisfiedConstraints.Clear();
            foreach (var c in Constraints)
                if (Unsatisfied(c))
                    UnsatisfiedConstraints.Add(c);
        }

        #endregion
    }
}