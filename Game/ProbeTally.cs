using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Reus2Archipelago.Game;

/// <summary>
/// One row per patched method: how many times the detour ran, when it last ran, and what it saw.
/// A target that is patched and never reached prints as "never fired", which is what separates a
/// hook Mono inlined away from a hook that ran and found nothing.
/// </summary>
internal static class ProbeTally
{
    /// <summary>Mono's inliner takes methods whose IL body is under this many bytes, which is why a
    /// detour on a two-statement method can never run.</summary>
    private const int MonoInlineLimit = 32;

    /// <summary>How many calls a quiet target writes a line for. Rule activity is recalculated every
    /// frame, so the rest of its calls only raise the count.</summary>
    private const int QuietLines = 3;

    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static
                                          | BindingFlags.Public | BindingFlags.NonPublic
                                          | BindingFlags.DeclaredOnly;

    private sealed class Entry
    {
        internal string Shape;
        internal long Count;
        internal string LastDetail;
        internal DateTime LastAt;
        internal int Lines;
        internal readonly HashSet<string> Patches = new HashSet<string>();
    }

    private static readonly SortedDictionary<string, Entry> Targets =
        new SortedDictionary<string, Entry>(StringComparer.Ordinal);

    private static readonly Dictionary<Assembly, Type[]> TypeCache = new Dictionary<Assembly, Type[]>();

    /// <summary>Set from Plugin.Awake: with Diagnostics.EnableProbes off nothing here records or
    /// writes anything.</summary>
    internal static bool Enabled { get; set; }

    internal static string Key(MethodBase method)
        => method?.DeclaringType == null ? "<unknown>" : $"{method.DeclaringType.Name}.{method.Name}";

    /// <summary>One row per patched method per patching class, so a check patch and an observing
    /// probe on the same method are counted apart.</summary>
    private static string Row(MethodBase method, Type owner) => $"{Key(method)} [{owner.Name}]";

    /// <summary>Takes a target into the tally. False when this same patch is already on the method,
    /// which happens when two type names resolve to one inherited method; the caller skips it. A
    /// patch from another class is kept, so one method can carry several detours.</summary>
    internal static bool Register(MethodBase method, Type owner, string patchId)
    {
        if (!Enabled)
            return true;
        var row = Row(method, owner);
        if (!Targets.TryGetValue(row, out var entry))
            Targets[row] = entry = new Entry { Shape = Shape(method) };
        return entry.Patches.Add(patchId);
    }

    /// <summary>Counts the call and writes the detail. A detail that throws is reported once and
    /// replaced, so the call still raises the count: a probe that reads a member the game no longer
    /// has must not read as a probe that never ran.</summary>
    internal static void Record(Type owner, MethodBase original, Func<string> detail, bool quiet = false)
    {
        if (!Enabled)
            return;
        string line;
        try
        {
            line = detail();
        }
        catch (Exception e)
        {
            ProbeLog.Fault(Key(original), e);
            line = "ran, detail unreadable";
        }
        Fired(owner, original, line, quiet);
    }

    private static void Fired(Type owner, MethodBase original, string detail, bool quiet = false)
    {
        if (!Enabled)
            return;
        var key = Row(original, owner);
        if (!Targets.TryGetValue(key, out var entry))
            Targets[key] = entry = new Entry { Shape = Shape(original) };

        entry.Count++;
        entry.LastAt = DateTime.Now;
        entry.LastDetail = detail;
        if (quiet && entry.Lines >= QuietLines)
            return;

        entry.Lines++;
        var closing = quiet && entry.Lines == QuietLines ? "  (from here this target is counted, not written)" : "";
        ProbeLog.Write($"{key} #{entry.Count}{(detail == null ? "" : "  " + detail)}{closing}");
    }

    internal static void Dump(string reason)
    {
        if (!Enabled)
            return;

        var silent = Targets.Where(t => t.Value.Count == 0).ToList();
        var fired = Targets.Where(t => t.Value.Count > 0).ToList();
        ProbeLog.Write($"=== probe tally ({reason}): {fired.Count} of {Targets.Count} targets have fired, "
                       + $"{silent.Count} never ===");
        foreach (var target in silent)
            ProbeLog.Write($"  never fired            {target.Key}  [{target.Value.Shape}]");
        foreach (var target in fired)
            ProbeLog.Write($"  fired {target.Value.Count,10}  {target.Key}"
                           + $"  last {target.Value.LastAt:HH:mm:ss}  {target.Value.LastDetail}");
        ProbeLog.Write("=== end of tally ===");
    }

    private static string Shape(MethodBase method)
    {
        var size = ILSize(method);
        var risk = size >= 0 && size < MonoInlineLimit
            ? $", under the {MonoInlineLimit} bytes at which Mono copies a call into its caller, so a detour here may never run"
            : "";
        return $"IL {(size < 0 ? "unreadable" : size + " bytes")}{risk}, {Kind(method)}, {Overrides(method)} overrides";
    }

    private static int ILSize(MethodBase method)
    {
        try
        {
            return method.GetMethodBody()?.GetILAsByteArray()?.Length ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    private static string Kind(MethodBase method)
    {
        if (method.IsStatic)
            return "static";
        if (method.IsAbstract)
            return "abstract";
        return method.IsVirtual ? method.IsFinal ? "virtual sealed" : "virtual" : "instance";
    }

    /// <summary>How many types in the target's own assembly declare their own version of it. A
    /// patch on the base method does not run for a call that lands on one of those.</summary>
    private static int Overrides(MethodBase method)
    {
        if (!(method is MethodInfo info) || !info.IsVirtual || info.IsFinal || info.DeclaringType == null)
            return 0;

        var parameters = info.GetParameters().Select(p => p.ParameterType).ToArray();
        var count = 0;
        foreach (var type in TypesIn(info.DeclaringType.Assembly))
        {
            if (type == info.DeclaringType || !info.DeclaringType.IsAssignableFrom(type))
                continue;
            try
            {
                if (type.GetMethod(info.Name, Declared, null, parameters, null) != null)
                    count++;
            }
            catch
            {
                // An unloadable member type: the count is a hint, not a contract.
            }
        }
        return count;
    }

    private static Type[] TypesIn(Assembly assembly)
    {
        if (TypeCache.TryGetValue(assembly, out var types))
            return types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t != null).ToArray();
        }
        catch
        {
            types = new Type[0];
        }
        TypeCache[assembly] = types;
        return types;
    }
}
