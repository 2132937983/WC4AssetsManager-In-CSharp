using WC4MapEditor.Core.Config;

namespace WC4MapEditor.Core.Models;

/// <summary>
/// 空袭 / 空中支援的"兵种"与"弹药"名录（值 → 名称）。
/// <para>
/// 优先取 setting.txt [空袭修改] 的 AirForceList / AirForcePowerList；配置里没有时退回游戏内置的默认表。
/// 内置默认名随界面语言（配置里写的正是默认中文名时也随界面语言）。
/// 列表窗口的下拉、随机生成的取值池、地图标记与命令行输出都从这里取，名称表只维护这一份
/// （原先 AirForce / AirSupport / AirForceRender 各抄了一份，未知值一处写十六进制、一处写十进制）。
/// </para>
/// </summary>
public static class AirForceCatalog
{
    /// <summary>战斗机。</summary>
    public const int Fighter = 0x14;

    /// <summary>轰炸机：新增空袭 / 空中支援时的默认兵种（与 VB 列表窗口的"新增"一致）。</summary>
    public const int Bomber = 0x15;

    /// <summary>默认弹药。</summary>
    public const int DefaultAmmo = 0;

    /// <summary>兵种 / 弹药字段的取值上限（游戏里都是单字节编号）。</summary>
    public const int MaxCode = 255;

    private static readonly (int Value, string Key, string Fallback)[] DefaultTypes =
    {
        (0x14, "Module.AirForce.Type.Fighter", "战斗机"),
        (0x15, "Module.AirForce.Type.Bomber", "轰炸机"),
        (0x16, "Module.AirForce.Type.Paratrooper", "空降伞兵"),
        (0x17, "Module.AirForce.Type.StrategicBomber", "战略轰炸机"),
        (0x18, "Module.AirForce.Type.SpecialBomber", "特色轰炸机"),
        (0x19, "Module.AirForce.Type.ShortRangeMissile", "近程导弹"),
        (0x1A, "Module.AirForce.Type.MediumRangeMissile", "中程导弹"),
        (0x1B, "Module.AirForce.Type.LongRangeMissile", "远程导弹"),
        (0x1E, "Module.AirForce.Type.Icbm", "洲际导弹"),
    };

    private static readonly (int Value, string Key, string Fallback)[] DefaultAmmos =
    {
        (0x00, "Module.AirForce.Ammo.Default", "默认弹药"),
        (0x1D, "Module.AirForce.Ammo.Atomic", "原子弹"),
        (0x1E, "Module.AirForce.Ammo.Hydrogen", "氢弹"),
        (0x1F, "Module.AirForce.Ammo.ThreePhase", "三相弹"),
        (0x20, "Module.AirForce.Ammo.Antimatter", "反物质弹"),
    };

    /// <summary>兵种（空袭的 UnitType / 空中支援的 AirForceSequence）的可选值。</summary>
    public static IReadOnlyList<ConfigKeyValue> Types
        => Pick(ConfigManager.Instance.GetAirForceEditConfig().AirForceList, DefaultTypes);

    /// <summary>弹药的可选值。</summary>
    public static IReadOnlyList<ConfigKeyValue> Ammos
        => Pick(ConfigManager.Instance.GetAirForceEditConfig().AirForcePowerList, DefaultAmmos);

    /// <summary>兵种名称；名录里没有时显示"未知(值)"。</summary>
    public static string TypeName(int value) => NameIn(Types, value);

    /// <summary>弹药名称；名录里没有时显示"未知(值)"。</summary>
    public static string AmmoName(int value) => NameIn(Ammos, value);

    /// <summary>在给定名录里取名称（绘制时每帧先取一次名录再逐条查，避免反复读配置）。</summary>
    public static string NameIn(IReadOnlyList<ConfigKeyValue> catalog, int value)
        => ConfigKeyValue.NameOf(catalog, value) ?? Loc.F("Module.AirForce.UnknownValue", value);

    /// <summary>
    /// 有配置时用配置（保持其顺序与取值），否则用内置默认表。
    /// 配置里的名称与内置默认名（简体中文）一致时换成当前界面语言的名称；用户自己改过的名称原样保留。
    /// </summary>
    private static IReadOnlyList<ConfigKeyValue> Pick(List<ConfigKeyValue> configured,
        (int Value, string Key, string Fallback)[] defaults)
    {
        if (configured.Count == 0)
            return defaults.Select(d => new ConfigKeyValue { Value = d.Value, Name = Loc.T(d.Key, d.Fallback) }).ToArray();

        var list = new ConfigKeyValue[configured.Count];
        for (int i = 0; i < list.Length; i++)
        {
            var entry = configured[i];
            string name = entry.Name;
            foreach (var d in defaults)
            {
                if (d.Value != entry.Value || d.Fallback != entry.Name) continue;
                name = Loc.T(d.Key, d.Fallback);
                break;
            }
            list[i] = new ConfigKeyValue { Value = entry.Value, Name = name };
        }
        return list;
    }
}
