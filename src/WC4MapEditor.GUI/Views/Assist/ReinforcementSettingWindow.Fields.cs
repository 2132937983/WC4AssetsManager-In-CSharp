using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using WC4MapEditor.Core.Modifiers;
using WC4MapEditor.Core.Parsers.General;

namespace WC4MapEditor.GUI.Views.Assist;

// 援军编辑器的字段表与字段控件：v1 / v3 记录都是连续的 int，字段按"槽位 = 文件偏移 / 4"描述，
// 一张表同时决定控件、取值范围和读写位置，两个版本共用同一套控件逻辑。
public sealed partial class ReinforcementSettingWindow
{
    private enum FieldKind { Number, UnitType, Owner, General, ReadOnly }

    /// <summary>
    /// 字段表的一行：分组标题（<see cref="Slot"/> = -1）或一个字段。
    /// <see cref="Raw"/> 字段（含义未完全弄清、游戏地图里常打包多个值）接受任意整数，<see cref="Min"/> ~ <see cref="Max"/> 只决定按十进制还是十六进制显示。
    /// </summary>
    private sealed record FieldRow(int Slot, string Label, int Min = 0, int Max = 0, FieldKind Kind = FieldKind.Number, string? Tooltip = null, bool Raw = false)
    {
        public bool IsSection => Slot < 0;
    }

    /// <summary>界面上的一个字段：文本框或可编辑下拉框（兵种 / 所属）。</summary>
    private sealed class BoundField
    {
        public BoundField(FieldRow row, TextBox? box, ComboBox? combo)
        {
            Row = row;
            Box = box;
            Combo = combo;
        }

        public FieldRow Row { get; }
        public TextBox? Box { get; }
        public ComboBox? Combo { get; }

        /// <summary>当前输入的数字部分（下拉框取 "值 - 名称" 开头的值）。</summary>
        public string Text
        {
            get
            {
                if (Combo == null) return Box!.Text.Trim();
                string text = (Combo.Text ?? "").Trim();
                int dash = text.IndexOf(" - ", StringComparison.Ordinal);
                return dash > 0 ? text[..dash] : text;
            }
        }
    }

    // 两个版本同位的槽位
    private const int SlotUnitType = 1;
    private const int SlotLevel = 2;
    private const int SlotOrganization = 3;
    private const int SlotGeneral = 7;
    private const int SlotRank = 8;
    private const int SlotSkill1 = 10;
    private const int SlotOwner = 18;
    private const int SlotSpawnRound = 19;

    private readonly List<BoundField> _fields = new();
    private readonly BoundField?[] _skillFields = new BoundField?[5];
    private BoundField? _unitField;
    private BoundField? _organizationField;
    private BoundField? _ownerField;
    private BoundField? _generalField;
    private BoundField? _rankField;
    private Dictionary<int, string> _unitNames = new();

    /// <summary>当前显示的记录（槽位值），用来判断下拉框的文字变化是不是真的改了值。</summary>
    private int[]? _shownSlots;
    private readonly Dictionary<int, BitmapSource?> _unitIcons = new();
    private readonly Dictionary<int, BitmapSource?> _generalHeads = new();

    private TextBlock _hexText = null!;
    private TextBlock _belongText = null!;
    private TextBlock _ownerInfo = null!;
    private TextBlock _generalName = null!;
    private Image _unitImage = null!;
    private Image _generalImage = null!;

    // ================================================================== 字段表

    private static FieldRow Section(string title) => new(-1, title);

    /// <param name="maxLevel">等级上限：v1 地图 6 级，v3 地图的精英兵种到 12 级</param>
    private static IEnumerable<FieldRow> BasicRows(int maxLevel) => new FieldRow[]
    {
        Section(Loc.T("Assist.Reinforce.Section.Basic", "基本信息")),
        new(SlotUnitType, Loc.T("Assist.Reinforce.Field.UnitType", "兵种"), 1, 255, FieldKind.UnitType),
        new(SlotLevel, Loc.T("Assist.Reinforce.Field.Level", "等级"), 1, maxLevel),
        new(SlotOrganization, Loc.T("Assist.Reinforce.Field.Organization", "编制"), 0, 5),
        new(SlotOwner, Loc.T("Assist.Reinforce.Field.Owner", "所属军团"), 0, 65535, FieldKind.Owner,
            Loc.T("Assist.Reinforce.Tip.Owner", "所属军团的行动顺序（ActionId）；填 0 时提交会自动取所在省份省会的军团")),
        new(SlotSpawnRound, Loc.T("Assist.Reinforce.Field.SpawnRound", "爆兵回合"), 1, 9999),
    };

