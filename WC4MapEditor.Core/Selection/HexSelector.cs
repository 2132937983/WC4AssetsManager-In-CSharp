using WC4MapEditor.Core.Models;

namespace WC4MapEditor.Core.Selection;

public enum SelectionMode
{
    All,
    UnitsOnly,
    BuildingsOnly,
    Custom
}

public sealed class HexSelectionChangedEventArgs : EventArgs
{
    public HexCoord? PreviousPrimary { get; }
    public HexCoord? CurrentPrimary { get; }
    public IReadOnlySet<HexCoord> SelectedHexes { get; }

    public HexSelectionChangedEventArgs(HexCoord? previousPrimary, HexCoord? currentPrimary, IReadOnlySet<HexCoord> selectedHexes)
    {
        PreviousPrimary = previousPrimary;
        CurrentPrimary = currentPrimary;
        SelectedHexes = selectedHexes;
    }
}

public sealed class SelectionRectChangedEventArgs : EventArgs
{
    public bool IsActive { get; }
    public double StartX { get; }
    public double StartY { get; }
    public double EndX { get; }
    public double EndY { get; }

    public SelectionRectChangedEventArgs(bool isActive, double startX, double startY, double endX, double endY)
    {
        IsActive = isActive;
        StartX = startX;
        StartY = startY;
        EndX = endX;
        EndY = endY;
    }
}

public sealed class HexSelector
{
    private static readonly object _lock = new();
    private static HexSelector? _instance;

