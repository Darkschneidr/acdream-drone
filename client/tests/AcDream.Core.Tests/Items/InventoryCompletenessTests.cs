using AcDream.Core.Items;

namespace AcDream.Core.Tests.Items;

public sealed class InventoryCompletenessTests
{
    [Fact]
    public void OptimisticDropDoesNotProveOwnershipLossUntilConfirmed()
    {
        var table = new ClientObjectTable();
        table.InitializeInventoryManifest(1, [new ContainerContentEntry(2, 0)]);
        Assert.True(table.HasCompleteInventory(1));
        table.MoveItemOptimistic(2, 0, -1);
        Assert.False(table.HasCompleteInventory(1));
        table.RollbackMove(2);
        Assert.True(table.HasCompleteInventory(1));
        table.MoveItemOptimistic(2, 0, -1);
        table.ConfirmMove(2);
        Assert.True(table.HasCompleteInventory(1));
        Assert.Empty(table.GetContents(1));
    }

    [Fact]
    public void LoginWaitsForNestedListingsAndClearRevokesCompleteness()
    {
        var table = new ClientObjectTable();
        Assert.False(table.HasCompleteInventory(1));
        table.InitializeInventoryManifest(1, [new ContainerContentEntry(2, 1)]);
        Assert.False(table.HasCompleteInventory(1));
        table.ReplaceContents(2, new ContainerContentEntry[] { new(3, 1) });
        Assert.False(table.HasCompleteInventory(1));
        table.ReplaceContents(3, Array.Empty<uint>());
        Assert.True(table.HasCompleteInventory(1));
        table.Clear();
        Assert.False(table.HasCompleteInventory(1));
    }

    [Fact]
    public void EmptyAuthoritativeInventoryIsComplete()
    {
        var table = new ClientObjectTable();
        table.InitializeInventoryManifest(1, Array.Empty<ContainerContentEntry>());
        Assert.True(table.HasCompleteInventory(1));
    }

    [Fact]
    public void SpellFocusNeverListedDoesNotBlockCompleteness()
    {
        // ACE lists a focus (container type 2) in the container slots but
        // never sends its contents, since a focus holds nothing.
        var table = new ClientObjectTable();
        table.InitializeInventoryManifest(1,
            [new ContainerContentEntry(2, 1), new ContainerContentEntry(3, 2)]);
        Assert.False(table.HasCompleteInventory(1));
        table.ReplaceContents(2, Array.Empty<uint>());
        Assert.True(table.HasCompleteInventory(1));
    }
}
