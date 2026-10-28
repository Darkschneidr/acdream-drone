using System.Numerics;

namespace AcDream.App.UI;

public sealed class UiMarkupList : UiElement
{
    public PluginUiPalette? ThemePalette { get; set; }

    public Func<IReadOnlyList<string>> ItemsSource { get; set; } =
        static () => Array.Empty<string>();
    public Func<IReadOnlyList<uint>> ItemColorsSource { get; set; } =
        static () => Array.Empty<uint>();
    public Func<IReadOnlyList<uint>>? IconIdsSource { get; set; }
    public Func<uint, (uint tex, int w, int h)>? IconResolve { get; set; }
    public Func<int> SelectedIndexSource { get; set; } = static () => -1;
    public Action<int>? SelectionChanged { get; set; }
    public IReadOnlyList<UiMarkupListColumn>? Columns
    {
        get => _columns;
        set
        {
            _columns = value;
            int count = value?.Count ?? 0;
            _cachedTextRows = new IReadOnlyList<string>?[count];
            _cachedColorRows = new IReadOnlyList<uint>?[count];
            _cachedCheckRows = new IReadOnlyList<bool>?[count];
            _cachedIconRows = new IReadOnlyList<uint>?[count];
            _cachedLayout = new (float x, float w)[count];
            _scratchIsAuto = new bool[count];
            _scratchFixedWidth = new float[count];
            _cachedRowCount = 0;
            _hasButtons = value?.Any(static c => c.Kind == UiMarkupListColumnKind.Button) ?? false;
            ClearButtonPointer();
        }
    }
    public UiDatFont? DatFont { get; set; }
    public float RowHeight { get; set; } = 18f;
    public float Padding { get; set; } = 3f;
    public Vector4 BackgroundColor { get; set; } = new(0f, 0f, 0f, 0.92f);
    public Vector4 BorderColor { get; set; } = new(0.46f, 0.37f, 0.16f, 1f);
    public Vector4 TextColor { get; set; } = new(0.91f, 0.87f, 0.76f, 1f);
    public Vector4 SelectedColor { get; set; } = new(0.28f, 0.23f, 0.08f, 0.95f);

    /// <summary>The face of a <c>button</c> column's cells; Classic matches a markup <c>&lt;button&gt;</c>.</summary>
    public Vector4 ButtonColor { get; set; } = new(0.1f, 0.1f, 0.15f, 0.8f);
    public Vector4 ButtonBorderColor { get; set; } = new(0.45f, 0.45f, 0.55f, 1f);

    public bool SelectionBandEnabled { get; set; }

    public Func<uint, (uint tex, int w, int h)>? SpriteResolve { get; set; }

    private const float ScrollbarWidth = 16f;

    private const float ScrollButtonExtent = 16f;

    private int _topRow;
    private IReadOnlyList<UiMarkupListColumn>? _columns;

    private int _lastRevealedSelected = int.MinValue;

    private readonly UiScrollable _scroll = new();

    private bool _draggingThumb;
    private float _thumbDragOffset;

    public UiMarkupList() { CapturesPointerDrag = true; }

    private IReadOnlyList<string>?[] _cachedTextRows = Array.Empty<IReadOnlyList<string>?>();
    private IReadOnlyList<uint>?[] _cachedColorRows = Array.Empty<IReadOnlyList<uint>?>();
    private IReadOnlyList<bool>?[] _cachedCheckRows = Array.Empty<IReadOnlyList<bool>?>();
    private IReadOnlyList<uint>?[] _cachedIconRows = Array.Empty<IReadOnlyList<uint>?>();
    private (float x, float w)[] _cachedLayout = Array.Empty<(float, float)>();
    private bool[] _scratchIsAuto = Array.Empty<bool>();
    private float[] _scratchFixedWidth = Array.Empty<float>();
    private int _cachedRowCount;

    // The button cell under the pointer and the one the left button went
    // down on, as (row, column); -1 for none.
    private bool _hasButtons;
    private int _hoverRow = -1, _hoverColumn = -1;
    private int _pressRow = -1, _pressColumn = -1;

    // What the list showed when the button went down: a release fires by row
    // index, so the press is dropped if the list scrolls, its row count
    // changes, or the pressed button's caption changes before then.
    private int _pressTopRow, _pressRowCount;
    private string _pressCaption = string.Empty;

