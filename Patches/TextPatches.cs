using HarmonyLib;
using Reus2Archipelago.Game;

namespace Reus2Archipelago.Patches;

/// <summary>
/// Replaces game texts. Every string the game shows goes through <c>MacroSettings.L</c>, which
/// looks its key up in the table <c>I18n_Translation.ReadTranslations</c> just built, so the
/// replacements are written into that table as it is loaded. The dump runs after
/// <c>ReadTranslationKeys</c>, the point where both the key table and the locale table are in place.
/// </summary>
internal static class TextPatches
{
    private static bool overrideTexts;
    private static bool dumpTexts;

    internal static void Install(Harmony harmony, bool applyOverrides, bool dump)
    {
        overrideTexts = applyOverrides;
        dumpTexts = dump;

        var owner = typeof(TextPatches);
        if (overrideTexts)
            Patching.Apply(harmony, "i18n.I18n_Translation", "ReadTranslations", owner, postfix: nameof(AfterReadTranslations));
        if (dumpTexts || overrideTexts)
            Patching.Apply(harmony, "MacroSettings", "ReadTranslationKeys", owner, postfix: nameof(AfterReadTranslationKeys));
    }

    private static void AfterReadTranslations(string locale, object __result)
    {
        if (overrideTexts)
            TextOverrides.Apply(__result, locale);
    }

    private static void AfterReadTranslationKeys()
    {
        if (overrideTexts)
            TextOverrides.ApplyDeferred();
        if (dumpTexts)
            TextDumper.DumpOnce();
    }
}
