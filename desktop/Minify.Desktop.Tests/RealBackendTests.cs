using Minify.Desktop.Backend;

namespace Minify.Desktop.Tests;

/// <summary>Starts the real Python backend from this checkout (needs `uv sync`).</summary>
[Collection(nameof(EnvironmentCollection))]
public sealed class RealBackendTests
{
    [Fact]
    public async Task Backend_starts_and_answers_calls()
    {
        BackendCommand command;
        try
        {
            command = BackendCommand.Resolve();
        }
        catch (FileNotFoundException ex)
        {
            Assert.Skip($"No Python backend available: {ex.Message}");
            return;
        }

        await using var backend = await BackendClient.StartAsync(command, InlineUiDispatcher.Instance);

        Assert.False(string.IsNullOrEmpty(backend.BackendVersion));
        Assert.Equal(backend.BackendVersion, await backend.CallAsync<string>("get_version"));
        var languages = await backend.CallAsync<List<string>>("get_available_languages");
        Assert.Contains("en", languages!);
        await Assert.ThrowsAsync<BackendException>(() => backend.CallAsync("_private"));

        // The C# models must match what Python actually sends.
        var details = await backend.CallAsync<ModDetails>("get_mod_details", "Dark Terrain", "en");
        Assert.Equal("Dark Terrain", details!.Name);
        Assert.True(details.HasNotes);
        var files = Assert.Single(details.Methods!, m => m.Name == "files");
        Assert.Equal("tree", files.Type);
        Assert.NotEmpty(files.Tree!.Children!);
        Assert.NotNull(await backend.CallAsync<List<PluginInfo>>("get_plugins"));

        // Every setting Python offers must have a native control.
        var settings = await backend.CallAsync("get_settings");
        var loc = new Minify.Desktop.Services.Localizer();
        foreach (var item in settings.GetProperty("schema").EnumerateArray())
        {
            var spec = Minify.Desktop.ViewModels.SettingSpec.Parse(item, loc);
            Assert.NotNull(Minify.Desktop.ViewModels.SettingItemViewModel.Create(
                spec, default, new(backend, loc, new FakeDialogs()), (_, _) => Task.CompletedTask));
        }
        Assert.Contains("--bg-primary", Minify.Desktop.Services.ThemeCss.Parse(
            await backend.CallAsync<string>("get_theme_css", "dark") ?? "").Colors.Keys);
    }
}
