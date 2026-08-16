using System.Diagnostics;
using System.IO;
using System.Text;
using SkiaSharp;
using WC4MapEditor.Core.Commands;
using WC4MapEditor.Core.Config;

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
    private System.Windows.Threading.DispatcherTimer? _cursorTimer;

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

        _cursorTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _cursorTimer.Tick += (_, _) =>
        {
            _cursorVisible = !_cursorVisible;
            InvalidateCallback?.Invoke();
        };
        _cursorTimer.Start();
    }

    public void WriteLine(string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string formattedMessage = $"[{timestamp}] {message}";

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

        while (_logLines.Count > _maxLogLines)
            _logLines.RemoveAt(0);

        if (_isVisible)
            InvalidateCallback?.Invoke();
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
        _cursorTimer?.Start();
        InvalidateCallback?.Invoke();
    }

    public void HideConsole()
    {
        if (!_isVisible) return;
        _isVisible = false;
        _cursorTimer?.Stop();
        _currentInput = "";
        _cursorPosition = 0;
        InvalidateCallback?.Invoke();
    }

    public void Toggle()
    {
        if (_isVisible) HideConsole();
        else ShowConsole();
    }

    public bool HandleKeyDown(int keyCode, int nativeKey)
    {
        if (!_isVisible) return false;

        System.Windows.Input.Key key = (System.Windows.Input.Key)keyCode;

        switch (key)
        {
            case System.Windows.Input.Key.Enter:
                ExecuteCurrentInput();
                return true;

            case System.Windows.Input.Key.Up:
                NavigateHistory(-1);
                return true;

            case System.Windows.Input.Key.Down:
                NavigateHistory(1);
                return true;

            case System.Windows.Input.Key.Left:
                if (_cursorPosition > 0) _cursorPosition--;
                return true;

            case System.Windows.Input.Key.Right:
                if (_cursorPosition < _currentInput.Length) _cursorPosition++;
                return true;

            case System.Windows.Input.Key.Escape:
                HideConsole();
                return true;

            case System.Windows.Input.Key.PageUp:
                _scrollOffset += 10;
                ClampScrollOffset();
                return true;

            case System.Windows.Input.Key.PageDown:
                _scrollOffset -= 10;
                ClampScrollOffset();
                return true;

            case System.Windows.Input.Key.Home:
                _scrollOffset = int.MaxValue;
                ClampScrollOffset();
                return true;

            case System.Windows.Input.Key.End:
                _scrollOffset = 0;
                return true;

            case System.Windows.Input.Key.Back:
                if (_cursorPosition > 0 && _currentInput.Length > 0)
                {
                    _currentInput = _currentInput[..(_cursorPosition - 1)] + _currentInput[_cursorPosition..];
                    _cursorPosition--;
                }
                return true;

            case System.Windows.Input.Key.Delete:
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

        CommandManager.Instance.ExecuteCommand(_currentInput);

        _currentInput = "";
        _cursorPosition = 0;
        InvalidateCallback?.Invoke();
    }

    private void NavigateHistory(int direction)
    {
        if (_commandHistory.Count == 0) return;

        _historyIndex += direction;
        _historyIndex = Math.Clamp(_historyIndex, 0, _commandHistory.Count);

        if (_historyIndex < _commandHistory.Count)
            _currentInput = _commandHistory[_historyIndex];
        else
            _currentInput = "";

        _cursorPosition = _currentInput.Length;
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
        _cursorTimer?.Stop();
        _cursorTimer = null;
    }
}