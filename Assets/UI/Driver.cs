using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Assets.SAT;
using Assets.SAT.Solvers;
using UnityEngine;

public class Driver : MonoBehaviour
{
    /// <summary>
    /// The Problem object we're trying to solve
    /// </summary>
    private Problem problem;

    /// <summary>
    /// True if the solver should be called to update on every frame.
    /// </summary>
    public bool Run;

    /// <summary>
    /// Number of steps the solver has run for so far.
    /// </summary>
    private int stepCount;

    /// <summary>
    /// Font information for screen display
    /// </summary>
    public GUIStyle GUIStyle;

    /// <summary>
    /// How much blank space to put at the edges of the window
    /// </summary>
    public int Border = 30;
    
    /// <summary>
    /// String buffer to store the noise level the user is typing in the UI
    /// </summary>
    private string noiseLevel = "10";

    /// <summary>
    /// String butter to store the name of the file in the UI
    /// </summary>
    private string fileName = "Test.txt";

    /// <summary>
    /// The list of SAT solvers the user can choose from (built once in Start).
    /// </summary>
    private ISatSolver[] solvers;

    /// <summary>
    /// Display names of <see cref="solvers"/>, shown in the selection dropdown.
    /// </summary>
    private string[] solverNames;

    /// <summary>
    /// Index into <see cref="solvers"/> of the currently selected solver.
    /// </summary>
    private int selectedSolver;

    /// <summary>
    /// Whether the solver dropdown list is currently expanded.
    /// </summary>
    private bool solverDropdownOpen;

    /// <summary>
    /// Area-relative rectangle of the dropdown header button, captured during layout so the
    /// popup list can be drawn as an overlay at the end of OnGUI.
    /// </summary>
    private Rect solverDropdownRect;

    /// <summary>
    /// The in-flight solve for an external (worker-thread) solver, or null when idle.
    /// </summary>
    private Task<SatResult> solveTask;

    /// <summary>
    /// Used to cancel a running external solve (e.g. when the problem is reloaded).
    /// </summary>
    private CancellationTokenSource solveCts;

    /// <summary>
    /// Human readable status of the most recent solve, shown in the UI.
    /// </summary>
    private string solverStatus = "";

    /// <summary>
    /// Called by Unity at the start of the program
    /// </summary>
    public void Start()
    {
        solvers = SolverRegistry.BuildSolvers().ToArray();
        solverNames = new string[solvers.Length];
        for (var i = 0; i < solvers.Length; i++)
            solverNames[i] = solvers[i].DisplayName;

        LoadProblem();
    }

    /// <summary>
    /// Load/reload problem from disk
    /// </summary>
    private void LoadProblem()
    {
        CancelSolve();
        solverStatus = "";
        problem = new Problem(Path.Combine(Path.Combine(Application.dataPath, "Problems"), fileName));
        stepCount = 0;
        Run = false;
    }

    /// <summary>
    /// Run solver for one step and update GUI.
    /// </summary>
    private bool Step()
    {
        stepCount++;
        var success = problem.StepOne();
        problem.CheckConsistency();
        return success;
    }

    /// <summary>
    /// True while an external solver is running on a worker thread.
    /// </summary>
    private bool IsSolving => solveTask != null && !solveTask.IsCompleted;

    /// <summary>
    /// Launch the currently selected solver.
    /// Main-thread solvers (the internal SLS) run synchronously; external solvers run on a worker thread and are polled for completion in OnGUI.
    /// </summary>
    private void StartSolve()
    {
        if (problem == null || IsSolving)
            return;

        var solver = solvers[selectedSolver];
        if (!solver.IsAvailable)
        {
            solverStatus = $"{solver.DisplayName}: unavailable — {solver.UnavailableReason}";
            return;
        }

        CancelSolve();
        solveCts = new CancellationTokenSource();

        if (solver.RequiresMainThread)
        {
            // Completes synchronously; safe to read the result immediately.
            var task = solver.SolveAsync(problem, solveCts.Token);
            ApplyResult(task.GetAwaiter().GetResult());
        }
        else
        {
            solverStatus = $"{solver.DisplayName}: solving…";
            solveTask = solver.SolveAsync(problem, solveCts.Token);
        }
    }