    // The pointer's last position in list space, so the hovered button can be
    // found again when the rows move under a pointer that did not.
    private bool _pointerKnown;
    private float _pointerX, _pointerY;

    public override bool HandlesClick => true;

    /// <summary>Button cells show hover, so a list with any takes moves while hovered.</summary>
    public override bool ReceivesHoverMouseMove => _hasButtons;

    protected override void OnDraw(UiRenderContext context)
    {
        if (Columns is { Count: > 0 } columns)
        {
            DrawColumns(context, columns);
            return;
        }

        IReadOnlyList<string> items = ItemsSource();
        IReadOnlyList<uint> itemColors = ItemColorsSource();
        IReadOnlyList<uint>? iconIds = IconIdsSource?.Invoke();
        float iconColumn = iconIds is not null
            ? MathF.Max(0f, RowHeight - 2f)
            : 0f;
        int visibleRows = VisibleRows;
        int selected = SelectedIndexSource();
        if (selected != _lastRevealedSelected)
        {
            _lastRevealedSelected = selected;
            if (selected >= 0 && selected < items.Count)
            {
                if (selected < _topRow)
                    _topRow = selected;
                else if (selected >= _topRow + visibleRows)
                    _topRow = selected - visibleRows + 1;
            }
        }
        ClampTop(items.Count, visibleRows);

        bool showScrollbar = items.Count > visibleRows;
        float contentWidth = showScrollbar ? MathF.Max(0f, Width - ScrollbarWidth) : Width;

        context.DrawFill(0f, 0f, Width, Height, BackgroundColor);
        context.DrawRectOutline(0f, 0f, Width, Height, BorderColor, 1f);
        int end = Math.Min(items.Count, _topRow + visibleRows);
        for (int index = _topRow; index < end; index++)
        {
            float y = (index - _topRow) * RowHeight;
            if (index == selected && SelectionBandEnabled)
                context.DrawFill(1f, y + 1f, contentWidth - 2f, RowHeight - 1f, SelectedColor);

            if (iconIds is not null && index < iconIds.Count && IconResolve is { } resolve)
            {
                uint iconId = iconIds[index];
                if (iconId != 0u)
                {
                    (uint tex, int w, int h) = resolve(iconId);
                    if (tex != 0u && w > 0 && h > 0)
                    {
                        float extent = MathF.Max(0f, iconColumn - 2f);
                        float scale = MathF.Min(extent / w, extent / h);
                        float drawWidth = w * scale;
                        float drawHeight = h * scale;
                        context.DrawSprite(
                            tex,
                            1f + (extent - drawWidth) * 0.5f,
                            y + (RowHeight - drawHeight) * 0.5f,
                            drawWidth, drawHeight,
                            0f, 0f, 1f, 1f, Vector4.One);
                    }
                }
            }

            string text = items[index];
            Vector4 textColor = index < itemColors.Count
                ? Rgb(itemColors[index])
                : TextColor;
            float textX = Padding + iconColumn;
            float textY = y + MathF.Max(0f,
                (RowHeight - (DatFont?.LineHeight ?? 14f)) * 0.5f);
            if (DatFont is { } font)
                context.DrawStringDat(font, text, textX, textY, textColor, ThemePalette is null);
            else
                context.DrawString(text, textX, textY, textColor);
        }

        if (showScrollbar)
            DrawScrollbar(context, contentWidth, items.Count, visibleRows);
    }

    public override bool OnEvent(in UiEvent e)
    {
        if (Columns is { Count: > 0 } columns)
            return OnEventColumns(e, columns);

        IReadOnlyList<string> items = ItemsSource();
        int visibleRows = VisibleRows;
        float contentWidth = items.Count > visibleRows
            ? MathF.Max(0f, Width - ScrollbarWidth)
            : Width;
        if (TryHandleScrollbarEvent(e, contentWidth, items.Count, visibleRows))
            return true;

        if (e.Type == UiEventType.Scroll)
        {
            _topRow -= Math.Sign(e.Data0);
            ClampTop(items.Count, visibleRows);
            return true;
        }
        if (e.Type != UiEventType.MouseDown || !Enabled)
            return false;
        int row = (int)MathF.Floor(e.Data2 / MathF.Max(1f, RowHeight));
        int index = _topRow + row;
        if (row >= 0 && row < visibleRows && index >= 0 && index < items.Count)
            SelectionChanged?.Invoke(index);
        return true;
    }

