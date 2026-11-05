using Bunit;
using Djohnnie.SoftwareFactory.Agents;
using Djohnnie.SoftwareFactory.Application;
using Djohnnie.SoftwareFactory.Application.Features.Speech;
using Djohnnie.SoftwareFactory.Contracts.InternalAgents;
using Djohnnie.SoftwareFactory.Contracts.Speech;
using Djohnnie.SoftwareFactory.DataAccess.Sql;
using Djohnnie.SoftwareFactory.Web.Components.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MudBlazor;
using MudBlazor.Services;

namespace Djohnnie.SoftwareFactory.Web.Tests;

/// <summary>Dictating a description in the MarkdownEditor, and the Description Writer that fits it into the description.</summary>
public class DescriptionDictationTests : BunitContext
{
    private const string Skeleton = "## Goal\n_What should happen?_\n\n## Acceptance criteria\n- [ ] ...";
    private const string Written = "## Goal\nLog in with a GitHub account.\n\n## Acceptance criteria\n- [ ] ...";

    private readonly CancellationToken _cancellationToken = Xunit.TestContext.Current.CancellationToken;
    private readonly BunitJSModuleInterop _module;
    private readonly BunitJSModuleInterop _editor;
    private readonly BunitJSModuleInterop _dictation;
    private ScriptedChatClient? _chatClient;

    public DescriptionDictationTests()
    {
        Services.AddMudServices();
        Services.AddLogging();
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        Services.AddSingleton<IDbContextFactory<AppDbContext>>(new ModelPricingScraperTests.TestDbFactory(dbOptions));
        Services.AddSingleton<ISpeechTokenIssuer>(new FakeTokenIssuer());
        Services.AddApplication();
        Services.AddSingleton(_ => new AssistantAgentFactory(_chatClient, NullLoggerFactory.Instance));

        JSInterop.Mode = JSRuntimeMode.Loose;
        _module = JSInterop.SetupModule("./Components/Shared/MarkdownEditor.razor.js");
        _editor = _module.SetupModule("create", _ => true);
        _editor.Mode = JSRuntimeMode.Loose;
        _dictation = JSInterop.SetupModule("./js/dictation.js");
        _dictation.Mode = JSRuntimeMode.Loose;
    }

    private SpeechService Speech => Services.GetRequiredService<SpeechService>();

    private void ConfigureSpeech()
    {
        Services.Configure<SpeechOptions>(options =>
        {
            options.Region = "westeurope";
            options.ApiKey = "speech-key";
        });
    }

    [Fact]
    public async Task Writer_FitsTheTranscriptIntoTheTemplate()
    {
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text($$"""{"description":{{System.Text.Json.JsonSerializer.Serialize(Written)}}}"""));

        var result = await Speech.WriteDescriptionAsync(new("Uh, users should log in with their GitHub account.", "user story", "Login", Skeleton, Skeleton),
            _cancellationToken);

