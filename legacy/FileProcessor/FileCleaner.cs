#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#endregion

/// <summary>
///     A simple class designed to stitch together and clean up many project files to bring them
///     together into one Streamer.bot-friendly code file.
/// </summary>
internal static class FileProcessor {
    private const string OutputFile = "Output/Cliparino_Clean.cs";

    private static readonly string[] FileProcessingOrder = {
        "../Cliparino/src/CPHInline.cs",
        "../Cliparino/src/Constants/CliparinoConstants.cs",
        "../Cliparino/src/Utilities/ValidationHelper.cs",
        "../Cliparino/src/Utilities/RetryHelper.cs",
        "../Cliparino/src/Utilities/HttpResponseBuilder.cs",
        "../Cliparino/src/Utilities/ConfigurationManager.cs",
        "../Cliparino/src/Utilities/ErrorHandler.cs",
        "../Cliparino/src/Utilities/InputProcessor.cs",
        "../Cliparino/src/Utilities/ManagerFactory.cs",
        "../Cliparino/src/Managers/CPHLogger.cs",
        "../Cliparino/src/Managers/ObsSceneManager.cs",
        "../Cliparino/src/Managers/ClipManager.cs",
        "../Cliparino/src/Managers/TwitchApiManager.cs",
        "../Cliparino/src/Managers/HttpManager.cs"
    };

    // ReSharper disable once MemberCanBePrivate.Global
    /// <summary>
    ///     Entry point for the "on save" mechanism. Validates the file, then triggers the merging and
    ///     processing flow.
    /// </summary>
    /// <param name="savedFilePath">
    ///     The path to the saved file.
    /// </param>
    private static void OnFileSave(string savedFilePath) {
        Console.WriteLine($"[FileProcessor] Detected save event for file: {savedFilePath}");

        Console.WriteLine("[FileProcessor] Resolving paths:");

        foreach (var file in FileProcessingOrder) {
            var fullPath = Path.GetFullPath(file); // Resolves paths relative to the current directory
            Console.WriteLine($"  Path: {fullPath} -- Exists: {File.Exists(fullPath)}");
        }

        if (!FileProcessingOrder.Contains(savedFilePath)) {
            Console.WriteLine($"[FileProcessor] Skipping file '{savedFilePath}'. Not part of FileProcessingOrder.");

            return;
        }

        try {
            ProcessFiles(FileProcessingOrder, OutputFile);
        } catch (Exception ex) {
            Console.WriteLine($"[FileProcessor] Error while processing files: {ex.Message}");
        }
    }

    /// <summary>
    ///     Reads the `using` directives from each file and collects unique directives.
    /// </summary>
    private static IEnumerable<string> ExtractUsingDirectives(IEnumerable<string> inputFiles) {
        var usingDirectives = new HashSet<string>();

        foreach (var file in inputFiles) {
            if (!File.Exists(file)) continue;

            Console.WriteLine($"[ExtractUsingDirectives] Reading file: {Path.GetFileName(file)}");

            var lines = File.ReadAllLines(file);

            foreach (var line in lines) {
                var trimmedLine = line.Trim();

                // Check if this is a using directive (not a using statement)
                // Using directives: "using System.IO;" 
                // Using statements: "using (var response = ...)" or "using var response = ..."
                if (!trimmedLine.StartsWith("using ", StringComparison.Ordinal) || !trimmedLine.EndsWith(";")) continue;

                // Skip using statements (those that contain parentheses or var)
                if (trimmedLine.Contains("(") || trimmedLine.Contains(" var ") || trimmedLine.Contains("\tvar "))
                    continue;

                Console.WriteLine($"  [Found Using] {trimmedLine}");
                usingDirectives.Add(trimmedLine);
            }
        }

        return usingDirectives;
    }

    /// <summary>
    ///     Processes and merges multiple files into a single output file.
    /// </summary>
    private static void ProcessFiles(IEnumerable<string> inputFiles, string outputFile) {
        Console.WriteLine($"[FileProcessor] Writing merged content to: {Path.GetFullPath(outputFile)}");

        var inputFileList = inputFiles.ToList();
        var usingDirectives = ExtractUsingDirectives(inputFileList);

        using (var writer = new StreamWriter(outputFile, false)) {
            Console.WriteLine("[FileProcessor] Writing using directives...");

            foreach (var directive in usingDirectives.OrderBy(u => u)) {
                Console.WriteLine($"  [Directive] {directive}");
                writer.WriteLine(directive);
            }

            writer.WriteLine();

            foreach (var inputFile in inputFileList) {
                if (!File.Exists(inputFile)) {
                    Console.WriteLine($"  [Warning] File not found: {inputFile}");

                    continue;
                }

                Console.WriteLine($"[FileProcessor] Processing {Path.GetFileName(inputFile)}...");
                var content = File.ReadAllText(inputFile);

                var cleanedContent = ProcessRegex(content);
                Console.WriteLine($"  [Cleaned Content] from {Path.GetFileName(inputFile)}:\n{cleanedContent}\n");

                writer.WriteLine(cleanedContent.Trim());
                writer.WriteLine();
            }
        }

        Console.WriteLine($"[FileProcessor] Successfully wrote merged file: {Path.GetFullPath(outputFile)}");
    }

    /// <summary>
    ///     Runs through and applies the Regular Expressions replacement rules defined for the project.
    /// </summary>
    /// <param name="content">
    ///     The content of the code file being processed.
    /// </param>
    /// <returns>
    ///     The result of applying the Regular Expressions rules to the content, producing cleaned content.
    /// </returns>
    private static string ProcessRegex(string content) {
        var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        // Only remove using directives, not using statements
        var nonUsingDirectiveLines = lines.Where(line => {
                                                     var trimmedLine = line.TrimStart();

                                                     if (!trimmedLine.StartsWith("using ", StringComparison.Ordinal))
                                                         return true;

                                                     // Keep using statements (those that contain parentheses or var)
                                                     return trimmedLine.Contains("(")
                                                            || trimmedLine.Contains(" var ")
                                                            || trimmedLine.Contains("\tvar ");
                                                 });

        var cleanedContent = string.Join(Environment.NewLine, nonUsingDirectiveLines);

        cleanedContent = RegexUtilities.RemoveDevEnvInheritance(cleanedContent);
        cleanedContent = RegexUtilities.RemoveXmlDocComments(cleanedContent);
        cleanedContent = RegexUtilities.RemoveReSharperComments(cleanedContent);
        cleanedContent = RegexUtilities.RemoveBlockComments(cleanedContent);
        cleanedContent = RegexUtilities.RemoveRegionBlocks(cleanedContent);
        cleanedContent = RegexUtilities.RemoveSuppressMessageAttributes(cleanedContent);
        cleanedContent = RegexUtilities.TrimBlankLines(cleanedContent);

        return cleanedContent;
    }

    /// <summary>
    ///     Main method for testing and running the FileProcessor. Simulates a save event using command
    ///     line arguments.
    /// </summary>
    private static void Main(string[] args) {
        if (args.Length == 0) {
            Console.WriteLine("[FileProcessor] No file specified. Simulating save event for the first file.");
            OnFileSave(FileProcessingOrder[0]); // Simulate a save for the first file in the order
        } else {
            var savedFilePath = args[0];

            OnFileSave(savedFilePath);
        }
    }
}