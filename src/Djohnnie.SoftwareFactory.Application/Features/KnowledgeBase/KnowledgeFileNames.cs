namespace Djohnnie.SoftwareFactory.Application.Features.KnowledgeBase;

public static class KnowledgeFileNames
{
    /// <summary>
    /// The name without any directory, for both <c>/</c> and <c>\</c> separators. <see cref="Path.GetFileName(string?)"/>
    /// only knows the separators of the server's OS, so on Linux it would keep a Windows path such as <c>C:\Users\me\a.txt</c>.
    /// </summary>
    public static string Clean(string? fileName)
    {
        var name = (fileName ?? string.Empty).Trim();
        return name[(name.LastIndexOfAny(['/', '\\']) + 1)..].Trim();
    }
}
