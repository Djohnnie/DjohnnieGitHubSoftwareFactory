namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>The addresses of the chat page with the DGHSF Agent.</summary>
public static class ChatRoutes
{
    /// <summary>A new conversation.</summary>
    public const string Path = "/chat";

    /// <summary>A stored conversation.</summary>
    public static string ChatPath(Guid id)
    {
        return $"{Path}/{id}";
    }
}
