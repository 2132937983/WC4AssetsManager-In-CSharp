using System.CommandLine;
using System.Globalization;
using WC4MapEditor.Core.Analyzers;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Models;
using WC4MapEditor.Core.Services;

namespace WC4MapEditor.Core.Commands;

// CliCommandHost 的数据检查命令组：inspect（文件结构 / 记录 / 十六进制）、compare（二进制对比）、convert（BTL v1 ↔ v3）。
// 与数据检查场景共用 Core 的 BtlInspector / BinaryCompare / BtlVersionConverter。
// inspect / compare / convert --roundtrip 只读文件；convert 只写 -o 指定的新文件，绝不覆盖源文件。
public sealed partial class CliCommandHost
{
    private const int DefaultListCount = 50;
    private const int MaxFindMatches = 200;

    // ============================================================= inspect =============================================================

    [CliSubcommand(Order = 100)]
    private Command BuildInspectCommand()
    {
        var cmd = new Command("inspect", "检查 BTL / BIN 文件结构：文件种类、头部、数据块地址表、记录解码、十六进制（只读）");

        var fileArg = new Argument<string>("file") { Description = "要检查的 .btl / .bin 文件" };
        var layoutOpt = new Option<string>("--layout") { Description = "布局：auto（按内容识别，默认）/ stage / conquest", DefaultValueFactory = _ => "auto" };
        var blockOpt = new Option<string?>("--block", "-b") { Description = "数据块：header legion terrain province belong building army trap case weather event reinforcement airraid placement capital strategy airsupport extension unparsed" };
        var indexOpt = new Option<int>("--index", "-i") { Description = "配合 --block：解码第 n 条记录（从 0 开始）", DefaultValueFactory = _ => -1 };
        var atOpt = new Option<string?>("--at") { Description = "配合 --block：解码位于格子 列,行 的记录（地形 / 省份 / 归属按格子换算，其余按坐标字段查找）" };
        var listOpt = new Option<int>("--list", "-n") { Description = "配合 --block：列出前 n 条记录（0 = 全部，默认 50）", DefaultValueFactory = _ => DefaultListCount };
        var hexOpt = new Option<string?>("--hex") { Description = "十六进制转储的起始偏移（0x 前缀或 h 后缀为十六进制，否则十进制）" };
        var lengthOpt = new Option<int>("--length") { Description = "配合 --hex：转储字节数（默认 256）", DefaultValueFactory = _ => 256 };
        var findOpt = new Option<string?>("--find") { Description = "查找字节序列，如 \"E9 00 ?? 00\"（?? 为任意字节）或 \"\\\"文本\\\"\"" };

        cmd.Arguments.Add(fileArg);
        cmd.Options.Add(layoutOpt);
        cmd.Options.Add(blockOpt);
        cmd.Options.Add(indexOpt);
        cmd.Options.Add(atOpt);
        cmd.Options.Add(listOpt);
        cmd.Options.Add(hexOpt);
        cmd.Options.Add(lengthOpt);
        cmd.Options.Add(findOpt);

        cmd.SetAction(parseResult =>
        {
            if (!TryParseLayout(parseResult.GetValue(layoutOpt), out var layout)) return;
            var inspector = LoadInspector(parseResult.GetValue(fileArg)!, layout);
            if (inspector == null) return;

            string? blockName = parseResult.GetValue(blockOpt);
            string? hexText = parseResult.GetValue(hexOpt);
            string? findText = parseResult.GetValue(findOpt);

            if (!string.IsNullOrWhiteSpace(blockName))
            {
                if (!TryParseBlock(blockName, out var block)) return;
                int index = parseResult.GetValue(indexOpt);
                string? atText = parseResult.GetValue(atOpt);
                if (!string.IsNullOrWhiteSpace(atText)) PrintRecordAt(inspector, block, atText);
                else if (index >= 0) PrintRecord(inspector, block, index);
                else PrintRecordList(inspector, block, parseResult.GetValue(listOpt));
                return;
            }

            if (!string.IsNullOrWhiteSpace(hexText))
            {
                PrintHexDump(inspector, hexText, parseResult.GetValue(lengthOpt));
                return;
            }

            if (!string.IsNullOrWhiteSpace(findText))
            {
                PrintMatches(inspector, findText);
                return;
            }

            PrintOverview(inspector);
        });

        return cmd;
    }