        Assert.True(result.Written);
        Assert.Equal(Written, result.Description);
        var message = string.Join("\n", Assert.Single(_chatClient.Requests).Where(m => m.Role == ChatRole.User).Select(m => m.Text));
        Assert.Contains("Merge what was said into the user story description.", message);
        Assert.Contains("users should log in with their GitHub account", message);
        Assert.Contains("\"templateSkeleton\": \"## Goal", message);
        Assert.Contains("\"title\": \"Login\"", message);
        Assert.Contains("You are the Description Writer", InternalAgentTests.SystemPrompt(_chatClient, 0));
    }

    [Fact]
    public async Task Writer_WhenDisabled_AddsTheTranscriptAtTheEnd()
    {
        _chatClient = new ScriptedChatClient();
        await InternalAgentTests.DisableAsync(Services, InternalAgentKeys.DescriptionWriter);

        var result = await Speech.WriteDescriptionAsync(new(" Also support GitLab. ", "user story", null, "Log in with GitHub.\n", null), _cancellationToken);

        Assert.False(result.Written);
        Assert.Equal("Log in with GitHub.\n\nAlso support GitLab.", result.Description);
        Assert.Empty(_chatClient.Requests);
    }

    [Fact]
    public async Task Writer_WithoutAModel_UsesTheTranscriptAsTheDescription()
    {
        var result = await Speech.WriteDescriptionAsync(new("Log in with GitHub.", "bug", null, "  ", null), _cancellationToken);

        Assert.Equal(new DictatedDescriptionResult("Log in with GitHub.", false), result);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void Editor_HasAMicrophone_OnlyWithDictationAndSpeech(bool dictation, bool speech, bool expected)
    {
        if (speech)
        {
            ConfigureSpeech();
        }

        var cut = Render<MarkdownEditor>(p => p
            .Add(e => e.Dictation, dictation ? new DescriptionDictation("bug") : null)
            .Add(e => e.Header, "<h6 id='heading'>Description</h6>"));

        Assert.NotNull(cut.Find("#heading"));
        Assert.Equal(expected, cut.FindAll("button.markdown-editor-mic[aria-label='Dictate the description']").Count == 1);
    }

    [Fact]
    public async Task Editor_ShowsWhatIsSaid_ThenFitsItIntoTheTemplate_WithUndo()
    {
        ConfigureSpeech();
        _chatClient = new ScriptedChatClient(ScriptedChatClient.Text($$"""{"description":{{System.Text.Json.JsonSerializer.Serialize(Written)}}}"""));
        var recognition = _dictation.Setup<DictationTextField.DictationResult>("recognizeContinuous", _ => true);
        var value = Skeleton;
        var snackbars = Render<MudSnackbarProvider>();
        var cut = Render<MarkdownEditor>(p => p
            .Add(e => e.Value, value)
            .Add(e => e.ValueChanged, v => { value = v; })
            .Add(e => e.Dictation, new DescriptionDictation("user story", "Login", Skeleton)));

        cut.Find("button.markdown-editor-mic").Click();
        cut.WaitForAssertion(() => Assert.Contains("Listening...", cut.Markup));
        Assert.Equal("Stop dictating", cut.Find("button.markdown-editor-mic[aria-pressed='true']").GetAttribute("aria-label"));
        var invocation = Assert.Single(_dictation.Invocations["recognizeContinuous"]);
        Assert.Equal("token-1", invocation.Arguments[1]);
        Assert.Equal(MarkdownEditor.MaxDictationSeconds, invocation.Arguments[4]);

        await cut.InvokeAsync(() => cut.Instance.OnDictationProgress("Users should log in"));
        cut.WaitForAssertion(() => Assert.Contains("Users should log in", cut.Markup));

        recognition.SetResult(new("recognized", "Users should log in with their GitHub account."));

        cut.WaitForAssertion(() => Assert.Equal(Written, value));
        Assert.Equal(Written, _editor.Invocations["setMarkdown"].Last().Arguments[0]);
        cut.WaitForAssertion(() => Assert.Equal("false", cut.Find("button.markdown-editor-mic").GetAttribute("aria-pressed")));
        Assert.DoesNotContain("Listening...", cut.Markup);

        snackbars.WaitForAssertion(() => Assert.Contains("Fitted what you said into the description.", snackbars.Markup));
        snackbars.FindAll("button").First(b => b.TextContent.Trim() == "Undo").Click();

        Assert.Equal(Skeleton, value);
        Assert.Equal(Skeleton, _editor.Invocations["setMarkdown"].Last().Arguments[0]);
    }

    [Fact]
    public async Task Editor_CancelingDiscardsWhatWasSaid()
    {
        ConfigureSpeech();
        _chatClient = new ScriptedChatClient();
        var recognition = _dictation.Setup<DictationTextField.DictationResult>("recognizeContinuous", _ => true);
        string? changed = null;
        var cut = Render<MarkdownEditor>(p => p
            .Add(e => e.Value, "Before")
            .Add(e => e.ValueChanged, v => { changed = v; })
            .Add(e => e.Dictation, new DescriptionDictation("bug")));

        var dictating = cut.InvokeAsync(() => cut.Instance.ToggleDictation());
        cut.WaitForAssertion(() => Assert.Contains("Listening...", cut.Markup));
        cut.FindAll("button").First(b => b.TextContent.Trim() == "Cancel").Click();
        recognition.SetResult(new("canceled"));
        await dictating;

        Assert.Single(_dictation.Invocations["cancel"]);
        Assert.Null(changed);
        Assert.Empty(_chatClient.Requests);
        Assert.DoesNotContain("Listening...", cut.Markup);
    }

    private sealed class FakeTokenIssuer : ISpeechTokenIssuer
    {
        private int _issued;

        public Task<IssuedSpeechToken> IssueAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(new IssuedSpeechToken($"token-{++_issued}", DateTimeOffset.UtcNow.AddMinutes(10)));
        }
    }
}
