using System.Diagnostics;
using System.IO;
using System.Text;
using SkiaSharp;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Config;
using WC4MapEditor.Core.Input;

namespace WC4MapEditor.Rendering.Skia;

public sealed class DebugConsole : IDisposable
{
    private static readonly object _lock = new();
    private static DebugConsole? _instance;

    public static DebugConsole Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new DebugConsole();
                }
            }
            return _instance;
        }
    }

    private readonly List<string> _logLines = new();
    private readonly List<string> _commandHistory = new();
    private int _maxLogLines = 1000;
    private int _historyIndex = -1;
    private string _currentInput = "";
    private int _cursorPosition;

    private float _scrollOffset;
    private readonly float _lineHeight = 18f;
    private readonly float _margin = 10f;

    private bool _isVisible;
    private bool _cursorVisible = true;
    private bool _batchMode;
    private System.Threading.Timer? _cursorTimer;

    private SKPaint? _backgroundPaint;
    private SKPaint? _textPaint;
    private SKPaint? _textShadowPaint;
    private SKPaint? _inputPaint;
    private SKPaint? _inputShadowPaint;
    private SKPaint? _cursorPaint;
    private SKPaint? _scrollBarBgPaint;
    private SKPaint? _scrollBarPaint;
    private SKFont? _font;

    private SKBitmap? _backgroundImage;
    private SKPaint? _backgroundImagePaint;
    private bool _hasBackgroundImage;

    private float _scrollBarTrackTop;
    private float _scrollBarTrackHeight;
    private SKRect _scrollBarRect;

    public bool IsVisible => _isVisible;

    public Action? InvalidateCallback { get; set; }

    private ICommandHost? _commandHost;

    /// <summary>
    /// 命令宿主。默认使用 Core 提供的内置实现，宿主层可注入自定义实现。
    /// </summary>
    public ICommandHost CommandHost
    {
        get => _commandHost ??= CommandHostProvider.Default;
        set => _commandHost = value;
    }

    /// <summary>
    /// 用于在 UI 线程执行回调的委托 - 由 GUI 层注入（如 WPF 的 Dispatcher.Invoke）
    /// </summary>
    public Action<Action>? InvokeOnUiThread { get; set; }

    /// <summary>
    /// 控制台可见性变化事件 - 参数为是否可见
    /// </summary>
    public event Action<bool>? VisibilityChanged;

    /// <summary>
    /// 宿主窗口的关闭回调 - 由 GUI 层注入（例如 WPF 窗口的 Close 方法）。
    /// 渲染库不依赖任何 UI 框架类型。
    /// </summary>
    private Action? _closeHostAction;

    private DebugConsole()
    {
        InitializePaints();
        LoadBackgroundImage();
        RegisterBuiltinCommands();

        WriteLine("=== 调试控制台 ===");
        WriteLine("按 F3 显示/隐藏此窗口");
        WriteLine("PageUp/PageDown 滚动查看历史");
        WriteLine("Home/End 跳转到顶部/底部");
        WriteLine("输入 help 查看可用命令");
        WriteLine("");
    }

    private void InitializePaints()
    {
        _backgroundPaint = new SKPaint { Color = new SKColor(0, 0, 0, 230), IsAntialias = true };

        _backgroundImagePaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 102),
            IsAntialias = true
        };

        var typeface = SKFontManager.Default.MatchCharacter('中')
                       ?? SKTypeface.FromFamilyName("Microsoft YaHei")
                       ?? SKTypeface.FromFamilyName("SimHei")
                       ?? SKTypeface.Default;
        _font = new SKFont(typeface, 14)
        {
            Edging = SKFontEdging.Antialias,
            Hinting = SKFontHinting.Slight
        };

        _textPaint = new SKPaint { Color = new SKColor(255, 255, 255), IsAntialias = true };
        _textShadowPaint = new SKPaint { Color = new SKColor(0, 0, 0, 140), IsAntialias = true };
        _inputPaint = new SKPaint { Color = new SKColor(144, 238, 144), IsAntialias = true };
        _inputShadowPaint = new SKPaint { Color = new SKColor(0, 0, 0, 140), IsAntialias = true };
        _cursorPaint = new SKPaint { Color = new SKColor(144, 238, 144) };
        _scrollBarBgPaint = new SKPaint { Color = new SKColor(100, 100, 100, 100) };
        _scrollBarPaint = new SKPaint { Color = new SKColor(200, 200, 200, 200) };
    }

    private void LoadBackgroundImage()
    {
        try
        {
            string consoleBgPath = ConfigManager.Instance.GetConsoleBackgroundPath();
            if (!string.IsNullOrEmpty(consoleBgPath) && File.Exists(consoleBgPath))
            {
                _backgroundImage = SKBitmap.Decode(consoleBgPath);
                _hasBackgroundImage = _backgroundImage != null;
            }
        }
        catch
        {
            _hasBackgroundImage = false;
        }
    }

    private void RegisterBuiltinCommands()
    {
        var cm = CommandManager.Instance;
        cm.RegisterCommand("help", _ =>
        {
            WriteLine(cm.GetHelp());
        }, "显示帮助信息");

        cm.RegisterCommand("clear", _ =>
        {
            Clear();
        }, "清除控制台输出");

        cm.RegisterCommand("exit", _ =>
        {
            HideConsole();
        }, "关闭控制台");

        cm.RegisterCommand("echo", args =>
        {
            WriteLine(string.Join(" ", args));
        }, "输出文本", "<text>");

        cm.RegisterCommand("history", _ =>
        {
            if (_commandHistory.Count == 0)
            {
                WriteLine("无历史命令");
                return;
            }
            for (int i = 0; i < _commandHistory.Count; i++)
                WriteLine($"  [{i}] {_commandHistory[i]}");
        }, "显示命令历史");

        cm.RegisterCommand("log", args =>
        {
            if (args.Length > 0 && int.TryParse(args[0], out int count))
            {
                count = Math.Clamp(count, 1, 100);
                int start = Math.Max(0, _logLines.Count - count);
                for (int i = start; i < _logLines.Count; i++)
                    WriteLine(_logLines[i]);
            }
            else
            {
                WriteLine($"当前日志行数: {_logLines.Count}/{_maxLogLines}");
            }
        }, "查看最近日志", "[行数]");
    }

    public void StartCursorTimer()
    {
        if (_cursorTimer != null) return;

        _cursorTimer = new System.Threading.Timer(_ =>
        {
            _cursorVisible = !_cursorVisible;
            if (InvokeOnUiThread != null)
                InvokeOnUiThread(() => InvalidateCallback?.Invoke());
            else
                InvalidateCallback?.Invoke();
        }, null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
    }

    public void WriteLine(string message)
    {
        string formattedMessage = _batchMode
            ? message
            : $"[{DateTime.Now:HH:mm:ss.fff}] {message}";

        int maxCharsPerLine = 100;
        if (formattedMessage.Length > maxCharsPerLine)
        {
            foreach (string line in WrapText(formattedMessage, maxCharsPerLine))
                _logLines.Add(line);
        }
        else
        {
            _logLines.Add(formattedMessage);
        }

        if (_logLines.Count > _maxLogLines)
            _logLines.RemoveRange(0, _logLines.Count - _maxLogLines);

        if (!_batchMode && _isVisible)
            InvalidateCallback?.Invoke();
    }

    public void BeginBatchMode()
    {
        _batchMode = true;
    }

    public void EndBatchMode()
    {
        _batchMode = false;
    }

    private static List<string> WrapText(string text, int maxCharsPerLine)
    {
        var result = new List<string>();
        var currentLine = new StringBuilder();
        int currentLength = 0;

        foreach (char ch in text)
        {
            if (ch == '\n')
            {
                if (currentLine.Length > 0)
                {
                    result.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLength = 0;
                }
                continue;
            }

            currentLine.Append(ch);
            int charWidth = ch > 127 ? 2 : 1;
            currentLength += charWidth;

            if (currentLength >= maxCharsPerLine)
            {
                int breakPos = FindBreakPosition(currentLine.ToString());
                if (breakPos > 0 && breakPos < currentLine.Length - 1)
                {
                    result.Add(currentLine.ToString()[..(breakPos + 1)]);
                    string remaining = currentLine.ToString()[(breakPos + 1)..];
                    currentLine.Clear();
                    currentLine.Append(remaining);
                    currentLength = remaining.Length;
                }
                else
                {
                    result.Add(currentLine.ToString());
                    currentLine.Clear();
                    currentLength = 0;
                }
            }
        }

        if (currentLine.Length > 0)
            result.Add(currentLine.ToString());

        return result;
    }

    private static int FindBreakPosition(string text)
    {
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] is ' ' or ',' or '.' or ';' or ':' or '-')
                return i;
        }
        return -1;
    }

    public void Clear()
    {
        _logLines.Clear();
        _scrollOffset = 0;
    }

    public void ShowConsole()
    {
        if (_isVisible) return;
        _isVisible = true;
        _cursorTimer?.Change(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        InvalidateCallback?.Invoke();
        VisibilityChanged?.Invoke(true);
    }

    public void HideConsole()
    {
        if (!_isVisible) return;
        _isVisible = false;
        _cursorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _currentInput = "";
        _cursorPosition = 0;
        InvalidateCallback?.Invoke();
        VisibilityChanged?.Invoke(false);

        var closeHost = _closeHostAction;
        _closeHostAction = null;
        closeHost?.Invoke();
    }

    public void Toggle()
    {
        if (_isVisible) HideConsole();
        else ShowConsole();
    }

    /// <summary>
    /// 设置当前输入文本（由WPF窗口调用）
    /// </summary>
    public void SetCurrentInput(string text)
    {
        _currentInput = text;
        _cursorPosition = text.Length;
    }

    /// <summary>
    /// 执行输入的命令（由WPF窗口调用）
    /// </summary>
    public void ExecuteInput(string input)
    {
        if (string.IsNullOrEmpty(input)) return;

        WriteLine($"> {input}");
        _commandHistory.Add(input);
        if (_commandHistory.Count > 50)
            _commandHistory.RemoveAt(0);
        _historyIndex = _commandHistory.Count;

        ExecuteCommandInternal(input);

        _currentInput = "";
        _cursorPosition = 0;
    }

    public void BatchExecuteInput(string input)
    {
        if (string.IsNullOrEmpty(input)) return;

        var result = CommandManager.Instance.ExecuteCommand(input);
        if (!result.Success)
        {
            var commandHost = CommandHost;
            var cliResult = commandHost.Execute(input);
            if (cliResult != 0)
            {
                var (errors, cmdName, cmdDesc) = commandHost.GetParseErrors(input);
                if (errors.Count > 0)
                {
                    WriteLine("[错误] 命令格式不正确:");
                    foreach (var error in errors)
                        WriteLine($"  {error}");
                }
            }
        }
    }

    private void ExecuteCommandInternal(string input)
    {
        var result = CommandManager.Instance.ExecuteCommand(input);
        if (!result.Success)
        {
            var commandHost = CommandHost;
            var cliResult = commandHost.Execute(input);
            if (cliResult != 0)
            {
                var (errors, cmdName, cmdDesc) = commandHost.GetParseErrors(input);
                if (errors.Count > 0)
                {
                    WriteLine("[错误] 命令格式不正确:");
                    foreach (var error in errors)
                        WriteLine($"  {error}");
                    if (!string.IsNullOrEmpty(cmdName) && !string.IsNullOrEmpty(cmdDesc))
                        WriteLine($"  用法: {cmdName} - {cmdDesc}");
                }
                else
                {
                    var helpText = commandHost.GetHelp();
                    if (!string.IsNullOrEmpty(helpText))
                    {
                        var lines = helpText.Split('\n');
                        foreach (var line in lines)
                            WriteLine(line.TrimEnd('\r'));
                    }
                }
            }
        }
    }

    /// <summary>
    /// 导航历史记录（由WPF窗口调用）
    /// </summary>
    public string? NavigateHistory(int direction)
    {
        if (_commandHistory.Count == 0) return null;

        _historyIndex += direction;
        _historyIndex = Math.Clamp(_historyIndex, 0, _commandHistory.Count);

        if (_historyIndex < _commandHistory.Count)
            return _commandHistory[_historyIndex];

        return null;
    }

    /// <summary>
    /// 获取所有日志行（由WPF窗口调用）
    /// </summary>
    public IReadOnlyList<string> GetLogLines() => _logLines;

    /// <summary>
    /// 设置宿主窗口的关闭回调（由 GUI 层调用，例如传入 WPF 窗口的 Close 方法）
    /// </summary>
    public void SetHostWindow(Action? closeAction) => _closeHostAction = closeAction;

    public bool HandleKeyDown(int keyCode, int nativeKey)
    {
        if (!_isVisible) return false;

        var key = (VirtualKey)keyCode;

        switch (key)
        {
            case VirtualKey.Enter:
                ExecuteCurrentInput();
                return true;

            case VirtualKey.Up:
                NavigateHistory(-1);
                return true;

            case VirtualKey.Down:
                NavigateHistory(1);
                return true;

            case VirtualKey.Left:
                if (_cursorPosition > 0) _cursorPosition--;
                return true;

            case VirtualKey.Right:
                if (_cursorPosition < _currentInput.Length) _cursorPosition++;
                return true;

            case VirtualKey.Escape:
                HideConsole();
                return true;

            case VirtualKey.PageUp:
                _scrollOffset += 10;
                ClampScrollOffset();
                return true;

            case VirtualKey.PageDown:
                _scrollOffset -= 10;
                ClampScrollOffset();
                return true;

            case VirtualKey.Home:
                _scrollOffset = int.MaxValue;
                ClampScrollOffset();
                return true;

            case VirtualKey.End:
                _scrollOffset = 0;
                return true;

            case VirtualKey.Back:
                if (_cursorPosition > 0 && _currentInput.Length > 0)
                {
                    _currentInput = _currentInput[..(_cursorPosition - 1)] + _currentInput[_cursorPosition..];
                    _cursorPosition--;
                }
                return true;

            case VirtualKey.Delete:
                if (_cursorPosition < _currentInput.Length)
                    _currentInput = _currentInput[.._cursorPosition] + _currentInput[(_cursorPosition + 1)..];
                return true;

            default:
                return true;
        }
    }

    public bool HandleTextInput(string text)
    {
        if (!_isVisible) return false;

        foreach (char c in text)
        {
            if (!char.IsControl(c))
            {
                _currentInput = _currentInput[.._cursorPosition] + c + _currentInput[_cursorPosition..];
                _cursorPosition++;
            }
        }

        InvalidateCallback?.Invoke();
        return true;
    }

    public bool HandleMouseWheel(double delta)
    {
        if (!_isVisible) return false;

        if (delta > 0)
            _scrollOffset -= 3;
        else
            _scrollOffset += 3;

        ClampScrollOffset();
        InvalidateCallback?.Invoke();
        return true;
    }

    private void ExecuteCurrentInput()
    {
        if (string.IsNullOrEmpty(_currentInput)) return;

        WriteLine($"> {_currentInput}");
        _commandHistory.Add(_currentInput);
        if (_commandHistory.Count > 50)
            _commandHistory.RemoveAt(0);
        _historyIndex = _commandHistory.Count;

        var result = CommandManager.Instance.ExecuteCommand(_currentInput);
        if (!result.Success)
        {
            var commandHost = CommandHost;
            var cliResult = commandHost.Execute(_currentInput);
            if (cliResult != 0)
            {
                var helpText = commandHost.GetHelp();
                if (!string.IsNullOrEmpty(helpText))
                {
                    var lines = helpText.Split('\n');
                    foreach (var line in lines)
                        WriteLine(line.TrimEnd('\r'));
                }
            }
        }

        _currentInput = "";
        _cursorPosition = 0;
        InvalidateCallback?.Invoke();
    }

    private void ClampScrollOffset()
    {
        if (_scrollOffset < 0) _scrollOffset = 0;
    }

    public void Render(SKCanvas canvas, int width, int height)
    {
        if (!_isVisible) return;

        if (_hasBackgroundImage && _backgroundImage != null)
        {
            canvas.DrawBitmap(_backgroundImage, new SKRect(0, 0, width, height), _backgroundImagePaint);
        }
        else
        {
            canvas.DrawRect(0, 0, width, height, _backgroundPaint);
        }

        float inputAreaHeight = 30f;
        float contentHeight = height - inputAreaHeight - _margin;
        int maxLines = (int)(contentHeight / _lineHeight);

        int maxScroll = Math.Max(0, _logLines.Count - maxLines);
        if (_scrollOffset > maxScroll) _scrollOffset = maxScroll;

        int startLineIndex = Math.Max(0, _logLines.Count - maxLines - (int)_scrollOffset);
        int endLineIndex = Math.Min(_logLines.Count - 1, startLineIndex + maxLines - 1);
        int visibleLineCount = endLineIndex - startLineIndex + 1;

        const float shadowOffset = 1.5f;

        for (int i = 0; i < visibleLineCount; i++)
        {
            int logIndex = startLineIndex + i;
            if (logIndex < 0 || logIndex >= _logLines.Count) continue;

            float y = _margin + (i + 1) * _lineHeight;
            string text = _logLines[logIndex];

            canvas.DrawText(text, _margin + shadowOffset, y + shadowOffset, SKTextAlign.Left, _font, _textShadowPaint);
            canvas.DrawText(text, _margin, y, SKTextAlign.Left, _font, _textPaint);
        }

        if (_logLines.Count > maxLines)
        {
            float scrollBarWidth = 6f;
            float scrollBarHeight = (maxLines / (float)_logLines.Count) * contentHeight;
            _scrollBarTrackTop = _margin;
            _scrollBarTrackHeight = contentHeight;
            float scrollRatio = maxScroll > 0 ? _scrollOffset / maxScroll : 0f;
            float scrollBarY = _scrollBarTrackTop + (contentHeight - scrollBarHeight) * (1 - scrollRatio);
            float scrollBarX = width - scrollBarWidth - 4;

            _scrollBarRect = new SKRect(scrollBarX, scrollBarY, scrollBarX + scrollBarWidth, scrollBarY + scrollBarHeight);

            canvas.DrawRect(scrollBarX, _scrollBarTrackTop, scrollBarWidth, contentHeight, _scrollBarBgPaint);
            canvas.DrawRect(_scrollBarRect, _scrollBarPaint);
        }
        else
        {
            _scrollBarRect = SKRect.Empty;
        }

        float inputY = height - inputAreaHeight + 15f;
        string inputText = "> " + _currentInput;

        canvas.DrawText(inputText, _margin + shadowOffset, inputY + shadowOffset, SKTextAlign.Left, _font, _inputShadowPaint);
        canvas.DrawText(inputText, _margin, inputY, SKTextAlign.Left, _font, _inputPaint);

        if (_cursorVisible)
        {
            string textBeforeCursor = "> " + _currentInput[..Math.Min(_cursorPosition, _currentInput.Length)];
            float cursorX = _margin + _font.MeasureText(textBeforeCursor);
            canvas.DrawRect(cursorX, inputY - 12, 2, 14, _cursorPaint);
        }
    }

    public void Dispose()
    {
        _backgroundPaint?.Dispose();
        _textPaint?.Dispose();
        _textShadowPaint?.Dispose();
        _inputPaint?.Dispose();
        _inputShadowPaint?.Dispose();
        _cursorPaint?.Dispose();
        _scrollBarBgPaint?.Dispose();
        _scrollBarPaint?.Dispose();
        _font?.Dispose();
        _backgroundImage?.Dispose();
        _backgroundImagePaint?.Dispose();
        _cursorTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _cursorTimer?.Dispose();
        _cursorTimer = null;
    }
}