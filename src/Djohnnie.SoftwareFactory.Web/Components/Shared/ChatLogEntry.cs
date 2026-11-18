namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>A message shown by <see cref="ChatLog"/>; the text of a reply grows while it streams in.</summary>
public sealed class ChatLogEntry(bool fromUser, string text)
{
    public bool FromUser { get; } = fromUser;
    public string Text { get; set; } = text;
    public bool IsError { get; set; }
}
