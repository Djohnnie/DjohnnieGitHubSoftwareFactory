using Djohnnie.SoftwareFactory.Application.Features.WorkItems;
using Djohnnie.SoftwareFactory.Contracts.AiModels;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.WorkItems;

namespace Djohnnie.SoftwareFactory.Web.Components.Pages;

/// <summary>
/// The Model Selector's suggestion for the model of a story, bug or task on the work item editor. It is debounced: every
/// change to the description restarts <see cref="Delay"/>, and only a description that stays unchanged that long is sent.
/// Other fields don't trigger it. An existing draft with a description gets a suggestion right away when it is opened. The
/// model is never applied by itself: the user clicks the suggestion (<see cref="Apply"/>).
/// </summary>
/// <param name="onUi">
/// Runs a change of this state on the component's renderer and renders it again (<c>InvokeAsync</c> plus
/// <c>StateHasChanged</c>), because suggestions arrive in the background.
/// </param>
public sealed class WorkItemModelSuggestions(WorkItemService workItemService, ILogger logger, Func<Action, Task> onUi) : IDisposable
{
    /// <summary>How long the description must stay unchanged before a model is suggested.</summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(3);

    private CancellationTokenSource? _cancellation;

    /// <summary>The description the current (or last) suggestion was asked for, so an unchanged description isn't sent again.</summary>
    private string? _suggestedDescription;

    /// <summary>The Model Selector is configured, enabled and has enabled models to choose from.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The enabled managed models, to show a suggested model by its name.</summary>
    public IReadOnlyList<AiModel> Models { get; private set; } = [];

    /// <summary>A suggestion is being written.</summary>
    public bool IsSuggesting { get; private set; }

    public ModelSelection? Suggestion { get; private set; }

    public void Configure(bool isActive, IReadOnlyList<AiModel> models)
    {
        IsActive = isActive;
        Models = models;
    }

    /// <summary>Starts over for the item as it was loaded (opened or reset): the suggestion only follows changes made from here on.</summary>
    public void Reset(WorkItem? item)
    {
        Cancel();
        Suggestion = null;
        _suggestedDescription = item?.Description;
    }

    /// <summary>An existing draft with a description gets a suggestion right away; new items only after their description changes.</summary>
    public void SuggestOnOpen(WorkItem? item)
    {
        if (item is null || item.Id == Guid.Empty || !IsActive || !item.CanEditAssignment || string.IsNullOrWhiteSpace(item.Description))
        {
            return;
        }
        Start(item, TimeSpan.Zero);
    }

    /// <summary>Called after every change to the form; only a changed description (re)starts the delay.</summary>
    public void Schedule(WorkItem? item)
    {
        if (item is null || !IsActive || !item.CanEditAssignment || string.Equals(item.Description, _suggestedDescription, StringComparison.Ordinal))
        {
            return;
        }

        Cancel();
        _suggestedDescription = item.Description;
        if (string.IsNullOrWhiteSpace(item.Description))
        {
            Suggestion = null;
            return;
        }
        Start(item, Delay);
    }

    public void Apply(WorkItem item)
    {
        if (Suggestion is { } suggestion && item.CanEditAssignment)
        {
            item.Model = suggestion.ModelId;
        }
    }

    public string ModelName(string modelId)
    {
        return Models.FirstOrDefault(m => string.Equals(m.ModelId, modelId, StringComparison.OrdinalIgnoreCase))?.Name ?? modelId;
    }

    public void Cancel()
    {
        _cancellation?.Cancel();
        _cancellation?.Dispose();
        _cancellation = null;
        IsSuggesting = false;
    }

    public void Dispose()
    {
        Cancel();
    }

    private void Start(WorkItem item, TimeSpan delay)
    {
        _cancellation = new CancellationTokenSource();
        _ = SuggestAsync(item.Copy(), delay, _cancellation.Token);
    }

    private async Task SuggestAsync(WorkItem item, TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
            await onUi(() =>
            {
                IsSuggesting = true;
            });
            var suggestion = await workItemService.SelectModelAsync(item, cancellationToken);
            await onUi(() =>
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    Suggestion = suggestion;
                    IsSuggesting = false;
                }
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            // Fire and forget: nothing else observes this task, so any failure (also an unexpected one, which is logged as
            // such) must not leave the spinner running. The suggestion is optional; the user still picks the model.
            logger.LogWarning(ex, "Suggesting a model for work item {Id} failed.", item.Id);
            await onUi(() =>
            {
                IsSuggesting = false;
            });
        }
    }
}