    private static IEnumerable<FieldRow> GeneralRows(string qualityLabel) => new FieldRow[]
    {
        Section(Loc.T("Assist.Reinforce.Section.General", "将领")),
        new(SlotGeneral, Loc.T("Assist.Reinforce.Field.General", "将领"), 0, 99999, FieldKind.General),
        new(SlotRank, Loc.T("Assist.Reinforce.Field.Rank", "军衔"), 0, 11),
        new(9, qualityLabel, 0, 4),
        new(SlotSkill1, Loc.T("Assist.Reinforce.Field.Skill1", "技能1等级"), 0, 5),
        new(SlotSkill1 + 1, Loc.T("Assist.Reinforce.Field.Skill2", "技能2等级"), 0, 5),
        new(SlotSkill1 + 2, Loc.T("Assist.Reinforce.Field.Skill3", "技能3等级"), 0, 5),
        new(SlotSkill1 + 3, Loc.T("Assist.Reinforce.Field.Skill4", "技能4等级"), 0, 5),
        new(SlotSkill1 + 4, Loc.T("Assist.Reinforce.Field.Skill5", "技能5等级"), 0, 5),
    };

    /// <summary>v1 / v2（80 字节）字段表：与 VB ReinforceSetting 的字段一致，另把 0x14 / 0x18 两个未定义字段只读列出。</summary>
    private static IReadOnlyList<FieldRow> V1Rows()
    {
        var rows = new List<FieldRow>(BasicRows(6));
        rows.Insert(4, new FieldRow(4, Loc.T("Assist.Reinforce.Field.Direction", "朝向"), 0, 5));
        rows.AddRange(GeneralRows(Loc.T("Assist.Reinforce.Field.Quality", "品质")));
        rows.Add(Section(Loc.T("Assist.Reinforce.Section.Badges", "胸章")));
        rows.Add(new FieldRow(15, Loc.T("Assist.Reinforce.Field.Badge1", "胸章一"), 0, 255));
        rows.Add(new FieldRow(16, Loc.T("Assist.Reinforce.Field.Badge2", "胸章二"), 0, 255));
        rows.Add(new FieldRow(17, Loc.T("Assist.Reinforce.Field.Badge3", "胸章三"), 0, 255));
        rows.Add(Section(Loc.T("Assist.Reinforce.Section.Advanced", "高级（只读）")));
        rows.Add(new FieldRow(5, Loc.T("Assist.Reinforce.Field.Reserved14", "0x14（未定义）"), Kind: FieldKind.ReadOnly));
        rows.Add(new FieldRow(6, Loc.T("Assist.Reinforce.Field.Reserved18", "0x18（未定义）"), Kind: FieldKind.ReadOnly));
        return rows;
    }

