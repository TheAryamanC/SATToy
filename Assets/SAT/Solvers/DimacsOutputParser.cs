using System.Collections.Generic;

namespace Assets.SAT.Solvers
{
    /// <summary>
    /// How a solver reports its answer.
    /// </summary>
    public enum SolverOutputFormat
    {
        /// <summary>
        /// SAT-competition text on stdout: comment lines start with 'c', a status line
        /// "s SATISFIABLE"/"s UNSATISFIABLE"/"s UNKNOWN", and value lines start with 'v'
        /// listing space separated signed integers terminated by 0.
        /// Used by Kissat, CaDiCaL, Glucose, CryptoMiniSat, ...
        /// </summary>
        DimacsCompetition,

        /// <summary>
        /// MiniSat result-file style: first token is "SAT" or "UNSAT"; if "SAT" the
        /// following signed integers (terminated by 0) are the model.
        /// </summary>
        MinisatResultFile
    }

    /// <summary>
    /// Parses solver output into a <see cref="SatResult"/>.
    /// </summary>
    public static class DimacsOutputParser
    {
        /// <summary>
        /// Parse solver text according to <paramref name="format"/>.
        /// </summary>
        /// <param name="text">stdout (competition) or result-file contents (minisat).</param>
        /// <param name="format">Which convention the text follows.</param>
        /// <param name="encoding">The encoding used, to map variables back to propositions.</param>
        /// <param name="solverName">Name for the produced result.</param>
        public static SatResult Parse(string text, SolverOutputFormat format, DimacsEncoding encoding, string solverName)
        {
            if (text == null)
                text = string.Empty;

            switch (format)
            {
                case SolverOutputFormat.MinisatResultFile:
                    return ParseMinisat(text, encoding, solverName);
                default:
                    return ParseCompetition(text, encoding, solverName);
            }
        }

        private static SatResult ParseCompetition(string text, DimacsEncoding encoding, string solverName)
        {
            bool sawSat = false;
            bool sawUnsat = false;
            var values = new Dictionary<int, bool>();

            using (var reader = new System.IO.StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                        continue;

                    var kind = line[0];
                    if (kind == 'c')
                        continue; // comment

                    if (kind == 's')
                    {
                        if (line.IndexOf("UNSATISFIABLE", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            sawUnsat = true;
                        else if (line.IndexOf("SATISFIABLE", System.StringComparison.OrdinalIgnoreCase) >= 0)
                            sawSat = true;
                        continue;
                    }

                    if (kind == 'v')
                    {
                        // Skip the leading 'v', then read signed integers.
                        CollectLiterals(line.Substring(1), values);
                    }
                }
            }

            if (sawUnsat)
                return new SatResult { Status = SatStatus.Unsatisfiable, SolverName = solverName, RawOutput = text };

            if (sawSat)
                return BuildSatResult(values, encoding, solverName, text);

            return new SatResult { Status = SatStatus.Unknown, SolverName = solverName, RawOutput = text };
        }

        private static SatResult ParseMinisat(string text, DimacsEncoding encoding, string solverName)
        {
            // First non-empty token decides SAT/UNSAT.
            var trimmed = text.TrimStart();
            if (trimmed.StartsWith("UNSAT", System.StringComparison.OrdinalIgnoreCase))
                return new SatResult { Status = SatStatus.Unsatisfiable, SolverName = solverName, RawOutput = text };

            if (trimmed.StartsWith("SAT", System.StringComparison.OrdinalIgnoreCase))
            {
                var values = new Dictionary<int, bool>();
                // Everything after the first line is the model.
                var newlineIndex = trimmed.IndexOf('\n');
                var body = newlineIndex >= 0 ? trimmed.Substring(newlineIndex + 1) : string.Empty;
                CollectLiterals(body, values);
                return BuildSatResult(values, encoding, solverName, text);
            }

            return new SatResult { Status = SatStatus.Unknown, SolverName = solverName, RawOutput = text };
        }

        /// <summary>
        /// Parse whitespace separated signed integers into a variable-&gt;bool map.
        /// A literal +v means v is true, -v means v is false.  The terminating 0 is ignored.
        /// </summary>
        private static void CollectLiterals(string segment, Dictionary<int, bool> values)
        {
            var tokens = segment.Split(new[] { ' ', '\t', '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (!int.TryParse(token, out var lit))
                    continue;
                if (lit == 0)
                    continue;
                var variable = lit > 0 ? lit : -lit;
                values[variable] = lit > 0;
            }
        }

        private static SatResult BuildSatResult(Dictionary<int, bool> values, DimacsEncoding encoding, string solverName, string rawOutput)
        {
            var assignment = new Dictionary<Proposition, bool>(encoding.VariableToProposition.Count);
            foreach (var pair in encoding.VariableToProposition)
            {
                // Default to false if the solver omitted this variable.
                values.TryGetValue(pair.Key, out var value);
                assignment[pair.Value] = value;
            }

            return new SatResult
            {
                Status = SatStatus.Satisfiable,
                SolverName = solverName,
                Assignment = assignment,
                RawOutput = rawOutput
            };
        }
    }
}
