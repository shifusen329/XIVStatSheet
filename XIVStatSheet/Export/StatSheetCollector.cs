using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Dalamud.Game.Inventory;
using Dalamud.Game.Player;
using Lumina.Excel.Sheets;

namespace XIVStatSheet.Export;

// Reads game state, so it must run on the game thread (window Draw and command handlers already do).
public static class StatSheetCollector
{
    private const int MainHandSlot = 0;
    private const int OffHandSlot = 1;
    private const int SoulCrystalSlot = 13;

    // Index = EquippedItems slot. Slot 5 is the old waist slot and is always empty.
    private static readonly string?[] SlotNames =
    [
        "Main Hand", "Off Hand", "Head", "Body", "Hands", null, "Legs", "Feet",
        "Earrings", "Necklace", "Bracelets", "Ring 1", "Ring 2", "Soul Crystal",
    ];

    // ClassJobCategory rows used to pick which stats the in-game Character window shows.
    private const uint DiscipleOfTheLand = 32;
    private const uint DiscipleOfTheHand = 33;

    private sealed record StatGroupDef(string Key, string Name, PlayerAttribute[] Attributes);

    private static readonly StatGroupDef AttributeGroup = new("attributes", "Attributes",
    [
        PlayerAttribute.Strength, PlayerAttribute.Dexterity, PlayerAttribute.Vitality,
        PlayerAttribute.Intelligence, PlayerAttribute.Mind,
    ]);

    private static readonly StatGroupDef[] CombatGroups =
    [
        new("resources", "HP / MP", [PlayerAttribute.HealthPoints, PlayerAttribute.MagicPoints]),
        new("offensive", "Offensive Properties", [PlayerAttribute.CriticalHit, PlayerAttribute.Determination, PlayerAttribute.DirectHitRate]),
        new("defensive", "Defensive Properties", [PlayerAttribute.Defense, PlayerAttribute.MagicDefense]),
        new("physical", "Physical Properties", [PlayerAttribute.AttackPower, PlayerAttribute.SkillSpeed]),
        new("mental", "Mental Properties", [PlayerAttribute.AttackMagicPotency, PlayerAttribute.HealingMagicPotency, PlayerAttribute.SpellSpeed]),
        new("role", "Role", [PlayerAttribute.Tenacity, PlayerAttribute.Piety]),
    ];

    private static readonly StatGroupDef[] CraftingGroups =
    [
        new("crafting", "Crafting", [PlayerAttribute.Craftsmanship, PlayerAttribute.Control, PlayerAttribute.CraftingPoints]),
    ];

    private static readonly StatGroupDef[] GatheringGroups =
    [
        new("gathering", "Gathering", [PlayerAttribute.Gathering, PlayerAttribute.Perception, PlayerAttribute.GatheringPoints]),
    ];

    public static StatSheet? Collect(out string? error)
    {
        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded)
        {
            error = "Not logged in.";
            return null;
        }

        if (!playerState.ClassJob.IsValid)
        {
            error = "Current job is not valid.";
            return null;
        }

        var classJob = playerState.ClassJob.Value;
        var character = new CharacterInfo(
            playerState.CharacterName,
            WorldName(playerState.HomeWorld),
            WorldName(playerState.CurrentWorld),
            classJob.RowId,
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(classJob.Name.ExtractText()),
            classJob.Abbreviation.ExtractText(),
            playerState.Level,
            playerState.IsLevelSynced,
            playerState.EffectiveLevel);