    /// <summary>v3（104 字节）字段表：行为模式、方向、HP 等级、勋章 / 勋带；未知字段与废弃的三个胸章位只读。</summary>
    private static IReadOnlyList<FieldRow> V3Rows()
    {
        var rows = new List<FieldRow>(BasicRows(ReinforcementModifierBase.MaxLevel))
        {
            Section(Loc.T("Assist.Reinforce.Section.Behavior", "行为")),
            // 游戏地图里高 16 位常带一个百分数（如 0x00640001），按原始整数编辑
            new(4, Loc.T("Assist.Reinforce.Field.BehaviorMode", "行为模式"), -1, 255,
                Tooltip: Loc.T("Assist.Reinforce.Tip.Raw", "原始整数：低位是取值，高位可能打包了其它数据；可输入 0x 十六进制"), Raw: true),
            new(5, Loc.T("Assist.Reinforce.Field.DirectionV3", "方向"), 0, 5),
        };
        rows.AddRange(GeneralRows(Loc.T("Assist.Reinforce.Field.HpLevel", "HP等级")));
        rows.Add(Section(Loc.T("Assist.Reinforce.Section.Medals", "勋章与勋带")));
        rows.Add(new FieldRow(20, Loc.T("Assist.Reinforce.Field.Medal1", "勋章一"), 0, 99999));
        rows.Add(new FieldRow(21, Loc.T("Assist.Reinforce.Field.Medal2", "勋章二"), 0, 99999));
        rows.Add(new FieldRow(22, Loc.T("Assist.Reinforce.Field.Medal3", "勋章三"), 0, 99999));
        rows.Add(new FieldRow(23, Loc.T("Assist.Reinforce.Field.Ribbon1", "勋带一"), 0, 99999));
        rows.Add(new FieldRow(24, Loc.T("Assist.Reinforce.Field.Ribbon2", "勋带二"), 0, 99999));
        rows.Add(new FieldRow(25, Loc.T("Assist.Reinforce.Field.Ribbon3", "勋带三"), 0, 99999));
        rows.Add(Section(Loc.T("Assist.Reinforce.Section.Advanced", "高级（只读）")));
        rows.Add(new FieldRow(6, Loc.T("Assist.Reinforce.Field.Unknown", "未知（0x18）"), Kind: FieldKind.ReadOnly));
        rows.Add(new FieldRow(15, Loc.T("Assist.Reinforce.Field.Waste1", "废弃一（0x3C）"), Kind: FieldKind.ReadOnly));
        rows.Add(new FieldRow(16, Loc.T("Assist.Reinforce.Field.Waste2", "废弃二（0x40）"), Kind: FieldKind.ReadOnly));
        rows.Add(new FieldRow(17, Loc.T("Assist.Reinforce.Field.Waste3", "废弃三（0x44）"), Kind: FieldKind.ReadOnly));
        return rows;
    }

    // ================================================================== 记录访问（按版本）

    /// <summary>按版本读写格子上的援军组；记录以 int 槽位数组交换。</summary>
    private interface IRecordAccess
    {
        ReinforcementModifierBase Module { get; }
        IReadOnlyList<FieldRow> Rows { get; }
        int Count(int col, int row);
        int[]? Read(int col, int row, int index);
        ModifierResult Write(int col, int row, int index, int[] slots);
        int Add(int col, int row);
        int Duplicate(int col, int row, int index);
        void Remove(int col, int row, int index);
    }

    private sealed class RecordAccess<T> : IRecordAccess where T : unmanaged, IReinforcementData
    {
        private readonly ReinforcementModifierBase<T> _module;

        public RecordAccess(ReinforcementModifierBase<T> module, IReadOnlyList<FieldRow> rows)
        {
            _module = module;
            Rows = rows;
        }

        public ReinforcementModifierBase Module => _module;
        public IReadOnlyList<FieldRow> Rows { get; }

        public int Count(int col, int row) => _module.CountAt(col, row);

        public int[]? Read(int col, int row, int index)
        {
            var group = _module.GetReinforcementsAt(col, row);
            return index >= 0 && index < group.Count ? ToSlots(group[index]) : null;
        }

        public ModifierResult Write(int col, int row, int index, int[] slots)
            => _module.UpdateReinforcementAt(col, row, index, FromSlots(slots));

        public int Add(int col, int row) => _module.AddReinforcement(col, row, _module.CreateNewReinforcement(col, row));

        public int Duplicate(int col, int row, int index) => _module.DuplicateReinforcementAt(col, row, index);

        public void Remove(int col, int row, int index) => _module.RemoveReinforcementAt(col, row, index);

        private static int[] ToSlots(T record) => MemoryMarshal.Cast<T, int>(MemoryMarshal.CreateReadOnlySpan(ref record, 1)).ToArray();

        private static T FromSlots(int[] slots) => MemoryMarshal.Read<T>(MemoryMarshal.AsBytes(slots.AsSpan()));
    }

    // ================================================================== 控件

