namespace Reus2Archipelago.Game;

/// <summary>
/// One line per entry in the text files, so the tabs and newlines inside a game string are written
/// as \t and \n.
/// </summary>
internal static class TextEscaping
{
    internal static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text
            .Replace("\\", "\\\\")
            .Replace("\r", "")
            .Replace("\n", "\\n")
            .Replace("\t", "\\t");
    }

    internal static string Unescape(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var result = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                result.Append(text[i]);
                continue;
            }

            i++;
            switch (text[i])
            {
                case 'n': result.Append('\n'); break;
                case 't': result.Append('\t'); break;
                case '\\': result.Append('\\'); break;
                default: result.Append('\\').Append(text[i]); break;
            }
        }

        return result.ToString();
    }
}
