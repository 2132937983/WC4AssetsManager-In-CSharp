using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WC4MapEditor.Core.Models;

public class GeneralSettingData
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("EName")] public string EName { get; set; } = string.Empty;
    [JsonPropertyName("InShop")] public int InShop { get; set; }
    [JsonPropertyName("Type")] public int Type { get; set; }
    [JsonPropertyName("Evaluate")] public int Evaluate { get; set; }
    [JsonPropertyName("Sequence")] public int Sequence { get; set; }
    [JsonPropertyName("UnlockHQLv")] public int UnlockHQLv { get; set; }
    [JsonPropertyName("CostMedal")] public int CostMedal { get; set; }
    [JsonPropertyName("CostGold")] public int CostGold { get; set; }
    [JsonPropertyName("MilitaryRank")] public int MilitaryRank { get; set; }
    [JsonPropertyName("Infantry")] public int Infantry { get; set; }
    [JsonPropertyName("Artillery")] public int Artillery { get; set; }
    [JsonPropertyName("Armor")] public int Armor { get; set; }
    [JsonPropertyName("Navy")] public int Navy { get; set; }
    [JsonPropertyName("AirForce")] public int AirForce { get; set; }
    [JsonPropertyName("March")] public int March { get; set; }
    [JsonPropertyName("Skills")] public List<int> Skills { get; set; } = new();
    [JsonPropertyName("Hp")] public int Hp { get; set; }
    [JsonPropertyName("Medals")] public List<int> Medals { get; set; } = new();
    [JsonPropertyName("Photo")] public string Photo { get; set; } = string.Empty;
    [JsonPropertyName("InfantryMax")] public int InfantryMax { get; set; }
    [JsonPropertyName("ArtilleryMax")] public int ArtilleryMax { get; set; }
    [JsonPropertyName("ArmorMax")] public int ArmorMax { get; set; }
    [JsonPropertyName("NavyMax")] public int NavyMax { get; set; }
    [JsonPropertyName("AirForceMax")] public int AirForceMax { get; set; }
    [JsonPropertyName("MarchMax")] public int MarchMax { get; set; }
    [JsonPropertyName("SkillsMax")] public int SkillsMax { get; set; }
    [JsonPropertyName("ResetSkills")] public int ResetSkills { get; set; }
}

public class PortraitPosEntry
{
    public string Name { get; set; } = string.Empty;
    public int PosX { get; set; } = -30;
    public int PosY { get; set; } = 40;
    public double Scale { get; set; } = 1.0;
}