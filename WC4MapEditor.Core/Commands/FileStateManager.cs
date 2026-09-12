using System.Diagnostics;
using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Commands;

public sealed class FileStateManager
{
    private readonly UndoManager _undoManager;
    private MapData? _currentMapData;
    private string? _currentFilePath;
    private string? _fileType;
    private bool _isDirty;

    public UndoManager UndoManager => _undoManager;
    public MapData? CurrentMapData => _currentMapData;
    public string? CurrentFilePath => _currentFilePath;
    public string? FileType => _fileType;
    public bool IsDirty => _isDirty;
    public bool HasFile => _currentMapData != null;

    public event Action? FileChanged;
    public event Action? DirtyStateChanged;

    public FileStateManager(int maxUndoHistory = 200)
    {
        _undoManager = new UndoManager(maxUndoHistory);
        _undoManager.StateChanged += OnUndoStateChanged;
    }

    public void OpenFile(MapData mapData, string filePath, string fileType)
    {
        _currentMapData = mapData;
        _currentFilePath = filePath;
        _fileType = fileType;
        _isDirty = false;
        _undoManager.Clear();
        Debug.WriteLine($"[FileState] 打开文件: {filePath} ({fileType})");
        FileChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public void CloseFile()
    {
        _currentMapData = null;
        _currentFilePath = null;
        _fileType = null;
        _isDirty = false;
        _undoManager.Clear();
        Debug.WriteLine("[FileState] 关闭文件");
        FileChanged?.Invoke();
        DirtyStateChanged?.Invoke();
    }

    public void MarkDirty()
    {
        if (!_isDirty)
        {
            _isDirty = true;
            DirtyStateChanged?.Invoke();
        }
    }

    public void MarkSaved()
    {
        if (_isDirty)
        {
            _isDirty = false;
            DirtyStateChanged?.Invoke();
        }
    }

    public bool Undo()
    {
        if (!_undoManager.CanUndo) return false;
        bool result = _undoManager.Undo();
        if (result && _currentMapData != null)
            _currentMapData.IsModified = true;
        return result;
    }

    public bool Redo()
    {
        if (!_undoManager.CanRedo) return false;
        bool result = _undoManager.Redo();
        if (result && _currentMapData != null)
            _currentMapData.IsModified = true;
        return result;
    }

    public TerrainChangeCommand CreateTerrainChangeCommand(
        string description, int col, int row, TerrainData before, TerrainData after)
    {
        return new TerrainChangeCommand(_currentMapData!, description,
            new[] { (col, row, before, after) });
    }

    public TerrainChangeCommand CreateTerrainChangeCommand(
        string description, (int col, int row, TerrainData before, TerrainData after)[] changes)
    {
        return new TerrainChangeCommand(_currentMapData!, description, changes);
    }

    private void OnUndoStateChanged()
    {
        if (_undoManager.CanUndo || _undoManager.CanRedo)
            MarkDirty();
    }
}