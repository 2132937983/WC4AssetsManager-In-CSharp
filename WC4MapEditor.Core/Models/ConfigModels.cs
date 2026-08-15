using System.Text.Json.Serialization;

namespace WC4MapEditor.Models;

public class CountryGeneralsConfig
{
    [JsonPropertyName("CountryId")]
    public int CountryId { get; set; }

    [JsonPropertyName("Generals")]
    public List<int> Generals { get; set; } = [];
}

public class LegionLevelConfig
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("rate")] public double Rate { get; set; }
    [JsonPropertyName("blood")] public double Blood { get; set; }
    [JsonPropertyName("hpPrent")] public int HpPercent { get; set; }
    [JsonPropertyName("generalHpMin")] public int GeneralHpMin { get; set; }
    [JsonPropertyName("generalHpMax")] public int GeneralHpMax { get; set; }
    [JsonPropertyName("armyNumMax")] public int ArmyNumMax { get; set; }
    [JsonPropertyName("armyLvMax")] public int ArmyLvMax { get; set; }
    [JsonPropertyName("armyHpBonus")] public List<int> ArmyHpBonus { get; set; } = [];
}

public class MaxFormationConfig
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("max_formation")] public int MaxFormation { get; set; }
}

public class GeneralSettings
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("Name")] public string Name { get; set; } = "";
    [JsonPropertyName("Infantry")] public int Infantry { get; set; }
    [JsonPropertyName("Armor")] public int Armor { get; set; }
    [JsonPropertyName("Artillery")] public int Artillery { get; set; }
    [JsonPropertyName("Navy")] public int Navy { get; set; }
    [JsonPropertyName("AirForce")] public int AirForce { get; set; }
    [JsonPropertyName("MilitaryRank")] public int MilitaryRank { get; set; }
    [JsonPropertyName("Hp")] public int Hp { get; set; }
    [JsonPropertyName("Skills")] public List<int> Skills { get; set; } = [];
}

public class UnitTypeRules
{
    public List<int> Infantry { get; set; } = [];
    public List<int> Armor { get; set; } = [];
    public List<int> Artillery { get; set; } = [];
    public List<int> Navy { get; set; } = [];
    public List<int> AirForce { get; set; } = [];
}