    private bool TryParseLayout(string? text, out InspectLayoutOverride layout)
    {
        switch ((text ?? "auto").Trim().ToLowerInvariant())
        {
            case "auto": layout = InspectLayoutOverride.Auto; return true;
            case "stage": layout = InspectLayoutOverride.Stage; return true;
            case "conquest": layout = InspectLayoutOverride.Conquest; return true;
            default:
                layout = InspectLayoutOverride.Auto;
                _output.WriteLine(Loc.F("DataCheck.Cli.BadLayout", text));
                return false;
        }
    }

    private bool TryParseBlock(string text, out BtlBlock block)
    {
        BtlBlock? alias = text.Trim().ToLowerInvariant() switch
        {
            "unit" or "units" or "troop" => BtlBlock.Army,
            "reinf" or "reinforcements" => BtlBlock.Reinforcement,
            "airforce" or "air" => BtlBlock.AirRaid,
            "placement" or "placements" => BtlBlock.UnitPlacement,
            "plan" or "plans" or "cases" => BtlBlock.Case,
            "ext" => BtlBlock.Extension,
            _ => null
        };
        if (alias is { } a)
        {
            block = a;
            return true;
        }
        if (Enum.TryParse(text.Trim(), true, out block) && Enum.IsDefined(block)) return true;

        _output.WriteLine(Loc.F("DataCheck.Cli.BadBlock", text));
        return false;
    }

    private BtlInspector? LoadInspector(string path, InspectLayoutOverride layout)
    {
        if (!File.Exists(path))
        {
            _output.WriteLine(Loc.F("DataCheck.Cli.FileMissing", path));
            return null;
        }

        try
        {
            return BtlInspector.Load(path, layout);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _output.WriteLine(Loc.F("DataCheck.LoadFailed", ex.Message));
            return null;
        }
    }

    private void PrintOverview(BtlInspector inspector)
    {
        _output.WriteLine(Loc.F("DataCheck.Cli.File", inspector.SourceName));
        _output.WriteLine(Loc.F("DataCheck.Cli.Kind", BtlInspector.KindDisplayName(inspector.Kind), inspector.FileVersion, inspector.FileLength));
        _output.WriteLine(inspector.IsBtl
            ? Loc.F("DataCheck.Cli.Layout", BtlInspector.FamilyDisplayName(inspector.Layout.Family), inspector.LayoutStatusText)
            : Loc.F("DataCheck.Cli.LayoutStatus", inspector.LayoutStatusText));

        _output.WriteLine(Loc.T("DataCheck.Cli.Issues"));
        foreach (var issue in inspector.Issues)
            _output.WriteLine($"  [{Loc.T("DataCheck.IssueLevel." + issue.Level)}] {issue.Message}");

        var header = inspector.GetRecord(BtlBlock.Header, 0);
        if (header != null)
        {
            _output.WriteLine(Loc.T("DataCheck.Cli.Header"));
            foreach (var field in header.Fields)
                _output.WriteLine($"  0x{field.Offset:X2}  {Pad(field.Field.DisplayName, 22)} {Pad(field.Value, 12)} {field.Meaning}");
        }

        _output.WriteLine(Loc.T("DataCheck.Cli.Segments"));
        _output.WriteLine("  " + Loc.T("DataCheck.Cli.SegmentColumns"));
        foreach (var segment in inspector.Layout.Segments)
        {
            string end = segment.Length > 0 ? $"0x{segment.End - 1:X8}" : "—";
            string note = segment.IsTruncated ? Loc.F("DataCheck.Segment.Truncated", segment.ReadableCount) : "";
            _output.WriteLine($"  {Pad(BtlInspector.BlockDisplayName(segment.Block), 14)} 0x{segment.Start:X8}  {end,-10}  " +
                              $"{segment.RecordSize,6}  {segment.Count,8}  {segment.Length,10:N0}  {note}");
        }
    }

