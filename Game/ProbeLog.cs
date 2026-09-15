using System;
using System.Collections.Generic;
using System.IO;

namespace Reus2Archipelago.Game;

/// <summary>
/// The probe trace. Every line goes to the BepInEx log and, unbuffered, to
/// BepInEx/plugins/reus2_probe.log, because a buffered tail is lost when the game closes.
/// </summary>
internal static class ProbeLog
{
    private static readonly HashSet<string> Faulted = new HashSet<string>();
    private static string path;

    internal static void Open()
    {
        path = Path.Combine(BepInEx.Paths.PluginPath, "reus2_probe.log");
        Write($"--- probes installed {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---");
    }

    internal static void Write(string line)
    {
        Plugin.Log.LogInfo($"[probe] {line}");
        Append(line);
    }

    /// <summary>Adds a line the BepInEx log already holds to the probe log, so the two files carry
    /// the same trace.</summary>
    internal static void Append(string line)
    {
        if (path == null)
            return;
        try
        {
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  {line}\n");
        }
        catch
        {
            // The BepInEx log still has the line.
        }
    }

    /// <summary>A probe that throws on a per-frame path would repeat its exception thousands of
    /// times, so each target reports once and is silent afterwards.</summary>
    internal static void Fault(string target, Exception e)
    {
        if (!Faulted.Add(target))
            return;
        Write($"probe on {target} threw and reports nothing further: {e.Message}");
    }
}
