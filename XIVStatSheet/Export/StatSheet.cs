using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace XIVStatSheet.Export;

// Snapshot of the character at export time. JSON shape is defined by these records plus the
// converters in StatSheetWriter, so renaming a property here changes the exported file.
public sealed record StatSheet(
    DateTime ExportedAtUtc,
    CharacterInfo Character,
    int AverageItemLevel,
    [property: JsonConverter(typeof(StatGroupListConverter))] IReadOnlyList<StatGroup> Stats,
    IReadOnlyList<GearPiece> Gear);

public sealed record CharacterInfo(
    string Name,
    string HomeWorld,
    string CurrentWorld,
    uint JobId,
    string Job,
    string JobAbbreviation,
    int Level,
    bool IsLevelSynced,
    int EffectiveLevel);

// Key is the camelCase English PlayerAttribute name (used in JSON), Name is the client-language
// BaseParam name (used in Markdown and the window).
public sealed record StatValue(string Key, string Name, int Value);

public sealed record StatGroup(string Key, string Name, IReadOnlyList<StatValue> Stats);

public sealed record MateriaInfo(
    string Name,
    string Stat,
    [property: JsonIgnore] string StatName,
    int Value);

public sealed record GearPiece(
    int SlotIndex,
    string Slot,
    uint ItemId,
    string Name,
    int ItemLevel,
    int EquipLevel,
    bool IsHq,
    string? Glamour,
    double Condition,
    double Spiritbond,
    int? PhysicalDamage,
    int? MagicDamage,
    double? Delay,
    int? Defense,
    int? MagicDefense,
    [property: JsonConverter(typeof(StatListConverter))] IReadOnlyList<StatValue> Stats,
    int MateriaSlots,
    IReadOnlyList<MateriaInfo> Materia);

public sealed record ExportResult(DateTime Time, IReadOnlyList<string> Paths, StatSheet? Sheet, string? Error);
