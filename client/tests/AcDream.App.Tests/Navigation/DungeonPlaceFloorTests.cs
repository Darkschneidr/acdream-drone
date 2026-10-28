using System.Numerics;
using AcDream.Core.Physics;
using AcDream.Plugin.Abstractions;
using AcDream.Core.Items;
using AcDream.Core.Net;
using AcDream.Core.Net.Messages;
using AcDream.Runtime;
using AcDream.Runtime.Entities;
using AcDream.Runtime.Gameplay;
using AcDream.Runtime.Navigation;
using static AcDream.App.Tests.Navigation.NavigationWalkControllerTests;

namespace AcDream.App.Tests.Navigation;

/// <summary>
/// A place inside a dungeon cell is often given by the middle of the cell's bounds, which in a
/// tall room is metres above its floor. Such a place stands on the floor under it.
/// </summary>
public sealed class DungeonPlaceFloorTests
{
    private const uint LowRoom = 0xA9B40100u;
    private const uint TallRoom = 0xA9B40101u;
    private const uint StackedLower = 0xA9B40102u;
    private const uint StackedUpper = 0xA9B40103u;
    private const float Floor = -30f;

    /// <summary>The middle of the 5 m room's bounds, 2.5 m over its floor, as a dungeon map gives it.</summary>
    private static readonly Vector3 TallMiddle = new(30f, 10f, -27.5f);

    /// <summary>The middle of the floorless upper cell's bounds, 7.5 m over the floor of the cell below.</summary>
    private static readonly Vector3 UpperMiddle = new(50f, 10f, -22.5f);

    /// <summary>
    /// Why the fix is needed: a goal left in mid-air, higher over the floor than a walk can arrive
    /// at, has no route at all, though the room is open to walk into.
    /// </summary>
    [Fact]
    public async Task APlaceLeftInMidAirOverATallRoomsFloorHasNoRoute()
    {
        PluginNavigationPlan plan = await Preview(new Goals(), TallRoom, TallMiddle);

        Assert.Equal(PluginNavigationPlanStatus.NoRoute, plan.Status);
    }

    [Fact]
    public async Task APlaceAtTheMiddleOfATallRoomRoutesToItsFloor()
    {
        PluginNavigationPlan plan = await Preview(new Goals { Floors = DungeonWithTallRooms() }, TallRoom, TallMiddle);

        Assert.Equal(PluginNavigationPlanStatus.Routed, plan.Status);
        Assert.InRange(plan.Path[^1].Elevation * 240d, Floor - 0.1d, Floor + 0.1d);
    }

    [Fact]
    public async Task APlaceInAFloorlessUpperCellRoutesToTheFloorOfTheCellBelow()
    {
        PhysicsEngine world = DungeonWithTallRooms();

        Vector3 placed = RuntimeNavigationGoalSource.OnTheCellFloor(world, StackedUpper, UpperMiddle);
        PluginNavigationPlan plan = await Preview(new Goals { Floors = world }, StackedUpper, UpperMiddle);

        Assert.Equal(new Vector3(50f, 10f, Floor), placed);
        Assert.Equal(PluginNavigationPlanStatus.Routed, plan.Status);
        Assert.Equal(StackedLower, plan.Path[^1].CellId);
    }

    [Fact]
    public async Task APlaceInALowRoomIsUnchanged()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        var onTheFloor = new Vector3(15f, 12f, Floor);
        var middle = new Vector3(15f, 12f, -28.5f);

        PluginNavigationPlan plan = await Preview(new Goals { Floors = world }, LowRoom, middle);

