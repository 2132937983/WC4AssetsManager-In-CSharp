using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using MoonSharp.Interpreter;

namespace WC4MapEditor.Scripting;

/// <summary>
/// 补齐 MoonSharp 默认环境里缺失或不可用的部分，让脚本拿到一个"正常 Lua"该有的基本环境。
/// <para>
/// 只做环境层面的补充（标准库、全局函数、模块路径），不涉及任何地图数据 ——
/// 那部分在 <see cref="LuaMapApi"/>。因此本类不引用 Core，依赖方向仍是 <c>Core → Scripting</c>。
/// </para>
/// <para>实测 MoonSharp 2.0 的缺口（均在本类修正）：</para>
/// <list type="bullet">
/// <item><c>os.clock()</c> 存在但恒返回 nil</item>
/// <item><c>os.time()</c> 返回带小数的浮点（Lua 语义是整数秒）</item>
/// <item><c>os.exit()</c> / <c>os.execute()</c> 可用 —— 脚本能把编辑器进程直接干掉或执行外部命令</item>
/// <item><c>io.write()</c> 绕过 Options.Stdout 直接写进程控制台，编辑器里看不见</item>
/// <item><c>utf8</c> 库不存在（中文脚本按字符处理很常用）</item>
/// <item><c>loadstring</c> 不存在（Lua 5.1 习惯写法）</item>
/// <item><c>package.path</c> 为 nil，<c>require</c> 的搜索路径不可见也不可控</item>
/// </list>
/// </summary>
internal static class LuaStandardEnvironment
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    /// <summary>
    /// 安装基本环境。必须在脚本执行前调用。
    /// </summary>
    /// <param name="script">目标脚本实例</param>
    /// <param name="modulePaths">require 的搜索路径（形如 <c>dir/?.lua</c>）</param>
    /// <param name="write">脚本的标准输出回调（io.write 用）</param>
    public static void Install(Script script, string[] modulePaths, Action<string> write)
    {
        // package.path：让 require 的搜索路径可查、可控
        var package = script.Globals.Get("package");
        if (package.Type == DataType.Table)
            package.Table.Set("path", DynValue.NewString(string.Join(";", modulePaths)));

        // Lua 5.1 兼容：loadstring 就是 load
        script.Globals["loadstring"] = script.Globals["load"];

        InstallOs(script);
        InstallIo(script, write);
        script.Globals["utf8"] = BuildUtf8(script);
    }

    // ------------------------------------------------------------------ os

    private static void InstallOs(Script script)
    {
        var os = script.Globals.Get("os");
        if (os.Type != DataType.Table) return;

        var t = os.Table;

        // 进程启动以来的单调秒数（MoonSharp 原生的 os.clock 恒返回 nil）
        t.Set("clock", DynValue.NewCallback((_, _) => DynValue.NewNumber(Clock.Elapsed.TotalSeconds)));

        // 整数秒（原生实现返回带小数的浮点，配合 os.date 用会得到奇怪结果）
        t.Set("time", DynValue.NewCallback((_, _) => DynValue.NewNumber(DateTimeOffset.Now.ToUnixTimeSeconds())));

        // 编辑器内不允许脚本杀进程 / 执行外部命令
        t.Set("exit", DynValue.NewCallback((_, _) =>
            throw new ScriptRuntimeException("os.exit 已禁用：脚本不能退出编辑器")));
        t.Set("execute", DynValue.NewCallback((_, _) =>
            throw new ScriptRuntimeException("os.execute 已禁用：编辑器内不允许执行外部命令")));
    }

    // ------------------------------------------------------------------ io

    private static void InstallIo(Script script, Action<string> write)
    {
        var io = script.Globals.Get("io");
        if (io.Type != DataType.Table) return;

        // MoonSharp 的 io.write 直接写进程控制台，这里统一收进脚本输出缓冲，
        // 否则 GUI 里脚本用 io.write 的输出完全看不到。
        io.Table.Set("write", DynValue.NewCallback((_, args) =>
        {
            var sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                var v = args[i];
                if (v.Type == DataType.String) sb.Append(v.String);
                else if (v.Type == DataType.Number) sb.Append(v.Number.ToString(CultureInfo.InvariantCulture));
                else throw new ScriptRuntimeException("io.write 只接受字符串或数字");
            }

            if (sb.Length > 0) write(sb.ToString());
            return DynValue.Nil;
        }));

        io.Table.Set("flush", DynValue.NewCallback((_, _) => DynValue.Nil));
    }

    // ---------------------------------------------------------------- utf8

    /// <summary>
    /// 最小可用的 utf8 库：len / char / codepoint / offset。
    /// <para>
    /// 索引与 Lua 5.3 一致：字符串按 <b>UTF-8 字节</b>计位（1 起），负索引从末尾算。
    /// </para>
    /// </summary>
    private static Table BuildUtf8(Script script)
    {
        var t = new Table(script);

        t.Set("len", DynValue.NewCallback((ctx, args) =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(ArgString(args, 0, "utf8.len"));
            var (start, end) = NormalizeRange(bytes.Length, ArgInt(args, 1, 1), ArgInt(args, 2, -1));

            int count = 0;
            int pos = start;
            while (pos < end)
            {
                if (Rune.DecodeFromUtf8(bytes.AsSpan(pos, end - pos), out _, out int consumed) != OperationStatus.Done)
                    return DynValue.NewTuple(DynValue.Nil, DynValue.NewNumber(pos + 1));

                pos += consumed;
                count++;
            }

            return DynValue.NewNumber(count);
        }));

        t.Set("char", DynValue.NewCallback((_, args) =>
        {
            var sb = new StringBuilder();
            for (int i = 0; i < args.Count; i++)
            {
                if (args[i].Type != DataType.Number)
                    throw new ScriptRuntimeException("utf8.char: 参数必须是数字码点");

                int cp = (int)args[i].Number;
                if (cp < 0 || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
                    throw new ScriptRuntimeException($"utf8.char: 非法码点 {cp}");

                sb.Append(char.ConvertFromUtf32(cp));
            }

            return DynValue.NewString(sb.ToString());
        }));

        t.Set("codepoint", DynValue.NewCallback((ctx, args) =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(ArgString(args, 0, "utf8.codepoint"));
            var (start, end) = NormalizeRange(bytes.Length, ArgInt(args, 1, 1), ArgInt(args, 2, ArgInt(args, 1, 1)));

            var values = new List<DynValue>();
            int pos = start;
            while (pos < end)
            {
                // 按"从 i 开始解码整字符"，j 只是停止位置（默认 j == i，与 Lua 一致）
                if (Rune.DecodeFromUtf8(bytes.AsSpan(pos), out Rune rune, out int consumed) != OperationStatus.Done)
                    throw new ScriptRuntimeException($"utf8.codepoint: 第 {pos + 1} 字节处不是合法的 UTF-8 序列");

                values.Add(DynValue.NewNumber(rune.Value));
                pos += consumed;
            }

            return DynValue.NewTuple(values.ToArray());
        }));

        t.Set("offset", DynValue.NewCallback((ctx, args) =>
        {
            string s = ArgString(args, 0, "utf8.offset");
            int n = ArgInt(args, 1, 1);
            byte[] bytes = Encoding.UTF8.GetBytes(s);

            var starts = new List<int>();   // 每个字符的起始字节（0 起）
            int pos = 0;
            while (pos < bytes.Length)
            {
                starts.Add(pos);
                if (Rune.DecodeFromUtf8(bytes.AsSpan(pos), out _, out int consumed) != OperationStatus.Done)
                    return DynValue.Nil;

                pos += consumed;
            }

            starts.Add(bytes.Length);       // 末尾哨兵，便于 n == 0 与越界判断

            int i = args.Count > 2 ? ArgInt(args, 2, 1) : (n >= 0 ? 1 : bytes.Length + 1);
            int byteIndex = i > 0 ? i - 1 : bytes.Length + i;
            if (byteIndex < 0 || byteIndex > bytes.Length) return DynValue.Nil;

            int charIndex = starts.IndexOf(byteIndex);
            if (charIndex < 0) return DynValue.Nil;   // i 不在字符边界上

            int target = n == 0 ? charIndex : (n > 0 ? charIndex + n - 1 : charIndex + n);
            if (target < 0 || target >= starts.Count - 1) return DynValue.Nil;

            return DynValue.NewNumber(starts[target] + 1);
        }));

        return t;
    }

    // ------------------------------------------------------------- helpers

    private static string ArgString(CallbackArguments args, int index, string func)
    {
        if (args.Count <= index || args[index].Type != DataType.String)
            throw new ScriptRuntimeException($"{func}: 第 {index + 1} 个参数必须是字符串");

        return args[index].String;
    }

    private static int ArgInt(CallbackArguments args, int index, int defaultValue)
    {
        if (args.Count <= index || args[index].Type != DataType.Number)
            return defaultValue;

        return (int)args[index].Number;
    }

    /// <summary>把 1 起、可负的 Lua 索引区间换算成 0 起、半开区间</summary>
    private static (int Start, int End) NormalizeRange(int length, int i, int j)
    {
        int start = i > 0 ? i - 1 : length + i;
        int end = j > 0 ? j : length + j + 1;

        if (start < 0) start = 0;
        if (end > length) end = length;
        if (end < start) end = start;

        return (start, end);
    }
}
