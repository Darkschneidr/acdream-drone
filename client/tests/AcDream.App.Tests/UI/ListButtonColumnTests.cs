using System.Numerics;
using AcDream.App.Rendering;
using AcDream.App.Rendering.Gpu;
using AcDream.App.Tests.Rendering.Gpu;
using AcDream.App.UI;
using Xunit;

namespace AcDream.App.Tests.UI;

public sealed class ListButtonColumnTests
{
    private static (uint, int, int) Sprite(uint id) => (1u, 32, 32);

    private sealed class FoundBinding
    {
        public IReadOnlyList<string> Names { get; } = new[] { "Drudge", "Rat", "Mosswart" };
        public IReadOnlyList<string> Captions { get; } = new[] { "Select", "", "Select" };
        public int Selected { get; set; } = -1;
        public List<int> Acted { get; } = new();
        public Action<int> Act => row => Acted.Add(row);
    }

    private static string FoundXml(string buttonColumn) =>
        "<panel x=\"0\" y=\"0\" w=\"200\" h=\"100\">" +
        "<list x=\"0\" y=\"0\" w=\"180\" h=\"60\" rowheight=\"18\" selected=\"{Selected}\">" +
        "  <column type=\"text\" width=\"100\" items=\"{Names}\"/>" +
        buttonColumn +
        "</list></panel>";

