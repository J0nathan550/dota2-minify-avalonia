using System.Text.Json;
using Minify.Desktop.Backend;
using Minify.Desktop.Services;

namespace Minify.Desktop.Tests;

public sealed class LocalizerTests
{
    [Fact]
    public void Loaded_strings_win_then_fallback_then_key()
    {
        var loc = new Localizer();
        Assert.Equal("PATCH", loc["button_patch"]);
        Assert.Equal("some_unknown_key", loc["some_unknown_key"]);

        loc.Load(new Dictionary<string, string> { ["button_patch"] = "ПАТЧ" });
        Assert.Equal("ПАТЧ", loc["button_patch"]);
    }

    [Fact]
    public void Load_notifies_indexer_bindings()
    {
        var loc = new Localizer();
        string? changed = null;
        loc.PropertyChanged += (_, e) => changed = e.PropertyName;

        loc.Load(new Dictionary<string, string>());

        Assert.Equal("Item[]", changed);
    }
}

public sealed class FileTypeParsingTests
{
    private static JsonElement Params(params string[] fileTypes) =>
        JsonSerializer.SerializeToElement(new { file_types = fileTypes });

    [Fact]
    public void Parses_pywebview_style_filters()
    {
        var types = DialogService.ParseFileTypes(Params("Font Files (*.ttf;*.otf)", "All Files (*.*)"))!;

        Assert.Equal("Font Files", types[0].Name);
        Assert.Equal(["*.ttf", "*.otf"], types[0].Patterns!);
        Assert.Equal(["*"], types[1].Patterns!);
    }

    [Fact]
    public void Missing_or_malformed_filters_mean_no_filter()
    {
        Assert.Null(DialogService.ParseFileTypes(default));
        Assert.Null(DialogService.ParseFileTypes(Params("no parentheses here")));
    }
}

[Collection(nameof(EnvironmentCollection))]
public sealed class BackendCommandTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("minify-cmd").FullName;

    [Fact]
    public void Override_pointing_to_an_executable_runs_it_in_rpc_mode()
    {
        var exe = Path.Combine(_dir, "Minify");
        File.WriteAllText(exe, "");
        Environment.SetEnvironmentVariable("MINIFY_BACKEND", exe);

        var command = BackendCommand.Resolve();

        Assert.Equal(exe, command.FileName);
        Assert.Equal(["rpc"], command.Arguments);
    }

    [Fact]
    public void Override_pointing_to_a_source_folder_runs_python()
    {
        var source = Directory.CreateDirectory(Path.Combine(_dir, "Minify")).FullName;
        Environment.SetEnvironmentVariable("MINIFY_BACKEND", source);

        var command = BackendCommand.Resolve();

        Assert.Equal(source, command.Arguments[^2]);
        Assert.Equal("rpc", command.Arguments[^1]);
        Assert.Equal(_dir, command.WorkingDirectory);
    }

    [Fact]
    public void Override_pointing_nowhere_fails_loudly()
    {
        Environment.SetEnvironmentVariable("MINIFY_BACKEND", Path.Combine(_dir, "missing"));
        Assert.Throws<FileNotFoundException>(BackendCommand.Resolve);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("MINIFY_BACKEND", null);
        Directory.Delete(_dir, recursive: true);
    }
}

[CollectionDefinition(nameof(EnvironmentCollection), DisableParallelization = true)]
public sealed class EnvironmentCollection;
