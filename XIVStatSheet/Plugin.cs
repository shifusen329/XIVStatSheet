using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using XIVStatSheet.Export;
using XIVStatSheet.Windows;

namespace XIVStatSheet;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IGameInventory GameInventory { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/statsheet";
    private const string ChatTag = "XIVStatSheet";

    public Configuration Configuration { get; init; }
    public ExportResult? LastExport { get; private set; }

    public readonly WindowSystem WindowSystem = new("XIVStatSheet");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the stat sheet window. Use \"/statsheet export\" to save your current stats and gear to file."
        });

        // Tell the UI system that we want our windows to be drawn through the window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    // Shared by the window button and the slash command, both of which run on the game thread
    public ExportResult Export()
    {
        var sheet = StatSheetCollector.Collect(out var error);
        if (sheet == null)
            return LastExport = new ExportResult(DateTime.Now, [], null, error);

        try
        {
            var paths = StatSheetWriter.Write(sheet, Configuration);
            return LastExport = new ExportResult(DateTime.Now, paths, sheet, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to write stat sheet");
            return LastExport = new ExportResult(DateTime.Now, [], sheet, ex.Message);
        }
    }

    private void OnCommand(string command, string args)
    {
        if (!args.Trim().Equals("export", StringComparison.OrdinalIgnoreCase))
        {
            MainWindow.Toggle();
            return;
        }

        var result = Export();
        if (result.Error != null)
        {
            ChatGui.PrintError($"Export failed: {result.Error}", ChatTag);
            return;
        }

        foreach (var path in result.Paths)
            ChatGui.Print($"Saved {path}", ChatTag);
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
