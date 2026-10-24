using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using XIVStatSheet.Export;

namespace XIVStatSheet.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    // Collected when the window opens and on Refresh, not every frame
    private StatSheet? preview;
    private string? previewError;

    // We give this window a hidden ID using ##.
    // The user will see "XIV Stat Sheet" as window title,
    // but for ImGui the ID is "XIV Stat Sheet##main"
    public MainWindow(Plugin plugin)
        : base("XIV Stat Sheet##main", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void OnOpen() => RefreshPreview();

    public override void Draw()
    {
        if (ImGui.Button("Export"))
        {
            var result = plugin.Export();
            if (result.Sheet != null)
            {
                preview = result.Sheet;
                previewError = null;
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
            RefreshPreview();

        ImGui.SameLine();
        if (ImGui.Button("Open Folder"))
            OpenExportFolder();

        ImGui.SameLine();
        if (ImGui.Button("Settings"))
            plugin.ToggleConfigUi();

        DrawLastExport();
        ImGui.Separator();

        // Normally a BeginChild() would have to be followed by an unconditional EndChild(),
        // ImRaii takes care of this after the scope ends.
        using var child = ImRaii.Child("Preview", Vector2.Zero);
        if (!child.Success)
            return;

        if (preview == null)
        {
            ImGui.TextUnformatted(previewError ?? "Nothing to show yet.");
            return;
        }

        DrawCharacter(preview);
        ImGuiHelpers.ScaledDummy(5f);
        DrawStats(preview);
        ImGuiHelpers.ScaledDummy(5f);
        DrawGear(preview);
    }

    private void RefreshPreview() => preview = StatSheetCollector.Collect(out previewError);

    private void DrawLastExport()
    {
        var last = plugin.LastExport;
        if (last == null)
        {
            ImGui.TextDisabled($"Exports are saved to {plugin.Configuration.ResolveExportDirectory()}");
            return;
        }

        var (color, message) = last.Error != null
                                   ? (ImGuiColors.DalamudRed, $"Export failed at {last.Time:T}: {last.Error}")
                                   : (ImGuiColors.HealerGreen, $"Exported at {last.Time:T}: {string.Join(", ", last.Paths.Select(Path.GetFileName))}");
        using (ImRaii.PushColor(ImGuiCol.Text, color))
            ImGui.TextWrapped(message);
    }

    private void OpenExportFolder()
    {
        var directory = plugin.Configuration.ResolveExportDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            Util.OpenLink(directory);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Could not open {directory}");
        }
    }

    private static void DrawCharacter(StatSheet sheet)
    {
        var character = sheet.Character;

        // Get the icon id from a known offset + the class jobs id.
        // Scaling hardcoded pixel values is important, as otherwise users with HUD scales above or below 100%
        // won't be able to see everything.
        var jobIcon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(62100 + character.JobId)).GetWrapOrEmpty();
        ImGui.Image(jobIcon.Handle, new Vector2(36, 36) * ImGuiHelpers.GlobalScale);

        ImGui.SameLine();
        using (ImRaii.Group())
        {
            ImGui.TextUnformatted($"{character.Name} @ {character.HomeWorld}");
            ImGui.TextUnformatted($"{character.Job}, {StatSheetWriter.FormatLevel(character)}, average item level {sheet.AverageItemLevel}");
        }
    }

    private static void DrawStats(StatSheet sheet)
    {
        using var table = ImRaii.Table("Stats", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Group", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Stat", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        foreach (var group in sheet.Stats)
        {
            for (var i = 0; i < group.Stats.Count; i++)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (i == 0)
                    ImGui.TextDisabled(group.Name);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(group.Stats[i].Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(group.Stats[i].Value.ToString("N0"));
            }
        }
    }

    private static void DrawGear(StatSheet sheet)
    {
        using var table = ImRaii.Table("Gear", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Materia", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableHeadersRow();

        foreach (var piece in sheet.Gear)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(piece.Slot);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(StatSheetWriter.FormatItemName(piece));
            if (ImGui.IsItemHovered())
                DrawGearTooltip(piece);

            ImGui.TableNextColumn();
            ImGui.TextUnformatted(piece.ItemLevel.ToString());

            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{piece.Materia.Count}/{piece.MateriaSlots}");
        }
    }

    private static void DrawGearTooltip(GearPiece piece)
    {
        using var tooltip = ImRaii.Tooltip();
        foreach (var line in StatSheetWriter.DescribeItemStats(piece))
            ImGui.TextUnformatted(line);

        foreach (var materia in piece.Materia)
            ImGui.TextUnformatted(StatSheetWriter.DescribeMateria(materia));

        if (piece.Glamour != null)
            ImGui.TextDisabled($"Glamour: {piece.Glamour}");
    }
}
