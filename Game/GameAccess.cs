using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Reus2Archipelago.Game;

/// <summary>
/// Reflection access to Reus 2. The plugin has no compile-time reference to Assembly-CSharp, so game
/// members are reached by name; lookups are cached because several run on hot paths.
/// </summary>
internal static class GameAccess
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly Dictionary<(Type, string), MemberInfo> Members = new Dictionary<(Type, string), MemberInfo>();
    private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();
    private static readonly Dictionary<string, object> Resources = new Dictionary<string, object>();
    private static Dictionary<string, object> definitionsByGuid;
    private static PropertyInfo gameManagerInstance;
    private static PropertyInfo sessionContext;

    /// <summary>A game type by full name, looked up in Assembly-CSharp first so a same-named type
    /// elsewhere is never picked.</summary>
    internal static Type TypeNamed(string name)
    {
        if (Types.TryGetValue(name, out var type))
            return type;
        var game = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");
        type = game?.GetType(name) ?? AccessTools.TypeByName(name);
        if (type != null)
            Types[name] = type;
        return type;
    }

    internal static object Install
    {
        get
        {
            gameManagerInstance ??= AccessTools.Property(TypeNamed("GameManager"), "Instance");
            return Get(gameManagerInstance?.GetValue(null), "Install");
        }
    }

    internal static object CurrentProfile => Get(Install, "CurrentProfile");

    /// <summary>The running planet's GameplayController, or null outside a planet.</summary>
    internal static object Gameplay
    {
        get
        {
            var gameplay = Get(SessionContext, "GameplayController");
            return gameplay is UnityEngine.Object unityObject && unityObject == null ? null : gameplay;
        }
    }

    /// <summary>The running planet's session id, or null outside a planet. A save keeps it, a new
    /// run gets a fresh one.</summary>
    internal static string SessionId => Get(Get(SessionContext, "CurrentSession"), "SessionID") as string;

    internal static object SessionContext
    {
        get
        {
            sessionContext ??= AccessTools.Property(TypeNamed("AbbeyBehaviour"), "SessionContext");
            try
            {
                return sessionContext?.GetValue(null);
            }
            catch
            {
                return null;
            }
        }
    }

    internal static bool DefinitionsReady => LoadDefinitions();

    internal static object Definition(string guid)
        => guid != null && LoadDefinitions() && definitionsByGuid.TryGetValue(guid, out var definition) ? definition : null;

    internal static string DefinitionId(object definition) => Get(definition, "ID") as string;

    internal static object Get(object target, string name)
    {
        if (target == null)
            return null;
        switch (Member(target.GetType(), name))
        {
            case PropertyInfo property:
                return property.GetValue(target);
            case FieldInfo field:
                return field.GetValue(target);
            default:
                return null;
        }
    }

    internal static void Set(object target, string name, object value)
    {
        if (target == null)
            return;
        switch (Member(target.GetType(), name))
        {
            case PropertyInfo property when property.CanWrite:
                property.SetValue(target, value);
                break;
            case FieldInfo field:
                field.SetValue(target, value);
                break;
            default:
                Plugin.Log.LogWarning($"Cannot set {target.GetType().Name}.{name}");
                break;
        }
    }

    /// <summary>Invokes the first method of that name whose parameters accept the arguments,
    /// searching base types too.</summary>
    internal static object Call(object target, string name, params object[] args)
    {
        if (target == null)
            return null;
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            foreach (var method in type.GetMethods(AnyInstance | BindingFlags.DeclaredOnly))
            {
                if (method.Name != name)
                    continue;
                var parameters = method.GetParameters();
                if (parameters.Length != args.Length || parameters.Where((p, i) => !Accepts(p.ParameterType, args[i])).Any())
                    continue;
                return method.Invoke(target, args);
            }
        }

        Plugin.Log.LogWarning($"No method {target.GetType().Name}.{name} taking {args.Length} arguments");
        return null;
    }

    internal static object EnumValue(string enumTypeName, string value) => Enum.Parse(TypeNamed(enumTypeName), value);

    /// <summary>The PlayerResource that GameplayController.GetResource and SetResource take. Each
    /// one is a ScriptableObject reached through the static property of that name on the class.</summary>
    internal static object Resource(string name)
    {
        if (Resources.TryGetValue(name, out var resource))
            return resource;
        resource = AccessTools.Property(TypeNamed("PlayerResource"), name)?.GetValue(null);
        if (resource != null)
            Resources[name] = resource;
        return resource;
    }

    internal static Type ElementType(IList list) => list.GetType().GetGenericArguments().FirstOrDefault();

    private static bool Accepts(Type parameter, object argument)
        => argument == null ? !parameter.IsValueType : parameter.IsInstanceOfType(argument);

    private static MemberInfo Member(Type type, string name)
    {
        var key = (type, name);
        if (Members.TryGetValue(key, out var member))
            return member;

        for (var t = type; t != null && member == null; t = t.BaseType)
        {
            member = (MemberInfo)t.GetProperty(name, AnyInstance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                     ?? t.GetField(name, AnyInstance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        }
        Members[key] = member;
        return member;
    }

    private static bool LoadDefinitions()
    {
        if (definitionsByGuid != null)
            return true;
        var database = AccessTools.Property(TypeNamed("DefinitionDatabase"), "Instance")?.GetValue(null);
        if (!(Get(database, "definitions") is IList definitions) || definitions.Count == 0)
            return false;

        var map = new Dictionary<string, object>();
        foreach (var definition in definitions)
        {
            var id = DefinitionId(definition);
            if (id != null)
                map[id] = definition;
        }
        definitionsByGuid = map;
        return true;
    }
}
