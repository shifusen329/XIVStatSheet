using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace XIVStatSheet.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("XIV Stat Sheet Settings###XIVStatSheetConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(480, 115);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextUnformatted("Output folder");

        var style = ImGui.GetStyle();
        var resetWidth = ImGui.CalcTextSize("Reset").X + style.FramePadding.X * 2;
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - resetWidth - style.ItemSpacing.X);

        // Can't ref a property, so use a local copy
        var directory = configuration.ExportDirectory;
        if (ImGui.InputTextWithHint("##ExportDirectory", Configuration.GetDefaultExportDirectory(), ref directory, 260))
            configuration.ExportDirectory = directory;

        // Save once editing finishes rather than on every keystroke
        if (ImGui.IsItemDeactivatedAfterEdit())
            configuration.Save();

        ImGui.SameLine();
        if (ImGui.Button("Reset"))
        {
            configuration.ExportDirectory = string.Empty;
            configuration.Save();
        }

        // Each checkbox is disabled while it is the only format still enabled, so an export always writes something
        var writeJson = configuration.WriteJson;
        using (ImRaii.Disabled(writeJson && !configuration.WriteMarkdown))
        {
            if (ImGui.Checkbox("Write JSON", ref writeJson))
            {
                configuration.WriteJson = writeJson;
                configuration.Save();
            }
        }

        ImGui.SameLine();
        var writeMarkdown = configuration.WriteMarkdown;
        using (ImRaii.Disabled(writeMarkdown && !configuration.WriteJson))
        {
            if (ImGui.Checkbox("Write Markdown", ref writeMarkdown))
            {
                configuration.WriteMarkdown = writeMarkdown;
                configuration.Save();
            }
        }
    }
}
