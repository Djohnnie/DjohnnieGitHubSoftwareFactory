using Djohnnie.SoftwareFactory.Contracts.WorkItems;
using Djohnnie.SoftwareFactory.Web.Components.Shared;

namespace Djohnnie.SoftwareFactory.Web.Tests;

public class DiffPatchTests
{
    [Fact]
    public void Parse_NumbersTheLinesOfBothFilesPerHunk()
    {
        var lines = DiffPatch.Parse("@@ -3,3 +3,4 @@ class App\n context\n-removed\n+added one\n+added two\n context\n@@ -20 +21 @@\n-x\n+y");

        Assert.Equal(
        [
            (DiffLineKind.Hunk, null, null),
            (DiffLineKind.Context, 3, 3),
            (DiffLineKind.Removed, 4, null),
            (DiffLineKind.Added, null, 4),
            (DiffLineKind.Added, null, 5),
            (DiffLineKind.Context, 5, 6),
            (DiffLineKind.Hunk, null, null),
            (DiffLineKind.Removed, 20, null),
            (DiffLineKind.Added, null, 21)
        ], lines.Select(l => (l.Kind, l.OldLine, l.NewLine)));
        Assert.Equal("added one", lines[3].Text);
    }

    [Fact]
    public void CommentTarget_IsTheOldFileForRemovedLinesAndTheNewFileOtherwise()
    {
        var lines = DiffPatch.Parse("@@ -1,2 +1,2 @@\n-old\n+new\n same\n\\ No newline at end of file\n");

        Assert.Equal((DiffSide.Left, (int?)1), (lines[1].Side, lines[1].CommentLine));
        Assert.Equal((DiffSide.Right, (int?)1), (lines[2].Side, lines[2].CommentLine));
        Assert.Equal((DiffSide.Right, (int?)2), (lines[3].Side, lines[3].CommentLine));
        Assert.Equal(DiffLineKind.Note, lines[4].Kind);
        Assert.False(lines[4].CanComment);
        Assert.False(lines[0].CanComment);
        Assert.Equal(5, lines.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_WithoutPatch_IsEmpty(string? patch)
    {
        Assert.Empty(DiffPatch.Parse(patch));
    }

    [Fact]
    public void Parse_KeepsAnEmptyContextLine()
    {
        var lines = DiffPatch.Parse("@@ -1,3 +1,3 @@\n a\n\n b");

        Assert.Equal([(1, 1), (2, 2), (3, 3)], lines.Skip(1).Select(l => (l.OldLine!.Value, l.NewLine!.Value)));
    }
}