    private int VisibleRows => Math.Max(1, (int)MathF.Floor(
        Height / MathF.Max(1f, RowHeight)));

    private void ClampTop(int count, int visibleRows) =>
        _topRow = Math.Clamp(_topRow, 0, Math.Max(0, count - visibleRows));

    private static Vector4 Rgb(uint value) => new(
        ((value >> 16) & 0xFFu) / 255f,
        ((value >> 8) & 0xFFu) / 255f,
        (value & 0xFFu) / 255f,
        1f);


    private void ComputeColumnLayout(IReadOnlyList<UiMarkupListColumn> columns, float totalWidth)
    {
        int n = columns.Count;
        float x = 0f;
        float sumFixed = 0f;
        int autoCount = 0;
        for (int i = 0; i < n; i++)
        {
            bool last = i == n - 1;
            bool auto = last || columns[i].IsAutoWidth;
            _scratchIsAuto[i] = auto;
            if (auto)
            {
                autoCount++;
                continue;
            }
            float avail = MathF.Max(0f, totalWidth - x);
            float w = MathF.Min(MathF.Max(0f, columns[i].Width), avail);
            _scratchFixedWidth[i] = w;
            x += w;
            sumFixed += w;
        }

        float remaining = MathF.Max(0f, totalWidth - sumFixed);
        float share = autoCount > 0 ? MathF.Floor(remaining / autoCount) : 0f;

        float cursor = 0f;
        for (int i = 0; i < n; i++)
        {
            float w;
            if (_scratchIsAuto[i])
            {
                bool isLast = i == n - 1;
                w = isLast
                    ? MathF.Max(0f, remaining - share * (autoCount - 1))
                    : share;
            }
            else
            {
                w = _scratchFixedWidth[i];
            }
            _cachedLayout[i] = (cursor, w);
            cursor += w;
        }
    }

    private void DrawColumns(UiRenderContext context, IReadOnlyList<UiMarkupListColumn> columns)
    {
        int rowCount = RefreshRows(columns);

        int visibleRows = VisibleRows;
        bool showScrollbar = rowCount > visibleRows;
        float contentWidth = showScrollbar ? MathF.Max(0f, Width - ScrollbarWidth) : Width;
        ComputeColumnLayout(columns, contentWidth);

        int selected = SelectedIndexSource();
        if (selected != _lastRevealedSelected)
        {
            _lastRevealedSelected = selected;
            if (selected >= 0 && selected < rowCount)
            {
                if (selected < _topRow)
                    _topRow = selected;
                else if (selected >= _topRow + visibleRows)
                    _topRow = selected - visibleRows + 1;
            }
        }
        ClampTop(rowCount, visibleRows);
        if (_hasButtons)
        {
            CancelPressIfRowsMoved();
            RefreshHover(contentWidth, visibleRows);
        }

        context.DrawFill(0f, 0f, Width, Height, BackgroundColor);
        context.DrawRectOutline(0f, 0f, Width, Height, BorderColor, 1f);

        int end = Math.Min(rowCount, _topRow + visibleRows);
        for (int index = _topRow; index < end; index++)
        {
            float y = (index - _topRow) * RowHeight;
            if (index == selected && SelectionBandEnabled)
                context.DrawFill(1f, y + 1f, contentWidth - 2f, RowHeight - 1f, SelectedColor);

            for (int c = 0; c < columns.Count; c++)
            {
                (float cellX, float cellW) = _cachedLayout[c];
                if (cellW <= 0f)
                    continue;

                context.PushClip(cellX, y, cellW, RowHeight);
                try
                {
                    switch (columns[c].Kind)
                    {
                        case UiMarkupListColumnKind.Text:
                            DrawTextCell(context, _cachedTextRows[c], _cachedColorRows[c], index, cellX, y);
                            break;
                        case UiMarkupListColumnKind.Check:
                            DrawCheckCell(context, _cachedCheckRows[c], index, cellX, cellW, y);
                            break;
                        case UiMarkupListColumnKind.Icon:
                            DrawIconCell(context, columns[c], _cachedIconRows[c], index, cellX, cellW, y);
                            break;
                        case UiMarkupListColumnKind.Button:
                            DrawButtonCell(context, c, index, cellX, cellW, y);
                            break;
                    }
                }
                finally
                {
                    context.PopClip();
                }
            }
        }

        if (showScrollbar)
            DrawScrollbar(context, contentWidth, rowCount, visibleRows);
    }

