using System.IO;
using UnityEngine;
using Assets.SAT;
using System.Threading.Tasks;

public class SolutionEnumerator : MonoBehaviour
{
    [Tooltip("Relative to Application.streamingAssetsPath")]
    public string constraintsFileName = "constraints.txt";

    [Tooltip("Name of the output file to create in the same folder as the constraints file")]
    public string outputFileName = "solutions.txt";

    [Tooltip("If true, enumeration will start automatically on Start (may freeze editor if problem is large)")]
    public bool runOnStart = false;

    [ContextMenu("Enumerate Solutions (sync)")]
    public void EnumerateSync()
    {
        EnumerateInternal(false).Forget();
    }

    [ContextMenu("Enumerate Solutions (async)")]
    public void EnumerateAsync()
    {
        EnumerateInternal(true).Forget();
    }

    private async Task EnumerateInternal(bool useBackgroundThread)
    {
        string constraintsPath = Path.Combine(Application.streamingAssetsPath, constraintsFileName);
        if (!File.Exists(constraintsPath))
        {
            Debug.LogError($"Constraints file not found: {constraintsPath}");
            return;
        }

        // Put the output file in the same folder as the constraints file.
        // If GetDirectoryName returns null for any reason, fall back to Application.dataPath.
        string constraintsDir = Path.GetDirectoryName(constraintsPath);
        if (string.IsNullOrEmpty(constraintsDir))
            constraintsDir = Application.dataPath;

        string outPath = Path.Combine(constraintsDir, outputFileName);

        Debug.Log($"Starting enumeration from: {constraintsPath}\nWriting output to: {outPath}");

        try
        {
            if (useBackgroundThread)
            {
                await Task.Run(() =>
                {
                    var problem = new Problem(constraintsPath);
                    problem.EnumerateAllSolutions(outPath);
                });
            }
            else
            {
                var problem = new Problem(constraintsPath);
                problem.EnumerateAllSolutions(outPath);
            }

            Debug.Log($"Enumeration complete. Output at: {outPath}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Enumeration failed: {ex}");
        }
    }

    private void Start()
    {
        if (runOnStart)
            EnumerateAsync();
    }
}

static class TaskExtensions
{
    public static async void Forget(this Task task)
    {
        try { await task; } catch (System.Exception) { /* errors are logged in EnumerateInternal */ }
    }
}
