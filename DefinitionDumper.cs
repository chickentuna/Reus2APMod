using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Reus2Archipelago;

/// <summary>
/// Walks the live <c>DefinitionProvider</c> and writes every game definition to JSON: ids, tags,
/// apex/cohort links, milestone requirement lists, objective filters and thresholds. This is the
/// input the Archipelago apworld generates its item pool and logic rules from.
///
/// Everything is reached by reflection rather than a compile-time reference to Assembly-CSharp, so
/// the plugin keeps building across game patches and needs no game DLLs checked into the repo.
/// </summary>
internal static class DefinitionDumper
{
    internal static int MaxDepth = 4;
    internal static string OutputPath = "";

    private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static bool done;

    /// <summary>True once the definition database is populated and a dump can be taken.</summary>
    internal static bool Ready()
    {
        var definitions = GetDefinitions();
        return definitions != null && definitions.Count > 0;
    }

    internal static void DumpOnce()
    {
        if (done) return;
        done = true;

        var definitions = GetDefinitions();
        if (definitions == null)
        {
            Plugin.Log.LogError("Could not reach DefinitionProvider.Instance.definitions — nothing dumped.");
            return;
        }

        var byType = new SortedDictionary<string, List<object>>(StringComparer.Ordinal);
        foreach (var def in definitions)
        {
            if (def == null) continue;
            var typeName = def.GetType().Name;
            if (!byType.TryGetValue(typeName, out var list)) byType[typeName] = list = new List<object>();
            list.Add(def);
        }

        var json = new Json();
        json.StartObject();

        json.StartObject("meta");
        json.Prop("gameVersion", Application.version);
        json.Prop("unityVersion", Application.unityVersion);
        json.Prop("dumpedAtUtc", DateTime.UtcNow.ToString("o"));
        json.Prop("definitionCount", definitions.Count);
        json.Prop("maxDepth", MaxDepth);
        json.EndObject();

        json.StartObject("counts");
        foreach (var pair in byType) json.Prop(pair.Key, pair.Value.Count);
        json.EndObject();

        json.StartObject("definitions");
        foreach (var pair in byType)
        {
            json.StartArray(pair.Key);
            foreach (var def in pair.Value)
            {
                var seen = new HashSet<object>(ReferenceComparer.Instance);
                WriteDefinition(json, def, seen);
            }
            json.EndArray();
        }
        json.EndObject();

        json.EndObject();

        var path = string.IsNullOrEmpty(OutputPath)
            ? Path.Combine(BepInEx.Paths.PluginPath, "reus2_definitions.json")
            : OutputPath;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json.ToString());
            Plugin.Log.LogInfo($"Wrote {definitions.Count} definitions across {byType.Count} types to {path}");
            foreach (var pair in byType) Plugin.Log.LogInfo($"  {pair.Value.Count,5}  {pair.Key}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Write failed: {e.Message}");
        }
    }

    /// <summary>
    /// Reads the catalogue without going through <c>DefinitionProvider</c>: its constructor copies
    /// the database once and caches it forever, so touching that singleton before the game has
    /// registered the database would leave the game itself with an empty provider.
    /// <c>DefinitionDatabase.Instance</c> is a plain static, and scanning loaded assets is the
    /// fallback if it has not been registered yet.
    /// </summary>
    private static IList GetDefinitions()
    {
        var asm = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
        if (asm == null) { Waiting("Assembly-CSharp not loaded yet"); return null; }

        var definitionType = asm.GetType("Definition");
        if (definitionType == null) { Waiting("no Definition type in Assembly-CSharp"); return null; }

        var databaseType = asm.GetType("DefinitionDatabase");
        var database = databaseType
            ?.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?.GetValue(null);
        if (database != null)
        {
            if (databaseType.GetField("definitions", InstanceFields)?.GetValue(database) is IList fromDatabase
                && fromDatabase.Count > 0)
                return fromDatabase;
        }

        var loaded = Resources.FindObjectsOfTypeAll(definitionType);
        if (loaded != null && loaded.Length > 0) return loaded;

        Waiting(database == null
            ? "DefinitionDatabase.Instance is still null and no Definition assets are loaded"
            : "DefinitionDatabase.Instance is registered but empty");
        return null;
    }

    private static string lastWaitReason;

    private static void Waiting(string reason)
    {
        if (reason == lastWaitReason) return;
        lastWaitReason = reason;
        Plugin.Log.LogInfo($"Waiting: {reason}");
        Status($"waiting: {reason}");
    }

    /// <summary>
    /// Appends a line to <c>reus2dump_status.txt</c>. The BepInEx log buffers, so a run that ends
    /// with the game being closed can lose its tail; this file is written straight through.
    /// </summary>
    internal static void Status(string line)
    {
        try
        {
            var path = Path.Combine(BepInEx.Paths.PluginPath, "reus2dump_status.txt");
            File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  {line}\n");
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Status write failed: {e.Message}");
        }
    }

    private static void WriteDefinition(Json json, object def, HashSet<object> seen)
    {
        json.StartObject();
        json.Prop("id", IdOf(def));
        json.Prop("name", NameOf(def));
        json.Prop("type", def.GetType().Name);
        seen.Add(def);
        WriteFields(json, def, seen, 0);
        json.EndObject();
    }

    private static void WriteFields(Json json, object obj, HashSet<object> seen, int depth)
    {
        foreach (var field in Fields(obj.GetType()))
        {
            object value;
            try { value = field.GetValue(obj); }
            catch { continue; }
            WriteValue(json, field.Name, value, seen, depth);
        }
    }

    /// <summary>Public fields plus the private ones Unity serializes, which is where several
    /// requirement lists live.</summary>
    private static IEnumerable<FieldInfo> Fields(Type type)
    {
        return type.GetFields(InstanceFields)
            .Where(f => f.IsPublic || f.GetCustomAttributes(inherit: true)
                .Any(a => a.GetType().Name is "SerializeField" or "SerializeReference"));
    }

    private static void WriteValue(Json json, string key, object value, HashSet<object> seen, int depth)
    {
        if (value == null) { json.Null(key); return; }

        switch (value)
        {
            case string s: json.Prop(key, s); return;
            case bool b: json.Prop(key, b); return;
            case Enum e: json.Prop(key, e.ToString()); return;
            case float f: json.Prop(key, f); return;
            case double d: json.Prop(key, d); return;
            case decimal m: json.Prop(key, (double)m); return;
        }

        var type = value.GetType();
        if (type.IsPrimitive)
        {
            json.Prop(key, Convert.ToInt64(value));
            return;
        }

        // Unity's fake-null: a destroyed or unassigned asset reference reads as non-null to plain
        // reflection but null to the engine.
        if (value is UnityEngine.Object unityObject)
        {
            if (unityObject == null) { json.Null(key); return; }
            if (IsDefinition(type))
            {
                json.StartObject(key);
                json.Prop("ref", IdOf(value));
                json.Prop("name", unityObject.name);
                json.Prop("type", type.Name);
                json.EndObject();
            }
            else
            {
                json.StartObject(key);
                json.Prop("asset", type.Name);
                json.Prop("name", unityObject.name);
                json.EndObject();
            }
            return;
        }

        if (value is IDictionary dictionary)
        {
            if (depth >= MaxDepth) { json.Prop(key, $"<dictionary depth {dictionary.Count}>"); return; }
            json.StartObject(key);
            foreach (DictionaryEntry entry in dictionary)
                WriteValue(json, KeyName(entry.Key), entry.Value, seen, depth + 1);
            json.EndObject();
            return;
        }

        if (value is IEnumerable sequence)
        {
            if (depth >= MaxDepth) { json.Prop(key, "<list>"); return; }
            json.StartArray(key);
            foreach (var item in sequence) WriteValue(json, null, item, seen, depth + 1);
            json.EndArray();
            return;
        }

        // KeyValuePair keeps its data in properties, which the field walk below would skip; this is
        // how base yields are stored.
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
        {
            json.StartObject(key);
            WriteValue(json, "key", type.GetProperty("Key")?.GetValue(value), seen, depth + 1);
            WriteValue(json, "value", type.GetProperty("Value")?.GetValue(value), seen, depth + 1);
            json.EndObject();
            return;
        }

        // Structs like Vector2 and Color carry no logic information; their text form is enough.
        if (type.Namespace != null && type.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
        {
            json.Prop(key, value.ToString());
            return;
        }

        if (depth >= MaxDepth || !seen.Add(value))
        {
            json.Prop(key, $"<{type.Name}>");
            return;
        }

        json.StartObject(key);
        json.Prop("$type", type.Name);
        WriteFields(json, value, seen, depth + 1);
        json.EndObject();
    }

    private static string KeyName(object key)
    {
        if (key == null) return "null";
        if (key is UnityEngine.Object o && IsDefinition(key.GetType())) return o.name;
        return key.ToString();
    }

    private static bool IsDefinition(Type type)
    {
        for (var t = type; t != null; t = t.BaseType)
            if (t.Name == "Definition") return true;
        return false;
    }

    private static string IdOf(object def)
        => def.GetType().GetProperty("ID", InstanceFields)?.GetValue(def) as string;

    private static string NameOf(object def)
        => def is UnityEngine.Object o ? o.name : def.ToString();

    private class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new ReferenceComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
