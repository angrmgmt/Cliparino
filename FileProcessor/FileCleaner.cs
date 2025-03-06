#region

using System;

#endregion

internal static class FileProcessor {
    private static void ProcessFile(string inputFile, string outputFile) {
        // Read all text from the input file
        var content = File.ReadAllText(inputFile);

        // Step 1: Remove triple-slash XML doc comments
        content = RegexUtilities.RemoveXmlDocComments(content);

        // Step 2: Remove ReSharper comment directives
        content = RegexUtilities.RemoveReSharperComments(content);

        // Step 3: Remove specific unwanted attributes
        content = RegexUtilities.RemoveSuppressMessageAttributes(content);

        // (Optional) Trim unnecessary blank lines
        content = RegexUtilities.TrimBlankLines(content);

        // Step 4: Write the cleaned content to the output file
        File.WriteAllText(outputFile, content);

        Console.WriteLine($"File processed successfully. Output saved to {outputFile}");
    }

    private static void Main(string[] args) {
        if (args.Length < 2) {
            Console.WriteLine("Usage: FileProcessor <inputFile> <outputFile>");

            return;
        }

        var inputFile = args[0];
        var outputFile = args[1];

        ProcessFile(inputFile, outputFile);
    }
}