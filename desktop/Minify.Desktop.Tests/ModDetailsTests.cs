using Avalonia.Headless.XUnit;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;
using Minify.Desktop.ViewModels;

namespace Minify.Desktop.Tests;

public sealed class MarkdownTextTests
{
    [Fact]
    public void Github_alerts_become_bold_headings()
    {
        var md = "Intro\n\n> [!WARNING]\n> Default terrain is required.\n";
        Assert.Equal("Intro\n\n> **Warning**\n> Default terrain is required.\n", MarkdownText.ConvertAlerts(md));
    }

    [Fact]
    public void Unknown_markers_and_normal_quotes_are_untouched()
    {
        const string md = "> [!SOMETHING]\n> plain quote [!NOTE] inline\n";
        Assert.Equal(md, MarkdownText.ConvertAlerts(md));
    }
}

public sealed class ModMethodTests
{
    [Fact]
    public void Plain_json_is_reindented_and_split_into_numbered_lines()
    {
        var method = new ModMethodViewModel(new ModMethod("xml.json", "json", "{\"a\":[1]}", null, "1 line"));

        Assert.Equal(1, method.Lines[0].Number);
        Assert.Equal("{", method.Lines[0].Text);
        Assert.True(method.Lines.Count > 3);
        Assert.True(method.HasBadge);
    }

    [Fact]
    public void Json_with_comments_is_shown_as_written()
    {
        const string jsonc = "{\n  // keep me\n  \"a\": 1\n}";
        Assert.Equal(jsonc, ModMethodViewModel.PrettyJson(jsonc));
    }

    [Fact]
    public void Trees_show_children_with_sizes()
    {
        var tree = new FileNode("files", "directory", "", null,
        [
            new FileNode("materials", "directory", "materials", null, [new FileNode("a.vtex_c", "file", "materials/a.vtex_c", 2048, null)]),
        ]);
        var method = new ModMethodViewModel(new ModMethod("files", "tree", null, tree, "1 file"));

        Assert.True(method.IsTree);
        Assert.Equal("materials", method.Tree![0].Name);
        Assert.Equal("2.0 KB", method.Tree[0].Children[0].Size);
    }

    [Fact]
    public void Empty_files_are_flagged()
    {
        Assert.True(new ModMethodViewModel(new ModMethod("blacklist.txt", "blacklist", "", null, "0 lines")).IsEmpty);
    }

    [Theory]
    [InlineData(0L, "")]
    [InlineData(512L, "512 B")]
    [InlineData(1536L, "1.5 KB")]
    [InlineData(3145728L, "3.0 MB")]
    public void Sizes_are_human_readable(long bytes, string expected) =>
        Assert.Equal(expected, FileNodeViewModel.FormatSize(bytes));
}

public sealed class ModDetailsViewModelTests
{
    private static AppSession Session(FakeBackend backend, FakeDialogs? dialogs = null) =>
        new(backend, new Localizer(), dialogs ?? new FakeDialogs());

    [AvaloniaFact]
    public async Task Load_fills_title_notes_and_methods()
    {
        var backend = new FakeBackend().On("get_mod_details", new
        {
            name = "Dark Terrain",
            display_name = "Dark Terrain (beta)",
            notes = "> [!WARNING]\n> Default terrain is required.",
            preview = (string?)null,
            has_notes = true,
            has_preview = false,
            methods = new[] { new { name = "styling.css", type = "css", content = "a{}", badge = "1 line" } },
        });
        var vm = new ModDetailsViewModel(Session(backend), "Dark Terrain");

        await vm.LoadAsync();

        Assert.False(vm.IsLoading);
        Assert.Equal("Dark Terrain (beta)", vm.Title);
        Assert.StartsWith("> **Warning**", vm.Notes);
        Assert.Equal("styling.css", vm.Methods.Single().Name);
        Assert.False(vm.IsEmpty);
        Assert.Equal(["Dark Terrain", "en"], backend.CallsTo("get_mod_details").Single());
    }

    [AvaloniaFact]
    public async Task Mod_without_anything_is_empty()
    {
        var backend = new FakeBackend().On("get_mod_details", new
        {
            name = "x", display_name = "x", notes = (string?)null, preview = (string?)null,
            has_notes = false, has_preview = false, methods = Array.Empty<object>(),
        });
        var vm = new ModDetailsViewModel(Session(backend), "x");

        await vm.LoadAsync();

        Assert.True(vm.IsEmpty);
        vm.ToggleLightboxCommand.Execute(null);
        Assert.False(vm.IsLightboxOpen);
    }

    [AvaloniaFact]
    public async Task Backend_errors_are_shown()
    {
        var backend = new FakeBackend().On("get_mod_details", _ => throw new IOException("gone"));
        var vm = new ModDetailsViewModel(Session(backend), "x");

        await vm.LoadAsync();

        Assert.Equal("gone", vm.Error);
        Assert.False(vm.IsLoading);
    }

    [AvaloniaFact]
    public async Task Clicking_a_locked_card_opens_details_instead_of_toggling()
    {
        var backend = FakeBackend.WithDefaults();
        var dialogs = new FakeDialogs();
        var page = new ModsPageViewModel(Session(backend, dialogs));
        await page.LoadAsync();

        await page.ActivateCommand.ExecuteAsync(page.Mods.Single(m => m.Name == "#base"));
        await page.ActivateCommand.ExecuteAsync(page.Mods.Single(m => m.Name == "Dark Terrain"));

        Assert.Equal("#base", Assert.Single(dialogs.DetailsShown).Title);
        Assert.Single(backend.CallsTo("set_mods"));
    }
}
