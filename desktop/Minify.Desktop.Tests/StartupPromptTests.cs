using System.Net;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Tests;

public sealed class AnnouncementRulesTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

    [Theory]
    [InlineData("1900000000-", true)]
    [InlineData("1700000000-", false)]
    [InlineData("1700000000+", true)]
    [InlineData("1900000000+", false)]
    [InlineData("1700000000=1900000000", true)]
    [InlineData("1850000000=1900000000", false)]
    [InlineData("garbage-", false)]
    [InlineData("", false)]
    public void Time_conditions(string time, bool active) => Assert.Equal(active, Announcements.IsActive(time, Now));

    [Fact]
    public void Versions_are_prefixes_from_a_list_or_a_comma_string()
    {
        Assert.True(Announcements.MatchesVersion(default, "1.0.0.3"));
        Assert.True(Announcements.MatchesVersion(JsonSerializer.SerializeToElement(new[] { "1.0" }), "1.0.0.3"));
        Assert.False(Announcements.MatchesVersion(JsonSerializer.SerializeToElement(new[] { "1.13" }), "1.0.0.3"));
        Assert.True(Announcements.MatchesVersion(JsonSerializer.SerializeToElement("2.0, 1.0.0"), "1.0.0.3"));
    }

    [Fact]
    public void Pending_skips_seen_expired_textless_and_other_versions()
    {
        Announcement A(string time, string? text, string[]? versions = null) =>
            new(time, text, null, null, null, versions is null ? default : JsonSerializer.SerializeToElement(versions));
        var all = new[]
        {
            A("1900000000-", "show me"),
            A("1900000001-", "already seen"),
            A("1700000000-", "expired"),
            A("1900000002-", null),
            A("1900000003-", "other version", ["9.9"]),
        };

        var pending = Announcements.Pending(all, ["1900000001"], Now, "1.0.0.1");

        Assert.Equal("show me", Assert.Single(pending).Text);
        Assert.Equal("1900000000", pending[0].Id);
    }

    [Fact]
    public async Task Http_source_reads_the_repository_announcements_file()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(RepoRoot.Path, "docs", "announcements.json"));
        var http = new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));

        var list = await new HttpAnnouncementSource(http, "https://example.test/a.json").FetchAsync(CancellationToken.None);

        Assert.NotEmpty(list);
        Assert.All(list, a => Assert.False(string.IsNullOrEmpty(a.Text)));
        Assert.Contains(list, a => a.AllUrls.Count > 0);
    }
}

public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        return Task.FromResult(respond(request));
    }
}

public sealed class StartupPromptTests
{
    private readonly FakeDialogs _dialogs = new();

    private sealed class FakeAnnouncements(params Announcement[] items) : IAnnouncementSource
    {
        public bool Fail { get; init; }

        public Task<IReadOnlyList<Announcement>> FetchAsync(CancellationToken cancellationToken) =>
            Fail ? throw new HttpRequestException("offline") : Task.FromResult<IReadOnlyList<Announcement>>(items);
    }

    private async Task<MainWindowViewModel> StartAsync(FakeBackend backend, IAnnouncementSource? announcements = null)
    {
        var vm = new MainWindowViewModel(_dialogs, () => Task.FromResult<IBackend>(backend), null, announcements);
        await vm.StartAsync();
        return vm;
    }

    private static Announcement Live(string text, params string[] urls) =>
        new("9999999999-", text, null, [.. urls], null, default);

    [AvaloniaFact]
    public async Task Legacy_mods_can_be_imported()
    {
        var backend = FakeBackend.WithDefaults().On("get_legacy_mods", new[] { "mods_old_1/My Mod" });
        _dialogs.Answer = r => new DialogResult(r.Title == "Import old mods" ? "import" : null);

        await StartAsync(backend);

        Assert.Equal(true, backend.CallsTo("import_legacy_mods").Single()[0]);
        Assert.Equal("mods_old_1/My Mod", _dialogs.Shown.Single().Details);
        Assert.Equal(2, backend.CallsTo("get_mods").Count());
    }

    [AvaloniaFact]
    public async Task Legacy_mods_can_be_discarded_or_left_for_later()
    {
        var backend = FakeBackend.WithDefaults().On("get_legacy_mods", new[] { "mods_old_1/x" });
        _dialogs.Answer = _ => new DialogResult("discard");
        await StartAsync(backend);
        Assert.Equal(false, backend.CallsTo("import_legacy_mods").Single()[0]);

        var later = FakeBackend.WithDefaults().On("get_legacy_mods", new[] { "mods_old_1/x" });
        _dialogs.Answer = _ => new DialogResult(null);
        await StartAsync(later);
        Assert.Empty(later.CallsTo("import_legacy_mods"));
    }