    /// <summary>Reads every column's rows from its binding; returns the row count, the longest column.</summary>
    private int RefreshRows(IReadOnlyList<UiMarkupListColumn> columns)
    {
        int rowCount = 0;
        for (int c = 0; c < columns.Count; c++)
        {
            var col = columns[c];
            switch (col.Kind)
            {
                case UiMarkupListColumnKind.Text:
                    _cachedTextRows[c] = col.TextSource!();
                    _cachedColorRows[c] = col.ColorsSource?.Invoke();
                    rowCount = Math.Max(rowCount, _cachedTextRows[c]!.Count);
                    break;
                case UiMarkupListColumnKind.Button:
                    _cachedTextRows[c] = col.TextSource!();
                    rowCount = Math.Max(rowCount, _cachedTextRows[c]!.Count);
                    break;
                case UiMarkupListColumnKind.Check:
                    _cachedCheckRows[c] = col.CheckSource!();
                    rowCount = Math.Max(rowCount, _cachedCheckRows[c]!.Count);
                    break;
                case UiMarkupListColumnKind.Icon:
                    _cachedIconRows[c] = col.IconValuesSource!();
                    rowCount = Math.Max(rowCount, _cachedIconRows[c]!.Count);
                    break;
            }
        }
        _cachedRowCount = rowCount;
        return rowCount;
    }

    private void DrawTextCell(
        UiRenderContext context, IReadOnlyList<string>? texts, IReadOnlyList<uint>? colors,
        int index, float cellX, float y)
    {
        if (texts is null || index >= texts.Count)
            return;
        string text = texts[index];
        Vector4 color = colors is { } cc && index < cc.Count ? Rgb(cc[index]) : TextColor;
        float textX = cellX + Padding;
        float textY = y + MathF.Max(0f, (RowHeight - (DatFont?.LineHeight ?? 14f)) * 0.5f);
        if (DatFont is { } font)
            context.DrawStringDat(font, text, textX, textY, color, ThemePalette is null);
        else
            context.DrawString(text, textX, textY, color);
    }

    private void DrawCheckCell(
        UiRenderContext context, IReadOnlyList<bool>? flags, int index, float cellX, float cellW, float y)
    {
        bool isChecked = flags is not null && index < flags.Count && flags[index];
        float extent = MathF.Max(0f, cellW - 2f);
        float lampX = cellX + 1f + MathF.Max(0f, extent - UiCheckLamp.LampSize) * 0.5f;
        float lampY = y + MathF.Max(1f, (RowHeight - UiCheckLamp.LampSize) * 0.5f);
        if (ThemePalette is { } p) p.DrawCheck(context, lampX, lampY, isChecked);
        else UiCheckLamp.Draw(context, lampX, lampY, isChecked);
    }

    private void DrawIconCell(
        UiRenderContext context, UiMarkupListColumn column, IReadOnlyList<uint>? ids,
        int index, float cellX, float cellW, float y)
    {
        if (ids is null || index >= ids.Count || column.IconResolve is not { } resolve)
            return;
        uint id = ids[index];
        if (id == 0u)
            return;
        (uint tex, int w, int h) = resolve(id);
        if (tex == 0u || w <= 0 || h <= 0)
            return;
        float extentW = MathF.Max(0f, cellW - 2f);
        float extentH = MathF.Max(0f, RowHeight - 2f);
        float scale = MathF.Min(extentW / w, extentH / h);
        float drawWidth = w * scale;
        float drawHeight = h * scale;
        context.DrawSprite(
            tex,
            cellX + 1f + (extentW - drawWidth) * 0.5f,
            y + (RowHeight - drawHeight) * 0.5f,
            drawWidth, drawHeight,
            0f, 0f, 1f, 1f, Vector4.One);
    }

