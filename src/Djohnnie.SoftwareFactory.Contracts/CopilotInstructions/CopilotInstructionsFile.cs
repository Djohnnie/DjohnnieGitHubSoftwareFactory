namespace Djohnnie.SoftwareFactory.Contracts.CopilotInstructions;

/// <summary>The repository-wide custom instructions file that GitHub Copilot reads for every request in a repository.</summary>
public static class CopilotInstructionsFile
{
    public const string Path = ".github/copilot-instructions.md";

    /// <summary>Copilot code review only reads the first 4,000 characters of an instructions file.</summary>
    public const int CodeReviewLimit = 4000;
}
