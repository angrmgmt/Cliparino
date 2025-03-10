#region

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#endregion

internal static class FileProcessor {
    // Define the output file
    private const string OutputFile = "Output/Cliparino_Clean.cs";

    // Define the order of the files to process and merge
    private static readonly string[] FileProcessingOrder = {
        "../Cliparino/src/CPHInline.cs",
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


        // If the saved file isn't part of the known processing order, skip it
        if (!FileProcessingOrder.Contains(savedFilePath)) {
            Console.WriteLine($"[FileProcessor] Skipping file '{savedFilePath}'. Not part of FileProcessingOrder.");

            return;
        }

        try {
            // Process and merge the files into a single output file
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

                // Add to a list if it's a valid `using` directive
                if (!trimmedLine.StartsWith("using ", StringComparison.Ordinal) || !trimmedLine.EndsWith(";")) continue;

                Console.WriteLine($"  [Found Using] {trimmedLine}");
                usingDirectives.Add(trimmedLine); // Deduplicates automatically because of HashSet
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

    private static string ProcessRegex(string content) {
        // Remove using directives
        var lines = content.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
        var nonUsingLines = lines.Where(line => !line.TrimStart().StartsWith("using ", StringComparison.Ordinal));
        var cleanedContent = string.Join(Environment.NewLine, nonUsingLines);

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