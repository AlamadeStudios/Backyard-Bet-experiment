using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using System.Threading;
using UnityEngine;

/// <summary>
/// One-shot headless installer, run via -executeMethod from the command line.
/// Client.Add with no version pin lets Package Manager resolve the current
/// compatible release itself, instead of us guessing a version string that
/// might not exist and breaking manifest.json resolution.
/// </summary>
public static class PackageInstaller
{
    public static void InstallNetcode()
    {
        AddAndWait("com.unity.netcode.gameobjects");
        AddAndWait("com.unity.services.multiplayer");
        Debug.Log("### PACKAGES DONE");
    }

    /// Multiplayer Play Mode: несколько виртуальных игроков в одном редакторе,
    /// замена ParrelSync - без него сеть придётся тестировать сборками.
    public static void InstallPlayMode()
    {
        AddAndWait("com.unity.multiplayer.playmode");
        Debug.Log("### PLAYMODE DONE");
    }

    static void AddAndWait(string id)
    {
        Debug.Log("### ADDING " + id);
        AddRequest req = Client.Add(id);
        int waited = 0;
        while (!req.IsCompleted && waited < 180)
        {
            Thread.Sleep(1000);
            waited++;
        }
        if (req.Status == StatusCode.Success)
            Debug.Log("### OK " + id + " -> " + req.Result.version);
        else
            Debug.LogError("### FAIL " + id + " -> " + req.Error?.message);
    }
}
