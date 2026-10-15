namespace WC4MapEditor.Core.Commands;

/// <summary>
/// <see cref="ICommandHost"/> 的默认实现提供者。
/// <para>
/// 把"具体命令宿主类型"的知识集中在这一个类中：渲染层只引用 <see cref="ICommandHost"/>，
/// 不再直接依赖 <see cref="CliCommandHost"/>。将来若替换命令宿主实现
/// （例如把 CliCommandHost 移出 Core 程序集），只需改动此处。
/// </para>
/// </summary>
public static class CommandHostProvider
{
    private static ICommandHost? _default;

    /// <summary>默认命令宿主（内置的 CLI 命令实现）</summary>
    public static ICommandHost Default => _default ??= CliCommandHost.Instance;

    /// <summary>替换默认命令宿主，供宿主层注入自定义实现</summary>
    public static void Override(ICommandHost host) => _default = host;
}
