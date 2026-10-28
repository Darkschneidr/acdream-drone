using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using AcDream.Content.CharGen;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;

namespace AcDream.Content.Tests.CharGen;

public sealed class ChargenAppearanceCatalogCacheTests
{
    [Fact]
    public void RepeatedCachedLookupsDoNotAllocateFactoriesOrReadTheFilesAgain()
    {
        var gate = new object();
        using var source = new PaletteContent(gate);
        var catalog = new ChargenAppearanceCatalog(source, gate);
        for (int i = 0; i < 100; i++)
        {
            catalog.TryGetColor(1, 0, out _);
            catalog.TryGetPalSet(2);
            catalog.TryGetClothingTable(3);
        }
        int reads = source.Reads;
        long before = GC.GetAllocatedBytesForCurrentThread();
        int red = 0;
        for (int i = 0; i < 1000; i++)
        {
            catalog.TryGetColor(1, 0, out var color);
            red += color.R;
            catalog.TryGetPalSet(2);
            catalog.TryGetClothingTable(3);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(12000, red);
        Assert.Equal(3, reads);
        Assert.Equal(reads, source.Reads);
        Assert.True(allocated < 1024, $"Cached lookups allocated {allocated} bytes.");

        source.Palette.Colors[0] = new ColorARGB { Red = 42, Green = 34, Blue = 56, Alpha = 255 };
        Assert.True(catalog.TryGetColor(1, 0, out var updated));
        Assert.Equal(42, updated.R);
        Assert.False(catalog.TryGetColor(1, -1, out _));
        Assert.False(catalog.TryGetColor(1, 1, out _));
    }

    private sealed class PaletteContent(object gate) : IDatReaderWriter
    {
        public Palette Palette { get; } = CreatePalette();
        public int Reads { get; private set; }
        private static Palette CreatePalette()
        {
            var palette = new Palette { Id = 1 };
            palette.Colors.Add(new ColorARGB { Red = 12, Green = 34, Blue = 56, Alpha = 255 });
            return palette;
        }
        [return: MaybeNull]
        public T Get<T>(uint fileId) where T : IDBObj
        {
            Assert.True(Monitor.IsEntered(gate));
            Reads++;
            return fileId == 1 && Palette is T value ? value : default;
        }
        public bool TryGet<T>(uint fileId, [MaybeNullWhen(false)] out T value) where T : IDBObj
        {
            value = Get<T>(fileId);
            return value is not null;
        }
        public string SourceDirectory => string.Empty;
        public IDatDatabase Portal => throw new NotSupportedException();
        public IDatDatabase Cell => throw new NotSupportedException();
        public IDatDatabase HighRes => throw new NotSupportedException();
        public IDatDatabase Language => throw new NotSupportedException();
        public IDatDatabase Local => throw new NotSupportedException();
        public ReadOnlyDictionary<uint, IDatDatabase> CellRegions { get; } = new(new Dictionary<uint, IDatDatabase>());
        public ReadOnlyDictionary<uint, uint> RegionFileMap { get; } = new(new Dictionary<uint, uint>());
        public int PortalIteration => 0;
        public int CellIteration => 0;
        public int HighResIteration => 0;
        public int LanguageIteration => 0;
        public bool TryGetFileBytes(uint regionId, uint fileId, ref byte[] bytes, out int bytesRead)
        {
            bytesRead = 0;
            return false;
        }
        public IEnumerable<uint> GetAllIdsOfType<T>() where T : IDBObj => [];
        public IEnumerable<IDatReaderWriter.IdResolution> ResolveId(uint id) => [];
        public bool TrySave<T>(T obj, int iteration = 0) where T : IDBObj => throw new NotSupportedException();
        public bool TrySave<T>(uint regionId, T obj, int iteration = 0) where T : IDBObj => throw new NotSupportedException();
        public void Dispose() { }
    }
}