    private void PrintRecordList(BtlInspector inspector, BtlBlock block, int limit)
    {
        int count = inspector.GetRecordCount(block);
        _output.WriteLine(Loc.F("DataCheck.Cli.RecordCount", BtlInspector.BlockDisplayName(block), count));
        int shown = limit <= 0 ? count : Math.Min(limit, count);
        for (int i = 0; i < shown; i++)
            _output.WriteLine("  " + inspector.DescribeRecord(block, i));
        if (shown < count)
            _output.WriteLine(Loc.F("DataCheck.Cli.MoreRecords", count - shown));
    }

    private void PrintRecord(BtlInspector inspector, BtlBlock block, int index)
    {
        var record = inspector.GetRecord(block, index);
        if (record == null)
        {
            _output.WriteLine(Loc.F("DataCheck.Cli.NoRecord", BtlInspector.BlockDisplayName(block), index, inspector.GetRecordCount(block)));
            return;
        }

        _output.WriteLine(Loc.F("DataCheck.Record.Title", BtlInspector.BlockDisplayName(block), index, record.Offset, record.Offset + record.Size - 1, record.Size));
        foreach (var field in record.Fields)
        {
            string meaning = string.IsNullOrEmpty(field.Meaning) ? "" : "  " + field.Meaning;
            string note = field.Field.Note is { } n ? "  ※" + n : "";
            _output.WriteLine($"  0x{field.Offset:X8} +0x{field.Field.Offset:X2}  {Pad(field.Field.DisplayName, 22)} {Pad(field.Value, 14)} {field.Hex}{meaning}{note}");
        }
    }

    private void PrintRecordAt(BtlInspector inspector, BtlBlock block, string cellText)
    {
        if (!BtlInspectorHex.TryParseCell(cellText, out int col, out int row))
        {
            _output.WriteLine(Loc.F("DataCheck.Cli.BadCell", cellText));
            return;
        }

        int index = inspector.FindRecordAt(block, col, row);
        if (index < 0)
        {
            _output.WriteLine(Loc.F("DataCheck.Cli.NoRecordAt", BtlInspector.BlockDisplayName(block), col, row));
            return;
        }
        PrintRecord(inspector, block, index);
    }

    private void PrintHexDump(BtlInspector inspector, string offsetText, int length)
    {
        if (!BtlInspectorHex.TryParseOffset(offsetText, out long start) || start >= inspector.Data.LongLength)
        {
            _output.WriteLine(Loc.F("DataCheck.Hex.BadOffset", offsetText));
            return;
        }

        long lineStart = start - start % BtlInspectorHex.BytesPerLine;
        long end = Math.Min(inspector.Data.LongLength, start + Math.Max(1, length));
        _output.WriteLine(Loc.F("DataCheck.Cli.HexAt", start, inspector.DescribeLocation(start)));
        for (long line = lineStart; line < end; line += BtlInspectorHex.BytesPerLine)
            _output.WriteLine(BtlInspectorHex.FormatLine(inspector.Data, line));
    }

    private void PrintMatches(BtlInspector inspector, string patternText)
    {
        if (!BtlInspectorHex.TryParsePattern(patternText, out var pattern))
        {
            _output.WriteLine(Loc.F("DataCheck.Hex.BadPattern", patternText));
            return;
        }

        var matches = BtlInspectorHex.FindAll(inspector.Data, pattern, MaxFindMatches, out bool truncated);
        _output.WriteLine(Loc.F("DataCheck.Cli.Matches", BtlInspectorHex.FormatPattern(pattern), matches.Count, truncated ? "+" : ""));
        foreach (long offset in matches)
            _output.WriteLine($"  0x{offset:X8}  {inspector.DescribeLocation(offset)}");
    }

    private static string Pad(string text, int width)
    {
        // 中文字符按两列宽计，保证表格对齐
        int columns = text.Sum(c => c > 0x2E80 ? 2 : 1);
        return columns >= width ? text : text + new string(' ', width - columns);
    }

    // ============================================================= compare =============================================================