    /// <summary>
    /// Apply a finished solve result to the problem (on the main thread) and update status.
    /// </summary>
    private void ApplyResult(SatResult r)
    {
        switch (r.Status)
        {
            case SatStatus.Satisfiable:
                problem.ApplyAssignment(r.Assignment);
                stepCount = 0;
                solverStatus = problem.IsSolved
                    ? $"{r.SolverName}: SATISFIABLE (verified) in {r.Seconds:0.000}s"
                    : $"{r.SolverName}: reported SAT but the assignment does NOT satisfy the constraints — encoding bug!";
                break;
            case SatStatus.Unsatisfiable:
                solverStatus = $"{r.SolverName}: UNSATISFIABLE (no solution exists) in {r.Seconds:0.000}s";
                break;
            case SatStatus.Unknown:
                solverStatus = $"{r.SolverName}: unknown — {r.Error}";
                break;
            case SatStatus.Error:
                solverStatus = $"{r.SolverName}: error — {r.Error}";
                break;
        }
    }

    /// <summary>
    /// Cancel and forget any in-flight external solve.
    /// </summary>
    private void CancelSolve()
    {
        if (solveCts != null)
        {
            try { solveCts.Cancel(); } catch { /* ignore */ }
            solveCts.Dispose();
            solveCts = null;
        }
        solveTask = null;
    }

    /// <summary>
    /// Make sure no external solver process outlives the app.
    /// </summary>
    private void OnDestroy()
    {
        CancelSolve();
    }

    /// <summary>
    /// Poll a running external solve and apply its result once it finishes.
    /// Called once per frame during the Repaint event to keep GUILayout stable.
    /// </summary>
    private void PollSolve()
    {
        if (solveTask == null || !solveTask.IsCompleted)
            return;

        var finished = solveTask;
        solveTask = null;

        if (finished.Status == TaskStatus.RanToCompletion)
            ApplyResult(finished.Result);
        else if (finished.IsCanceled)
            solverStatus = "Solve cancelled.";
        else
            solverStatus = "Solver crashed: " + finished.Exception?.GetBaseException().Message;
    }

    /// <summary>
    /// True if the space bar was just pressed.
    /// </summary>
    private bool SpacePressed => Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Space;

    /// <summary>
    /// Draws the expanded solver dropdown list as an overlay (absolute positioning, not
    /// GUILayout) so it floats over the UI instead of pushing other controls around.
    /// Selecting an item, or clicking anywhere outside the list, closes it.
    /// </summary>
    private void DrawSolverDropdown()
    {
        if (!solverDropdownOpen || solverNames == null || solverNames.Length == 0)
            return;

        var itemHeight = solverDropdownRect.height > 1f ? solverDropdownRect.height : 24f;
        var x = solverDropdownRect.x;
        var y = solverDropdownRect.y + itemHeight;

        // Size the list to the widest option so long names do not clip.
        var width = solverDropdownRect.width;
        foreach (var name in solverNames)
            width = Mathf.Max(width, GUIStyle.CalcSize(new GUIContent(name)).x + 16f);

        var listRect = new Rect(x, y, width, itemHeight * solverNames.Length);
        GUI.Box(listRect, GUIContent.none);

        for (var i = 0; i < solverNames.Length; i++)
        {
            var itemRect = new Rect(x, y + itemHeight * i, width, itemHeight);
            if (GUI.Button(itemRect, solverNames[i], GUIStyle))
            {
                selectedSolver = i;
                solverDropdownOpen = false;
            }
        }

        // Click outside the list (and not on the header button) closes the dropdown.
        if (Event.current.type == EventType.MouseDown
            && !listRect.Contains(Event.current.mousePosition)
            && !solverDropdownRect.Contains(Event.current.mousePosition))
            solverDropdownOpen = false;
    }

