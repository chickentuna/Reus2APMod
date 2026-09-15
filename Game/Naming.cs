using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Reus2Archipelago.Game;

/// <summary>
/// Turns a game object into the name a reader of the log can act on: a definition prints as its
/// asset name and GUID, a model prints as the definition it was created from. Nothing here prints
/// an object hash, which no later question can be asked of.
/// </summary>
internal static class Naming
{
    private const int MaxItems = 12;
    private const int MaxDepth = 2;

    internal static string Of(object value)
    {
        try
        {
            return Render(value, 0);
        }
        catch (Exception e)
        {
            return $"<unreadable {value?.GetType().Name}: {e.Message}>";
        }
    }

    /// <summary>Comma-separated names, cut off after twelve so one line stays one line.</summary>
    internal static string Each(object values)
    {
        if (!(values is IEnumerable items) || values is string)
            return Of(values);
        var names = new List<string>();
        var total = 0;
        foreach (var item in items)
        {
            total++;
            if (names.Count < MaxItems)
                names.Add(Of(item));
        }
        return total > MaxItems
            ? $"{string.Join(", ", names)} and {total - MaxItems} more"
            : names.Count == 0 ? "none" : string.Join(", ", names);
    }

    /// <summary>How many items, or -1 when the value is not a sequence.</summary>
    internal static int Count(object values)
    {
        if (!(values is IEnumerable items) || values is string)
            return -1;
        return items.Cast<object>().Count();
    }

    private static string Render(object value, int depth)
    {
        if (value == null)
            return "<null>";
        if (value is string text)
            return $"\"{text}\"";
        if (value is Enum || value.GetType().IsPrimitive || value is decimal)
            return value.ToString();
        if (value is UnityEngine.Object unity && unity == null)
            return $"<destroyed {value.GetType().Name}>";

        var id = GameAccess.Get(value, "ID") as string;
        if (id != null)
            return $"{NameOrType(value)} [{id}]";

        if (depth < MaxDepth)
        {
            var definition = GameAccess.Get(value, "Definition") ?? GameAccess.Get(value, "definition");
            if (definition != null && !ReferenceEquals(definition, value))
                return $"{value.GetType().Name}({Render(definition, depth + 1)})";
        }

        if (value is IEnumerable && depth < MaxDepth)
            return $"[{Each(value)}]";

        return NameOrType(value);
    }

    private static string NameOrType(object value)
    {
        var name = (value as UnityEngine.Object)?.name;
        return string.IsNullOrEmpty(name) ? value.GetType().Name : $"{value.GetType().Name} '{name}'";
    }
}
