using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace WC4MapEditor.Mcp;

/// <summary>
/// 在编辑器进程内启动一个只监听 127.0.0.1 的 MCP HTTP 服务。
/// <para>
/// 为什么不用 stdio：MCP 客户端要能访问"当前打开的地图"，只能连到正在运行的编辑器；
/// 进程内 HTTP 不需要额外进程，也不会出现两个编辑器抢同一张地图。
/// </para>
/// <para>
/// 传输层用 Streamable HTTP，客户端配置形如：
/// <c>http://127.0.0.1:4396/wc4-mcp</c>。
/// </para>
/// </summary>
public static class McpServerHost
{
    private static WebApplication? _app;
    private static readonly object _gate = new();

    public const int DefaultPort = 4396;

    public static bool IsRunning => _app != null;

    public static int Port { get; private set; } = DefaultPort;

    public static string Endpoint => $"http://127.0.0.1:{Port}/wc4-mcp";

    /// <summary>最近一次启动失败原因（端口占用、缺少 ASP.NET Core 运行时等）。</summary>
    public static string? LastError { get; private set; }

    /// <summary>启动 MCP 服务；重复调用直接返回当前状态。</summary>
    public static async Task<bool> StartAsync(int port = DefaultPort)
    {
        lock (_gate)
        {
            if (_app != null) return true;
        }

        try
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ContentRootPath = AppContext.BaseDirectory,
            });

            // 桌面程序不需要控制台日志；MCP 自己的诊断走 Console/Debug
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

            builder.Services
                .AddMcpServer()
                .WithHttpTransport()
                .WithListToolsHandler((_, _) => ValueTask.FromResult(McpToolBridge.ListTools()))
                .WithCallToolHandler((context, token) =>
                    new ValueTask<CallToolResult>(McpToolBridge.CallAsync(context.Params, token)));

            var app = builder.Build();
            app.MapMcp("/wc4-mcp");

            await app.StartAsync().ConfigureAwait(false);

            lock (_gate)
            {
                _app = app;
                Port = port;
                LastError = null;
            }

            Console.WriteLine($"[MCP] listening on {Endpoint}");
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Console.WriteLine($"[MCP] 启动失败：{ex.Message}");
            return false;
        }
    }

    public static async Task StopAsync()
    {
        WebApplication? app;
        lock (_gate)
        {
            app = _app;
            _app = null;
        }

        if (app == null) return;

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await app.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // 退出阶段不因为 MCP 关不掉而阻塞
        }
        finally
        {
            try { await app.DisposeAsync().ConfigureAwait(false); } catch { }
        }
    }
}
