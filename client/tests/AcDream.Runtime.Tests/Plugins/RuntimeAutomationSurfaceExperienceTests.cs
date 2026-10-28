using AcDream.Core.Items;
using AcDream.Core.Properties;
using AcDream.Plugin.Abstractions;
using AcDream.Runtime.Plugins;
using AcDream.Runtime.Tests.Support;

namespace AcDream.Runtime.Tests.Plugins;

/// <summary>
/// What a plugin reads about the character's experience and luminance, and
/// when it is told that changed. Both hosts bind this one surface, so what is
/// proved here holds for each.
/// </summary>
public sealed class RuntimeAutomationSurfaceExperienceTests
{
    private const uint PlayerId = 0x50000001u;
    private const uint LevelId = (uint)PropertyInt.Level;

    /// <summary>A short level curve: level 3 is the top of the table.</summary>
    private static readonly ulong[] Levels = [0UL, 1_000UL, 3_000UL, 6_000UL];

    [Fact]
    public void NothingIsReportedBeforeTheDescriptionArrives()
    {
        using var world = World.InWorld();

        ICharacterInfo character = world.Surface.Character;
        Assert.False(character.HasExperience);
        Assert.Equal(0L, character.TotalExperience);
        Assert.Equal(0L, character.UnassignedExperience);
        Assert.Null(character.ExperienceToNextLevel);
        Assert.Equal(0L, character.AvailableLuminance);
        Assert.Equal(0L, character.MaximumLuminance);

        // An Int64 update on its own is not the description.
        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.TotalExperience, 500L);
        Assert.False(character.HasExperience);
        Assert.Equal(0, world.Raised);
    }

    /// <summary>
    /// Mutation: reading the object table instead of the local player's
    /// bundle leaves every value at 0 here, since the description in this
    /// test never reaches the table.
    /// </summary>
    [Fact]
    public void TheDescriptionAndLaterUpdatesAreReported()
    {
        using var world = World.InWorld();
        ICharacterInfo character = world.Surface.Character;

        world.Describe(level: 1, total: 1_400L, unassigned: 150L, luminance: 20L, maximumLuminance: 1_000L);

        Assert.True(character.HasExperience);
        Assert.Equal(1_400L, character.TotalExperience);
        Assert.Equal(150L, character.UnassignedExperience);
        Assert.Equal(1_600L, character.ExperienceToNextLevel);
        Assert.Equal(20L, character.AvailableLuminance);
        Assert.Equal(1_000L, character.MaximumLuminance);
        Assert.Equal(1, world.Raised);

        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.TotalExperience, 1_900L);
        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.AvailableExperience, 650L);
        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.AvailableLuminance, 35L);

        Assert.Equal(1_900L, character.TotalExperience);
        Assert.Equal(650L, character.UnassignedExperience);
        Assert.Equal(1_100L, character.ExperienceToNextLevel);
        Assert.Equal(35L, character.AvailableLuminance);
        Assert.Equal(4, world.Raised);
    }

    /// <summary>
    /// The server sends the new total before the new level. In between, the
    /// character needs nothing more for a level it has not been given yet;
    /// once the level lands the distance is measured to the level after.
    /// Mutation: not watching the player object for its level leaves the
    /// distance at 0 after the level-up and raises nothing for it.
    /// </summary>
    [Fact]
    public void ALevelUpMovesTheDistanceToTheNextLevel()
    {
        using var world = World.InWorld();
        ICharacterInfo character = world.Surface.Character;
        world.Describe(level: 1, total: 2_500L);
        Assert.Equal(500L, character.ExperienceToNextLevel);

        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.TotalExperience, 3_200L);
        Assert.Equal(0L, character.ExperienceToNextLevel);

        world.Runtime.InventoryOwner.Objects.UpdateIntProperty(PlayerId, LevelId, 2);
        Assert.Equal(2_800L, character.ExperienceToNextLevel);
        Assert.Equal(3, world.Raised);
    }

    [Fact]
    public void TheTopOfTheTableHasNoNextLevel()
    {
        using var world = World.InWorld();
        world.Describe(level: 3, total: 7_000L);

        Assert.True(world.Surface.Character.HasExperience);
        Assert.Null(world.Surface.Character.ExperienceToNextLevel);
        Assert.Equal(7_000L, world.Surface.Character.TotalExperience);
    }

    [Fact]
    public void WithoutAnExperienceTableTheDistanceIsUnknown()
    {
        using var world = World.InWorld(bindLevels: false);
        world.Describe(level: 1, total: 400L);

        Assert.True(world.Surface.Character.HasExperience);
        Assert.Equal(400L, world.Surface.Character.TotalExperience);
        Assert.Null(world.Surface.Character.ExperienceToNextLevel);
    }

    /// <summary>
    /// Mutation: raising on every CharacterChanged counts the skill, the
    /// unrelated Int64, the position and the repeated value here.
    /// </summary>
    [Fact]
    public void TheEventIsRaisedOncePerRealChangeOnly()
    {
        using var world = World.InWorld();
        world.Describe(level: 1, total: 400L);
        Assert.Equal(1, world.Raised);

        world.Player.OnSkillUpdate(6u, 10u, 2u, 0u, 10u, 0u, 0d, 0u);
        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.AugmentationCost, 9L);
        world.Player.OnPosition(0x0Eu, new AcDream.Core.Physics.Position(
            0x01010001u, System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity));
        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.TotalExperience, 400L);
        world.Runtime.InventoryOwner.Objects.UpdateIntProperty(PlayerId, LevelId, 1);
        Assert.Equal(1, world.Raised);

        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.TotalExperience, 401L);
        Assert.Equal(2, world.Raised);
    }

    /// <summary>
    /// The plugins hear the logoff while the session is still whole, before
    /// the teardown clears the character. Mutation: not taking a baseline
    /// when the character leaves keeps reporting its experience here, and
    /// again on the first stray update before the next description.
    /// </summary>
    [Fact]
    public void TheLogoffEdgeResetsBeforeTheCharacterIsCleared()
    {
        using var world = World.InWorld();
        ICharacterInfo character = world.Surface.Character;
        world.Describe(level: 1, total: 1_400L);
        Assert.Equal(1, world.Raised);

        ((AcDream.Runtime.IRuntimeEventObserver)world.Surface).OnLeavingWorld();

        Assert.False(character.HasExperience);
        Assert.Equal(0L, character.TotalExperience);
        Assert.Equal(2, world.Raised);

        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.TotalExperience, 1_500L);
        Assert.False(character.HasExperience);
        Assert.Equal(2, world.Raised);

        world.Describe(level: 1, total: 1_600L);
        Assert.True(character.HasExperience);
        Assert.Equal(1_600L, character.TotalExperience);
        Assert.Equal(3, world.Raised);
    }

    [Fact]
    public void LeavingTheWorldResetsUntilTheNextDescription()
    {
        using var world = World.InWorld();
        ICharacterInfo character = world.Surface.Character;
        world.Describe(level: 1, total: 400L, luminance: 10L, maximumLuminance: 100L);
        Assert.Equal(1, world.Raised);

        world.Host.Stop();

        Assert.False(character.HasExperience);
        Assert.Equal(0L, character.TotalExperience);
        Assert.Equal(0L, character.AvailableLuminance);
        Assert.Null(character.ExperienceToNextLevel);
        Assert.Equal(2, world.Raised);

        world.Player.OnInt64PropertyUpdate((uint)PropertyInt64.TotalExperience, 999L);
        Assert.False(character.HasExperience);
        Assert.Equal(2, world.Raised);

        world.Describe(level: 2, total: 1_200L);
        Assert.True(character.HasExperience);
        Assert.Equal(1_200L, character.TotalExperience);
        Assert.Equal(3, world.Raised);
    }

    private sealed class World : IDisposable
    {
        private World(NoWindowGameRuntimeHost host, RuntimeAutomationSurface surface)
        {
            Host = host;
            Surface = surface;
            surface.Character.ExperienceChanged += () => Raised++;
        }

        internal NoWindowGameRuntimeHost Host { get; }
        internal RuntimeAutomationSurface Surface { get; }
        internal GameRuntime Runtime => Host.Runtime;
        internal AcDream.Core.Player.LocalPlayerState Player =>
            Runtime.CharacterOwner.LocalPlayer;
        internal int Raised { get; private set; }

        internal static World InWorld(bool bindLevels = true)
        {
            var host = new NoWindowGameRuntimeHost();
            host.Start();
            for (int i = 0; i < 4; i++)
                host.Session.Tick();
            Assert.True(host.Runtime.Session.IsInWorld);
            host.Runtime.PlayerIdentity.ServerGuid = PlayerId;
            host.Runtime.InventoryOwner.Objects.AddOrUpdate(
                RuntimeEntityTestSpawns.PlayerObject(PlayerId));

            var surface = new RuntimeAutomationSurface();
            surface.Bind(host.Runtime, host.Runtime.CharacterOwner, host.Runtime.ActionOwner.SpellCast);
            if (bindLevels)
                surface.BindExperienceLevels(() => Levels);
            return new World(host, surface);
        }

        /// <summary>The PlayerDescription's property bundle, as login applies it.</summary>
        internal void Describe(
            int level,
            long total,
            long unassigned = 0L,
            long luminance = 0L,
            long maximumLuminance = 0L)
        {
            var properties = new PropertyBundle();
            properties.Ints[LevelId] = level;
            properties.Int64s[(uint)PropertyInt64.TotalExperience] = total;
            properties.Int64s[(uint)PropertyInt64.AvailableExperience] = unassigned;
            if (maximumLuminance > 0L)
            {
                properties.Int64s[(uint)PropertyInt64.AvailableLuminance] = luminance;
                properties.Int64s[(uint)PropertyInt64.MaximumLuminance] = maximumLuminance;
            }
            Player.OnProperties(properties);
        }

        public void Dispose()
        {
            Surface.Dispose();
            Host.Dispose();
        }
    }
}