    /// <summary>
    /// Called regularly by Unity to redraw the GUI and handle user input.
    /// </summary>
    public void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Border, Border, Screen.width - 2 * Border, Screen.height - 2 * Border));

        // BUTTONS and TEXT BOXES
        GUILayout.BeginHorizontal();
        var runPressed = GUILayout.Button(Run ? "Stop" : "Run", GUIStyle);
        if (runPressed && !Run && problem.IsSolved)
            LoadProblem();
        Run ^= runPressed;
        GUILayout.Space(30);
        if ((GUILayout.Button("Step", GUIStyle) || SpacePressed) && !problem.IsSolved)
            Step();
        GUILayout.Space(30);
        if (GUILayout.Button("Reset", GUIStyle))
            LoadProblem();
        GUILayout.Space(20);
        GUILayout.Label("Noise level: ", GUIStyle);
        noiseLevel = GUILayout.TextField(noiseLevel, GUIStyle);
        if (problem != null && int.TryParse(noiseLevel, out var n))
            problem.NoiseLevel = n;
        GUILayout.Space(20);
        GUILayout.Label("File: ", GUIStyle);
        fileName = GUILayout.TextField(fileName, GUIStyle);
        GUILayout.EndHorizontal();

        GUILayout.Space(20);

        // SOLVER SELECTION (dropdown)
        GUILayout.BeginHorizontal();
        GUILayout.Label("Solver: ", GUIStyle);
        var selectedName = (solverNames != null && selectedSolver >= 0 && selectedSolver < solverNames.Length)
            ? solverNames[selectedSolver]
            : "(none)";
        if (GUILayout.Button(selectedName + "  \u25BC", GUIStyle))
            solverDropdownOpen = !solverDropdownOpen;
        // Remember where the header button is so the popup list can be drawn over the UI below.
        if (Event.current.type != EventType.Layout)
            solverDropdownRect = GUILayoutUtility.GetLastRect();
        GUILayout.Space(20);
        GUI.enabled = problem != null && !IsSolving;
        if (GUILayout.Button("Solve", GUIStyle))
            StartSolve();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        // Always drawn (possibly empty) to keep the GUILayout structure stable.
        GUILayout.Label(solverStatus, GUIStyle);

        GUILayout.Space(20);

        // PRINT THE CLAUSES 
        GUILayout.Label("Clauses:", GUIStyle);
        var solved = true;
        if (problem != null)
            foreach (var clause in problem.Constraints)
            {
                var clauseSolved = problem.Satisfied(clause);
                GUILayout.Label(clauseSolved ? $"<color=green>{clause}</color>" : $"<color=red>{clause}</color>",
                    GUIStyle);
                solved &= clauseSolved;
            }

        var maybeSolved = solved ? "<b>solved</b> in " : "";
        GUILayout.Label($"{maybeSolved}{stepCount} steps", GUIStyle);

        GUILayout.Space(20);

        // PRINT THE TRUTH ASSIGNMENT
        GUILayout.Label("Truth assignment", GUIStyle);
        GUILayout.BeginHorizontal();
        if (problem != null)
            foreach (var p in problem.Propositions)
            {
                GUILayout.Label(problem.Solution[p] ? p.Name : $"<color=grey>{p.Name}</color>", GUIStyle);
                GUILayout.Space(20);
            }

        GUILayout.EndHorizontal();

        // Draw the expanded solver dropdown last so it overlays the controls below it.
        DrawSolverDropdown();

        GUILayout.EndArea();

        // Poll async solvers and advance Run mode once per frame.
        if (Event.current.type == EventType.Repaint)
        {
            PollSolve();
            if (Run && !IsSolving)
                Run = !Step();
        }
    }
}
