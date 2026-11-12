using Djohnnie.SoftwareFactory.Application.Features.Speech;
using Djohnnie.SoftwareFactory.Contracts;
using Microsoft.JSInterop;
using MudBlazor;

namespace Djohnnie.SoftwareFactory.Web.Components.Shared;

/// <summary>
/// Speech-to-text for a text field (DictationTextField.razor, the chat input of AssistantChat.razor): wwwroot/js/dictation.js
/// recognizes the speech in the browser, directly with Azure AI Speech, using a short-lived token from
/// <see cref="SpeechService"/> (the Speech key stays on the server). Problems are shown in a snackbar.
/// </summary>
public sealed class SpeechDictation : IAsyncDisposable
{
    private const string ModulePath = "./js/dictation.js";

    /// <summary>The longest a continuous dictation listens before it stops by itself.</summary>
    public const int MaxContinuousSeconds = 300;

    private readonly SpeechService _speech;
    private readonly IJSRuntime _js;
    private readonly ISnackbar _snackbar;
    private readonly ILogger _logger;
    private IJSObjectReference? _module;
    private DotNetObjectReference<SpeechDictation>? _selfReference;

    public SpeechDictation(SpeechService speech, IJSRuntime js, ISnackbar snackbar, ILogger logger)
    {
        _speech = speech;
        _js = js;
        _snackbar = snackbar;
        _logger = logger;
    }

    /// <summary>Whether Azure AI Speech is configured.</summary>
    public bool IsEnabled
    {
        get
        {
            return _speech.IsEnabled;
        }
    }

    public bool IsListening { get; private set; }

    /// <summary>While a continuous dictation listens: what has been said so far.</summary>
    public string Transcript { get; private set; } = string.Empty;

    /// <summary>Raised when <see cref="Transcript"/> changes during a continuous dictation.</summary>
    public event Action? TranscriptChanged;

    /// <summary>Listens until the speaker pauses; the recognized text, or null (nothing recognized, canceled or failed).</summary>
    public async Task<string?> ListenOnceAsync()
    {
        return await ListenAsync(continuous: false);
    }

    /// <summary>Listens until <see cref="StopAsync"/>; everything that was said, or null (nothing recognized, canceled or failed).</summary>
    public async Task<string?> ListenContinuouslyAsync()
    {
        return await ListenAsync(continuous: true);
    }

    private async Task<string?> ListenAsync(bool continuous)
    {
        IsListening = true;
        Transcript = string.Empty;
        try
        {
            var token = await _speech.GetTokenAsync();
            _module ??= await _js.InvokeAsync<IJSObjectReference>("import", ModulePath);
            DictationTextField.DictationResult result;
            if (continuous)
            {
                _selfReference ??= DotNetObjectReference.Create(this);
                result = await _module.InvokeAsync<DictationTextField.DictationResult>("recognizeContinuous",
                    TimeSpan.FromSeconds(MaxContinuousSeconds + 60), _selfReference, token.Token, token.Region, token.Languages, MaxContinuousSeconds);
            }
            else
            {
                result = await _module.InvokeAsync<DictationTextField.DictationResult>("recognizeOnce",
                    TimeSpan.FromMinutes(1), token.Token, token.Region, token.Languages);
            }

            switch (result.Status)
            {
                case "recognized" when !string.IsNullOrWhiteSpace(result.Text):
                    return result.Text.Trim();
                case "recognized":
                case "nomatch":
                    _snackbar.Add("No speech was recognized. Check your microphone and try again.", Severity.Warning);
                    break;
                case "error":
                    _logger.LogWarning("Dictation failed: {Error}", result.Error);
                    _snackbar.Add($"Dictation failed: {result.Error}", Severity.Error);
                    break;
            }
        }
        catch (Exception ex) when (ex is DomainRuleException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Could not get an Azure AI Speech token.");
            _snackbar.Add($"Dictation is unavailable: {ex.Message}", Severity.Error);
        }
        catch (JSDisconnectedException)
        {
        }
        catch (Exception ex) when (ex is JSException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Dictation failed.");
            _snackbar.Add("Dictation failed. Allow microphone access for this site and try again.", Severity.Error);
        }
        finally
        {
            IsListening = false;
            Transcript = string.Empty;
        }

        return null;
    }

    /// <summary>Stops listening; a continuous dictation still returns what was said so far.</summary>
    public async Task StopAsync()
    {
        await InvokeAsync("stop");
    }

    /// <summary>Stops listening without a result.</summary>
    public async Task CancelAsync()
    {
        await InvokeAsync("cancel");
    }

    [JSInvokable]
    public void OnDictationProgress(string text)
    {
        if (IsListening)
        {
            Transcript = text;
            TranscriptChanged?.Invoke();
        }
    }

    /// <summary>A fresh token for a dictation that lasts longer than its token.</summary>
    [JSInvokable]
    public async Task<string?> GetSpeechToken()
    {
        try
        {
            return (await _speech.GetTokenAsync()).Token;
        }
        catch (Exception ex) when (ex is DomainRuleException or HttpRequestException)
        {
            _logger.LogWarning(ex, "Could not renew the Azure AI Speech token.");
            return null;
        }
    }

    /// <summary>Adds dictated text after what was already typed, as a new sentence, within <paramref name="maxLength"/>.</summary>
    public static string Append(string? text, string dictated, int? maxLength = null)
    {
        var before = (text ?? string.Empty).TrimEnd();
        var result = before.Length == 0 ? dictated.Trim() : $"{before} {dictated.Trim()}";
        if (maxLength is { } max && result.Length > max)
        {
            result = result[..max].TrimEnd();
        }

        return result;
    }

    private async Task InvokeAsync(string identifier)
    {
        try
        {
            if (_module is not null)
            {
                await _module.InvokeVoidAsync(identifier);
            }
        }
        catch (JSDisconnectedException)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _selfReference?.Dispose();
        if (_module is null)
        {
            return;
        }

        try
        {
            await _module.InvokeVoidAsync("cancel");
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