    [Fact]
    public void ButtonColumn_BindsItsCaptionsAndOnclick()
    {
        var binding = new FoundBinding();
        var panel = MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" items=\"{Captions}\" onclick=\"{Act}\"/>"),
            binding, Sprite);
        var list = Assert.IsType<UiMarkupList>(panel.Children[0]);

        var column = list.Columns![1];
        Assert.Equal(UiMarkupListColumnKind.Button, column.Kind);
        Assert.Equal(binding.Captions, column.TextSource!());
        Assert.Equal(3, column.RowCount());
        column.ButtonClicked!(2);
        Assert.Equal(new[] { 2 }, binding.Acted);
    }

    [Fact]
    public void ButtonColumn_MissingOnclick_ThrowsAtBuild()
    {
        var ex = Assert.Throws<FormatException>(() => MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" items=\"{Captions}\"/>"),
            new FoundBinding(), Sprite));
        Assert.Contains("column[1] type=\"button\" onclick", ex.Message);
    }

    [Fact]
    public void ButtonColumn_MissingItems_ThrowsAtBuild()
    {
        var ex = Assert.Throws<FormatException>(() => MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" onclick=\"{Act}\"/>"),
            new FoundBinding(), Sprite));
        Assert.Contains("column[1] type=\"button\" items", ex.Message);
    }

    [Fact]
    public void UnknownColumnType_NamesButtonAmongTheExpectedTypes()
    {
        var ex = Assert.Throws<FormatException>(() => MarkupDocument.Build(
            FoundXml("<column type=\"bogus\" width=\"60\" items=\"{Captions}\"/>"),
            new FoundBinding(), Sprite));
        Assert.Contains("button", ex.Message);
    }

    private static UiMarkupList MakeList(
        List<int> acted, List<int> selections, string[]? captions = null)
    {
        captions ??= new[] { "Go", "", "Go" };
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            SelectionChanged = row => selections.Add(row),
            BackgroundColor = default, BorderColor = default,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "a", "b", "c" }, null),
                UiMarkupListColumn.Button(50f, () => captions, row => acted.Add(row)),
            },
        };
        var (_, ctx) = MakeContext(200f, 200f);
        list.DrawSelfAndChildren(ctx);
        return list;
    }

    [Fact]
    public void PressAndReleaseOnAButton_FiresOnclickWithTheRow_WithoutSelecting()
    {
        var acted = new List<int>();
        var selections = new List<int>();
        var list = MakeList(acted, selections);

        // Row 2 (y = 40..60), button column (x = 50..100).
        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 45 });
        Assert.Empty(acted);
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 72, Data2 = 47 });

        Assert.Equal(new[] { 2 }, acted);
        Assert.Empty(selections);
    }

    [Fact]
    public void ReleaseOffThePressedButton_DoesNotFire()
    {
        var acted = new List<int>();
        var list = MakeList(acted, new List<int>());

        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 5 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 70, Data2 = 45 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 5 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 10, Data2 = 5 });

        Assert.Empty(acted);
    }

    [Fact]
    public void EmptyCaption_IsNoButton_AndAClickOnItDoesNothing()
    {
        var acted = new List<int>();
        var selections = new List<int>();
        var list = MakeList(acted, selections);

        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 25 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 70, Data2 = 25 });

        Assert.Empty(acted);
        Assert.Empty(selections);
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(1, 1));
    }

    [Fact]
    public void ButtonCell_ShowsHoverAndPressOnlyOnTheCellUnderThePointer()
    {
        var list = MakeList(new List<int>(), new List<int>());
        Assert.True(list.ReceivesHoverMouseMove);

        list.OnEvent(new UiEvent { Type = UiEventType.HoverEnter, Data1 = 70, Data2 = 5 });
        Assert.Equal(UiControlState.Hovered, list.ButtonStateForTest(0, 1));
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(2, 1));

        list.OnEvent(new UiEvent { Type = UiEventType.MouseMove, Data1 = 70, Data2 = 45 });
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(0, 1));
        Assert.Equal(UiControlState.Hovered, list.ButtonStateForTest(2, 1));

        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 70, Data2 = 45 });
        Assert.Equal(UiControlState.Pressed, list.ButtonStateForTest(2, 1));

        // Dragged off the pressed button: it lets go of its pressed look.
        list.OnEvent(new UiEvent { Type = UiEventType.MouseMove, Data1 = 70, Data2 = 5 });
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(2, 1));
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(0, 1));

        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 70, Data2 = 5 });
        Assert.Equal(UiControlState.Hovered, list.ButtonStateForTest(0, 1));

        list.OnEvent(new UiEvent { Type = UiEventType.HoverLeave });
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(0, 1));
    }

    [Fact]
    public void ListWithoutButtonColumns_TakesNoHoverMoves()
    {
        var list = new UiMarkupList
        {
            Columns = new[] { UiMarkupListColumn.Text(50f, () => new[] { "a" }, null) },
        };
        Assert.False(list.ReceivesHoverMouseMove);
    }

    [Fact]
    public void DisabledList_ShowsItsButtonsDisabled()
    {
        var list = MakeList(new List<int>(), new List<int>());
        list.Enabled = false;
        Assert.Equal(UiControlState.Disabled, list.ButtonStateForTest(0, 1));
    }

    [Fact]
    public void ClassicButtonCell_DrawsAFaceInsideItsCell_OnlyWhereThereIsACaption()
    {
        var face = new Vector4(0.2f, 0.4f, 0.6f, 1f);
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            BackgroundColor = default, BorderColor = default,
            ButtonColor = face,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "a", "b", "c" }, null),
                UiMarkupListColumn.Button(50f, () => new[] { "Go", "", "Go" }, _ => { }),
            },
        };
        var (renderer, ctx) = MakeContext(200f, 200f);

        list.DrawSelfAndChildren(ctx);

        var faceVerts = renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == 0u)
            .SelectMany(s => Chunk(s.Verts))
            .Where(v => ColorMatches(v, face))
            .ToList();
        Assert.NotEmpty(faceVerts);
        Assert.All(faceVerts, v => Assert.InRange(v[0], 50f, 100f));
        // Rows 0 and 2 only: nothing in row 1 (y = 20..40).
        Assert.Contains(faceVerts, v => v[1] < 20f);
        Assert.Contains(faceVerts, v => v[1] > 40f);
        Assert.DoesNotContain(faceVerts, v => v[1] > 21f && v[1] < 39f);
    }

    [Fact]
    public void ButtonColumn_BindsItsButtonSize()
    {
        var panel = MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" items=\"{Captions}\" onclick=\"{Act}\" buttonw=\"40\" buttonh=\"12\"/>"),
            new FoundBinding(), Sprite);
        var column = Assert.IsType<UiMarkupList>(panel.Children[0]).Columns![1];
        Assert.Equal(40f, column.ButtonWidth);
        Assert.Equal(12f, column.ButtonHeight);
    }

    [Fact]
    public void ButtonColumn_NegativeButtonSize_ThrowsAtBuild()
    {
        var ex = Assert.Throws<FormatException>(() => MarkupDocument.Build(
            FoundXml("<column type=\"button\" width=\"60\" items=\"{Captions}\" onclick=\"{Act}\" buttonh=\"-1\"/>"),
            new FoundBinding(), Sprite));
        Assert.Contains("column[1] type=\"button\" buttonh", ex.Message);
    }

    [Fact]
    public void SizedButton_IsCentredInItsCell_AndOnlyTheButtonTakesClicks()
    {
        var face = new Vector4(0.2f, 0.4f, 0.6f, 1f);
        var acted = new List<int>();
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            BackgroundColor = default, BorderColor = default,
            ButtonColor = face,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "a" }, null),
                // Cell x = 50..100, y = 0..20; a 30x10 button sits at x = 60..90, y = 5..15.
                UiMarkupListColumn.Button(50f, () => new[] { "Go" }, row => acted.Add(row),
                    buttonWidth: 30f, buttonHeight: 10f),
            },
        };
        var (renderer, ctx) = MakeContext(200f, 200f);
        list.DrawSelfAndChildren(ctx);

        var faceVerts = renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == 0u)
            .SelectMany(s => Chunk(s.Verts))
            .Where(v => ColorMatches(v, face))
            .ToList();
        Assert.NotEmpty(faceVerts);
        Assert.All(faceVerts, v => Assert.InRange(v[0], 60f, 90f));
        Assert.All(faceVerts, v => Assert.InRange(v[1], 5f, 15f));

        // Inside the cell but beside the button: nothing.
        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 55, Data2 = 10 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 55, Data2 = 10 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseMove, Data1 = 75, Data2 = 2 });
        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(0, 1));
        Assert.Empty(acted);

        list.OnEvent(new UiEvent { Type = UiEventType.MouseDown, Data1 = 75, Data2 = 10 });
        list.OnEvent(new UiEvent { Type = UiEventType.MouseUp, Data1 = 75, Data2 = 10 });
        Assert.Equal(new[] { 0 }, acted);
    }

    [Fact]
    public void SizedButton_NeverGrowsPastItsCell()
    {
        var face = new Vector4(0.2f, 0.4f, 0.6f, 1f);
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            BackgroundColor = default, BorderColor = default,
            ButtonColor = face,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "a" }, null),
                UiMarkupListColumn.Button(50f, () => new[] { "Go" }, _ => { },
                    buttonWidth: 500f, buttonHeight: 500f),
            },
        };
        var (renderer, ctx) = MakeContext(200f, 200f);
        list.DrawSelfAndChildren(ctx);

        var faceVerts = renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == 0u)
            .SelectMany(s => Chunk(s.Verts))
            .Where(v => ColorMatches(v, face))
            .ToList();
        Assert.All(faceVerts, v => Assert.InRange(v[0], 51f, 99f));
        Assert.All(faceVerts, v => Assert.InRange(v[1], 1f, 19f));
    }

    // ── A press names the row it went down on ───────────

    private sealed class LiveRows
    {
        public string[] Names = { "a", "b", "c", "d", "e" };
        public string[] Captions = { "Go", "Go", "Go", "Go", "Go" };
        public List<int> Acted { get; } = new();
    }

    /// <summary>A 60-high list of 20-high rows (three visible), names in x = 0..50, buttons in 50..84.</summary>
    private static UiMarkupList MakeLiveList(LiveRows rows)
    {
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            BackgroundColor = default, BorderColor = default,
            Columns = new[]
            {
                // Fresh arrays every read, the way a markup binding hands them over.
                UiMarkupListColumn.Text(50f, () => rows.Names.ToArray(), null),
                UiMarkupListColumn.Button(50f, () => rows.Captions.ToArray(), row => rows.Acted.Add(row)),
            },
        };
        Draw(list);
        return list;
    }

    private static void Draw(UiMarkupList list)
    {
        var (_, ctx) = MakeContext(200f, 200f);
        list.DrawSelfAndChildren(ctx);
    }

    private static UiEvent At(int type, int x, int y) => new() { Type = type, Data1 = x, Data2 = y };

    [Fact]
    public void RowsRedrawnWithTheSameContent_StillFire()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);

        list.OnEvent(At(UiEventType.MouseDown, 60, 25));
        Draw(list);
        list.OnEvent(At(UiEventType.MouseUp, 60, 25));

        Assert.Equal(new[] { 1 }, rows.Acted);
    }

    [Fact]
    public void RowRemovedAboveThePress_BeforeRelease_DoesNotFireForTheRowThatMovedIn()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);

        list.OnEvent(At(UiEventType.MouseDown, 60, 25));       // row 1, "b"
        rows.Names = new[] { "b", "c", "d", "e" };             // "a" goes; "c" is now row 1
        rows.Captions = new[] { "Go", "Go", "Go", "Go" };
        list.OnEvent(At(UiEventType.MouseUp, 60, 25));

        Assert.Empty(rows.Acted);
    }

    [Fact]
    public void PressedButtonsCaptionChangedBeforeRelease_DoesNotFire_EvenWithNoDrawBetween()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);

        list.OnEvent(At(UiEventType.MouseDown, 60, 25));       // row 1
        rows.Captions = new[] { "Go", "Stop", "Go", "Go", "Go" };
        list.OnEvent(At(UiEventType.MouseUp, 60, 25));

        Assert.Empty(rows.Acted);
    }

    [Fact]
    public void PressedButtonsCaptionEmptiedBeforeRelease_DoesNotFire()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);

        list.OnEvent(At(UiEventType.MouseDown, 60, 25));       // row 1
        rows.Captions = new[] { "Go", "", "Go", "Go", "Go" };
        Draw(list);
        rows.Captions = new[] { "Go", "Go", "Go", "Go", "Go" };
        list.OnEvent(At(UiEventType.MouseUp, 60, 25));

        Assert.Empty(rows.Acted);
    }

    [Fact]
    public void OtherColumnChangedBetweenPressAndRelease_StillFires()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);

        list.OnEvent(At(UiEventType.MouseDown, 60, 25));       // row 1
        rows.Names = new[] { "a", "b 12.3m", "c", "d", "e" };  // a live column ticks
        Draw(list);
        rows.Names = new[] { "a", "b 11.9m", "c", "d", "e" };
        list.OnEvent(At(UiEventType.MouseUp, 60, 25));

        Assert.Equal(new[] { 1 }, rows.Acted);
    }

    [Fact]
    public void ButtonCaptionChangedWhilePressed_DropsThePressedLookAtTheNextDraw()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);

        list.OnEvent(At(UiEventType.MouseDown, 60, 25));
        Assert.Equal(UiControlState.Pressed, list.ButtonStateForTest(1, 1));
        rows.Captions = new[] { "Go", "Stop", "Go", "Go", "Go" };
        Draw(list);

        Assert.Equal(UiControlState.Hovered, list.ButtonStateForTest(1, 1));
    }

    [Fact]
    public void ListScrolledWhilePressed_DoesNotFire()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);

        list.OnEvent(At(UiEventType.MouseDown, 60, 25));       // row 1
        list.OnEvent(new UiEvent { Type = UiEventType.Scroll, Data0 = -1, Data1 = 60, Data2 = 25 });
        // Row 1 is now drawn at y = 0..20; release on it there.
        list.OnEvent(At(UiEventType.MouseUp, 60, 5));

        Assert.Empty(rows.Acted);
    }

    [Fact]
    public void WheelScroll_WithNoPointerMove_MovesTheHoverToTheButtonNowUnderThePointer()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(list);

        root.OnMouseMove(60, 5);
        Assert.Equal(UiControlState.Hovered, list.ButtonStateForTest(0, 1));

        root.OnScroll(-1);

        Assert.Equal(UiControlState.Normal, list.ButtonStateForTest(0, 1));
        Assert.Equal(UiControlState.Hovered, list.ButtonStateForTest(1, 1));
    }

    [Fact]
    public void ThroughUiRoot_PressAndRelease_FiresOnce()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(list);

        root.OnMouseMove(60, 45);
        root.OnMouseDown(UiMouseButton.Left, 60, 45);
        root.OnMouseUp(UiMouseButton.Left, 60, 45);

        Assert.Equal(new[] { 2 }, rows.Acted);
    }

    [Fact]
    public void ThroughUiRoot_CaptureTakenAway_ClearsThePress()
    {
        var rows = new LiveRows();
        var list = MakeLiveList(rows);
        var other = new UiPanel { Left = 300, Top = 300, Width = 20, Height = 20 };
        var root = new UiRoot { Width = 800, Height = 600 };
        root.AddChild(list);
        root.AddChild(other);

        root.OnMouseMove(60, 45);
        root.OnMouseDown(UiMouseButton.Left, 60, 45);
        Assert.Same(list, root.Captured);
        Assert.Equal(UiControlState.Pressed, list.ButtonStateForTest(2, 1));

        root.SetCapture(other);

        Assert.NotEqual(UiControlState.Pressed, list.ButtonStateForTest(2, 1));
        list.OnEvent(At(UiEventType.MouseUp, 60, 45));
        Assert.Empty(rows.Acted);
    }

    [Fact]
    public void DisabledList_DoesNotFire()
    {
        var acted = new List<int>();
        var list = MakeList(acted, new List<int>());
        list.Enabled = false;

        list.OnEvent(At(UiEventType.MouseDown, 70, 5));
        list.OnEvent(At(UiEventType.MouseUp, 70, 5));

        Assert.Empty(acted);
    }

    [Fact]
    public void ListDisabledWhilePressed_DoesNotFire_EvenIfEnabledAgainBeforeRelease()
    {
        var acted = new List<int>();
        var list = MakeList(acted, new List<int>());

        list.OnEvent(At(UiEventType.MouseDown, 70, 5));
        list.Enabled = false;
        list.Enabled = true;
        list.OnEvent(At(UiEventType.MouseUp, 70, 5));

        Assert.Empty(acted);
    }

    // ── Drawing ───────────

    [Fact]
    public void DisabledList_DrawsItsButtonFaceAndBorderFaded()
    {
        var face = new Vector4(0.2f, 0.4f, 0.6f, 1f);
        var border = new Vector4(0.7f, 0.1f, 0.3f, 1f);
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f,
            SelectedIndexSource = () => -1,
            BackgroundColor = default, BorderColor = default,
            ButtonColor = face, ButtonBorderColor = border,
            Enabled = false,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "a" }, null),
                UiMarkupListColumn.Button(50f, () => new[] { "Go" }, _ => { }),
            },
        };
        var (renderer, ctx) = MakeContext(200f, 200f);
        list.DrawSelfAndChildren(ctx);

        var verts = renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == 0u)
            .SelectMany(s => Chunk(s.Verts))
            .ToList();
        float faded = UiMarkupToggle.DisabledAlpha;
        Assert.Contains(verts, v => ColorMatches(v, face with { W = faded }));
        Assert.Contains(verts, v => ColorMatches(v, border with { W = faded }));
        Assert.DoesNotContain(verts, v => ColorMatches(v, face));
        Assert.DoesNotContain(verts, v => ColorMatches(v, border));
    }

    [Fact]
    public void CaptionWiderThanItsButton_IsCutAtTheButtonEdges()
    {
        var glyphs = new Dictionary<char, DatReaderWriter.Types.FontCharDesc>
        {
            ['W'] = new DatReaderWriter.Types.FontCharDesc { Unicode = 'W', Width = 8, Height = 8 },
        };
        var font = new UiDatFont(
            fgTex: 1u, fgW: 32, fgH: 32,
            bgTex: 0, bgW: 0, bgH: 0,
            lineHeight: 16f, baselineOffset: 12f,
            glyphs);
        var list = new UiMarkupList
        {
            Width = 100f, Height = 60f, RowHeight = 20f, DatFont = font,
            SelectedIndexSource = () => -1,
            BackgroundColor = default, BorderColor = default,
            Columns = new[]
            {
                UiMarkupListColumn.Text(50f, () => new[] { "" }, null),
                // Cell x = 50..100; a 20-wide button sits at x = 65..85. The caption is 80 wide.
                UiMarkupListColumn.Button(50f, () => new[] { "WWWWWWWWWW" }, _ => { }, buttonWidth: 20f),
            },
        };
        var (renderer, ctx) = MakeContext(200f, 200f);
        list.DrawSelfAndChildren(ctx);

        var glyphVerts = renderer.DebugSpriteSegmentVerts
            .Where(s => s.Texture == 1u)
            .SelectMany(s => Chunk(s.Verts))
            .ToList();
        Assert.NotEmpty(glyphVerts);
        Assert.All(glyphVerts, v => Assert.InRange(v[0], 65f - 0.01f, 85f + 0.01f));
    }

    private static IEnumerable<float[]> Chunk(IReadOnlyList<float> verts)
    {
        for (int i = 0; i + 8 <= verts.Count; i += 8)
            yield return verts.Skip(i).Take(8).ToArray();
    }

    private static bool ColorMatches(float[] vertex, Vector4 color) =>
        MathF.Abs(vertex[4] - color.X) < 0.001f
        && MathF.Abs(vertex[5] - color.Y) < 0.001f
        && MathF.Abs(vertex[6] - color.Z) < 0.001f
        && MathF.Abs(vertex[7] - color.W) < 0.001f;

    private static (TextRenderer renderer, UiRenderContext ctx) MakeContext(float w, float h)
    {
        var device = new RecordingGpuDevice();
        var renderer = new TextRenderer(device, new NullGpuFrameSource(), "unused");
        renderer.Begin(new Vector2(w, h));
        var ctx = new UiRenderContext(renderer, new Vector2(w, h));
        return (renderer, ctx);
    }

    private sealed class NullGpuFrameSource : ICurrentGpuFrameSource
    {
        public IGpuFrame? CurrentFrame => null;
    }
}