    /// <summary>
    /// A button cell inset by a pixel all round, so neighbouring buttons and
    /// rows do not touch, with its caption centred in the list's text colour
    /// and cut at the button's edges. A disabled list draws face, border and
    /// caption at the alpha a disabled toggle's caption takes.
    /// </summary>
    private void DrawButtonCell(UiRenderContext context, int column, int index, float cellX, float cellW, float y)
    {
        if (!HasButtonCaption(column, index, out string caption))
            return;
        (float x, float top, float w, float h) = ButtonRect(_columns![column], cellX, cellW, y);
        UiControlState state = ButtonState(index, column);
        Vector4 face = state switch
        {
            UiControlState.Hovered => Vector4.Lerp(ButtonColor, ButtonBorderColor, 0.3f) with { W = ButtonColor.W },
            UiControlState.Pressed => Vector4.Lerp(ButtonColor, Vector4.Zero, 0.25f) with { W = ButtonColor.W },
            _ => ButtonColor,
        };
        Vector4 border = ButtonBorderColor;
        Vector4 textColor = TextColor;
        if (state == UiControlState.Disabled)
        {
            face = Dimmed(face);
            border = Dimmed(border);
            textColor = Dimmed(textColor);
        }
        context.DrawFill(x, top, w, h, face);
        context.DrawRectOutline(x, top, w, h, border, 1f);

        context.PushClip(x, top, w, h);
        try
        {
            if (DatFont is { } font)
            {
                context.DrawStringDat(font, caption,
                    x + (w - font.MeasureWidth(caption)) * 0.5f,
                    top + (h - font.LineHeight) * 0.5f,
                    textColor, ThemePalette is null);
            }
            else if (context.DefaultFont is { } fallback)
            {
                context.DrawString(caption,
                    x + (w - fallback.MeasureWidth(caption)) * 0.5f,
                    top + (h - fallback.LineHeight) * 0.5f,
                    textColor);
            }
        }
        finally
        {
            context.PopClip();
        }
    }

    private static Vector4 Dimmed(Vector4 color) => color with { W = color.W * UiMarkupToggle.DisabledAlpha };

    /// <summary>
    /// Where a button sits in its cell: its column's size, centred, never past
    /// the cell's one-pixel inset; a size of 0 fills the inset cell.
    /// </summary>
    private (float x, float y, float w, float h) ButtonRect(
        UiMarkupListColumn column, float cellX, float cellW, float rowY)
    {
        float maxW = MathF.Max(0f, cellW - 2f), maxH = MathF.Max(0f, RowHeight - 2f);
        float w = column.ButtonWidth > 0f ? MathF.Min(column.ButtonWidth, maxW) : maxW;
        float h = column.ButtonHeight > 0f ? MathF.Min(column.ButtonHeight, maxH) : maxH;
        return (MathF.Floor(cellX + (cellW - w) * 0.5f), MathF.Floor(rowY + (RowHeight - h) * 0.5f), w, h);
    }

    private bool HasButtonCaption(int column, int index, out string caption)
    {
        caption = string.Empty;
        if (_columns is not { } columns || column < 0 || column >= columns.Count
            || columns[column].Kind != UiMarkupListColumnKind.Button)
            return false;
        if (_cachedTextRows[column] is not { } captions || index < 0 || index >= captions.Count)
            return false;
        caption = captions[index] ?? string.Empty;
        return caption.Length > 0;
    }

    /// <summary>
    /// The look of one button cell: pressed only while the pointer is still
    /// over the button it went down on, and no other button hovers while one
    /// is held.
    /// </summary>
    private UiControlState ButtonState(int index, int column)
    {
        if (!Enabled)
            return UiControlState.Disabled;
        bool over = _hoverRow == index && _hoverColumn == column;
        if (_pressRow >= 0)
            return _pressRow == index && _pressColumn == column && over
                ? UiControlState.Pressed
                : UiControlState.Normal;
        return over ? UiControlState.Hovered : UiControlState.Normal;
    }

    internal UiControlState ButtonStateForTest(int index, int column) =>
        HasButtonCaption(column, index, out _) ? ButtonState(index, column) : UiControlState.Normal;

    /// <summary>The button cell at a point inside the list, or (-1, -1) where there is none.</summary>
    private (int index, int column) ButtonCellAt(float localX, float localY, float contentWidth, int visibleRows)
    {
        if (_columns is not { } columns || localX < 0f || localX >= contentWidth)
            return (-1, -1);
        int row = (int)MathF.Floor(localY / MathF.Max(1f, RowHeight));
        int index = _topRow + row;
        if (row < 0 || row >= visibleRows)
            return (-1, -1);
        for (int c = 0; c < columns.Count; c++)
        {
            (float cellX, float cellW) = _cachedLayout[c];
            if (localX < cellX || localX >= cellX + cellW)
                continue;
            if (!HasButtonCaption(c, index, out _))
                return (-1, -1);
            (float bx, float by, float bw, float bh) = ButtonRect(columns[c], cellX, cellW, row * RowHeight);
            bool onButton = localX >= bx && localX < bx + bw && localY >= by && localY < by + bh;
            return onButton ? (index, c) : (-1, -1);
        }
        return (-1, -1);
    }

