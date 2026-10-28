using AcDream.App.UI;
using AcDream.App.UI.Layout;
using AcDream.Core.Items;
using AcDream.Core.Selection;

namespace AcDream.App.Tests.UI.Layout;

[Trait("Lane", "InstalledDat")]
public sealed class InventoryPackScrollingInstalledDatTests
{
    [Theory]
    [InlineData(7)]
    [InlineData(8)]
    public void AugmentedPackColumn_showsEverySideSlotWithoutScrolling(int capacity)
    {
        string datDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Documents", "Asheron's Call");
        using var dats = new BoundedTestDatCollection(datDir);
        ElementInfo info = LayoutImporter.ImportInfos(dats, 0x21000023u)!;
        ImportedLayout layout = LayoutImporter.Build(info, resolve: _ => (1u, 16, 16), datFont: null);
        const uint player = 0x50000001u;
        var objects = new ClientObjectTable();
        objects.AddOrUpdate(new ClientObject { ObjectId = player, ContainersCapacity = capacity });
        InventoryController.Bind(layout, objects, () => player,
            iconIds: (_, _, _, _, _) => 0u, strength: () => 100,
            selection: new SelectionState(), datFont: null,
            resolveAppropriateName: ItemTooltipCaptionNames.Resolve);
        var root = new UiRoot { Width = 1920, Height = 1080 };
        root.AddChild(layout.Root);
        var list = Assert.IsType<UiItemList>(layout.FindElement(0x100001CAu));
        Assert.Equal(capacity, list.GetNumUIItems());
        Assert.Equal(capacity * 36, list.Height);
        for (int frame = 0; frame < 3; frame++) ApplyAnchors(root);
        layout.Root.Height += 120;
        ApplyAnchors(root);
        Assert.Equal(capacity * 36, list.Height);
        Assert.Equal(0, list.Scroll.ScrollY);
        for (int i = 0; i < capacity; i++)
        {
            UiItemSlot slot = list.GetItem(i)!;
            Assert.True(slot.Visible);
            var p = slot.ScreenPosition;
            int x = (int)(p.X + slot.Width / 2), y = (int)(p.Y + slot.Height / 2);
            Assert.Same(slot, root.Pick(x, y));
            for (UiElement? parent = slot.Parent; parent is not null; parent = parent.Parent)
            {
                Assert.True(p.Y + slot.Height <= parent.ScreenPosition.Y + parent.Height, Describe(list));
            }
        }
        UiItemSlot main = Assert.IsType<UiItemList>(layout.FindElement(InventoryController.TopContainerId)).GetItem(0)!;
        Assert.True(main.ScreenPosition.Y + main.Height <= list.ScreenPosition.Y);
    }

    private static void ApplyAnchors(UiElement parent)
    {
        foreach (UiElement child in parent.Children)
        {
            child.ApplyAnchor(parent.Width, parent.Height);
            ApplyAnchors(child);
        }
    }

    private static string Describe(UiElement element)
    {
        var lines = new List<string>();
        for (UiElement? e = element; e is not null; e = e.Parent)
            lines.Add($"{e.DatElementId:X8}: top={e.Top}, height={e.Height}, screen={e.ScreenPosition}");
        return string.Join("; ", lines);
    }
}
