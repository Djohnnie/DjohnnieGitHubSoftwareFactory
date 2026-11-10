using System.Text.RegularExpressions;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

public enum DiffLineKind
{
    /// <summary>A <c>@@ -a,b +c,d @@</c> header.</summary>
    Hunk,
    Context,
    Added,
    Removed,

    /// <summary><c>\ No newline at end of file</c>.</summary>
    Note
}

/// <param name="OldLine">The line number in the old file (context and removed lines).</param>
/// <param name="NewLine">The line number in the new file (context and added lines).</param>
public sealed record DiffLine(DiffLineKind Kind, int? OldLine, int? NewLine, string Text)
{
    /// <summary>Whether a review comment can be put on the line (only lines of the diff, not headers).</summary>
    public bool CanComment => Kind is DiffLineKind.Context or DiffLineKind.Added or DiffLineKind.Removed;

    /// <summary>The side a review comment on the line belongs to: removed lines are on the old file.</summary>
    public DiffSide Side => Kind == DiffLineKind.Removed ? DiffSide.Left : DiffSide.Right;

    /// <summary>The line number a review comment on the line uses, in the file of <see cref="Side"/>.</summary>
    public int? CommentLine => Kind == DiffLineKind.Removed ? OldLine : NewLine;
}

/// <summary>Splits the patch of one file (unified diff hunks, as GitHub's pull request files API returns it) into numbered lines.</summary>
public static partial class DiffPatch
{
    public static IReadOnlyList<DiffLine> Parse(string? patch)
    {
        var lines = new List<DiffLine>();
        if (string.IsNullOrEmpty(patch))
        {
            return lines;
        }

        int oldLine = 0, newLine = 0;
        var rows = patch.ReplaceLineEndings("\n").Split('\n');
        // A trailing newline gives an empty last row that isn't part of the diff.
        var count = rows[^1].Length == 0 ? rows.Length - 1 : rows.Length;
        for (var i = 0; i < count; i++)
        {
            var row = rows[i];
            var hunk = HunkHeader().Match(row);
            if (hunk.Success)
            {
                oldLine = int.Parse(hunk.Groups["old"].Value);
                newLine = int.Parse(hunk.Groups["new"].Value);
                lines.Add(new(DiffLineKind.Hunk, null, null, row));
                continue;
            }

            var text = row.Length > 0 ? row[1..] : string.Empty;
            switch (row.Length > 0 ? row[0] : ' ')
            {
                case '+':
                    lines.Add(new(DiffLineKind.Added, null, newLine++, text));
                    break;
                case '-':
                    lines.Add(new(DiffLineKind.Removed, oldLine++, null, text));
                    break;
                case '\\':
                    lines.Add(new(DiffLineKind.Note, null, null, row));
                    break;
                default:
                    lines.Add(new(DiffLineKind.Context, oldLine++, newLine++, text));
                    break;
            }
        }
        return lines;
    }

    [GeneratedRegex(@"^@@ -(?<old>\d+)(?:,\d+)? \+(?<new>\d+)(?:,\d+)? @@")]
    private static partial Regex HunkHeader();
}
