#region

using System.Text.RegularExpressions;

#endregion

internal static class RegexUtilities {
    private static Regex XmlDocCommentsRegex() {
        return new Regex(@"^\s*///.*$", RegexOptions.Multiline);
    }

    private static Regex ReSharperCommentsRegex() {
        return new Regex(@"^\s*// ReSharper\s+\S+.*$", RegexOptions.Multiline);
    }

    private static Regex SuppressMessageAttributesRegex() {
        return new Regex(@"^\s*\[SuppressMessage\(.*?\)\]\s*$", RegexOptions.Multiline);
    }

    private static Regex BlankLinesRegex() {
        return new Regex(@"^\s*\n", RegexOptions.Multiline);
    }

    public static string RemoveXmlDocComments(string input) {
        return XmlDocCommentsRegex().Replace(input, string.Empty);
    }

    public static string RemoveReSharperComments(string input) {
        return ReSharperCommentsRegex().Replace(input, string.Empty);
    }

    public static string RemoveSuppressMessageAttributes(string input) {
        return SuppressMessageAttributesRegex().Replace(input, string.Empty);
    }

    public static string TrimBlankLines(string input) {
        return BlankLinesRegex().Replace(input, string.Empty);
    }
}