    private void BuildFieldControls(FieldPanel f)
    {
        _unitNames = ReinforcementModifierBase.BuildUnitTypeNames();

        f.Section(Loc.T("Assist.Reinforce.Section.Hex", "位置"));
        _hexText = InfoText();
        f.Custom(Loc.T("Assist.Reinforce.Field.Hex", "格子"), _hexText);
        _belongText = InfoText();
        f.Custom(Loc.T("Assist.Reinforce.Field.Belong", "归属"), _belongText,
            Loc.T("Assist.Reinforce.Tip.Belong", "提交后格子归属自动设为第一个援军所属军团"));

        foreach (var row in _access.Rows)
        {
            if (row.IsSection)
            {
                f.Section(row.Label);
                continue;
            }

            var field = row.Kind switch
            {
                FieldKind.UnitType => BuildUnitRow(f, row),
                FieldKind.Owner => BuildOwnerRow(f, row),
                FieldKind.General => BuildGeneralRow(f, row),
                _ => BuildTextRow(f, row),
            };
            _fields.Add(field);

            if (row.Slot == SlotUnitType) _unitField = field;
            else if (row.Slot == SlotOrganization) _organizationField = field;
            else if (row.Slot == SlotOwner) _ownerField = field;
            else if (row.Slot == SlotGeneral) _generalField = field;
            else if (row.Slot == SlotRank) _rankField = field;
            else if (row.Slot >= SlotSkill1 && row.Slot < SlotSkill1 + _skillFields.Length) _skillFields[row.Slot - SlotSkill1] = field;
        }

        f.Hint(Loc.T("Assist.Reinforce.Hint",
            "兵种、所属、将领可直接输入编号，十六进制写成 0x…；没改动的字段保持原值（游戏地图里有些字段打包了多个值）。Enter 提交，Esc 撤回。"));
    }

