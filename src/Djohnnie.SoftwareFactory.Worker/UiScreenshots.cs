using System.ComponentModel;
using Djohnnie.SoftwareFactory.Application.Features.Uploads;
using Microsoft.Extensions.AI;

namespace Djohnnie.SoftwareFactory.Worker;

/// <summary>A screenshot the agent attached: the file it came from, the image as it was then, and what it shows.</summary>
public sealed record UiScreenshot(string Source, byte[] Content, string Caption);

/// <summary>A screenshot on the pull request: what it shows and its public URL.</summary>
public sealed record PostedScreenshot(string Caption, string Url);

/// <summary>
/// The screenshots of the user interface that the agent attaches with the <see cref="ToolName"/> tool while it works. The
/// tool reads each image straight away; the worker uploads them to the image storage after the run and shows them in the
/// pull request. The agent saves them in <see cref="Directory"/>, outside the repository, so they are never committed.
/// </summary>
public sealed class UiScreenshots(string directory, string repository, long maxImageSize)
{
    public const string ToolName = "attach_screenshot";
    public const int MaxScreenshots = 8;
    public const int MaxCaptionLength = 200;

    private readonly List<UiScreenshot> _screenshots = [];
    private readonly Lock _lock = new();

    /// <summary>Where the agent saves its screenshots.</summary>
    public string Directory
    {
        get
        {
            return directory;
        }
    }

    public IReadOnlyList<UiScreenshot> Screenshots
    {
        get
        {
            lock (_lock)
            {
                return [.. _screenshots];
            }
        }
    }

    public AIFunction CreateTool()
    {
        return AIFunctionFactory.Create(Attach, ToolName,
            "Attaches a screenshot of the user interface you changed, which DGHSF shows in the pull request. Save it as a PNG, JPEG, GIF or WebP file in "
            + $"{directory} (not in the repository, so it isn't committed) and call this once per screenshot, at most {MaxScreenshots} times. "
            + "Calling it again with the same file replaces that screenshot.");
    }

    private string Attach(
        [Description("The path of the image file, absolute or relative to the working directory.")] string path,
        [Description("One short sentence about what the screenshot shows, e.g. 'The new export button on the orders page'.")] string caption)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "The path is empty.";
        }
        var file = Path.GetFullPath(path.Trim(), repository);
        if (IsInside(file, repository))
        {
            return $"{file} is inside the repository, so it would be committed. Move it to {directory} and attach it from there.";
        }
        if (!File.Exists(file))
        {
            return $"There is no file {file}.";
        }
        var size = new FileInfo(file).Length;
        if (size > maxImageSize)
        {
            return $"The image is {size / (1024 * 1024.0):0.#} MB; a screenshot can be at most {maxImageSize / (1024 * 1024)} MB. Take a smaller one (e.g. a viewport instead of a full page).";
        }
        var content = File.ReadAllBytes(file);
        if (ImageFormat.Detect(content) is null)
        {
            return "That isn't a PNG, JPEG, GIF or WebP image.";
        }

        var text = string.IsNullOrWhiteSpace(caption) ? Path.GetFileNameWithoutExtension(file) : caption.Trim().ReplaceLineEndings(" ");
        var screenshot = new UiScreenshot(file, content, text.Length <= MaxCaptionLength ? text : text[..MaxCaptionLength]);
        lock (_lock)
        {
            var existing = _screenshots.FindIndex(s => string.Equals(s.Source, file, StringComparison.Ordinal));
            if (existing >= 0)
            {
                _screenshots[existing] = screenshot;
                return $"Replaced the screenshot {file}. {_screenshots.Count} of {MaxScreenshots} attached.";
            }
            if (_screenshots.Count >= MaxScreenshots)
            {
                return $"There are already {MaxScreenshots} screenshots attached, the most a pull request gets.";
            }
            _screenshots.Add(screenshot);
            return $"Attached {file}. {_screenshots.Count} of {MaxScreenshots} attached.";
        }
    }

    private static bool IsInside(string file, string folder)
    {
        var relative = Path.GetRelativePath(folder, file);
        return relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }
}
