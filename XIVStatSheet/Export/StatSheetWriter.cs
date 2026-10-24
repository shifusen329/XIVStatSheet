using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XIVStatSheet.Export;

public static class StatSheetWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep apostrophes (Y'shtola) and non-Latin names readable instead of \u escapes
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Writes the enabled formats and returns the paths written. IO exceptions propagate to the caller.
    public static IReadOnlyList<string> Write(StatSheet sheet, Configuration configuration)
    {
        var directory = configuration.ResolveExportDirectory();
        Directory.CreateDirectory(directory);

        var character = sheet.Character;
        var baseName = SanitizeFileName(
            $"{character.Name}_{character.JobAbbreviation}_{sheet.ExportedAtUtc.ToLocalTime():yyyyMMdd-HHmmss}");

        var paths = new List<string>();
        if (configuration.WriteJson)
            paths.Add(WriteFile(directory, baseName + ".json", ToJson(sheet)));
        if (configuration.WriteMarkdown)
            paths.Add(WriteFile(directory, baseName + ".md", ToMarkdown(sheet)));

        return paths;
    }

    public static string ToJson(StatSheet sheet) => JsonSerializer.Serialize(sheet, JsonOptions);

    public static string ToMarkdown(StatSheet sheet)
    {
        var character = sheet.Character;
        var md = new StringBuilder();

        md.AppendLine($"# {character.Name} @ {character.HomeWorld}");
        md.AppendLine();
        md.AppendLine($"**{character.Job}** ({character.JobAbbreviation}), {FormatLevel(character)}, average item level **{sheet.AverageItemLevel}**");
        md.AppendLine();
        md.AppendLine($"Exported {sheet.ExportedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}. " +
                      "Stats are the live Character window values, so food and level sync are included.");

        md.AppendLine();
        md.AppendLine("## Stats");
        foreach (var group in sheet.Stats)
        {
            md.AppendLine();
            md.AppendLine($"### {group.Name}");
            md.AppendLine();
            md.AppendLine("| Stat | Value |");
            md.AppendLine("|---|---:|");
            foreach (var stat in group.Stats)
                md.AppendLine($"| {Cell(stat.Name)} | {stat.Value:N0} |");
        }

        var showGlamour = sheet.Gear.Any(piece => piece.Glamour != null);
        md.AppendLine();
        md.AppendLine("## Gear");
        md.AppendLine();
        md.AppendLine(showGlamour
                          ? "| Slot | Item | iLvl | Stats | Materia | Glamour |"
                          : "| Slot | Item | iLvl | Stats | Materia |");
        md.AppendLine(showGlamour ? "|---|---|---:|---|---|---|" : "|---|---|---:|---|---|");
        foreach (var piece in sheet.Gear)
        {
            var row = $"| {piece.Slot} | {Cell(FormatItemName(piece))} | {piece.ItemLevel} " +
                      $"| {Cell(string.Join(", ", DescribeItemStats(piece)))} " +
                      $"| {Cell(string.Join(", ", piece.Materia.Select(DescribeMateria)))} |";
            if (showGlamour)
                row += $" {Cell(piece.Glamour ?? string.Empty)} |";
            md.AppendLine(row);
        }

        return md.ToString();
    }

    public static string FormatLevel(CharacterInfo character) =>
        character.IsLevelSynced
            ? $"Lv. {character.Level} (synced to {character.EffectiveLevel})"
            : $"Lv. {character.Level}";

    public static string FormatItemName(GearPiece piece) => piece.IsHq ? $"{piece.Name} (HQ)" : piece.Name;

    public static IEnumerable<string> DescribeItemStats(GearPiece piece)
    {
        if (piece.PhysicalDamage is { } physical)
            yield return $"Physical Damage {physical}";
        if (piece.MagicDamage is { } magic)
            yield return $"Magic Damage {magic}";
        if (piece.Delay is { } delay)
            yield return $"Delay {delay:0.00}";
        if (piece.Defense is { } defense)
            yield return $"Defense {defense}";
        if (piece.MagicDefense is { } magicDefense)
            yield return $"Magic Defense {magicDefense}";
        foreach (var stat in piece.Stats)
            yield return $"{stat.Name} +{stat.Value}";
    }

    public static string DescribeMateria(MateriaInfo materia) => $"{materia.Name} (+{materia.Value} {materia.StatName})";

    private static string WriteFile(string directory, string fileName, string contents)
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, contents, new UTF8Encoding(false));
        return path;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    private static string Cell(string text) => text.Replace("|", "\\|");
}

// Writes a stat list as { "strength": 450, ... } instead of an array of objects.
public sealed class StatListConverter : JsonConverter<IReadOnlyList<StatValue>>
{
    public override IReadOnlyList<StatValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<StatValue> value, JsonSerializerOptions options) =>
        WriteStats(writer, value);

    internal static void WriteStats(Utf8JsonWriter writer, IEnumerable<StatValue> stats)
    {
        writer.WriteStartObject();
        foreach (var stat in stats)
            writer.WriteNumber(stat.Key, stat.Value);
        writer.WriteEndObject();
    }
}

// Writes stat groups as { "attributes": { "strength": 450 }, "offensive": { ... } }.
public sealed class StatGroupListConverter : JsonConverter<IReadOnlyList<StatGroup>>
{
    public override IReadOnlyList<StatGroup> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new NotSupportedException();

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<StatGroup> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var group in value)
        {
            writer.WritePropertyName(group.Key);
            StatListConverter.WriteStats(writer, group.Stats);
        }
        writer.WriteEndObject();
    }
}
