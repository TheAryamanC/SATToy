SATToy external SAT solvers

This folder holds the command-line SAT solver executables that the "Solve" button in SATToy can run. At runtime the app looks for a binary at: StreamingAssets/Solvers/<platform>/<name>[.exe] where <platform> is one of: windows, osx, linux and <name> is the solver's ExecutableBaseName from SolverRegistry.cs (currently "minisat" and "kissat").

Licenses of the bundled binaries are in KISSAT-LICENSE.txt and MINISAT-LICENSE.txt.
