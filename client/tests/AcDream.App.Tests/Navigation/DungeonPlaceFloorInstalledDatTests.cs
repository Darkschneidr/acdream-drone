using System.Numerics;
using AcDream.Core.Navigation;
using AcDream.Core.Physics;
using AcDream.Runtime.Navigation;
using Xunit.Abstractions;

namespace AcDream.App.Tests.Navigation;

/// <summary>
/// Every cell of an installed dungeon routed to by the middle of its bounds, the place a
/// dungeon map gives for it, as it is given and as it stands on the floor under it.
/// </summary>
[Trait("Lane", "InstalledDat")]
[Trait("Purpose", "Diagnostic")]
public sealed class DungeonPlaceFloorInstalledDatTests
{
    private readonly ITestOutputHelper _output;

    public DungeonPlaceFloorInstalledDatTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// 0x5B48's wings are joined by more than its cells, so from its largest wing about 265 of
    /// its cells route on the floor and few as given; 0x5948 routes nearly every cell.
    /// </summary>
    [Theory]
    [InlineData(0x5B48FFFFu, 0x5B48013Cu)]
    [InlineData(0x5948FFFFu, 0x59480100u)]
    public void TheMiddleOfEveryCellRoutesOnceItStandsOnTheFloor(uint dungeon, uint startCell)
    {
        string? directory = System.Environment.GetEnvironmentVariable("ACDREAM_DAT_DIR");
        Assert.False(string.IsNullOrWhiteSpace(directory), "Set ACDREAM_DAT_DIR to the installed game files.");
        uint[] cellIds = [.. Enumerable.Range(0x100, 0x400).Select(offset => (dungeon & 0xFFFF0000u) | (uint)offset)];
        PublishedLandblock world = PublishedLandblock.Load(directory!, dungeon, cellIds);
        Assert.True(world.Engine.TryGetLandblockCollision(dungeon, out _, out IReadOnlyList<CellSurface> cells, out _));
        Assert.True(NavGeometry.TryMeasureCells(world.Engine, dungeon, out Vector2 minimum, out Vector2 maximum));
        float size = MathF.Ceiling((MathF.Max(maximum.X - minimum.X, maximum.Y - minimum.Y) + 48f) / 16f) * 16f;
        float originX = MathF.Floor((((minimum.X + maximum.X) * 0.5f) - (size * 0.5f)) / NavGrid.DefaultCellSize) * NavGrid.DefaultCellSize;
        float originY = MathF.Floor((((minimum.Y + maximum.Y) * 0.5f) - (size * 0.5f)) / NavGrid.DefaultCellSize) * NavGrid.DefaultCellSize;
        NavGrid grid = NavGrid.Build(NavGeometry.CaptureDungeon(world.Engine, dungeon, originX, originY, size)!, world.Body);

        int startNode = grid.FindWalkableNode(world.CellOrigins[startCell], 1f, 1.5f);
        Assert.True(startNode >= 0);
        Vector3 start = grid.Position(startNode);
        float arrival = NavigationWalkController.PlanningRadius(2.5f);
        int asGiven = 0;
        int onTheFloor = 0;
        int tall = 0;
        int floorless = 0;
        int faceUpFloor = 0;
        var reasons = new Dictionary<string, int>();
        var flooredReasons = new Dictionary<string, int>();
        foreach (CellSurface cell in cells)
        {
            var low = new Vector3(float.PositiveInfinity);
            var high = new Vector3(float.NegativeInfinity);
            foreach ((Vector3 a, Vector3 b, Vector3 c) in cell.Triangles)
            {
                low = Vector3.Min(low, Vector3.Min(a, Vector3.Min(b, c)));
                high = Vector3.Max(high, Vector3.Max(a, Vector3.Max(b, c)));
            }
            if (float.IsInfinity(low.X))
                continue;
            Vector3 middle = (low + high) * 0.5f;
            Vector3 placed = RuntimeNavigationGoalSource.OnTheCellFloor(world.Engine, cell.CellId, middle);
            if (middle.Z - placed.Z > NavRouter.GoalHeightTolerance)
                tall++;
            if (NavGeometry.HighestFloorUnder(world.Engine, dungeon, middle.X, middle.Y, float.NegativeInfinity, middle.Z) is not null)
                faceUpFloor++;
            if (cell.SampleLevelZAtOrBelow(middle.X, middle.Y, middle.Z) is null)
                floorless++;
            NavRoute raw = NavRouter.Find(grid, start, middle, arrival, arriveOnGoalFloor: true);
            if (raw.Outcome == NavRouteOutcome.Routed)
                asGiven++;
            else
                reasons[raw.Reason ?? "(none)"] = reasons.GetValueOrDefault(raw.Reason ?? "(none)") + 1;
            NavRoute floored = NavRouter.Find(grid, start, placed, arrival, arriveOnGoalFloor: true);
            if (floored.Outcome == NavRouteOutcome.Routed)
                onTheFloor++;
            else
                flooredReasons[floored.Reason ?? "(none)"] = flooredReasons.GetValueOrDefault(floored.Reason ?? "(none)") + 1;
        }

        _output.WriteLine($"0x{dungeon >> 16:X4}: {cells.Count} cells, {tall} with their middle higher over the floor than a walk arrives, {floorless} with no floor of their own under it");
        _output.WriteLine($"  routed as given: {asGiven}; routed on the floor: {onTheFloor}");
        _output.WriteLine($"  {faceUpFloor} with a face-up floor somewhere under their middle");
        foreach ((string reason, int count) in reasons.OrderByDescending(pair => pair.Value).Take(5))
            _output.WriteLine($"  {count} as given: {reason}");
        foreach ((string reason, int count) in flooredReasons.OrderByDescending(pair => pair.Value).Take(5))
            _output.WriteLine($"  {count} on the floor: {reason}");
        Assert.True(onTheFloor > asGiven);
    }

}
