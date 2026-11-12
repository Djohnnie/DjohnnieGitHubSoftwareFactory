using System.Net;
using Bunit;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.Speech;
using Djohnnie.SoftwareFactory.Contracts;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>Speech tokens issued by the server, and the contract between DictationTextField.razor and wwwroot/js/dictation.js.</summary>
public class DictationTests : BunitContext
{
    private readonly FakeTokenEndpoint _endpoint = new();
    private readonly ManualTime _time = new();

    public DictationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddLogging();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddApplication();
        Services.AddHttpClient(AzureSpeechTokenIssuer.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => _endpoint);
    }

    private SpeechService Speech => Services.GetRequiredService<SpeechService>();

    private void Configure(string? endpoint = null, string language = "en-US")
    {
        Services.Configure<SpeechOptions>(options =>
        {
            options.Region = "westeurope";
            options.ApiKey = "speech-key";
            options.Endpoint = endpoint;
            options.Language = language;
        });
    }

    [Fact]
    public async Task GetToken_ExchangesTheKeyAtTheRegionalEndpoint()
    {
        Configure(language: "en-US, nl-BE,en-us");

        var token = await Speech.GetTokenAsync(Xunit.TestContext.Current.CancellationToken);

        var request = Assert.Single(_endpoint.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://westeurope.api.cognitive.microsoft.com/sts/v1.0/issueToken", request.Uri);
        Assert.Equal("speech-key", request.Key);
        Assert.Equal("token-1", token.Token);
        Assert.Equal("westeurope", token.Region);
        Assert.Equal(["en-US", "nl-BE"], token.Languages);
        Assert.Equal(_time.Now + TimeSpan.FromMinutes(10), token.ExpiresAt);
    }

    [Fact]
    public async Task GetToken_UsesTheCustomEndpoint()
    {
        Configure(endpoint: "https://dghsf-speech.cognitiveservices.azure.com");

        await Speech.GetTokenAsync(Xunit.TestContext.Current.CancellationToken);

        Assert.Equal("https://dghsf-speech.cognitiveservices.azure.com/sts/v1.0/issueToken", Assert.Single(_endpoint.Requests).Uri);
    }

    [Fact]
    public async Task GetToken_ReusesATokenUntilTwoMinutesBeforeItExpires()
    {
        Configure();
        var cancellationToken = Xunit.TestContext.Current.CancellationToken;

        var first = await Speech.GetTokenAsync(cancellationToken);
        _time.Now += TimeSpan.FromMinutes(7);
        Assert.Equal(first.Token, (await Speech.GetTokenAsync(cancellationToken)).Token);

        _time.Now += TimeSpan.FromMinutes(1);
        Assert.Equal("token-2", (await Speech.GetTokenAsync(cancellationToken)).Token);
        Assert.Equal(2, _endpoint.Requests.Count);
    }

    [Fact]
    public async Task GetToken_ReportsARefusedKey()
    {
        Configure();
        _endpoint.Status = HttpStatusCode.Unauthorized;

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Speech.GetTokenAsync(Xunit.TestContext.Current.CancellationToken));

        Assert.Contains("401", error.Message);
    }

    [Fact]
    public async Task GetToken_RequiresConfiguration()
    {
        Assert.False(Speech.IsEnabled);
        await Assert.ThrowsAsync<DomainRuleException>(() => Speech.GetTokenAsync(Xunit.TestContext.Current.CancellationToken));
        Assert.Empty(_endpoint.Requests);
    }

    [Fact]
    public void Field_HasNoMicrophoneWithoutConfiguration()
    {
        var cut = Render<DictationTextField>(p => p.Add(f => f.Label, "Title"));

        Assert.NotEmpty(cut.FindAll("input"));
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Field_HasNoMicrophoneWhenReadOnly()
    {
        Configure();
        var cut = Render<DictationTextField>(p => p.Add(f => f.Label, "Title").Add(f => f.ReadOnly, true));

        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void Field_RecognizesSpeechInTheBrowserWithTheTokenAndReplacesTheValue()
    {
        Configure();
        var module = JSInterop.SetupModule("./js/dictation.js");
        var recognition = module.Setup<DictationTextField.DictationResult>("recognizeOnce", _ => true);
        string? value = "Old title";
        string? dictated = null;
        var cut = Render<DictationTextField>(p => p
            .Add(f => f.Label, "Title")
            .Add(f => f.Value, value)
            .Add(f => f.ValueChanged, v => { value = v; })
            .Add(f => f.OnDictated, title => { dictated = title; }));

        cut.Find(".mud-input-adornment-end button[aria-label='Dictate the title']").Click();

        var invocation = Assert.Single(module.Invocations["recognizeOnce"]);
        Assert.Equal("token-1", invocation.Arguments[0]);
        Assert.Equal("westeurope", invocation.Arguments[1]);
        Assert.Equal(new[] { "en-US" }, invocation.Arguments[2]);
        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".dictation-listening button[aria-label='Stop dictating']")));

        recognition.SetResult(new("recognized", "Add a login page with GitHub."));

        cut.WaitForAssertion(() => Assert.Equal("Add a login page with GitHub", dictated));
        Assert.Equal("Add a login page with GitHub", value);
        Assert.Empty(cut.FindAll(".dictation-listening"));
    }

    [Fact]
    public void Field_WhileListening_StopsWithoutAValue()
    {
        Configure();
        var module = JSInterop.SetupModule("./js/dictation.js");
        var recognition = module.Setup<DictationTextField.DictationResult>("recognizeOnce", _ => true);
        module.SetupVoid("cancel").SetVoidResult();
        var dictated = false;
        var cut = Render<DictationTextField>(p => p
            .Add(f => f.DictationLabel, "title example")
            .Add(f => f.OnDictated, _ => { dictated = true; }));

        cut.Find("button").Click();
        cut.WaitForAssertion(() => Assert.Equal("Stop dictating", cut.Find("button").GetAttribute("aria-label")));
        cut.Find("button").Click();
        recognition.SetResult(new("canceled"));

        Assert.Single(module.Invocations["cancel"]);
        cut.WaitForAssertion(() => Assert.Equal("Dictate the title example", cut.Find("button").GetAttribute("aria-label")));
        Assert.False(dictated);
    }

    [Fact]
    public void Field_ShowsRecognitionErrors()
    {
        Configure();
        var module = JSInterop.SetupModule("./js/dictation.js");
        module.Setup<DictationTextField.DictationResult>("recognizeOnce", _ => true).SetResult(new("error", Error: "Microphone access denied"));
        var cut = Render<DictationTextField>();

        cut.Find("button").Click();

        cut.WaitForAssertion(() => Assert.Contains(Services.GetRequiredService<ISnackbar>().ShownSnackbars,
            s => s.Message!.Contains("Microphone access denied") && s.Severity == Severity.Error));
    }

    [Fact]
    public void MultiLineField_ListensUntilTheMicrophoneIsClickedAgainAndAddsWhatWasSaid()
    {
        Configure();
        var module = JSInterop.SetupModule("./js/dictation.js");
        var recognition = module.Setup<DictationTextField.DictationResult>("recognizeContinuous", _ => true);
        module.SetupVoid("stop").SetVoidResult();
        string? value = "Use the existing styles.";
        var cut = Render<DictationTextField>(p => p
            .Add(f => f.Label, "Custom instructions")
            .Add(f => f.Lines, 3)
            .Add(f => f.Value, value)
            .Add(f => f.ValueChanged, v => { value = v; }));

        cut.Find("button[aria-label='Dictate the custom instructions']").Click();

        var invocation = Assert.Single(module.Invocations["recognizeContinuous"]);
        Assert.Equal("token-1", invocation.Arguments[1]);
        Assert.Equal(SpeechDictation.MaxContinuousSeconds, invocation.Arguments[4]);
        cut.WaitForAssertion(() => Assert.Contains("Listening", cut.Markup));

        cut.Find("button[aria-label='Stop dictating and add what was said']").Click();
        Assert.Single(module.Invocations["stop"]);
        recognition.SetResult(new("recognized", "Add tests for the new page."));

        cut.WaitForAssertion(() => Assert.Equal("Use the existing styles. Add tests for the new page.", value));
        Assert.Empty(cut.FindAll(".dictation-listening"));
    }

    [Fact]
    public void Field_HasNoMicrophoneWhenDisabled()
    {
        Configure();
        var cut = Render<DictationTextField>(p => p.Add(f => f.Label, "Comment").Add(f => f.Disabled, true));

        Assert.Empty(cut.FindAll("button"));
    }

    [Theory]
    [InlineData(null, "Add tests.", null, "Add tests.")]
    [InlineData("Use the styles.  ", " Add tests.", null, "Use the styles. Add tests.")]
    [InlineData("Use the styles.", "Add tests.", 20, "Use the styles. Add")]
    public void Append_AddsDictatedTextAsANewSentence(string? text, string dictated, int? maxLength, string expected)
    {
        Assert.Equal(expected, SpeechDictation.Append(text, dictated, maxLength));
    }

    [Theory]
    [InlineData("  Fix the login redirect.  ", null, "Fix the login redirect")]
    [InlineData("Why does sync fail?", null, "Why does sync fail?")]
    [InlineData("Add dark mode to the dashboard.", 8, "Add dark")]
    [InlineData(".", null, "")]
    public void ToTitle_CleansUpRecognizedSpeech(string text, int? maxLength, string expected)
    {
        Assert.Equal(expected, DictationTextField.ToTitle(text, maxLength));
    }

    private sealed class FakeTokenEndpoint : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Uri, string? Key)> Requests { get; } = [];

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.ToString(),
                request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var keys) ? keys.Single() : null));
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent($"token-{Requests.Count}") });
        }
    }

    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            return Now;
        }
    }
}
