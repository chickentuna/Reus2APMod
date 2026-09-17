using System;
using UnityEngine;

namespace Reus2Archipelago.Game;

/// <summary>
/// Shows the goal in the top right corner of a planet of the slot's profile: the Archipelago logo,
/// then how many different biotica have been built out of the seed's goal_builds.
/// </summary>
internal static class GoalCounter
{
    private static readonly Color Panel = new Color(0.05f, 0.05f, 0.05f, 0.6f);
    private static readonly Color Done = new Color(0.62f, 0.93f, 0.62f);

    private static Texture2D panelTexture;
    private static GUIStyle style;
    private static int styledHeight;
    private static int shownBuilt = -1;
    private static int shownGoal = -1;
    private static string text;

    /// <summary>Called from OnGUI.</summary>
    internal static void Draw()
    {
        if (Event.current.type != EventType.Repaint)
            return;
        var goal = ApState.GoalBuilds;
        if (goal <= 0 || GameAccess.Gameplay == null || !ItemApplier.ProfileBound)
            return;

        var built = Math.Min(ApState.BuiltCount, goal);
        if (built != shownBuilt || goal != shownGoal)
        {
            shownBuilt = built;
            shownGoal = goal;
            text = built >= goal
                ? Text("Goal complete: {0}!", goal)
                : Text("Goal: {0} / {1}", built, goal);
        }

        EnsureStyle();
        var content = new GUIContent(text);
        var textSize = style.CalcSize(content);
        var icon = style.fontSize * 1.5f;
        var padding = style.fontSize * 0.5f;
        var margin = Screen.height * 0.024f;

        var width = padding + icon + padding + textSize.x + padding;
        var height = Mathf.Max(icon, textSize.y) + padding;
        var box = new Rect(Screen.width - margin - width, margin, width, height);
        GUI.DrawTexture(box, panelTexture);

        var logo = ApIcon.Load();
        if (logo != null)
            GUI.DrawTexture(new Rect(box.x + padding, box.y + (height - icon) / 2f, icon, icon), logo.texture, ScaleMode.ScaleToFit);

        var label = new Rect(box.x + padding + icon + padding, box.y + (height - textSize.y) / 2f, textSize.x, textSize.y);
        var previous = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.8f);
        GUI.Label(new Rect(label.x + 1f, label.y + 1f, label.width, label.height), content, style);
        GUI.color = built >= goal ? Done : Color.white;
        GUI.Label(label, content, style);
        GUI.color = previous;
    }

    /// <summary>The style follows the screen height, so the counter keeps its size across resolutions.</summary>
    private static void EnsureStyle()
    {
        if (panelTexture == null)
        {
            panelTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            panelTexture.SetPixel(0, 0, Panel);
            panelTexture.Apply();
        }

        if (style != null && styledHeight == Screen.height)
            return;
        styledHeight = Screen.height;
        style = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(12, Mathf.RoundToInt(Screen.height / 48f)),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = false,
        };
        style.normal.textColor = Color.white;
    }

    /// <summary>Through the game's translation, so reus2_text_overrides.tsv can reword the counter.</summary>
    private static string Text(string english, params object[] args)
    {
        try
        {
            return MacroSettings.L(english, args);
        }
        catch
        {
            return string.Format(english, args);
        }
    }
}
