using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Reus2Dump;

/// <summary>
/// Minimal JSON writer. The game ships Newtonsoft, but referencing it would tie the plugin to the
/// game's assembly set; this keeps the dumper dependency-free.
/// </summary>
internal class Json
{
    private readonly StringBuilder sb = new StringBuilder();
    private readonly Stack<bool> firstInScope = new Stack<bool>();
    private int indent;

    public Json() => firstInScope.Push(true);

    public override string ToString() => sb.ToString();

    public void StartObject(string key = null) => Open('{', key);
    public void EndObject() => Close('}');
    public void StartArray(string key = null) => Open('[', key);
    public void EndArray() => Close(']');

    public void Prop(string key, string value) => Write(key, value == null ? "null" : Quote(value));
    public void Prop(string key, bool value) => Write(key, value ? "true" : "false");
    public void Prop(string key, long value) => Write(key, value.ToString(CultureInfo.InvariantCulture));
    public void Prop(string key, double value) => Write(key, Number(value));
    public void Raw(string key, string rawJson) => Write(key, rawJson);

    public void Value(string value) => Write(null, value == null ? "null" : Quote(value));
    public void Value(bool value) => Write(null, value ? "true" : "false");
    public void Value(long value) => Write(null, value.ToString(CultureInfo.InvariantCulture));
    public void Value(double value) => Write(null, Number(value));
    public void Null(string key = null) => Write(key, "null");

    private void Open(char brace, string key)
    {
        Separate();
        if (key != null) sb.Append(Quote(key)).Append(": ");
        sb.Append(brace);
        firstInScope.Push(true);
        indent++;
    }

    private void Close(char brace)
    {
        var wasEmpty = firstInScope.Pop();
        indent--;
        if (!wasEmpty) NewLine();
        sb.Append(brace);
    }

    private void Write(string key, string rawValue)
    {
        Separate();
        if (key != null) sb.Append(Quote(key)).Append(": ");
        sb.Append(rawValue);
    }

    private void Separate()
    {
        if (firstInScope.Count == 0) return;
        var first = firstInScope.Pop();
        if (!first) sb.Append(',');
        firstInScope.Push(false);
        if (indent > 0 || !first) NewLine();
    }

    private void NewLine()
    {
        sb.Append('\n');
        sb.Append(' ', indent * 2);
    }

    // Doubles that are whole numbers still serialize with a decimal point so consumers see a float.
    private static string Number(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "null";
        var s = value.ToString("R", CultureInfo.InvariantCulture);
        return s;
    }

    public static string Quote(string s)
    {
        var b = new StringBuilder(s.Length + 2);
        b.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                    break;
            }
        }
        b.Append('"');
        return b.ToString();
    }
}