    private void ClearButtonPointer()
    {
        _hoverRow = _hoverColumn = -1;
        _pressRow = _pressColumn = -1;
        _pointerKnown = false;
    }

    protected override void OnEnabledChanged()
    {
        if (!Enabled)
            _pressRow = _pressColumn = -1;
    }

    private void TrackPointer(in UiEvent e)
    {
        _pointerKnown = true;
        (_pointerX, _pointerY) = (e.Data1, e.Data2);
    }

    /// <summary>Finds the hovered button again from the last pointer position, after the rows moved.</summary>
    private void RefreshHover(float contentWidth, int visibleRows) =>
        (_hoverRow, _hoverColumn) = _pointerKnown && !_draggingThumb
            ? ButtonCellAt(_pointerX, _pointerY, contentWidth, visibleRows)
            : (-1, -1);

    private void RememberPressedRow()
    {
        _pressTopRow = _topRow;
        _pressRowCount = _cachedRowCount;
        HasButtonCaption(_pressColumn, _pressRow, out _pressCaption);
    }

    /// <summary>
    /// Drops the press when the row under it may no longer be the item that was
    /// pressed: the list scrolled, its row count changed, or the pressed
    /// button's caption changed or went empty. Other columns are left out on
    /// purpose: a live column such as a distance changes every frame and would
    /// make the buttons beside it unreliable.
    /// </summary>
    private void CancelPressIfRowsMoved()
    {
        if (_pressRow < 0)
            return;
        bool moved = _topRow != _pressTopRow
            || _cachedRowCount != _pressRowCount
            || !HasButtonCaption(_pressColumn, _pressRow, out string caption)
            || !string.Equals(caption, _pressCaption, StringComparison.Ordinal);
        if (moved)
            _pressRow = _pressColumn = -1;
    }

    /// <summary>
    /// Follows the pointer over button cells and fires a button when the left
    /// button comes up over the one it went down on. Only a release that fires
    /// a button is handled here; everything else carries on to the list.
    /// </summary>
    private bool TrackButtonPointer(
        in UiEvent e, IReadOnlyList<UiMarkupListColumn> columns, float contentWidth, int visibleRows)
    {
        switch (e.Type)
        {
            case UiEventType.HoverEnter:
            case UiEventType.MouseMove:
            case UiEventType.MouseDown:
                TrackPointer(e);
                RefreshHover(contentWidth, visibleRows);
                return false;
            case UiEventType.HoverLeave:
                _pointerKnown = false;
                _hoverRow = _hoverColumn = -1;
                return false;
            case UiEventType.CaptureChanged:
                _pressRow = _pressColumn = -1;
                return false;
            case UiEventType.MouseUp:
            {
                TrackPointer(e);
                if (_pressRow >= 0)
                {
                    // Read the rows as they are now, not as last drawn.
                    RefreshRows(columns);
                    CancelPressIfRowsMoved();
                }
                (int index, int column) = (_pressRow, _pressColumn);
                _pressRow = _pressColumn = -1;
                (_hoverRow, _hoverColumn) = ButtonCellAt(e.Data1, e.Data2, contentWidth, visibleRows);
                if (index < 0 || !Enabled || (_hoverRow, _hoverColumn) != (index, column))
                    return false;
                columns[column].ButtonClicked?.Invoke(index);
                return true;
            }
            default:
                return false;
        }
    }

