namespace WC4MapEditor.GUI.Views.Assist;

/// <summary>
/// v3 单位（64 字节 <see cref="Army_3"/>）设置对话框：显示并修改传入单位的副本（新建或编辑都一样），
/// 确定后由 <see cref="ResultArmy3"/> 返回。
/// <para>
/// 与 v1 共用外观和交互；字段按 v3 的含义标注：v1 的爵位 / 胸章 / 关联对话 / 能否攻击 所在字节在 v3 里是
/// HP 等级 / 废弃字段 / 死亡触发事件 / 等级标志显示，另有"v3扩展属性"分组。表单里没有的字段（填充字节、LegionId）原样保留。
/// </para>
/// </summary>
public sealed class ArmySettingV3Window : ArmySettingWindowBase
{
    private Army_3 _army3;

    public ArmySettingV3Window(Army_3 army3, bool isNew = false)
        : base(isNew ? Loc.T("Assist.Army.CreateV3Title") : Loc.T("Assist.Army.EditV3Title"))
    {
        _army3 = army3;
        BuildContent();
    }

    /// <summary>编辑后的单位；未确定时为传入的原值。</summary>
    public Army_3 ResultArmy3 => _army3;

    protected override IReadOnlyList<ArmyFieldGroup> CreateFieldGroups() =>
    [
        new(Loc.T("Common.BasicInfo"),
        [
            CoordinateField(() => _army3.CoordinateIndex),
            UnitTypeField(() => _army3.UnitType, v => _army3.UnitType = (byte)v),
            LevelField(() => _army3.Level, v => _army3.Level = (byte)v),
            OrganizationField(() => _army3.Organization, v => _army3.Organization = (byte)v),
            DirectionField(() => _army3.Direction, v => _army3.Direction = (byte)v),
        ]),
        new(Loc.T("Assist.Army.Section.Attributes"),
        [
            ByteField(Loc.T("Assist.Army.Field.Mobility"), () => _army3.Mobility, v => _army3.Mobility = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.BuiltRound"), () => _army3.BuiltRound, v => _army3.BuiltRound = (byte)v),
            Int16Field(Loc.T("Assist.Army.Field.Experience"), () => _army3.Experience, v => _army3.Experience = (short)v),
            HealthBonusField(() => _army3.HealthBonus, v => _army3.HealthBonus = (short)v),
            Int16Field(Loc.T("Assist.Army.Field.CurrentHealth"), () => _army3.CurrentHealth, v => _army3.CurrentHealth = (short)v),
            Int16Field(Loc.T("Assist.Army.Field.MaxHealth"), () => _army3.MaxHealth, v => _army3.MaxHealth = (short)v),
        ]),
        new(Loc.T("Assist.Army.Section.General"),
        [
            GeneralField(() => _army3.General, v => _army3.General = (short)v),
            RankField(() => _army3.Rank, v => _army3.Rank = (byte)v),
            HpLevelField(Loc.T("Assist.Army.Field.HpLevel"), () => _army3.HpLevel, v => _army3.HpLevel = (byte)v),
        ]),
        new(Loc.T("Assist.Army.Section.Skills"),
        [
            SkillField(1, () => _army3.SkillLevel1, v => _army3.SkillLevel1 = (byte)v),
            SkillField(2, () => _army3.SkillLevel2, v => _army3.SkillLevel2 = (byte)v),
            SkillField(3, () => _army3.SkillLevel3, v => _army3.SkillLevel3 = (byte)v),
            SkillField(4, () => _army3.SkillLevel4, v => _army3.SkillLevel4 = (byte)v),
            SkillField(5, () => _army3.SkillLevel5, v => _army3.SkillLevel5 = (byte)v),
        ]),
        new(Loc.T("Assist.Army.Section.Others"),
        [
            KeyPointField(() => _army3.KeyPoint, v => _army3.KeyPoint = (byte)v),
            PolicyField(() => _army3.Policy, v => _army3.Policy = (byte)v),
            PlanField(() => _army3.Plan, v => _army3.Plan = (short)v),
            Int16Field(Loc.T("Assist.Army.Field.ChangeRound"), () => _army3.ChangeRound, v => _army3.ChangeRound = (short)v),
            MoraleField(() => _army3.Morale, v => _army3.Morale = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.Duration"), () => _army3.Duration, v => _army3.Duration = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.DeathDialogue"), () => _army3.DeathDialogue, v => _army3.DeathDialogue = (byte)v)
                with { Tooltip = Loc.T("Assist.Army.Hint.DeathDialogue") },
            ByteField(Loc.T("Assist.Army.Field.LevelMarkDisplay"), () => _army3.LevelMarkDisplay, v => _army3.LevelMarkDisplay = (byte)v)
                with
                {
                    Tooltip = Loc.T("Assist.Army.Hint.LevelMark"),
                    Options =
                    [
                        new(0, Loc.T("Assist.Army.Option.LevelMark.0")),
                        new(9, Loc.T("Assist.Army.Option.LevelMark.9"))
                    ]
                },
        ]),
        new(Loc.T("Assist.Army.Section.V3Extended"),
        [
            ByteField(Loc.T("Assist.Army.Field.TransportShip"), () => _army3.TransportShip, v => _army3.TransportShip = (byte)v)
                with
                {
                    Options =
                    [
                        new(0, Loc.T("Assist.Army.Option.TransportShip.0")),
                        new(2, Loc.T("Assist.Army.Option.TransportShip.2"))
                    ]
                },
            Int16Field(Loc.T("Assist.Army.Field.Hatred"), () => _army3.Hatred, v => _army3.Hatred = (short)v)
                with { Tooltip = Loc.T("Assist.Army.Hint.Hatred") },
            Int16Field(Loc.T("Assist.Army.Field.AttackTarget"), () => _army3.AttackTarget, v => _army3.AttackTarget = (short)v)
                with { Tooltip = Loc.T("Assist.Army.Hint.AttackTarget") },
            Int32Field(Loc.T("Assist.Army.Field.AntiDefenseMode"), () => _army3.AntiDefenseMode, v => _army3.AntiDefenseMode = v)
                with { Tooltip = Loc.T("Assist.Army.Hint.AntiDefense") },
            MedalField(1, () => _army3.Medal1, v => _army3.Medal1 = (byte)v),
            MedalField(2, () => _army3.Medal2, v => _army3.Medal2 = (byte)v),
            MedalField(3, () => _army3.Medal3, v => _army3.Medal3 = (byte)v),
            RibbonField(1, () => _army3.Ribbon1, v => _army3.Ribbon1 = (byte)v),
            RibbonField(2, () => _army3.Ribbon2, v => _army3.Ribbon2 = (byte)v),
            RibbonField(3, () => _army3.Ribbon3, v => _army3.Ribbon3 = (byte)v),
            ByteField(Loc.T("Assist.Army.Field.Field1F"), () => _army3.Field1F, v => _army3.Field1F = (byte)v)
                with { Tooltip = Loc.T("Assist.Army.Hint.Unknown") },
        ]),
        new(Loc.T("Assist.Army.Section.Unused"),
        [
            WasteField(1, () => _army3.Waste1, v => _army3.Waste1 = (byte)v),
            WasteField(2, () => _army3.Waste2, v => _army3.Waste2 = (byte)v),
            WasteField(3, () => _army3.Waste3, v => _army3.Waste3 = (byte)v),
        ]),
    ];

    private static ArmyField MedalField(int slot, Func<int> get, Action<int> set)
        => ByteField(Loc.T($"Assist.Army.Field.Medal{slot}"), get, set) with { Tooltip = Loc.T("Assist.Army.Hint.Medal") };

    private static ArmyField RibbonField(int slot, Func<int> get, Action<int> set)
        => ByteField(Loc.T($"Assist.Army.Field.Ribbon{slot}"), get, set) with { Tooltip = Loc.T("Assist.Army.Hint.Ribbon") };

    private static ArmyField WasteField(int slot, Func<int> get, Action<int> set)
        => ByteField(Loc.T($"Assist.Army.Field.Waste{slot}"), get, set) with { Tooltip = Loc.T("Assist.Army.Hint.Waste") };
}
