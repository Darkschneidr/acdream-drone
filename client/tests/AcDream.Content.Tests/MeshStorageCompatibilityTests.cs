using System.Numerics;
using AcDream.Content.Pak;
using AcDream.Core.Rendering.Wb;

namespace AcDream.Content.Tests;

public sealed class MeshStorageCompatibilityTests
{
    // Captured before replacing the external mesh primitives; covers every texture format.
    private const string ExistingRecord = "QgAAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABwAAAAQAAAAEAAAAAAAAAAEAAAAAAAAAAAAAAAAABAAAAAECAwQAAAAAAAAAAAAAAwAAAAAAAQACAAAAAAAAAAAAAAAA/////wAAAAAAAAAAgD8ABAAAAAQAAAABAAAAAQAAAAAAAAAAAAAAAAAEAAAAAQIDBAAAAAAAAAAAAAADAAAAAAABAAIAAAAAAAAAAAAAAAD/////AAAAAAAAAACAPwAEAAAABAAAAAIAAAABAAAAAAAAAAAAAAAAAAQAAAABAgMEAAAAAAAAAAAAAAMAAAAAAAEAAgAAAAAAAAAAAAAAAP////8AAAAAAAAAAIA/AAQAAAAEAAAAAwAAAAEAAAAAAAAAAAAAAAAABAAAAAECAwQAAAAAAAAAAAAAAwAAAAAAAQACAAAAAAAAAAAAAAAA/////wAAAAAAAAAAgD8ABAAAAAQAAAAEAAAAAQAAAAAAAAAAAAAAAAAEAAAAAQIDBAAAAAAAAAAAAAADAAAAAAABAAIAAAAAAAAAAAAAAAD/////AAAAAAAAAACAPwAEAAAABAAAAAUAAAABAAAAAAAAAAAAAAAAAAQAAAABAgMEAAAAAAAAAAAAAAMAAAAAAAEAAgAAAAAAAAAAAAAAAP////8AAAAAAAAAAIA/AAQAAAAEAAAABgAAAAEAAAAAAAAAAAAAAAAABAAAAAECAwQAAAAAAAAAAAAAAwAAAAAAAQACAAAAAAAAAAAAAAAA/////wAAAAAAAAAAgD8AAAAAwAAAQEAAAIBAAACgQAAAwEAAABBBAACAPwAAAEAAAEBAAAAAAAAAAAAA";

    [Fact]
    public void ExistingPakRecord_ReadsAndWritesWithoutChangingBytes()
    {
        byte[] bytes = Convert.FromBase64String(ExistingRecord);
        ObjectMeshData mesh = ObjectMeshDataSerializer.Read(bytes);
        Assert.Equal(0x01000042u, mesh.ObjectId);
        Assert.Equal(new Vector3(-2, 3, 4), mesh.BoundingBox.Min);
        Assert.Equal(new Vector3(5, 6, 9), mesh.BoundingBox.Max);
        Assert.Equal(new Vector3(1.5f, 4.5f, 6.5f), mesh.BoundingBox.Center);
        Assert.Equal(new Vector3(7, 3, 5), mesh.BoundingBox.Size);
        Assert.Equal(7, mesh.TextureBatches.Count);
        TextureFormat[] formats = [TextureFormat.RGBA8, TextureFormat.RGB8, TextureFormat.A8,
            TextureFormat.Rgba32f, TextureFormat.DXT1, TextureFormat.DXT3, TextureFormat.DXT5];
        for (int i = 0; i < formats.Length; i++)
        {
            Assert.Equal(i, (int)formats[i]);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, Assert.Single(mesh.TextureBatches[(4, 4, formats[i])]).TextureData);
        }
        using var output = new MemoryStream();
        ObjectMeshDataSerializer.Write(mesh, output);
        Assert.Equal(bytes, output.ToArray());
    }
}