    public static HexSelector Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new HexSelector();
                }
            }
            return _instance;
        }
    }

    private readonly HashSet<HexCoord> _selectedHexes = [];
    private HexCoord? _primarySelected;
    private bool _multiSelectEnabled;
    private SelectionMode _selectionMode = SelectionMode.All;
    private Func<HexCoord, bool>? _customFilter;

    private bool _selectionRectActive;
    private double _selectionRectStartX;
    private double _selectionRectStartY;
    private double _selectionRectEndX;
    private double _selectionRectEndY;

    private bool _isMovingSelection;
    private int _moveOffsetCol;
    private int _moveOffsetRow;
    private HashSet<(int, int)>? _originalSelectedHexes;

    public event EventHandler<HexSelectionChangedEventArgs>? SelectionChanged;
    public event EventHandler<SelectionRectChangedEventArgs>? SelectionRectChanged;

    /// <summary>供依赖注入使用的公开构造（替代单例入口）</summary>
    public HexSelector() { }

    public HexCoord? PrimarySelected => _primarySelected;
    public IReadOnlySet<HexCoord> SelectedHexes => _selectedHexes;
    public int SelectedCount => _selectedHexes.Count;
    public bool HasSelection => _selectedHexes.Count > 0;
    public bool MultiSelectEnabled
    {
        get => _multiSelectEnabled;
        set => _multiSelectEnabled = value;
    }

    public SelectionMode CurrentSelectionMode
    {
        get => _selectionMode;
        set => _selectionMode = value;
    }

    public Func<HexCoord, bool>? CustomFilter
    {
        get => _customFilter;
        set => _customFilter = value;
    }

    public bool IsSelectionRectActive => _selectionRectActive;
    public (double StartX, double StartY, double EndX, double EndY) SelectionRect =>
        (_selectionRectStartX, _selectionRectStartY, _selectionRectEndX, _selectionRectEndY);

    public bool IsMovingSelection => _isMovingSelection;
    public int MoveOffsetCol => _moveOffsetCol;
    public int MoveOffsetRow => _moveOffsetRow;
    public IReadOnlySet<(int, int)>? OriginalSelectedHexes => _originalSelectedHexes;

    public void SetSelectionMoving(bool isMoving, int offsetCol = 0, int offsetRow = 0, IEnumerable<(int, int)>? originalHexes = null)
    {
        _isMovingSelection = isMoving;
        _moveOffsetCol = offsetCol;
        _moveOffsetRow = offsetRow;
        if (originalHexes == null)
            _originalSelectedHexes = null;
        else
            _originalSelectedHexes = new HashSet<(int, int)>(originalHexes);
    }

    public void Select(int col, int row, int mapWidth, int mapHeight)
    {
        if (col < 0 || col >= mapWidth || row < 0 || row >= mapHeight) return;
        Select(new HexCoord(col, row));
    }

    public void Select(HexCoord coord)
    {
        var previousPrimary = _primarySelected;
        if (_multiSelectEnabled)
        {
            if (_selectedHexes.Contains(coord))
                _selectedHexes.Remove(coord);
            else
                _selectedHexes.Add(coord);
            _primarySelected = coord;
        }
        else
        {
            _selectedHexes.Clear();
            _selectedHexes.Add(coord);
            _primarySelected = coord;
        }
        OnSelectionChanged(previousPrimary, _primarySelected);
    }

    public void AddToSelection(int col, int row, int mapWidth, int mapHeight)
    {
        if (col < 0 || col >= mapWidth || row < 0 || row >= mapHeight) return;
        AddToSelection(new HexCoord(col, row));
    }

    public void AddToSelection(HexCoord coord)
    {
        var previousPrimary = _primarySelected;
        _selectedHexes.Add(coord);
        _primarySelected = coord;
        OnSelectionChanged(previousPrimary, _primarySelected);
    }

    public void RemoveFromSelection(HexCoord coord)
    {
        if (!_selectedHexes.Contains(coord)) return;
        var previousPrimary = _primarySelected;
        _selectedHexes.Remove(coord);
        if (_primarySelected.HasValue && _primarySelected.Value == coord)
            _primarySelected = _selectedHexes.Count > 0 ? _selectedHexes.Last() : null;
        OnSelectionChanged(previousPrimary, _primarySelected);
    }

    public void ClearSelection()
    {
        if (_selectedHexes.Count == 0 && !_isMovingSelection) return;
        var previousPrimary = _primarySelected;
        _selectedHexes.Clear();
        _primarySelected = null;
        _isMovingSelection = false;
        _moveOffsetCol = 0;
        _moveOffsetRow = 0;
        _originalSelectedHexes = null;
        OnSelectionChanged(previousPrimary, null);
    }

    public bool IsSelected(int col, int row) => _selectedHexes.Contains(new HexCoord(col, row));
    public bool IsSelected(HexCoord coord) => _selectedHexes.Contains(coord);

    public void SetSelection(IEnumerable<HexCoord> coords)
    {
        var previousPrimary = _primarySelected;
        _selectedHexes.Clear();
        foreach (var coord in coords)
            _selectedHexes.Add(coord);
        _primarySelected = _selectedHexes.Count > 0 ? _selectedHexes.Last() : null;
        OnSelectionChanged(previousPrimary, _primarySelected);
    }

    public void SelectRange(HexCoord from, HexCoord to, int mapWidth, int mapHeight)
    {
        var previousPrimary = _primarySelected;
        int minCol = Math.Max(0, Math.Min(from.Col, to.Col));
        int maxCol = Math.Min(mapWidth - 1, Math.Max(from.Col, to.Col));
        int minRow = Math.Max(0, Math.Min(from.Row, to.Row));
        int maxRow = Math.Min(mapHeight - 1, Math.Max(from.Row, to.Row));

        for (int c = minCol; c <= maxCol; c++)
            for (int r = minRow; r <= maxRow; r++)
                _selectedHexes.Add(new HexCoord(c, r));

        _primarySelected = to;
        OnSelectionChanged(previousPrimary, _primarySelected);
    }

    public void BeginSelectionRect(double startX, double startY)
    {
        _selectionRectActive = true;
        _selectionRectStartX = startX;
        _selectionRectStartY = startY;
        _selectionRectEndX = startX;
        _selectionRectEndY = startY;
        OnSelectionRectChanged();
    }

    public void UpdateSelectionRect(double endX, double endY)
    {
        if (!_selectionRectActive) return;
        _selectionRectEndX = endX;
        _selectionRectEndY = endY;
        OnSelectionRectChanged();
    }

    public List<HexCoord> EndSelectionRect(
        Func<double, double, (int col, int row)> screenToHex,
        Func<int, int, (double x, double y)> hexToScreen,
        int mapWidth, int mapHeight,
        Func<HexCoord, bool>? filter = null)
    {
        if (!_selectionRectActive) return [];

        _selectionRectActive = false;
        OnSelectionRectChanged();

        double minX = Math.Min(_selectionRectStartX, _selectionRectEndX);
        double maxX = Math.Max(_selectionRectStartX, _selectionRectEndX);
        double minY = Math.Min(_selectionRectStartY, _selectionRectEndY);
        double maxY = Math.Max(_selectionRectStartY, _selectionRectEndY);

        var corners = new (double x, double y)[]
        {
            (minX, minY), (maxX, minY), (maxX, maxY), (minX, maxY)
        };

        int minCol = int.MaxValue, maxCol = int.MinValue;
        int minRow = int.MaxValue, maxRow = int.MinValue;
        foreach (var (x, y) in corners)
        {
            var (c, r) = screenToHex(x, y);
            minCol = Math.Min(minCol, c);
            maxCol = Math.Max(maxCol, c);
            minRow = Math.Min(minRow, r);
            maxRow = Math.Max(maxRow, r);
        }

        minCol = Math.Max(0, minCol);
        maxCol = Math.Min(mapWidth - 1, maxCol);
        minRow = Math.Max(0, minRow);
        maxRow = Math.Min(mapHeight - 1, maxRow);

        var effectiveFilter = filter ?? GetFilterForCurrentMode();
        var matched = new List<HexCoord>();

        for (int c = minCol; c <= maxCol; c++)
        {
            for (int r = minRow; r <= maxRow; r++)
            {
                var (hexScreenX, hexScreenY) = hexToScreen(c, r);
                if (hexScreenX < minX || hexScreenX > maxX || hexScreenY < minY || hexScreenY > maxY)
                    continue;

                var coord = new HexCoord(c, r);
                if (effectiveFilter != null && !effectiveFilter(coord)) continue;
                matched.Add(coord);
            }
        }

        return matched;
    }

    public void CancelSelectionRect()
    {
        if (!_selectionRectActive) return;
        _selectionRectActive = false;
        OnSelectionRectChanged();
    }

    private Func<HexCoord, bool>? GetFilterForCurrentMode()
    {
        return _selectionMode switch
        {
            SelectionMode.All => null,
            SelectionMode.Custom => _customFilter,
            _ => null
        };
    }

    private void OnSelectionChanged(HexCoord? previousPrimary, HexCoord? currentPrimary)
    {
        SelectionChanged?.Invoke(this, new HexSelectionChangedEventArgs(previousPrimary, currentPrimary, _selectedHexes));
    }

    private void OnSelectionRectChanged()
    {
        SelectionRectChanged?.Invoke(this, new SelectionRectChangedEventArgs(
            _selectionRectActive,
            _selectionRectStartX, _selectionRectStartY,
            _selectionRectEndX, _selectionRectEndY));
    }
}