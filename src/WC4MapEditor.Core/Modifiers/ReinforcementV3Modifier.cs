using System.Collections.ObjectModel;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Modifiers;

/// <summary>
/// v3 地图（BtlVersion ≥ 3）的援军模块，数据在 <see cref="MapData.ReinforcementsV3"/>。
/// 全部功能来自 <see cref="ReinforcementModifierBase{T}"/>，与 v1 模块共用一份实现。
/// </summary>
public sealed class ReinforcementV3Modifier : ReinforcementModifierBase<Reinforcement_3>
{
    public override string Name => "reinforcement_v3";
    public override string DisplayName => Loc.T("Module.Reinforcement.NameV3", "v3援军修改器");

    protected override ObservableCollection<Reinforcement_3> RecordsOf(MapData map) => map.ReinforcementsV3;

    protected override Reinforcement_3 CreateDefaultRecord() => Reinforcement_3.CreateDefault();
}
