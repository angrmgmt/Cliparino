#region

using System.Text.RegularExpressions;

#endregion

/// <summary>
///     A storage class for Regular Expressions objects representing replacement rules for unwanted
///     text in project files.
/// </summary>
internal static class RegexUtilities {
    private static Regex XmlDocCommentsRegex() {
        return new Regex(@"^\s*///.*$", RegexOptions.Multiline);
    }

    private static Regex ReSharperCommentsRegex() {
        return new Regex(@"^\s*// ReSharper\s+\S+.*$", RegexOptions.Multiline);
    }

    private static Regex BlockCommentRegex() {
        return new Regex(@"/\*.*?\*/", RegexOptions.Singleline); // For license/header block
    }

    private static Regex RegionLineRegex() {
        return new Regex(@"^\s*#(region|endregion).*$", RegexOptions.Multiline);
    }

    private static Regex SuppressMessageAttributesRegex() {
        return new Regex(@"^\s*\[SuppressMessage\(.*?\)\]\s*$", RegexOptions.Multiline);
    }

    private static Regex BlankLinesRegex() {
        return new Regex(@"^\s*\n", RegexOptions.Multiline);
    }

    private static Regex DevEnvInheritanceRegex() {
        return new Regex(@"\s{1}\:\s{1}CPHInlineBase", RegexOptions.Multiline);
    }

    public static string RemoveXmlDocComments(string input) {
        return XmlDocCommentsRegex().Replace(input, string.Empty);
    }

    public static string RemoveReSharperComments(string input) {
        return ReSharperCommentsRegex().Replace(input, string.Empty);
    }

    public static string RemoveBlockComments(string content) {
        return BlockCommentRegex().Replace(content, string.Empty);
    }

    public static string RemoveRegionBlocks(string content) {
        return RegionLineRegex().Replace(content, string.Empty);
    }

    public static string RemoveSuppressMessageAttributes(string input) {
        return SuppressMessageAttributesRegex().Replace(input, string.Empty);
    }

    public static string TrimBlankLines(string input) {
        return BlankLinesRegex().Replace(input, string.Empty);
    }

    public static string RemoveDevEnvInheritance(string input) {
        return DevEnvInheritanceRegex().Replace(input, string.Empty);
    }
}