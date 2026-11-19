namespace Djohnnie.SoftwareFactory.Web.Components.Layout;

/// <summary>
/// Marks a page that only shows or changes things on GitHub (for example the Actions workflows), so
/// <see cref="MainLayout"/> doesn't render it for a repository that was removed from GitHub.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RequiresGitHubAttribute : Attribute;