    private bool OnEventColumns(in UiEvent e, IReadOnlyList<UiMarkupListColumn> columns)
    {
        int rowCount = _cachedRowCount;
        int visibleRows = VisibleRows;
        float contentWidth = rowCount > visibleRows
            ? MathF.Max(0f, Width - ScrollbarWidth)
            : Width;
        if (_hasButtons && TrackButtonPointer(e, columns, contentWidth, visibleRows))
            return true;
        if (TryHandleScrollbarEvent(e, contentWidth, rowCount, visibleRows))
            return true;

        if (e.Type == UiEventType.Scroll)
        {
            _topRow -= Math.Sign(e.Data0);
            ClampTop(rowCount, visibleRows);
            if (_hasButtons)
            {
                // The rows moved under the pointer: a press no longer names
                // the row it went down on, and the hovered button may differ.
                CancelPressIfRowsMoved();
                TrackPointer(e);
                RefreshHover(contentWidth, visibleRows);
            }
            return true;
        }
        if (e.Type != UiEventType.MouseDown || !Enabled)
            return false;

        int row = (int)MathF.Floor(e.Data2 / MathF.Max(1f, RowHeight));
        int index = _topRow + row;
        if (row < 0 || row >= visibleRows || index < 0 || index >= rowCount)
            return true;

        float localX = e.Data1;
        for (int c = 0; c < columns.Count; c++)
        {
            (float cellX, float cellW) = _cachedLayout[c];
            if (localX < cellX || localX >= cellX + cellW)
                continue;
            switch (columns[c].Kind)
            {
                case UiMarkupListColumnKind.Text:
                    if (columns[c].TextClicked is { } onTextClick)
                    {
                        if (index < (_cachedTextRows[c]?.Count ?? 0))
                            onTextClick(index);
                    }
                    else
                    {
                        SelectionChanged?.Invoke(index);
                    }
                    break;
                case UiMarkupListColumnKind.Check:
                    if (index < (_cachedCheckRows[c]?.Count ?? 0))
                        columns[c].CheckChanged?.Invoke(index);
                    break;
                case UiMarkupListColumnKind.Icon:
                    if (index < (_cachedIconRows[c]?.Count ?? 0))
                        columns[c].IconClicked?.Invoke(index);
                    break;
                case UiMarkupListColumnKind.Button:
                    // Fires on release over the same button; see TrackButtonPointer.
                    if ((_hoverRow, _hoverColumn) == (index, c))
                    {
                        (_pressRow, _pressColumn) = (index, c);
                        RememberPressedRow();
                    }
                    break;
            }
            break;
        }
        return true;
    }


    private void ConfigureScroll(int rowCount, int visibleRows)
    {
        int lineHeight = Math.Max(1, (int)MathF.Round(RowHeight));
        _scroll.LineHeight = lineHeight;
        _scroll.SetExtents(rowCount * lineHeight, visibleRows * lineHeight);
        _scroll.SetScrollY(_topRow * lineHeight);
    }

    private void DrawScrollbar(UiRenderContext ctx, float x, int rowCount, int visibleRows)
    {
        ConfigureScroll(rowCount, visibleRows);
        if (ThemePalette is { } p)
        {
            ctx.DrawFill(x, 0, ScrollbarWidth, Height, p.Field);
            ctx.DrawRectOutline(x, 0, ScrollbarWidth, Height, p.Border, 1);
            float button = Math.Min(ScrollButtonExtent, Height / 2);
            var (top, size) = UiScrollbar.ThumbRect(_scroll, button, Math.Max(0, Height - 2 * button));
            ctx.DrawFill(x + 3, top, ScrollbarWidth - 6, size, p.Muted);
            ctx.DrawFill(x + 5, button / 2, ScrollbarWidth - 10, 1, p.Muted);
            ctx.DrawFill(x + 5, Height - button / 2, ScrollbarWidth - 10, 1, p.Muted);
            return;
        }
        if (SpriteResolve is not { } resolve) return;

        float decExtent = Math.Clamp(ScrollButtonExtent, 0f, Height);
        float incExtent = Math.Clamp(ScrollButtonExtent, 0f, Height - decExtent);

        DrawTiledSprite(ctx, resolve, RetailScrollbarChrome.Track, x, 0f, ScrollbarWidth, Height);
        DrawFlatSprite(ctx, resolve, RetailScrollbarChrome.UpNormal, x, 0f, ScrollbarWidth, decExtent);
        DrawFlatSprite(ctx, resolve, RetailScrollbarChrome.DownNormal, x, Height - incExtent, ScrollbarWidth, incExtent);

        float trackTop = decExtent;
        float trackLen = MathF.Max(0f, Height - decExtent - incExtent);
        var (ty, th) = UiScrollbar.ThumbRect(_scroll, trackTop, trackLen);
        const float capH = 3f;
        if (th >= 2f * capH)
        {
            DrawFlatSprite(ctx, resolve, RetailScrollbarChrome.ThumbTopNormal, x, ty, ScrollbarWidth, capH);
            DrawTiledSprite(ctx, resolve, RetailScrollbarChrome.ThumbMidNormal, x, ty + capH, ScrollbarWidth, th - 2f * capH);
            DrawFlatSprite(ctx, resolve, RetailScrollbarChrome.ThumbBotNormal, x, ty + th - capH, ScrollbarWidth, th <= 0f ? 0f : capH);
        }
        else
        {
            DrawFlatSprite(ctx, resolve, RetailScrollbarChrome.ThumbMidNormal, x, ty, ScrollbarWidth, th);
        }
    }

