using DatReaderWriter.DBObjs;

namespace AcDream.Content;

/// <summary>
/// Reads the experience table: the level curve and the raise-cost curves for
/// attributes, vitals and skills. The character sheet and the plugin surface
/// both load it through here.
/// </summary>
public static class ExperienceTableReader
{
    public const uint ExperienceTableDid = 0x0E000018u;

    /// <summary>
    /// The installed experience table, or null when the files hold none. A
    /// failed read of the usual id falls back to the first table of the type.
    /// </summary>
    public static ExperienceTable? Load(IDatReaderWriter? dats, Action<string>? log = null)
    {
        if (dats is null) return null;

        try
        {
            var table = dats.Get<ExperienceTable>(ExperienceTableDid);
            if (table is not null) return table;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[UI] ExperienceTable 0x0E000018 read failed ({ex.GetType().Name}: {ex.Message}); trying type scan.");
        }

        try
        {
            foreach (uint id in dats.GetAllIdsOfType<ExperienceTable>())
            {
                var table = dats.Get<ExperienceTable>(id);
                if (table is not null) return table;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[UI] ExperienceTable type scan failed ({ex.GetType().Name}: {ex.Message}); raise costs unavailable.");
        }

        return null;
    }
}