    private static TextBlock InfoText() => new()
    {
        Foreground = EditorTheme.TextMuted,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static string RangeTip(FieldRow row) => Loc.F("Assist.Reinforce.Tip.Range", row.Min, row.Max);

    private static BoundField BuildTextRow(FieldPanel f, FieldRow row)
    {
        bool readOnly = row.Kind == FieldKind.ReadOnly;
        var box = f.Text(row.Label, row.Tooltip ?? (readOnly ? Loc.T("Assist.Reinforce.Tip.ReadOnly", "只读：原样保存") : RangeTip(row)));
        if (readOnly)
        {
            box.IsReadOnly = true;
            box.Foreground = EditorTheme.TextMuted;
        }
        return new BoundField(row, box, null);
    }

    private BoundField BuildUnitRow(FieldPanel f, FieldRow row)
    {
        var combo = EditableCombo(_unitNames.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} - {kv.Value}"));
        var field = new BoundField(row, null, combo);
        HookChanged(field, f.Changed, UpdateUnitPreview);
        _unitImage = new Image { Width = 30, Height = 30, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 6, 0) };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(_unitImage);
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);
        f.Custom(row.Label, grid, row.Tooltip ?? RangeTip(row));
        return field;
    }

    private BoundField BuildOwnerRow(FieldPanel f, FieldRow row)
    {
        var legions = Map.Legions;
        var combo = EditableCombo(legions.Select((l, i) => $"{l.ActionId} - {LegionName(i)}"));
        var field = new BoundField(row, null, combo);
        HookChanged(field, f.Changed, UpdateOwnerPreview);
        _ownerInfo = InfoText();
        _ownerInfo.Margin = new Thickness(0, 3, 0, 0);

        var panel = new StackPanel();
        panel.Children.Add(combo);
        panel.Children.Add(_ownerInfo);
        f.Custom(row.Label, panel, row.Tooltip);
        return field;
    }

    private BoundField BuildGeneralRow(FieldPanel f, FieldRow row)
    {
        var box = new TextBox { Width = 80, VerticalAlignment = VerticalAlignment.Center };
        var changed = f.Changed;
        box.TextChanged += (_, _) =>
        {
            changed();
            UpdateGeneralPreview();
        };
        _generalImage = new Image { Width = 28, Height = 28, Stretch = Stretch.Uniform, Margin = new Thickness(6, 0, 6, 0) };
        _generalName = InfoText();
        var random = EditorTheme.Button(Loc.T("Assist.Reinforce.RandomGeneral", "随机将领"), (_, _) => OnRandomGeneral());
        random.ToolTip = Loc.T("Assist.Reinforce.Tip.RandomGeneral", "按所属军团的国家随机挑选一名将领，并填写军衔与技能等级");
        random.Margin = new Thickness(6, 0, 0, 0);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(box);
        Grid.SetColumn(_generalImage, 1);
        grid.Children.Add(_generalImage);
        Grid.SetColumn(_generalName, 2);
        grid.Children.Add(_generalName);
        Grid.SetColumn(random, 3);
        grid.Children.Add(random);
        f.Custom(row.Label, grid, row.Tooltip ?? RangeTip(row));
        return new BoundField(row, box, null);
    }

    private static ComboBox EditableCombo(IEnumerable<string> items)
    {
        var combo = EditorTheme.ComboBox();
        combo.IsEditable = true;
        foreach (var item in items) combo.Items.Add(item);
        return combo;
    }

    /// <summary>
    /// 下拉框选择或输入变化时：值确实变了才标记未提交改动，然后刷新预览。
    /// 可编辑下拉框的内部文本框要等模板应用后（窗口显示时，已过了加载阶段）才拿到文字，
    /// 不比较数值的话窗口一打开就成了"有未提交修改"，切换列表项或关窗时会把没改过的数据再提交一遍。
    /// </summary>
    private void HookChanged(BoundField field, Action markDirty, Action preview)
    {
        void OnChanged()
        {
            if (!IsShownValue(field)) markDirty();
            preview();
        }
        field.Combo!.SelectionChanged += (_, _) => OnChanged();
        field.Combo.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => OnChanged()));
    }

    private bool IsShownValue(BoundField field)
        => _shownSlots != null && TryParseInt(field.Text, out int value) && value == _shownSlots[field.Row.Slot];

    // ================================================================== 显示与读取

    /// <summary>在范围内的值显示十进制；范围外（打包或未初始化的原始值）显示 0x 十六进制，避免一长串负数。</summary>
    private static string Format(int value, FieldRow row)
    {
        bool plain = row.Kind == FieldKind.ReadOnly ? value is >= 0 and <= 65535 : value >= row.Min && value <= row.Max;
        return plain ? value.ToString(CultureInfo.InvariantCulture) : "0x" + value.ToString("X8", CultureInfo.InvariantCulture);
    }

    private static void ShowValue(BoundField field, int value)
    {
        string text = Format(value, field.Row);
        if (field.Combo == null)
        {
            field.Box!.Text = text;
        }
        else if (text.StartsWith("0x", StringComparison.Ordinal))
        {
            field.Combo.SelectedIndex = -1;
            field.Combo.Text = text;
        }
        else
        {
            SetChoice(field.Combo, value);
        }
    }

    private void ShowRecord(int[] slots)
    {
        _shownSlots = slots;
        foreach (var field in _fields) ShowValue(field, slots[field.Row.Slot]);
        UpdateUnitPreview();
        UpdateOwnerPreview();
        UpdateGeneralPreview();
    }

    /// <summary>提交后把可能被模块自动填写的所属显示回来。</summary>
    private void ShowAutoFilled(int[] saved)
    {
        _shownSlots = saved;
        if (_ownerField == null) return;
        ShowValue(_ownerField, saved[SlotOwner]);
        UpdateOwnerPreview();
    }

    /// <summary>解析十进制或 0x 十六进制（十六进制按 32 位原样解释，可以表示负数）。</summary>
    private static bool TryParseInt(string text, out int value)
    {
        text = text.Trim();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            bool ok = uint.TryParse(text.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint hex);
            value = unchecked((int)hex);
            return ok;
        }
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// 读出全部可编辑字段：没改动的值原样保留（游戏地图里有打包 / 越界的原始值），改过的必须在取值范围内；
    /// 改了兵种或编制时编制不能超过该兵种的最大编制。有错误时写入 <paramref name="errors"/> 并返回 false。
    /// </summary>
    private bool TryReadRecord(int[] original, List<string> errors, out int[] slots)
    {
        slots = (int[])original.Clone();
        foreach (var field in _fields)
        {
            if (field.Row.Kind == FieldKind.ReadOnly) continue;
            int slot = field.Row.Slot;
            if (!TryParseInt(field.Text, out int value))
            {
                errors.Add(Loc.F("Assist.ListEditor.NotInteger", field.Row.Label));
                continue;
            }
            if (value != original[slot] && !field.Row.Raw && (value < field.Row.Min || value > field.Row.Max))
            {
                errors.Add(Loc.F("Assist.ListEditor.OutOfRange", field.Row.Label, field.Row.Min, field.Row.Max));
                continue;
            }
            slots[slot] = value;
        }

        bool formationTouched = slots[SlotUnitType] != original[SlotUnitType] || slots[SlotOrganization] != original[SlotOrganization];
        int maxFormation = ConfigManager.Instance.GetMaxFormation(slots[SlotUnitType]);
        if (errors.Count == 0 && formationTouched && maxFormation > 0 && slots[SlotOrganization] > maxFormation)
            errors.Add(Loc.F("Assist.Reinforce.OrganizationTooLarge", _organizationField?.Row.Label ?? "", UnitName(slots[SlotUnitType]), maxFormation));

        return errors.Count == 0;
    }

    // ================================================================== 预览（图标、名称、所属说明）

    private void UpdateUnitPreview()
    {
        if (_unitField == null) return;
        bool ok = TryParseInt(_unitField.Text, out int unitType);
        _unitImage.Source = ok ? UnitIcon(unitType) : null;
        if (_organizationField?.Box != null)
        {
            int max = ok ? ConfigManager.Instance.GetMaxFormation(unitType) : 0;
            _organizationField.Box.ToolTip = max > 0
                ? Loc.F("Assist.Reinforce.Tip.MaxFormation", UnitName(unitType), max)
                : RangeTip(_organizationField.Row);
        }
    }

    private void UpdateOwnerPreview()
    {
        if (_ownerField == null) return;
        if (!TryParseInt(_ownerField.Text, out int owner))
        {
            _ownerInfo.Text = "";
            return;
        }
        int belong = _access.Module.BelongOfOwner(owner);
        _ownerInfo.Foreground = belong < 0 && owner > 0 ? EditorTheme.Warning : EditorTheme.TextMuted;
        _ownerInfo.Text = owner <= 0 ? Loc.T("Assist.Reinforce.OwnerAuto", "提交时自动取所在省份省会的军团")
            : belong >= 0 ? Loc.F("Assist.Reinforce.OwnerLegion", LegionName(belong))
            : Loc.F("Assist.Reinforce.OwnerUnknown", owner);
    }

    private void UpdateGeneralPreview()
    {
        if (_generalField == null) return;
        bool ok = TryParseInt(_generalField.Text, out int general);
        _generalImage.Source = ok ? GeneralHead(general) : null;
        _generalName.Text = !ok ? "" : general <= 0 ? Loc.T("Assist.Reinforce.NoGeneral", "无将领") : GeneralName(general);
    }

    private void UpdateHexInfo()
    {
        if (!IsValidHex)
        {
            _hexText.Text = Loc.T("Assist.Reinforce.NoHexShort", "未选择格子");
            _belongText.Text = "";
            return;
        }
        _hexText.Text = Loc.F("Assist.Reinforce.HexInfo", _col, _row, _row * Map.MapWidth + _col, Map.Header.BtlVersion);
        _belongText.Text = Loc.F("Assist.Reinforce.BelongInfo",
            BelongText(Map.GetBelongValue(_col, _row)), BelongText(Map.GetProvinceCapitalBelong(_col, _row)));
    }

    /// <summary>列表项文本：序号、兵种、等级、编制、爆兵回合（有将领时附将领名）。</summary>
    private string DescribeRecord(int index, int[] slots)
    {
        string text = Loc.F("Assist.Reinforce.Item", index + 1, UnitName(slots[SlotUnitType]),
            Short(slots[SlotLevel]), Short(slots[SlotOrganization]), Short(slots[SlotSpawnRound]));
        int general = slots[SlotGeneral];
        return general > 0 ? text + Loc.F("Assist.Reinforce.ItemGeneral", GeneralName(general)) : text;
    }

    private static string Short(int value)
        => value is >= 0 and <= 9999 ? value.ToString(CultureInfo.InvariantCulture) : "0x" + value.ToString("X", CultureInfo.InvariantCulture);

    private string UnitName(int unitType)
        => _unitNames.TryGetValue(unitType, out var name) ? name : Loc.F("Assist.Reinforce.UnknownUnit", unitType);

    private static string GeneralName(int general)
    {
        string? name = GeneralSettingParser.Instance.GetById(general)?.Name;
        return string.IsNullOrWhiteSpace(name) ? Loc.F("Assist.Reinforce.UnknownGeneral", general) : name;
    }

    /// <summary>"军团 2（德国，行动顺序 3）"。</summary>
    private string LegionName(int legionIndex)
    {
        var legion = Map.Legions[legionIndex];
        return Loc.F("Assist.Reinforce.LegionName", legionIndex + 1, CountryNameProvider.Get(legion.CountryId), legion.ActionId);
    }

    private string BelongText(int belong)
    {
        if (!ReinforcementModifierBase.IsBelongValue(belong)) return Loc.T("Assist.Reinforce.NoBelong", "无");
        return belong < Map.Legions.Count ? LegionName(belong) : Loc.F("Assist.Reinforce.BelongNotLegion", belong);
    }

    // ================================================================== 随机将领

    private void OnRandomGeneral()
    {
        if (CurrentIndex < 0 || _generalField == null) return;
        int owner = _ownerField != null && TryParseInt(_ownerField.Text, out int typed) ? typed : 0;
        if (owner <= 0) owner = _access.Module.ResolveNewOwner(_col, _row);

        var pick = _access.Module.PickRandomGeneral(owner);
        if (pick == null)
        {
            SetStatus(Loc.T("Assist.Reinforce.NoGenerals", "将领表为空，无法随机挑选"), true);
            return;
        }

        var p = pick.Value;
        ShowValue(_generalField, p.GeneralId);
        if (_rankField != null) ShowValue(_rankField, p.Rank);
        for (int i = 0; i < _skillFields.Length; i++)
        {
            if (_skillFields[i] is { } skill) ShowValue(skill, i < p.SkillLevels.Count ? p.SkillLevels[i] : 0);
        }
        SetStatus(Loc.F("Assist.Reinforce.GeneralPicked", p.Name, p.GeneralId));
    }

    // ================================================================== 图片

    /// <summary>兵种图标 ArmyMark/legion_icon_{兵种}.png（没有则为 null）。</summary>
    private BitmapSource? UnitIcon(int unitType)
    {
        if (_unitIcons.TryGetValue(unitType, out var cached)) return cached;

        BitmapSource? icon = null;
        try
        {
            string path = IOPath.Combine(ConfigManager.Instance.GetArmyMarkPath(), $"legion_icon_{unitType}.png");
            if (System.IO.File.Exists(path))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                icon = bitmap;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReinforcementSettingWindow] 加载兵种图标失败 {unitType}: {ex.Message}");
        }
        _unitIcons[unitType] = icon;
        return icon;
    }

    /// <summary>将领头像（战术图集里的 head_{英文名}.png）；图集未就绪时返回 null 且不缓存。</summary>
    private BitmapSource? GeneralHead(int general)
    {
        if (general <= 0) return null;
        if (_generalHeads.TryGetValue(general, out var cached)) return cached;

        var cache = TacticalMapImageCache.Instance;
        if (!cache.IsInitialized) return null;

        string? ename = GeneralSettingParser.Instance.GetById(general)?.EName;
        var image = string.IsNullOrEmpty(ename) ? null : cache.GetImage($"head_{ename}.png");
        var head = image == null ? null : ToBitmapSource(image);
        _generalHeads[general] = head;
        return head;
    }

    private static BitmapSource? ToBitmapSource(SKImage image)
    {
        try
        {
            var info = new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var bitmap = new WriteableBitmap(info.Width, info.Height, 96, 96, PixelFormats.Pbgra32, null);
            bool ok;
            bitmap.Lock();
            try
            {
                ok = image.ReadPixels(info, bitmap.BackBuffer, bitmap.BackBufferStride, 0, 0);
                if (ok) bitmap.AddDirtyRect(new Int32Rect(0, 0, info.Width, info.Height));
            }
            finally
            {
                bitmap.Unlock();
            }
            if (!ok) return null;
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReinforcementSettingWindow] 转换将领头像失败: {ex.Message}");
            return null;
        }
    }
}