    private static void DrawFlatSprite(
        UiRenderContext ctx, Func<uint, (uint tex, int w, int h)> resolve,
        uint id, float x, float y, float w, float h)
    {
        if (id == 0 || w <= 0f || h <= 0f) return;
        var (tex, _, _) = resolve(id);
        if (tex == 0) return;
        ctx.DrawSprite(tex, x, y, w, h, 0f, 0f, 1f, 1f, Vector4.One);
    }

    private static void DrawTiledSprite(
        UiRenderContext ctx, Func<uint, (uint tex, int w, int h)> resolve,
        uint id, float x, float y, float w, float h)
    {
        if (id == 0 || w <= 0f || h <= 0f) return;
        var (tex, tw, th) = resolve(id);
        if (tex == 0 || tw == 0 || th == 0) return;
        ctx.DrawSprite(tex, x, y, w, h, 0f, 0f, w / tw, h / th, Vector4.One);
    }

    private bool TryHandleScrollbarEvent(in UiEvent e, float contentWidth, int rowCount, int visibleRows)
    {
        if (_draggingThumb)
        {
            if (e.Type == UiEventType.MouseMove)
            {
                DragThumb(e.Data2, rowCount, visibleRows);
                return true;
            }
            if (e.Type is UiEventType.MouseUp or UiEventType.CaptureChanged)
            {
                _draggingThumb = false;
                return true;
            }
        }

        if (rowCount <= visibleRows) return false;
        if (e.Type != UiEventType.MouseDown || !Enabled) return false;
        if (e.Data1 < contentWidth) return false;

        ConfigureScroll(rowCount, visibleRows);
        float decExtent = Math.Clamp(ScrollButtonExtent, 0f, Height);
        float incExtent = Math.Clamp(ScrollButtonExtent, 0f, Height - decExtent);
        float ly = e.Data2;

        if (ly < decExtent) { StepRow(-1, rowCount, visibleRows); return true; }
        if (ly >= Height - incExtent) { StepRow(1, rowCount, visibleRows); return true; }

        float trackTop = decExtent;
        float trackLen = MathF.Max(0f, Height - decExtent - incExtent);
        var (ty, th) = UiScrollbar.ThumbRect(_scroll, trackTop, trackLen);
        if (ly >= ty && ly <= ty + th)
        {
            _draggingThumb = true;
            _thumbDragOffset = ly - ty;
        }
        else
        {
            PageRow(ly < ty ? -1 : 1, rowCount, visibleRows);
        }
        return true;
    }

    private void DragThumb(float ly, int rowCount, int visibleRows)
    {
        ConfigureScroll(rowCount, visibleRows);
        float decExtent = Math.Clamp(ScrollButtonExtent, 0f, Height);
        float incExtent = Math.Clamp(ScrollButtonExtent, 0f, Height - decExtent);
        float trackTop = decExtent;
        float trackLen = MathF.Max(0f, Height - decExtent - incExtent);
        var (_, thumbH) = UiScrollbar.ThumbRect(_scroll, trackTop, trackLen);
        float travel = MathF.Max(1f, trackLen - thumbH);
        float ratio = (ly - _thumbDragOffset - trackTop) / travel;
        _scroll.SetPositionRatio(ratio);

        int lineHeight = Math.Max(1, (int)MathF.Round(RowHeight));
        _topRow = (int)MathF.Round((float)_scroll.ScrollY / lineHeight);
        ClampTop(rowCount, visibleRows);
    }

    private void StepRow(int lines, int rowCount, int visibleRows)
    {
        _topRow += lines;
        ClampTop(rowCount, visibleRows);
    }

    private void PageRow(int pages, int rowCount, int visibleRows)
    {
        _topRow += pages * visibleRows;
        ClampTop(rowCount, visibleRows);
    }
}
