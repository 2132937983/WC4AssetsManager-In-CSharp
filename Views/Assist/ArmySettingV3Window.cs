using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Views.Assist;

public sealed class ArmySettingV3Window : ArmySettingWindow
{
    private Army_3 _army3;
    private bool _isNewArmy3;

    public Army_3 ResultArmy3 => _army3;

    public ArmySettingV3Window(Army_3 army3, bool isNew = false) : base(new Army(), isNew)
    {
        _army3 = army3;
        _isNewArmy3 = isNew;

        Title = isNew ? "创建v3单位" : "编辑v3单位";
    }

    protected override void LoadArmyData()
    {
        // Load base fields that overlap with Army
        SetTextBoxValue("coordinate", _army3.Coordinate);
        SetTextBoxValue("unit_type", _army3.UnitType);
        SetTextBoxValue("level", _army3.Level);
        SetTextBoxValue("organization", _army3.Organization);
        SetTextBoxValue("direction", _army3.Direction);
        SetTextBoxValue("mobility", _army3.Mobility);
        SetTextBoxValue("built_round", _army3.BuiltRound);
        SetTextBoxValue("experience", _army3.Experience);
        SetTextBoxValue("health_bonus", _army3.HealthBonus);
        SetTextBoxValue("current_health", _army3.CurrentHealth);
        SetTextBoxValue("max_health", _army3.MaxHealth);
        SetTextBoxValue("general", _army3.General);
        SetTextBoxValue("rank", _army3.Rank);
        SetTextBoxValue("skill_level1", _army3.SkillLevel1);
        SetTextBoxValue("skill_level2", _army3.SkillLevel2);
        SetTextBoxValue("skill_level3", _army3.SkillLevel3);
        SetTextBoxValue("skill_level4", _army3.SkillLevel4);
        SetTextBoxValue("skill_level5", _army3.SkillLevel5);
        SetTextBoxValue("key_point", _army3.KeyPoint);
        SetTextBoxValue("policy", _army3.Policy);
        SetTextBoxValue("plan", _army3.Plan);
        SetTextBoxValue("change_round", _army3.ChangeRound);
        SetTextBoxValue("morale", _army3.Morale);
        SetTextBoxValue("duration", _army3.Duration);

        // v3 specific fields - need to add UI for these
        // For now, store them in the base _textBoxes with v3 prefix keys
        SetTextBoxValue("transport_ship", _army3.TransportShip);
        SetTextBoxValue("field1f", _army3.Field1F);
        SetTextBoxValue("hatred", _army3.Hatred);
        SetTextBoxValue("attack_target", _army3.AttackTarget);
        SetTextBoxValue("death_dialogue", _army3.DeathDialogue);
        SetTextBoxValue("level_mark_display", _army3.LevelMarkDisplay);
        SetTextBoxValue("anti_defense_mode", _army3.AntiDefenseMode);
        SetTextBoxValue("medal1", _army3.Medal1);
        SetTextBoxValue("medal2", _army3.Medal2);
        SetTextBoxValue("medal3", _army3.Medal3);
        SetTextBoxValue("ribbon1", _army3.Ribbon1);
        SetTextBoxValue("ribbon2", _army3.Ribbon2);
        SetTextBoxValue("ribbon3", _army3.Ribbon3);

        UpdateGeneralInfo(_army3.General.ToString());
    }

    protected override void SaveArmyData()
    {
        _army3.Coordinate = ParseShort(_textBoxes.GetValueOrDefault("coordinate")?.Text);
        _army3.UnitType = ParseByte(_textBoxes.GetValueOrDefault("unit_type")?.Text);
        _army3.Level = ParseByte(_textBoxes.GetValueOrDefault("level")?.Text);
        _army3.Organization = ParseByte(_textBoxes.GetValueOrDefault("organization")?.Text);
        _army3.Direction = ParseByte(_textBoxes.GetValueOrDefault("direction")?.Text);
        _army3.Mobility = ParseByte(_textBoxes.GetValueOrDefault("mobility")?.Text);
        _army3.BuiltRound = ParseByte(_textBoxes.GetValueOrDefault("built_round")?.Text);
        _army3.Experience = ParseShort(_textBoxes.GetValueOrDefault("experience")?.Text);
        _army3.HealthBonus = ParseShort(_textBoxes.GetValueOrDefault("health_bonus")?.Text);
        _army3.CurrentHealth = ParseShort(_textBoxes.GetValueOrDefault("current_health")?.Text);
        _army3.MaxHealth = ParseShort(_textBoxes.GetValueOrDefault("max_health")?.Text);
        _army3.General = ParseShort(_textBoxes.GetValueOrDefault("general")?.Text);
        _army3.Rank = ParseByte(_textBoxes.GetValueOrDefault("rank")?.Text);
        _army3.SkillLevel1 = ParseByte(_textBoxes.GetValueOrDefault("skill_level1")?.Text);
        _army3.SkillLevel2 = ParseByte(_textBoxes.GetValueOrDefault("skill_level2")?.Text);
        _army3.SkillLevel3 = ParseByte(_textBoxes.GetValueOrDefault("skill_level3")?.Text);
        _army3.SkillLevel4 = ParseByte(_textBoxes.GetValueOrDefault("skill_level4")?.Text);
        _army3.SkillLevel5 = ParseByte(_textBoxes.GetValueOrDefault("skill_level5")?.Text);
        _army3.KeyPoint = ParseByte(_textBoxes.GetValueOrDefault("key_point")?.Text);
        _army3.Policy = ParseByte(_textBoxes.GetValueOrDefault("policy")?.Text);
        _army3.Plan = ParseShort(_textBoxes.GetValueOrDefault("plan")?.Text);
        _army3.ChangeRound = ParseShort(_textBoxes.GetValueOrDefault("change_round")?.Text);
        _army3.Morale = ParseByte(_textBoxes.GetValueOrDefault("morale")?.Text);
        _army3.Duration = ParseByte(_textBoxes.GetValueOrDefault("duration")?.Text);

        // v3 specific
        _army3.TransportShip = ParseByte(_textBoxes.GetValueOrDefault("transport_ship")?.Text);
        _army3.Field1F = ParseByte(_textBoxes.GetValueOrDefault("field1f")?.Text);
        _army3.Hatred = ParseShort(_textBoxes.GetValueOrDefault("hatred")?.Text);
        _army3.AttackTarget = ParseShort(_textBoxes.GetValueOrDefault("attack_target")?.Text);
        _army3.DeathDialogue = ParseByte(_textBoxes.GetValueOrDefault("death_dialogue")?.Text);
        _army3.LevelMarkDisplay = ParseByte(_textBoxes.GetValueOrDefault("level_mark_display")?.Text);
        _army3.AntiDefenseMode = ParseInt(_textBoxes.GetValueOrDefault("anti_defense_mode")?.Text);
        _army3.Medal1 = ParseByte(_textBoxes.GetValueOrDefault("medal1")?.Text);
        _army3.Medal2 = ParseByte(_textBoxes.GetValueOrDefault("medal2")?.Text);
        _army3.Medal3 = ParseByte(_textBoxes.GetValueOrDefault("medal3")?.Text);
        _army3.Ribbon1 = ParseByte(_textBoxes.GetValueOrDefault("ribbon1")?.Text);
        _army3.Ribbon2 = ParseByte(_textBoxes.GetValueOrDefault("ribbon2")?.Text);
        _army3.Ribbon3 = ParseByte(_textBoxes.GetValueOrDefault("ribbon3")?.Text);
    }

    private static int ParseInt(string? text)
    {
        return int.TryParse(text, out var value) ? value : 0;
    }
}