    [CliSubcommand(Order = 101)]
    private Command BuildCompareCommand()
    {
        var cmd = new Command("compare", "二进制对比两个 BTL / BIN 文件：差异区间按各自的数据块定位，并按记录逐字段对比（只读）");

        var fileAArg = new Argument<string>("fileA") { Description = "文件 A（基准）" };
        var fileBArg = new Argument<string>("fileB") { Description = "文件 B（对比）" };
        var outOpt = new Option<string?>("--out", "-o") { Description = "把完整报告写入指定文件（UTF-8）" };
        var rangesOpt = new Option<int>("--ranges") { Description = "控制台最多列出的差异区间数（默认 200）", DefaultValueFactory = _ => 200 };
        var recordsOpt = new Option<int>("--records") { Description = "控制台每个数据块最多列出的差异记录数（默认 100）", DefaultValueFactory = _ => 100 };

        cmd.Arguments.Add(fileAArg);
        cmd.Arguments.Add(fileBArg);
        cmd.Options.Add(outOpt);
        cmd.Options.Add(rangesOpt);
        cmd.Options.Add(recordsOpt);

        cmd.SetAction(parseResult =>
        {
            var a = LoadInspector(parseResult.GetValue(fileAArg)!, InspectLayoutOverride.Auto);
            var b = a == null ? null : LoadInspector(parseResult.GetValue(fileBArg)!, InspectLayoutOverride.Auto);
            if (a == null || b == null) return;

            var result = BinaryCompare.Compare(a, b);
            _output.Write(BinaryCompare.BuildReport(result, parseResult.GetValue(rangesOpt), parseResult.GetValue(recordsOpt)));

            string? outPath = parseResult.GetValue(outOpt);
            if (string.IsNullOrWhiteSpace(outPath)) return;
            try
            {
                File.WriteAllText(outPath, BinaryCompare.BuildReport(result), new UTF8Encoding(true));
                _output.WriteLine(Loc.F("DataCheck.Compare.Saved", outPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _output.WriteLine(Loc.F("DataCheck.Compare.SaveFailed", ex.Message));
            }
        });

        return cmd;
    }

    // ============================================================= convert =============================================================

    [CliSubcommand(Order = 102)]
    private Command BuildConvertCommand()
    {
        var cmd = new Command("convert",
            "BTL 单位/援军记录版本转换 v1↔v3：convert <文件> --to 1|3 -o <新文件>（只换单位/援军块与版本号，不覆盖源文件）；" +
            "convert <文件或文件夹> --roundtrip（只读往返检查）；convert --to 1|3（转换当前地图，可撤销）");

        var pathArg = new Argument<string?>("path") { Description = "BTL 文件（--roundtrip 时也可以是文件夹）；省略则转换当前地图（编辑器数据结构放不下的字段会拒绝，--allow-loss 强制）", Arity = ArgumentArity.ZeroOrOne };
        var toOpt = new Option<int>("--to") { Description = "目标版本：1 或 3", DefaultValueFactory = _ => 0 };
        var outOpt = new Option<string?>("--out", "-o") { Description = "转换结果的新文件路径（文件转换时必填，不能等于源文件）" };
        var forceOpt = new Option<bool>("--force") { Description = "允许覆盖已存在的输出文件（仍然不允许覆盖源文件）" };
        var roundTripOpt = new Option<bool>("--roundtrip") { Description = "只做往返检查，不写任何文件" };
        var detailsOpt = new Option<bool>("--details") { Description = "配合 --roundtrip 文件夹：逐个文件输出完整报告" };
        var allowLossOpt = new Option<bool>("--allow-loss") { Description = "转换当前地图时，即使编辑器的数据结构放不下部分字段也继续" };
        var layoutOpt = new Option<string>("--layout") { Description = "布局：auto（默认）/ stage / conquest", DefaultValueFactory = _ => "auto" };

        cmd.Arguments.Add(pathArg);
        cmd.Options.Add(toOpt);
        cmd.Options.Add(outOpt);
        cmd.Options.Add(forceOpt);
        cmd.Options.Add(roundTripOpt);
        cmd.Options.Add(detailsOpt);
        cmd.Options.Add(allowLossOpt);
        cmd.Options.Add(layoutOpt);

        cmd.SetAction(parseResult =>
        {
            if (!TryParseLayout(parseResult.GetValue(layoutOpt), out var layout)) return;
            string? path = parseResult.GetValue(pathArg);
            int target = parseResult.GetValue(toOpt);

            if (parseResult.GetValue(roundTripOpt))
            {
                if (string.IsNullOrWhiteSpace(path)) { _output.WriteLine(Loc.T("DataCheck.Cli.RoundTripNeedsPath")); return; }
                if (Directory.Exists(path)) RoundTripFolder(path, layout, parseResult.GetValue(detailsOpt));
                else RoundTripFile(path, layout);
                return;
            }

            if (target is not (1 or 3)) { _output.WriteLine(Loc.F("DataCheck.Convert.Err.Target", target)); return; }

            if (string.IsNullOrWhiteSpace(path)) ConvertCurrentMap(target, parseResult.GetValue(allowLossOpt));
            else ConvertFileCommand(path, target, parseResult.GetValue(outOpt), parseResult.GetValue(forceOpt), layout);
        });

        return cmd;
    }

    private void ConvertFileCommand(string path, int target, string? outPath, bool force, InspectLayoutOverride layout)
    {
        if (!File.Exists(path)) { _output.WriteLine(Loc.F("DataCheck.Cli.FileMissing", path)); return; }
        if (string.IsNullOrWhiteSpace(outPath))
        {
            _output.WriteLine(Loc.F("DataCheck.Cli.NeedOutput", Path.GetFileNameWithoutExtension(path) + "_v" + target + ".btl"));
            return;
        }

        string fullSource = Path.GetFullPath(path);
        string fullOutput = Path.GetFullPath(outPath);
        if (string.Equals(fullSource, fullOutput, StringComparison.OrdinalIgnoreCase))
        {
            _output.WriteLine(Loc.T("DataCheck.Convert.Err.SameFile"));
            return;
        }
        if (File.Exists(fullOutput) && !force)
        {
            _output.WriteLine(Loc.F("DataCheck.Cli.OutputExists", fullOutput));
            return;
        }

        try
        {
            var result = BtlVersionConverter.ConvertFile(ReadAllBytesShared(fullSource), target, layout);
            if (result.Success)
                File.WriteAllBytes(fullOutput, result.Output!);
            _output.Write(result.ToReport(fullSource, result.Success ? fullOutput : null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _output.WriteLine(Loc.F("DataCheck.Convert.WriteFailed", ex.Message));
        }
    }

    private void RoundTripFile(string path, InspectLayoutOverride layout)
    {
        if (!File.Exists(path)) { _output.WriteLine(Loc.F("DataCheck.Cli.FileMissing", path)); return; }
        try
        {
            var result = BtlVersionConverter.CheckRoundTrip(ReadAllBytesShared(path), layout);
            _output.Write(result.ToReport(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _output.WriteLine(Loc.F("DataCheck.LoadFailed", ex.Message));
        }
    }

    /// <summary>对文件夹里的全部 .btl 做往返检查并汇总（按源记录族分组统计每个字段丢失的记录数 / 文件数）。</summary>
    private void RoundTripFolder(string folder, InspectLayoutOverride layout, bool details)
    {
        var files = Directory.GetFiles(folder, "*.btl", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        var groups = new SortedDictionary<int, RoundTripGroup>();
        var refused = new List<string>();

        foreach (var file in files)
        {
            BtlRoundTripResult result;
            try
            {
                result = BtlVersionConverter.CheckRoundTrip(ReadAllBytesShared(file), layout);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                refused.Add($"{Path.GetFileName(file)}: {ex.Message}");
                continue;
            }

            if (details) _output.Write(result.ToReport(file));
            if (!result.Success)
            {
                refused.Add($"{Path.GetFileName(file)}: {result.Error}");
                continue;
            }

            if (!groups.TryGetValue(result.SourceVersion, out var group))
                groups[result.SourceVersion] = group = new RoundTripGroup(result.ViaVersion);
            group.Add(Path.GetFileName(file), result);
        }

        _output.WriteLine(Loc.F("DataCheck.RoundTrip.FolderTitle", folder, files.Count));
        foreach (var (version, group) in groups)
        {
            _output.WriteLine(Loc.F("DataCheck.RoundTrip.Group", version, group.Via, version, group.Files, group.Armies, group.Reinforcements));
            _output.WriteLine(Loc.F("DataCheck.RoundTrip.GroupIntact", group.Intact, group.Files));
            foreach (var name in group.OtherChanged)
                _output.WriteLine(Loc.F("DataCheck.RoundTrip.GroupOtherChanged", name));
            if (group.Fields.Count == 0)
                _output.WriteLine(Loc.T("DataCheck.RoundTrip.AllRestored"));
            foreach (var entry in group.Fields.Values.OrderBy(e => e.Block).ThenBy(e => e.Field.Offset))
                _output.WriteLine(Loc.F("DataCheck.RoundTrip.FieldTotal", BtlInspector.BlockDisplayName(entry.Block), entry.Field.DisplayName,
                                        "0x" + entry.Field.Offset.ToString("X2", CultureInfo.InvariantCulture), entry.Records, entry.Files, entry.Example)
                                  + BtlVersionConverter.FillerNote(entry.Fillers));
        }

        if (refused.Count > 0)
        {
            _output.WriteLine(Loc.F("DataCheck.RoundTrip.Refused", refused.Count));
            foreach (var line in refused) _output.WriteLine("  " + line);
        }
    }

    /// <summary>同一源版本的往返检查汇总。</summary>
    private sealed class RoundTripGroup
    {
        public RoundTripGroup(int via) => Via = via;

        public int Via { get; }
        public int Files { get; private set; }
        public int Intact { get; private set; }
        public long Armies { get; private set; }
        public long Reinforcements { get; private set; }
        public List<string> OtherChanged { get; } = new();
        public Dictionary<(BtlBlock, int, string), FieldTotal> Fields { get; } = new();

        public void Add(string name, BtlRoundTripResult result)
        {
            Files++;
            Armies += result.ArmyCount;
            Reinforcements += result.ReinforcementCount;
            if (result.OtherDataIntact) Intact++;
            else OtherChanged.Add(name);

            foreach (var change in result.Changes)
            {
                var key = (change.Block, change.Field.Offset, change.Field.Name);
                if (!Fields.TryGetValue(key, out var total))
                    Fields[key] = total = new FieldTotal(change.Block, change.Field);
                total.Add(name, change);
            }
        }
    }

    /// <summary>某字段在一组文件里的往返变化汇总；示例优先取真实数据（不是 0xCC 填充）的记录。</summary>
    private sealed class FieldTotal
    {
        private bool _exampleIsFiller = true;

        public FieldTotal(BtlBlock block, BtlFieldDef field)
        {
            Block = block;
            Field = field;
        }

        public BtlBlock Block { get; }
        public BtlFieldDef Field { get; }
        public string Example { get; private set; } = "";
        public long Records { get; private set; }
        public long Fillers { get; private set; }
        public int Files { get; private set; }

        public void Add(string fileName, BtlRoundTripChange change)
        {
            bool onlyFiller = change.FillerRecords == change.Records;
            if (Files == 0 || (_exampleIsFiller && !onlyFiller))
            {
                Example = $"{fileName} #{change.FirstRecord}: {change.Before} → {change.After}";
                _exampleIsFiller = onlyFiller;
            }
            Records += change.Records;
            Fillers += change.FillerRecords;
            Files++;
        }
    }

    private void ConvertCurrentMap(int target, bool allowLoss)
    {
        BtlMapConversion? planned = null;
        WithModifiers((map, _) => planned = BtlVersionConverter.PlanMapConversion(map, target), readOnly: true);
        if (planned == null) return;

        BtlMapConversion plan = planned;
        _output.Write(plan.ToReport());
        if (!plan.Success) return;

        if (plan.ModelLosses.Count > 0 && !allowLoss)
        {
            _output.WriteLine(Loc.T("DataCheck.Convert.Map.Refused"));
            return;
        }

        WithModifiers((map, mgr) =>
        {
            mgr.RecordEntityChange(Loc.F("DataCheck.Convert.Map.Undo", plan.SourceVersion, plan.TargetVersion),
                                   () => plan.Apply(map), () => plan.Revert(map));
            _output.WriteLine(Loc.F("DataCheck.Convert.Map.Done", plan.TargetVersion));
        });
    }

    /// <summary>只读方式读取整个文件（允许其它程序同时打开）。</summary>
    private static byte[] ReadAllBytesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        return data;
    }
}
