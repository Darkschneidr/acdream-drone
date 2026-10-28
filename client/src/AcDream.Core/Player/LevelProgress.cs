namespace AcDream.Core.Player;

/// <summary>
/// Where a character stands between its level and the next, measured on the
/// experience table's level curve. The character sheet's experience meter and
/// the plugin surface both read it from here, so the two cannot disagree.
/// </summary>
/// <param name="ToNext">
/// Experience still needed to reach the next level; 0 at the top of the table.
/// </param>
/// <param name="Fraction">How far through the current level, from 0 to 1.</param>
/// <param name="NoNextLevel">
/// Nothing left to earn: the top of the table, or experience that already
/// reaches the next level's threshold. The sheet shows "Infinity!" for this.
/// </param>
/// <param name="AtTopOfTable">
/// The table has no next level for this character at all.
/// </param>
public readonly record struct LevelProgress(
    long ToNext,
    float Fraction,
    bool NoNextLevel,
    bool AtTopOfTable)
{
    /// <summary>
    /// Measures <paramref name="totalExperience"/> against the level curve.
    /// Null when there is no curve or the level is negative.
    /// </summary>
    /// <param name="levels">
    /// The experience table's level curve: the total experience at which each
    /// level starts, indexed by level.
    /// </param>
    /// <param name="level">The character's level.</param>
    /// <param name="totalExperience">Total experience ever earned.</param>
    public static LevelProgress? Measure(
        IReadOnlyList<ulong>? levels, int level, long totalExperience)
    {
        if (levels is null || level < 0)
            return null;
        // The top of the table: there is no next level to measure towards.
        if (level + 1 >= levels.Count)
            return new LevelProgress(0L, 0f, NoNextLevel: true, AtTopOfTable: true);

        long current = ClampToLong(levels[level]);
        long next = ClampToLong(levels[level + 1]);
        if (next <= current)
            return new LevelProgress(0L, 0f, NoNextLevel: true, AtTopOfTable: true);

        long clamped = totalExperience < current
            ? current
            : totalExperience > next ? next : totalExperience;
        long toNext = next - clamped;
        float fraction = (float)(clamped - current) / (next - current);
        // Nothing left to earn reads the same way as no next level at all.
        return new LevelProgress(toNext, fraction, toNext <= 0L, AtTopOfTable: false);
    }

    private static long ClampToLong(ulong value) =>
        value > long.MaxValue ? long.MaxValue : (long)value;
}
