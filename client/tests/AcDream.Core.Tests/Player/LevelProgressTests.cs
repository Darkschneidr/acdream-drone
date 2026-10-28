using AcDream.Core.Player;

namespace AcDream.Core.Tests.Player;

public sealed class LevelProgressTests
{
    private static readonly ulong[] Levels = [0UL, 1_000UL, 3_000UL, 6_000UL];

    [Fact]
    public void MeasuresTheDistanceToTheNextLevel()
    {
        LevelProgress progress = LevelProgress.Measure(Levels, 1, 2_000L)!.Value;

        Assert.Equal(1_000L, progress.ToNext);
        Assert.Equal(0.5f, progress.Fraction);
        Assert.False(progress.NoNextLevel);
        Assert.False(progress.AtTopOfTable);
    }

    [Fact]
    public void ExperiencePastTheNextLevelIsNothingLeftButNotTheTop()
    {
        LevelProgress progress = LevelProgress.Measure(Levels, 1, 3_500L)!.Value;

        Assert.Equal(0L, progress.ToNext);
        Assert.True(progress.NoNextLevel);
        Assert.False(progress.AtTopOfTable);
    }

    [Fact]
    public void ExperienceBelowTheLevelClampsToItsStart()
    {
        LevelProgress progress = LevelProgress.Measure(Levels, 2, 10L)!.Value;

        Assert.Equal(3_000L, progress.ToNext);
        Assert.Equal(0f, progress.Fraction);
    }

    [Fact]
    public void TheLastLevelIsTheTopOfTheTable()
    {
        LevelProgress progress = LevelProgress.Measure(Levels, 3, 9_000L)!.Value;

        Assert.True(progress.AtTopOfTable);
        Assert.True(progress.NoNextLevel);
    }

    [Fact]
    public void NoCurveOrANegativeLevelMeasuresNothing()
    {
        Assert.Null(LevelProgress.Measure(null, 1, 0L));
        Assert.Null(LevelProgress.Measure(Levels, -1, 0L));
    }
}
