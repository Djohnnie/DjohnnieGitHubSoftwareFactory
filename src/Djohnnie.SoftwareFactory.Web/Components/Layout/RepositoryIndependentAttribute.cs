namespace Djohnnie.SoftwareFactory.Web.Components.Layout;

/// <summary>
/// Marks a page that works without a selected repository (for example agent templates), so
/// <see cref="MainLayout"/> renders it instead of asking for a repository first.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RepositoryIndependentAttribute : Attribute;
