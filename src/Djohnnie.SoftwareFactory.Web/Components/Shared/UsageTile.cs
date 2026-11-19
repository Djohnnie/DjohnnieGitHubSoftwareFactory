namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>One colored figure of a <see cref="UsageTileGrid"/>.</summary>
/// <param name="ColorClass">One of the <c>usage-*</c> color classes of the grid, e.g. <c>usage-input</c>.</param>
/// <param name="Wide">Spans the whole grid when the tiles are in two columns.</param>
public sealed record UsageTile(string Label, string Value, string ColorClass, bool Wide = false);
