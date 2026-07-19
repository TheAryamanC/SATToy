namespace Assets.SAT.Solvers
{
    /// <summary>
    /// Static description of an external command-line SAT solver.
    /// One of these plus the current platform is enough for
    /// <see cref="ProcessSatSolver"/> to locate the binary, build its command line
    /// and interpret its output.
    /// </summary>
    public class SolverDescriptor
    {
        /// <summary>Name shown in the UI, e.g. "Kissat".</summary>
        public string DisplayName;

        /// <summary>
        /// Executable file name without extension and without platform folder,
        /// e.g. "kissat" or "minisat".  The concrete path is resolved at runtime to
        /// StreamingAssets/Solvers/&lt;platform&gt;/&lt;name&gt;[.exe].
        /// </summary>
        public string ExecutableBaseName;

        /// <summary>
        /// Command line arguments template.  The tokens "{cnf}" and "{out}" are replaced
        /// with the (quoted) paths of the input CNF file and the desired output file.
        /// Example (Kissat): "\"{cnf}\"".  Example (MiniSat): "\"{cnf}\" \"{out}\"".
        /// </summary>
        public string ArgumentsTemplate;

        /// <summary>Which convention the solver uses to report its answer.</summary>
        public SolverOutputFormat OutputFormat;

        /// <summary>
        /// True if the answer must be read from the "{out}" file (MiniSat).
        /// False if the answer is printed to stdout (Kissat and most competition solvers).
        /// </summary>
        public bool ReadResultFile;
    }
}
