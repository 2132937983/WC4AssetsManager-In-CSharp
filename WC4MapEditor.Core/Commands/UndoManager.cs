using System.Diagnostics;

namespace WC4MapEditor.Core.Commands;

public sealed class UndoManager
{
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();
    private readonly int _maxHistory;

    public int UndoCount => _undoStack.Count;
    public int RedoCount => _redoStack.Count;
    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? LastUndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;
    public string? LastRedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    public event Action? StateChanged;

    public UndoManager(int maxHistory = 200)
    {
        _maxHistory = maxHistory;
    }

    public void ExecuteAndRecord(IUndoableCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        TrimHistory();
        OnStateChanged();
        Debug.WriteLine($"[Undo] 执行并记录: {command.Description}, 撤销栈={_undoStack.Count}");
    }

    public void Record(IUndoableCommand command)
    {
        _undoStack.Push(command);
        _redoStack.Clear();
        TrimHistory();
        OnStateChanged();
        Debug.WriteLine($"[Undo] 记录: {command.Description}, 撤销栈={_undoStack.Count}");
    }

    public bool Undo()
    {
        if (_undoStack.Count == 0) return false;
        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
        OnStateChanged();
        Debug.WriteLine($"[Undo] 撤销: {command.Description}, 撤销栈={_undoStack.Count}, 重做栈={_redoStack.Count}");
        return true;
    }

    public bool Redo()
    {
        if (_redoStack.Count == 0) return false;
        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
        OnStateChanged();
        Debug.WriteLine($"[Undo] 重做: {command.Description}, 撤销栈={_undoStack.Count}, 重做栈={_redoStack.Count}");
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        OnStateChanged();
    }

    private void TrimHistory()
    {
        while (_undoStack.Count > _maxHistory)
        {
            var array = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = 0; i < _maxHistory; i++)
                _undoStack.Push(array[_undoStack.Count]);
        }
    }

    private void OnStateChanged() => StateChanged?.Invoke();
}