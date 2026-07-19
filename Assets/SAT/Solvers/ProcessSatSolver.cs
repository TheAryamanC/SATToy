using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Assets.SAT.Solvers
{
    /// <summary>
    /// Runs an external command-line SAT solver (MiniSat, Kissat, ...) as a child process.
    /// The problem is encoded to DIMACS CNF, written to a temp file, handed to the solver,
    /// and the solver's output is parsed back into a <see cref="SatResult"/>.
    ///
    /// All Unity API values (paths, platform) are captured in the constructor, which runs on
    /// the main thread, so the actual solve can safely run on a worker thread.
    /// Only works on desktop standalone / editor where child processes are available.
    /// </summary>
    public class ProcessSatSolver : ISatSolver
    {
        private readonly SolverDescriptor descriptor;
        private readonly string streamingAssetsPath;
        private readonly string tempDirectory;
        private readonly string executablePath;   // null if the platform is unsupported
        private readonly bool platformSupportsProcesses;
        private readonly bool needsChmod;
        private bool executableEnsured;

        /// <summary>Maximum time to let the solver run before it is killed, in milliseconds.</summary>
        public int TimeoutMilliseconds = 30000;

        public ProcessSatSolver(SolverDescriptor descriptor)
        {
            this.descriptor = descriptor;

            // Capture Unity values here (main thread) so the worker thread never touches the Unity API.
            streamingAssetsPath = UnityEngine.Application.streamingAssetsPath;
            tempDirectory = UnityEngine.Application.temporaryCachePath;

            var platform = UnityEngine.Application.platform;
            string folder = null;
            string extension = null;
            needsChmod = false;

            switch (platform)
            {
                case UnityEngine.RuntimePlatform.WindowsPlayer:
                case UnityEngine.RuntimePlatform.WindowsEditor:
                    folder = "windows";
                    extension = ".exe";
                    break;
                case UnityEngine.RuntimePlatform.OSXPlayer:
                case UnityEngine.RuntimePlatform.OSXEditor:
                    folder = "osx";
                    extension = string.Empty;
                    needsChmod = true;
                    break;
                case UnityEngine.RuntimePlatform.LinuxPlayer:
                case UnityEngine.RuntimePlatform.LinuxEditor:
                    folder = "linux";
                    extension = string.Empty;
                    needsChmod = true;
                    break;
            }

            platformSupportsProcesses = folder != null;
            if (platformSupportsProcesses)
                executablePath = Path.Combine(streamingAssetsPath, "Solvers", folder, descriptor.ExecutableBaseName + extension);
        }

        public string DisplayName => descriptor.DisplayName;

        public bool RequiresMainThread => false;

        public bool IsAvailable => platformSupportsProcesses && executablePath != null && File.Exists(executablePath);

        public string UnavailableReason
        {
            get
            {
                if (!platformSupportsProcesses)
                    return "External solvers are only supported on desktop standalone/editor.";
                if (executablePath == null || !File.Exists(executablePath))
                    return $"Executable not found at: {executablePath}";
                return null;
            }
        }

        public Task<SatResult> SolveAsync(Problem problem, CancellationToken cancellationToken)
        {
            // Encode on the calling (main) thread while the problem structure is guaranteed stable,
            // then run the external process on a worker thread.
            DimacsEncoding encoding;
            try
            {
                encoding = DimacsEncoder.Encode(problem);
            }
            catch (Exception e)
            {
                return Task.FromResult(SatResult.FromError(DisplayName, "CNF encoding failed: " + e.Message));
            }

            return Task.Run(() => SolveBlocking(encoding, cancellationToken), cancellationToken);
        }

        private SatResult SolveBlocking(DimacsEncoding encoding, CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();

            if (!IsAvailable)
                return SatResult.FromError(DisplayName, UnavailableReason);

            var unique = Guid.NewGuid().ToString("N");
            var cnfPath = Path.Combine(tempDirectory, "sattoy_" + unique + ".cnf");
            var outPath = Path.Combine(tempDirectory, "sattoy_" + unique + ".out");

            try
            {
                File.WriteAllText(cnfPath, encoding.Cnf);
                EnsureExecutable();

                var arguments = descriptor.ArgumentsTemplate
                    .Replace("{cnf}", cnfPath)
                    .Replace("{out}", outPath);

                var startInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = tempDirectory
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    using (ct.Register(() => TryKill(process)))
                    {
                        // Read both streams concurrently to avoid pipe-buffer deadlocks.
                        var stdoutTask = process.StandardOutput.ReadToEndAsync();
                        var stderrTask = process.StandardError.ReadToEndAsync();

                        if (!process.WaitForExit(TimeoutMilliseconds))
                        {
                            TryKill(process);
                            return new SatResult
                            {
                                Status = SatStatus.Unknown,
                                SolverName = DisplayName,
                                Error = $"Timed out after {TimeoutMilliseconds} ms",
                                Seconds = stopwatch.Elapsed.TotalSeconds
                            };
                        }

                        // Process has exited; the pipes are closed so these complete promptly.
                        var stdout = stdoutTask.GetAwaiter().GetResult();
                        var stderr = stderrTask.GetAwaiter().GetResult();
                        var exitCode = process.ExitCode;

                        string outputText;
                        if (descriptor.ReadResultFile)
                            outputText = File.Exists(outPath) ? File.ReadAllText(outPath) : string.Empty;
                        else
                            outputText = stdout;

                        var result = DimacsOutputParser.Parse(outputText, descriptor.OutputFormat, encoding, DisplayName);
                        result.Seconds = stopwatch.Elapsed.TotalSeconds;

                        // Use the conventional exit codes (10 = SAT, 20 = UNSAT) as a fallback
                        // only when text parsing was inconclusive.
                        if (result.Status == SatStatus.Unknown && exitCode == 20)
                            result.Status = SatStatus.Unsatisfiable;

                        if (result.Status == SatStatus.Unknown && !string.IsNullOrEmpty(stderr))
                            result.Error = stderr.Trim();

                        return result;
                    }
                }
            }
            catch (Exception e)
            {
                return new SatResult
                {
                    Status = SatStatus.Error,
                    SolverName = DisplayName,
                    Error = e.Message,
                    Seconds = stopwatch.Elapsed.TotalSeconds
                };
            }
            finally
            {
                TryDelete(cnfPath);
                TryDelete(outPath);
            }
        }

        /// <summary>
        /// On macOS/Linux, Unity strips the executable bit from files under StreamingAssets,
        /// so restore it once before the first run.
        /// </summary>
        private void EnsureExecutable()
        {
            if (!needsChmod || executableEnsured)
                return;

            executableEnsured = true;
            try
            {
                using (var chmod = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/chmod",
                        Arguments = "+x \"" + executablePath + "\"",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                })
                {
                    chmod.Start();
                    chmod.WaitForExit(5000);
                }
            }
            catch
            {
                // Best effort; if this fails the solver run will report a clear error.
            }
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
                // Ignore: process may already have exited.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Temp files; ignore cleanup failures.
            }
        }
    }
}
