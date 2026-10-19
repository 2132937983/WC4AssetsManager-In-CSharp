using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Parsers.Country;

/// <summary>
/// 国家解析器：CountrySettings.json（可编辑）、ConquerCountrySettings.json（删除 / 改 Id 时级联）、
/// ConquerSettings.json（只读），以及 stringtable 中的国家真实名称 country_{Id}。
/// 修改只记在内存与字符串表的待保存层里，由编辑场景统一保存（不再每改一个字段就写盘）。
/// </summary>
public sealed class CountrySettingParser : IGameSettingsSource
{
    private static readonly object _lock = new();
    private static CountrySettingParser? _instance;
    public static CountrySettingParser Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new CountrySettingParser();
                }
            }
            return _instance;
        }
    }

    private CountrySettingParser()
    {
        CountryTable = new GameJsonTable<CountrySettingData>(GameJson.AssetPath("json", "CountrySettings.json"));
        ConquerCountryTable = new GameJsonTable<ConquerCountrySettingData>(GameJson.AssetPath("json", "ConquerCountrySettings.json"));
        ConquerTable = new GameJsonTable<ConquerSettingData>(GameJson.AssetPath("json", "ConquerSettings.json"));
        TacticalMapDir = GameJson.AssetPath("image");
        FlagsHdImagePath = GameJson.AssetPath("image", "image_flags_hd.webp");
        FlagsHdXmlPath = GameJson.AssetPath("image", "image_flags_hd.xml");
        Strings = StringTableStore.Default;
        Texts = new RecordTextKeys(Strings, this, "country_{0}");
        LoadAll();
    }

    public GameJsonTable<CountrySettingData> CountryTable { get; }
    public GameJsonTable<ConquerCountrySettingData> ConquerCountryTable { get; }

    /// <summary>征服列表只读（显示征服名称用）。</summary>
    public GameJsonTable<ConquerSettingData> ConquerTable { get; }

    public StringTableParser Strings { get; }

    /// <summary>每个国家的文本键：0 = 真实名称。</summary>
    public RecordTextKeys Texts { get; }

    public IReadOnlyList<IEditableTable> Tables => [CountryTable, ConquerCountryTable];
    StringTableParser? IGameSettingsSource.Strings => Strings;

    public List<CountrySettingData> CountryItems => CountryTable.Items;
    public IReadOnlyList<CountrySettingData> Countries => CountryTable.Items;
    public IReadOnlyList<ConquerCountrySettingData> ConquerCountries => ConquerCountryTable.Items;
    public IReadOnlyList<ConquerSettingData> Conquers => ConquerTable.Items;

    public string CountrySettingsPath => CountryTable.FilePath;
    public string ConquerCountrySettingsPath => ConquerCountryTable.FilePath;
    public string ConquerSettingsPath => ConquerTable.FilePath;
    public string TacticalMapDir { get; }
    public string FlagsHdImagePath { get; }
    public string FlagsHdXmlPath { get; }
    public string StringTablePath => Strings.FilePath;

    public void LoadAll()
    {
        CountryTable.Load();
        ConquerCountryTable.Load();
        ConquerTable.Load();
    }

    public void Reload() => LoadAll();

    public CountrySettingData? GetCountryById(int id) => Countries.FirstOrDefault(c => c.Id == id);

    public List<ConquerCountrySettingData> GetConquerCountriesByConquerId(int conquerId) => ConquerCountries.Where(cc => cc.ConquerId == conquerId).ToList();

    public List<ConquerCountrySettingData> GetConquerCountriesByCountryId(int countryId) => ConquerCountries.Where(cc => cc.CountryId == countryId).ToList();

    public ConquerSettingData? GetConquerById(int id) => Conquers.FirstOrDefault(c => c.Id == id);

    public string GetCountryName(int countryId) => GetCountryById(countryId)?.Name ?? Loc.F("JsonEditor.Country.UnknownCountry", countryId);

    public string GetConquerName(int conquerId) => GetConquerById(conquerId)?.Name ?? Loc.F("JsonEditor.Country.UnknownConquer", conquerId);

    public int GetNextCountryId() => (Countries.Count == 0 ? 0 : Math.Max(0, Countries.Max(c => c.Id))) + 1;

    /// <summary>删除国家：同时删除引用它的征服国家条目与 stringtable 名称（都只改内存，可撤销）。</summary>
    public void RemoveCountry(CountrySettingData country)
    {
        CountryItems.Remove(country);
        ConquerCountryTable.Items.RemoveAll(cc => cc.CountryId == country.Id);
        Texts.Remove(country.Id);
    }

    /// <summary>国家 Id 改变：征服国家条目的 CountryId 与 stringtable 名称跟着改。</summary>
    public void ChangeCountryId(CountrySettingData country, int newId)
    {
        int oldId = country.Id;
        if (oldId == newId) return;
        foreach (var cc in ConquerCountryTable.Items.Where(cc => cc.CountryId == oldId)) cc.CountryId = newId;
        Texts.Move(oldId, newId);
        country.Id = newId;
    }

    /// <summary>新 Id 没有被别的国家占用。</summary>
    public bool IsCountryIdFree(int id) => Countries.All(c => c.Id != id);

    // ========== StringTable 真实名称 ==========

    /// <summary>国家在 stringtable 中的真实名称 (country_{id})</summary>
    public string GetCountryRealName(int countryId) => Texts.Get(0, countryId);

    /// <summary>列表与选择器里显示的国家名：游戏里显示的是真实名称，没有时用 JSON 的 Name。</summary>
    public string GetCountryDisplayName(int countryId)
    {
        var realName = GetCountryRealName(countryId);
        return string.IsNullOrEmpty(realName) ? GetCountryName(countryId) : realName;
    }

    /// <summary>设置国家在 stringtable 中的真实名称（待保存）。</summary>
    public void SetCountryRealName(int countryId, string realName) => Texts.Set(0, countryId, realName);

    public static void ClearInstance()
    {
        lock (_lock)
        {
            _instance = null;
        }
    }
}
