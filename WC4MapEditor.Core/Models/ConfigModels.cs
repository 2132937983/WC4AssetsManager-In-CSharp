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
    [JsonPropertyName("EName")] public string EName { get; set; } = "";
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
    [JsonPropertyName("Skills")] public List<int> Skills { get; set; } = [];
    [JsonPropertyName("Hp")] public int Hp { get; set; }
    [JsonPropertyName("Medals")] public List<int> Medals { get; set; } = [];
    [JsonPropertyName("Photo")] public string Photo { get; set; } = "";
    [JsonPropertyName("InfantryMax")] public int InfantryMax { get; set; }
    [JsonPropertyName("ArtilleryMax")] public int ArtilleryMax { get; set; }
    [JsonPropertyName("ArmorMax")] public int ArmorMax { get; set; }
    [JsonPropertyName("NavyMax")] public int NavyMax { get; set; }
    [JsonPropertyName("AirForceMax")] public int AirForceMax { get; set; }
}

public class UnitTypeRules
{
    public List<int> Infantry { get; set; } = [];
    public List<int> Armor { get; set; } = [];
    public List<int> Artillery { get; set; } = [];
    public List<int> Navy { get; set; } = [];
    public List<int> AirForce { get; set; } = [];
}

public class ConquerCountryConfig
{
    [JsonPropertyName("Id")] public int Id { get; set; }
    [JsonPropertyName("ConquerId")] public int ConquerId { get; set; }
    [JsonPropertyName("Seat")] public int Seat { get; set; }
    [JsonPropertyName("Star")] public int Star { get; set; }
    [JsonPropertyName("Camp")] public int Camp { get; set; }
    [JsonPropertyName("CountryId")] public int CountryId { get; set; }
    [JsonPropertyName("WarTurn")] public int WarTurn { get; set; }
    [JsonPropertyName("PrizeExp")] public int PrizeExp { get; set; }
    [JsonPropertyName("PrizeGold")] public int PrizeGold { get; set; }
    [JsonPropertyName("PrizeIndustry")] public int PrizeIndustry { get; set; }
    [JsonPropertyName("PrizeEnergy")] public int PrizeEnergy { get; set; }
    [JsonPropertyName("PrizeTech")] public int PrizeTech { get; set; }
    [JsonPropertyName("Photo")] public string Photo { get; set; } = "";
    [JsonPropertyName("TechCategoryIds")] public List<int> TechCategoryIds { get; set; } = [];
    [JsonPropertyName("CloseTechTypes")] public List<int> CloseTechTypes { get; set; } = [];
    [JsonPropertyName("CostMoney")] public int CostMoney { get; set; }
    [JsonPropertyName("CostGear")] public int CostGear { get; set; }
    [JsonPropertyName("CostAtomic")] public int CostAtomic { get; set; }
    [JsonPropertyName("Coefficient")] public double Coefficient { get; set; }
    [JsonPropertyName("Surrender")] public bool Surrender { get; set; }
}

public class GeneralRandomTemplate
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("evaluate")] public int Evaluate { get; set; }
    [JsonPropertyName("sequenceMin")] public int SequenceMin { get; set; }
    [JsonPropertyName("sequenceMax")] public int SequenceMax { get; set; }
    [JsonPropertyName("unlockHQLv")] public int UnlockHQLv { get; set; }
    [JsonPropertyName("costMedalMin")] public int CostMedalMin { get; set; }
    [JsonPropertyName("costMedalMax")] public int CostMedalMax { get; set; }
    [JsonPropertyName("militaryRankMin")] public int MilitaryRankMin { get; set; }
    [JsonPropertyName("militaryRankMax")] public int MilitaryRankMax { get; set; }
    [JsonPropertyName("marchMin")] public int MarchMin { get; set; }
    [JsonPropertyName("marchMax")] public int MarchMax { get; set; }
    [JsonPropertyName("hpMin")] public int HpMin { get; set; }
    [JsonPropertyName("hpMax")] public int HpMax { get; set; }
    [JsonPropertyName("infantryDivisor")] public double InfantryDivisor { get; set; }
    [JsonPropertyName("artilleryDivisor")] public double ArtilleryDivisor { get; set; }
    [JsonPropertyName("armorDivisor")] public double ArmorDivisor { get; set; }
    [JsonPropertyName("navyDivisor")] public double NavyDivisor { get; set; }
    [JsonPropertyName("airForceDivisor")] public double AirForceDivisor { get; set; }
    [JsonPropertyName("skillLevelMin")] public int SkillLevelMin { get; set; }
    [JsonPropertyName("skillLevelMax")] public int SkillLevelMax { get; set; }
    [JsonPropertyName("skillCount")] public int SkillCount { get; set; }
}

public class GeneralSpecialtyTemplate
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mainStat")] public string MainStat { get; set; } = "";
    [JsonPropertyName("mainStatValue")] public int MainStatValue { get; set; }
    [JsonPropertyName("subStat1")] public string SubStat1 { get; set; } = "";
    [JsonPropertyName("subStat1Value")] public int SubStat1Value { get; set; }
    [JsonPropertyName("subStat2")] public string SubStat2 { get; set; } = "";
    [JsonPropertyName("subStat2Value")] public int SubStat2Value { get; set; }
    [JsonPropertyName("marchMin")] public int MarchMin { get; set; }
    [JsonPropertyName("marchMax")] public int MarchMax { get; set; }
    [JsonPropertyName("hpMin")] public int HpMin { get; set; }
    [JsonPropertyName("hpMax")] public int HpMax { get; set; }
    [JsonPropertyName("skillPool")] public List<int> SkillPool { get; set; } = [];
    [JsonPropertyName("skillLevelMin")] public int SkillLevelMin { get; set; }
    [JsonPropertyName("skillLevelMax")] public int SkillLevelMax { get; set; }
    [JsonPropertyName("skillCount")] public int SkillCount { get; set; }
}