namespace WC4MapEditor.Core.Models;

public class ConquerCountrySettingData
{
    public int Id { get; set; }
    public int ConquerId { get; set; }
    public int Seat { get; set; }
    public int Star { get; set; }
    public int Camp { get; set; }
    public int CountryId { get; set; }
    public int WarTurn { get; set; }
    public int PrizeExp { get; set; }
    public int PrizeGold { get; set; }
    public int PrizeIndustry { get; set; }
    public int PrizeEnergy { get; set; }
    public int PrizeTech { get; set; }
    public string Photo { get; set; } = "";
    public List<int> TechCategoryIds { get; set; } = new();
    public List<int> CloseTechTypes { get; set; } = new();
    public int CostMoney { get; set; }
    public int CostGear { get; set; }
    public int CostAtomic { get; set; }
    public double Coefficient { get; set; }
    public bool Surrender { get; set; }
}