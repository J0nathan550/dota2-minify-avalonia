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
    }
}