        var gear = CollectGear();
        error = null;
        return new StatSheet(DateTime.UtcNow, character, AverageItemLevel(gear), CollectStats(classJob), gear);
    }

    private static List<StatGroup> CollectStats(ClassJob classJob)
    {
        var groups = classJob.ClassJobCategory.RowId switch
        {
            DiscipleOfTheHand => CraftingGroups,
            DiscipleOfTheLand => GatheringGroups,
            _ => CombatGroups,
        };

        return groups.Prepend(AttributeGroup)
                     .Select(group => new StatGroup(group.Key, group.Name, group.Attributes
                         .Select(attribute => Stat((uint)attribute, Plugin.PlayerState.GetAttribute(attribute)))
                         .ToList()))
                     .ToList();
    }

    private static List<GearPiece> CollectGear()
    {
        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
        var gear = new List<GearPiece>();

        foreach (var slot in Plugin.GameInventory.GetInventoryItems(GameInventoryType.EquippedItems))
        {
            if (slot.IsEmpty || slot.InventorySlot >= SlotNames.Length || SlotNames[slot.InventorySlot] is not { } slotName)
                continue;

            if (!itemSheet.TryGetRow(slot.BaseItemId, out var item))
                continue;

            // HQ items get their bonus from the "special" columns, keyed by BaseParam like the regular stats.
            var hqBonus = new Dictionary<uint, int>();
            if (slot.IsHq)
            {
                for (var i = 0; i < item.BaseParamSpecial.Count; i++)
                {
                    if (item.BaseParamSpecial[i].RowId != 0)
                        hqBonus[item.BaseParamSpecial[i].RowId] = item.BaseParamValueSpecial[i];
                }
            }

            int? WithBonus(int baseValue, PlayerAttribute attribute) =>
                baseValue == 0 ? null : baseValue + hqBonus.GetValueOrDefault((uint)attribute);

            var stats = new List<StatValue>();
            for (var i = 0; i < item.BaseParam.Count; i++)
            {
                var statId = item.BaseParam[i].RowId;
                if (statId != 0)
                    stats.Add(Stat(statId, item.BaseParamValue[i] + hqBonus.GetValueOrDefault(statId)));
            }

            string? glamour = null;
            if (slot.GlamourId != 0 && itemSheet.TryGetRow(slot.GlamourId, out var glamourItem))
                glamour = glamourItem.Name.ExtractText();

            gear.Add(new GearPiece(
                (int)slot.InventorySlot,
                slotName,
                item.RowId,
                item.Name.ExtractText(),
                (int)item.LevelItem.RowId,
                item.LevelEquip,
                slot.IsHq,
                glamour,
                Math.Round(slot.Condition / 300.0, 2),
                Math.Round(slot.SpiritbondOrCollectability / 100.0, 2),
                WithBonus(item.DamagePhys, PlayerAttribute.PhysicalDamage),
                WithBonus(item.DamageMag, PlayerAttribute.MagicDamage),
                item.Delayms == 0 ? null : item.Delayms / 1000.0,
                WithBonus(item.DefensePhys, PlayerAttribute.Defense),
                WithBonus(item.DefenseMag, PlayerAttribute.MagicDefense),
                stats,
                item.MateriaSlotCount,
                CollectMateria(slot)));
        }

        return gear;
    }

    // Nominal materia values; the per-item substat cap is already reflected in the character's totals.
    private static List<MateriaInfo> CollectMateria(GameInventoryItem slot)
    {
        var materia = new List<MateriaInfo>();
        foreach (var entry in slot.MateriaEntries)
        {
            if (entry.Type.RowId == 0 || !entry.Type.IsValid)
                continue;

            var row = entry.Type.Value;
            var grade = (int)entry.Grade.RowId;
            if (grade >= row.Item.Count || !row.Item[grade].IsValid)
                continue;

            var stat = Stat(row.BaseParam.RowId, row.Value[grade]);
            materia.Add(new MateriaInfo(row.Item[grade].Value.Name.ExtractText(), stat.Key, stat.Name, stat.Value));
        }

        return materia;
    }

    // The game averages the 12 gear slots (soul crystal excluded); a two-handed weapon counts twice.
    private static int AverageItemLevel(List<GearPiece> gear)
    {
        var hasOffHand = gear.Any(piece => piece.SlotIndex == OffHandSlot);
        var total = 0;
        foreach (var piece in gear)
        {
            if (piece.SlotIndex == SoulCrystalSlot)
                continue;

            total += piece.ItemLevel;
            if (piece.SlotIndex == MainHandSlot && !hasOffHand && IsTwoHanded(piece.ItemId))
                total += piece.ItemLevel;
        }

        return total / 12;
    }

    private static bool IsTwoHanded(uint itemId) =>
        Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item)
        && item.EquipSlotCategory.IsValid
        && item.EquipSlotCategory.Value.OffHand == -1;

    private static StatValue Stat(uint baseParamId, int value)
    {
        var attribute = (PlayerAttribute)baseParamId;
        var key = Enum.IsDefined(attribute) ? attribute.ToString() : $"BaseParam{baseParamId}";
        var name = Plugin.DataManager.GetExcelSheet<BaseParam>().TryGetRow(baseParamId, out var row)
                       ? row.Name.ExtractText()
                       : key;

        return new StatValue(JsonNamingPolicy.CamelCase.ConvertName(key), name, value);
    }

    private static string WorldName(Lumina.Excel.RowRef<World> world) =>
        world.IsValid ? world.Value.Name.ExtractText() : string.Empty;
}
