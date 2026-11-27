using ImageConverter.Core.Batch;
using ImageConverter.Core.Localization;
using ImageConverter.Core.Settings;

namespace ImageConverter.Core.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Missing_file_gives_defaults()
    {
        using var d = new TempDir();
        var s = new SettingsStore(Path.Combine(d.Path, "s.json")).Load();

        Assert.Equal(new[] { "png", "jpg", "bmp" }, s.QuickMenu.Formats);
    }

    [Fact]
    public void Roundtrips_settings()
    {
        using var d = new TempDir();
        var store = new SettingsStore(Path.Combine(d.Path, "x", "s.json"));
        store.Save(new AppSettings { QuickMenu = { Formats = { "webp" } }, Conflict = ConflictPolicy.Skip, MaxParallelism = 4 });

        var s = store.Load();

        Assert.Contains("webp", s.QuickMenu.Formats);
        Assert.Equal(ConflictPolicy.Skip, s.Conflict);
        Assert.Equal(4, s.EffectiveParallelism);
    }

    [Fact]
    public void Writes_the_quickMenu_contract_shape()
    {
        using var d = new TempDir();
        var path = Path.Combine(d.Path, "s.json");
        new SettingsStore(path).Save(new AppSettings());

        var root = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement;
        var qm = root.GetProperty("quickMenu");

        Assert.Equal("png", qm.GetProperty("formats")[0].GetString());
        Assert.True(qm.GetProperty("showDetails").GetBoolean());
        Assert.False(qm.GetProperty("deleteOriginal").GetBoolean());
    }

    [Fact]
    public void Roundtrips_deleteOriginal()
    {
        using var d = new TempDir();
        var store = new SettingsStore(Path.Combine(d.Path, "s.json"));
        store.Save(new AppSettings { QuickMenu = { DeleteOriginal = true } });

        Assert.True(store.Load().QuickMenu.DeleteOriginal);
    }

    [Fact]
    public void Writes_and_roundtrips_move_original_settings()
    {
        using var d = new TempDir();
        var path = Path.Combine(d.Path, "s.json");
        var store = new SettingsStore(path);
        var folder = Path.Combine(d.Path, "원본 보관");
        store.Save(new AppSettings
        {
            QuickMenu = { MoveOriginal = true, MoveOriginalTo = folder },
            OriginalAction = OriginalFileAction.Move,
            OriginalMoveFolder = folder,
        });

        var qm = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("quickMenu");
        Assert.True(qm.GetProperty("moveOriginal").GetBoolean());
        Assert.Equal(folder, qm.GetProperty("moveOriginalTo").GetString());

        var loaded = store.Load();
        Assert.True(loaded.QuickMenu.MoveOriginal);
        Assert.Equal(folder, loaded.QuickMenu.MoveOriginalTo);
        Assert.Equal(OriginalFileAction.Move, loaded.OriginalAction);
        Assert.Equal(folder, loaded.OriginalMoveFolder);
    }

    [Fact]
    public void Originals_are_kept_by_default()
    {
        var s = new AppSettings();
        Assert.False(s.QuickMenu.MoveOriginal);
        Assert.Equal(OriginalFileAction.Keep, s.OriginalAction);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        using var d = new TempDir();
        var path = Path.Combine(d.Path, "s.json");
        File.WriteAllText(path, "{ not json");

        Assert.Equal("png", new SettingsStore(path).Load().DefaultTargetFormat);
    }

    [Fact]
    public void Null_quickMenu_or_formats_fall_back_to_defaults()
    {
        using var d = new TempDir();
        var path = Path.Combine(d.Path, "s.json");
        File.WriteAllText(path, "{ \"quickMenu\": { \"formats\": null } }");

        Assert.Equal(3, new SettingsStore(path).Load().QuickMenu.Formats.Count);

        File.WriteAllText(path, "{ \"quickMenu\": null }");
        Assert.Equal(3, new SettingsStore(path).Load().QuickMenu.Formats.Count);
    }

    [Fact]
    public void Writes_notify_and_update_keys_the_shell_extension_reads()
    {
        using var d = new TempDir();
        var store = new SettingsStore(Path.Combine(d.Path, "s.json"));
        store.Save(new AppSettings { QuickMenu = { Notify = QuickNotify.Always }, CheckForUpdates = false });

        var json = File.ReadAllText(store.Path);

        Assert.Contains("\"notify\": \"always\"", json);
        Assert.Contains("\"checkForUpdates\": false", json);
        var back = store.Load();
        Assert.Equal(QuickNotify.Always, back.QuickMenu.Notify);
        Assert.False(back.CheckForUpdates);
    }

    [Fact]
    public void Writes_language_key_the_shell_extension_reads()
    {
        using var d = new TempDir();
        var store = new SettingsStore(Path.Combine(d.Path, "s.json"));
        Assert.Equal(UiLanguage.Auto, store.Load().Language);
        store.Save(new AppSettings { Language = UiLanguage.English });

        Assert.Contains("\"language\": \"english\"", File.ReadAllText(store.Path));
        Assert.Equal(UiLanguage.English, store.Load().Language);
    }

    [Fact]
    public void Notify_defaults_to_failures_only_and_updates_on()
    {
        var s = new AppSettings();
        Assert.Equal(QuickNotify.Failures, s.QuickMenu.Notify);
        Assert.True(s.CheckForUpdates);
    }
}
