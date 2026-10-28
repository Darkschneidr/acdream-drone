using AcDream.Content;
using AcDream.Core.Rendering.Wb;
using System;

namespace AcDream.App.Rendering.Wb {
    public static class TextureFormatExtensions {

        public static UploadPixelFormat ToPixelFormat(this AcDream.Core.Rendering.Wb.TextureFormat format) {
            return format switch {
                AcDream.Core.Rendering.Wb.TextureFormat.RGBA8 => UploadPixelFormat.Rgba,
                AcDream.Core.Rendering.Wb.TextureFormat.RGB8 => UploadPixelFormat.Rgb,
                AcDream.Core.Rendering.Wb.TextureFormat.A8 => UploadPixelFormat.Red,
                AcDream.Core.Rendering.Wb.TextureFormat.Rgba32f => UploadPixelFormat.Rgba,
                _ => throw new NotSupportedException($"Texture format {format} is not supported."),
            };
        }

        public static UploadPixelType ToPixelType(this AcDream.Core.Rendering.Wb.TextureFormat format) {
            return format switch {
                TextureFormat.RGBA8 => UploadPixelType.UnsignedByte,
                TextureFormat.RGB8 => UploadPixelType.UnsignedByte,
                TextureFormat.A8 => UploadPixelType.UnsignedByte,
                TextureFormat.Rgba32f => UploadPixelType.Float,
                _ => throw new NotSupportedException($"Texture format {format} is not supported."),
            };
        }
    }
}