        Assert.Equal(onTheFloor, RuntimeNavigationGoalSource.OnTheCellFloor(world, LowRoom, onTheFloor));
        Assert.Equal(onTheFloor, RuntimeNavigationGoalSource.OnTheCellFloor(world, LowRoom, middle));
        Assert.Equal(PluginNavigationPlanStatus.Routed, plan.Status);
    }

    [Fact]
    public void APlaceGivenALittleUnderItsFloorStaysOnThatFloor()
    {
        PhysicsEngine world = DungeonWithTallRooms();

        Vector3 placed = RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, new Vector3(30f, 10f, Floor - 0.2f));

        Assert.Equal(Floor, placed.Z);
    }

    [Theory]
    [InlineData(0xA9B40001u)]
    [InlineData(0xA9B4FFFFu)]
    [InlineData(0u)]
    public void AnOutdoorPlaceIsUnchanged(uint cellId)
    {
        PhysicsEngine world = DungeonWithTallRooms();
        var place = new Vector3(30f, 10f, -27.5f);

        Assert.Equal(place, RuntimeNavigationGoalSource.OnTheCellFloor(world, cellId, place));
    }

    [Fact]
    public void APlaceWithNoFloorUnderItIsUnchanged()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        var outside = new Vector3(100f, 100f, -27.5f);
        var belowEveryFloor = new Vector3(30f, 10f, -40f);

        Assert.Equal(outside, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, outside));
        Assert.Equal(belowEveryFloor, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, belowEveryFloor));
    }

    /// <summary>The walk to a place stands its goal where the preview does.</summary>
    [Fact]
    public void AWalkToAPlaceAtTheMiddleOfATallRoomArrives()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        var body = new SimulatedBody(new Vector3(10f, 10f, Floor)) { CellId = LowRoom };
        var walk = new NavigationWalkController(world, body, new Goals { Floors = world }, isSealedDungeon: _ => true);

        walk.WalkToPlace(TallRoom, TallMiddle, 2.5f);
        NavigationWalkReport report = RunUntilSettled(walk, body);

        Assert.Equal(NavigationWalkState.Arrived, report.State);
        Assert.InRange(Vector2.Distance(new Vector2(body.Position.X, body.Position.Y), new Vector2(30f, 10f)), 0f, 2.6f);
    }

    /// <summary>
    /// The client's own goal source stands a place on the floor, not only the test double:
    /// a preview through it to the middle of a tall room routes to that room's floor.
    /// </summary>
    [Fact]
    public async Task TheClientsGoalSourceStandsAPlaceAtTheMiddleOfATallRoomOnItsFloor()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        RuntimeNavigationGoalSource goals = ClientGoals(world);

        Assert.True(goals.TryLocatePlace(TallRoom, TallMiddle, out Vector3 placed));
        var body = new SimulatedBody(new Vector3(10f, 10f, Floor)) { CellId = LowRoom };
        var walk = new NavigationWalkController(world, body, goals, isSealedDungeon: _ => true);
        PluginNavigationPlan plan = await walk.PreviewPathAsync(TallRoom, TallMiddle, 2.5f);

        Assert.Equal(new Vector3(30f, 10f, Floor), placed);
        Assert.Equal(PluginNavigationPlanStatus.Routed, plan.Status);
        Assert.InRange(plan.Path[^1].Elevation * 240d, Floor - 0.1d, Floor + 0.1d);
    }

    /// <summary>
    /// A place on top of an object in a cell, such as a route waypoint recorded where the
    /// character stood on a platform, stays on the platform: the route grid stands a body on
    /// the object's top, so a walk arrives there, or not at all. A place a little under the
    /// top is lifted onto it.
    /// </summary>
    [Fact]
    public void APlaceOnAPlatformInACellStaysOnThePlatform()
    {
        PhysicsEngine world = DungeonWithAPlatform();
        RuntimeNavigationGoalSource goals = ClientGoals(world);
        var onTop = new Vector3(30f, 10f, PlatformTop);
        var justOver = onTop with { Z = PlatformTop + 0.4f };

        Assert.NotEmpty(NavigationGeometry(world).Cylinders);
        Assert.Equal(onTop, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, onTop));
        Assert.Equal(onTop, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, onTop with { Z = PlatformTop - 0.2f }));
        Assert.Equal(justOver, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, justOver));
        Assert.True(goals.TryLocatePlace(TallRoom, onTop, out Vector3 placed));
        Assert.Equal(onTop, placed);
        Assert.Equal(new Vector3(36f, 10f, Floor), RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, new Vector3(36f, 10f, PlatformTop)));
    }

    /// <summary>
    /// A waypoint recorded at a platform's edge stays on the platform, as far out as the route
    /// grid still holds the platform's top: half a grid column beyond its radius.
    /// </summary>
    [Fact]
    public void APlaceAtAPlatformsEdgeStaysOnThePlatform()
    {
        PhysicsEngine world = DungeonWithAPlatform();
        float edge = 30f + PlatformRadius;
        float slack = AcDream.Core.Navigation.NavGrid.DefaultCellSize * 0.5f;
        var atTheEdge = new Vector3(edge + (slack * 0.8f), 10f, PlatformTop);
        var pastTheEdge = new Vector3(edge + (slack * 1.5f), 10f, PlatformTop);

        Assert.Equal(atTheEdge, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, atTheEdge));
        Assert.Equal(pastTheEdge with { Z = Floor }, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, pastTheEdge));
    }

    /// <summary>
    /// The middle of a 3 m room is 1.5 m over its floor and 0.6 m over the top of a table
    /// standing there. A table top is no floor a waypoint was recorded on, so the place drops
    /// to the room's floor and a route reaches it.
    /// </summary>
    [Fact]
    public async Task TheMiddleOfALowRoomOverATableDropsToTheFloorAndRoutes()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        RegisterCylinder(world, 0x7A9B_4002u, new Vector3(15f, 12f, Floor), radius: 0.6f, height: 0.9f, LowRoom, isStatic: true);
        RuntimeNavigationGoalSource goals = ClientGoals(world);
        var middle = new Vector3(15f, 12f, -28.5f);

        Assert.True(goals.TryLocatePlace(LowRoom, middle, out Vector3 placed));
        var body = new SimulatedBody(new Vector3(10f, 10f, Floor)) { CellId = LowRoom };
        var walk = new NavigationWalkController(world, body, goals, isSealedDungeon: _ => true);
        PluginNavigationPlan plan = await walk.PreviewPathAsync(LowRoom, middle, 2.5f);

        Assert.Equal(middle with { Z = Floor }, placed);
        Assert.Equal(PluginNavigationPlanStatus.Routed, plan.Status);
        Assert.InRange(plan.Path[^1].Elevation * 240d, Floor - 0.1d, Floor + 0.1d);
    }

    /// <summary>
    /// The underside of a ceiling, an arch or anything hanging faces down, and nothing stands
    /// on it: a place just under the 5 m room's ceiling drops to the floor, never up onto
    /// the ceiling.
    /// </summary>
    [Fact]
    public void APlaceJustUnderACeilingDropsToTheFloorNotOntoTheCeiling()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        var underTheCeiling = new Vector3(30f, 10f, -25.3f);

        Assert.Equal(
            underTheCeiling with { Z = Floor },
            RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, underTheCeiling));
    }

    /// <summary>
    /// An object the server placed is no part of the landblock, so a place is kept on its top
    /// only because the walk names it as something a body stands on, as it does when it
    /// builds its grid. The client's goal source passes that on.
    /// </summary>
    [Fact]
    public void APlaceOnAPlatformTheServerPlacedStaysOnIt()
    {
        PhysicsEngine world = DungeonWithTallRooms();
        using GameRuntime runtime = GameRuntimeTestFactory.Create();
        RuntimeEntityRecord record = runtime.EntityObjects.RegisterEntity(Spawn(0x7000_0001u)).Canonical!;
        runtime.EntityObjects.ApplyAcceptedSpawn(record, record.CreateIntegrationVersion, record.Snapshot, replaceGeneration: false);
        runtime.InventoryOwner.Objects.AddOrUpdate(new ClientObject { ObjectId = 0x7000_0001u, Name = "Platform" });
        uint local = runtime.EntityObjects.Entities.ClaimLocalId(record);
        RegisterCylinder(world, local, new Vector3(30f, 10f, Floor), PlatformRadius, 2f, TallRoom, isStatic: false);
        RuntimeNavigationGoalSource goals = ClientGoals(world, runtime);
        var onTop = new Vector3(30f, 10f, PlatformTop);

        Assert.True(goals.StandsStill(local));
        Assert.True(goals.TryLocatePlace(TallRoom, onTop, out Vector3 placed));
        Assert.Equal(onTop, placed);
        Assert.Equal(onTop with { Z = Floor }, RuntimeNavigationGoalSource.OnTheCellFloor(world, TallRoom, onTop));
    }

    private const float PlatformTop = Floor + 2f;
    private const float PlatformRadius = 3f;

    /// <summary>The 5 m room with a platform 2 m high and 6 m across in its middle, which the landblock holds.</summary>
    private static PhysicsEngine DungeonWithAPlatform()
    {
        PhysicsEngine physics = DungeonWithTallRooms();
        RegisterCylinder(physics, 0x7A9B_4001u, new Vector3(30f, 10f, Floor), PlatformRadius, 2f, TallRoom, isStatic: true);
        return physics;
    }

    private static void RegisterCylinder(
        PhysicsEngine physics,
        uint entityId,
        Vector3 at,
        float radius,
        float height,
        uint cellId,
        bool isStatic) =>
        physics.ShadowObjects.Register(
            entityId,
            gfxObjId: 0u,
            at,
            Quaternion.Identity,
            radius,
            worldOffsetX: 0f,
            worldOffsetY: 0f,
            landblockId: 0xA9B4FFFFu,
            collisionType: ShadowCollisionType.Cylinder,
            cylHeight: height,
            seedCellId: cellId,
            isStatic: isStatic);

    private static AcDream.Core.Navigation.NavGeometry NavigationGeometry(PhysicsEngine world) =>
        AcDream.Core.Navigation.NavGeometry.CaptureDungeon(world, 0xA9B4FFFFu, 0f, 0f, 64f)!;

    /// <summary>The client's own goal source, the character standing in the low room.</summary>
    private static RuntimeNavigationGoalSource ClientGoals(PhysicsEngine world, GameRuntime? runtime = null)
    {
        var start = new Vector3(10f, 10f, Floor);
        var controller = new PlayerMovementController(world);
        controller.PreparePositionForCommit(start, LowRoom, start);
        var movement = new RuntimeLocalPlayerMovementState { Controller = controller };
        return new RuntimeNavigationGoalSource(world, runtime ?? GameRuntimeTestFactory.Create(), movement);
    }

    /// <summary>A server object as the client is told of it: no player, nothing ethereal, no creature.</summary>
    private static WorldSession.EntitySpawn Spawn(uint guid)
    {
        var position = new CreateObject.ServerPosition(TallRoom, 30f, 10f, Floor, 1f, 0f, 0f, 0f);
        var timestamps = new PhysicsTimestamps(
            Position: 1,
            Movement: 1,
            State: 1,
            Vector: 1,
            Teleport: 0,
            ServerControlledMove: 1,
            ForcePosition: 0,
            ObjDesc: 1,
            Instance: 1);
        var physics = new PhysicsSpawnData(
            RawState: 0u,
            Position: position,
            Movement: null,
            AnimationFrame: null,
            SetupTableId: 0x0200_0001u,
            MotionTableId: null,
            SoundTableId: null,
            PhysicsScriptTableId: null,
            Parent: null,
            Children: null,
            Scale: null,
            Friction: null,
            Elasticity: null,
            Translucency: null,
            Velocity: null,
            Acceleration: null,
            AngularVelocity: null,
            DefaultScriptType: null,
            DefaultScriptIntensity: null,
            Timestamps: timestamps);
        return new WorldSession.EntitySpawn(
            guid,
            position,
            0x0200_0001u,
            [],
            [],
            [],
            null,
            null,
            guid.ToString("X8"),
            null,
            null,
            null,
            PhysicsState: physics.RawState,
            InstanceSequence: 1,
            MovementSequence: 1,
            ServerControlSequence: 1,
            PositionSequence: 1,
            Physics: physics);
    }

    private static Task<PluginNavigationPlan> Preview(Goals goals, uint cellId, Vector3 local)
    {
        var body = new SimulatedBody(new Vector3(10f, 10f, Floor)) { CellId = LowRoom };
        var walk = new NavigationWalkController(goals.Floors ?? DungeonWithTallRooms(), body, goals, isSealedDungeon: _ => true);
        return walk.PreviewPathAsync(cellId, local, 2.5f);
    }

    /// <summary>
    /// Three rooms in a row 30 m down: a low room 3 m high, a room 5 m high, and a room 10 m
    /// high authored as a cell on top of a cell, the upper one with walls and a ceiling but
    /// no floor.
    /// </summary>
    internal static PhysicsEngine DungeonWithTallRooms()
    {
        static CellSurface Cell(uint cellId, float x0, float x1, float? floor, float low, float top, bool roofed = true)
        {
            var vertices = new Dictionary<ushort, Vector3>();
            var polygons = new List<List<short>>();
            void Add(params Vector3[] corners)
            {
                var ids = new List<short>();
                foreach (Vector3 corner in corners)
                {
                    ids.Add((short)vertices.Count);
                    vertices[(ushort)vertices.Count] = corner;
                }
                polygons.Add(ids);
            }
            if (floor is { } z)
                Add(new(x0, 0f, z), new(x1, 0f, z), new(x1, 20f, z), new(x0, 20f, z));
            if (roofed)
                Add(new(x0, 0f, top), new(x0, 20f, top), new(x1, 20f, top), new(x1, 0f, top));
            Add(new(x0, 0f, low), new(x1, 0f, low), new(x1, 0f, top), new(x0, 0f, top));
            Add(new(x0, 20f, low), new(x1, 20f, low), new(x1, 20f, top), new(x0, 20f, top));
            return new CellSurface(cellId, vertices, polygons);
        }

        var physics = new PhysicsEngine();
        physics.AddLandblock(
            0xA9B4FFFFu,
            new TerrainSurface(new byte[81], new float[256]),
            [
                Cell(LowRoom, 0f, 20f, Floor, Floor, -27f),
                Cell(TallRoom, 20f, 40f, Floor, Floor, -25f),
                Cell(StackedLower, 40f, 60f, Floor, Floor, -25f, roofed: false),
                Cell(StackedUpper, 40f, 60f, null, -25f, -20f),
            ],
            [],
            0f,
            0f);
        return physics;
    }
}