    [AvaloniaFact]
    public async Task Announcements_are_shown_once_and_remembered()
    {
        var backend = FakeBackend.WithDefaults().On("get_state", args => args[0] as string == "announcements_seen" ? new[] { "1" } : null);

        await StartAsync(backend, new FakeAnnouncements(Live("New release!", "https://example.test")));

        var shown = Assert.Single(_dialogs.Shown);
        Assert.Equal("Announcement", shown.Title);
        Assert.Equal(["https://example.test"], shown.Links!);
        var saved = backend.CallsTo("set_state").Single();
        Assert.Equal(Announcements.SeenStateKey, saved[0]);
        Assert.Equal(["1", "9999999999"], (List<string>)saved[1]!);
    }

    [AvaloniaFact]
    public async Task Offline_announcements_are_skipped()
    {
        var vm = await StartAsync(FakeBackend.WithDefaults(), new FakeAnnouncements { Fail = true });

        Assert.Equal(BackendState.Ready, vm.State);
        Assert.Empty(_dialogs.Shown);
    }

    [AvaloniaFact]
    public async Task Missing_workshop_tools_are_offered_and_can_be_ignored()
    {
        var backend = FakeBackend.WithDefaults().On("is_workshop_installed", false);
        _dialogs.Answer = _ => new DialogResult("ignore");

        var vm = await StartAsync(backend);

        Assert.True(vm.IsWorkshopMissing);
        Assert.Equal(["ignore", "later", "download"], _dialogs.Shown.Single().Buttons.Select(b => b.Id));
        Assert.Equal(["ignore_workshop_download", true], backend.CallsTo("set_state").Single());
        Assert.Empty(backend.CallsTo("download_workshop_tools"));
    }

    [AvaloniaFact]
    public async Task Ignored_workshop_tools_are_not_offered_on_startup_but_still_from_the_header()
    {
        var backend = FakeBackend.WithDefaults().On("is_workshop_installed", false).On("get_state", true).On("download_workshop_tools", true);
        var vm = await StartAsync(backend);
        Assert.Empty(_dialogs.Shown);

        await vm.DownloadWorkshopToolsCommand.ExecuteAsync(null);

        Assert.Equal(["later", "download"], _dialogs.Shown.Single().Buttons.Select(b => b.Id));
        Assert.False(vm.IsWorkshopMissing);
    }

    [AvaloniaFact]
    public async Task Failed_workshop_download_can_be_retried()
    {
        var attempts = 0;
        var backend = FakeBackend.WithDefaults()
            .On("is_workshop_installed", false)
            .On("download_workshop_tools", _ => ++attempts >= 2);

        var vm = await StartAsync(backend);

        Assert.Equal(2, attempts);
        Assert.NotNull(_dialogs.Shown[1].Details);
        Assert.Equal("download", _dialogs.Shown[1].Buttons.Single(b => b.IsDefault).Id);
        Assert.False(vm.IsWorkshopMissing);
        Assert.Equal(2, backend.CallsTo("get_mods").Count());
    }

    [AvaloniaFact]
    public async Task Announcements_postpone_the_workshop_prompt()
    {
        var backend = FakeBackend.WithDefaults().On("is_workshop_installed", false);

        await StartAsync(backend, new FakeAnnouncements(Live("hello")));

        Assert.Equal("hello", Assert.Single(_dialogs.Shown).Text);
    }

    [AvaloniaTheory]
    [InlineData("cancel", false, false)]
    [InlineData("skip", false, true)]
    [InlineData("extract", true, true)]
    public async Task Patch_offers_to_extract_detected_workshop_tools(string answer, bool extracted, bool patched)
    {
        var backend = FakeBackend.WithDefaults().On("check_workshop_tools_needed", true).On("start_patch", new { status = "started" });
        var vm = await StartAsync(backend);
        _dialogs.Answer = _ => new DialogResult(answer);

        await vm.PatchCommand.ExecuteAsync(null);

        Assert.Equal(extracted, backend.CallsTo("extract_workshop_tools").Any());
        Assert.Equal(patched, backend.CallsTo("start_patch").Any());
    }
}
