using System.Numerics;

namespace AcDream.Core.Rendering.Wb;

/// <summary>Axis-aligned mesh bounds, stored as minimum then maximum in PAK records.</summary>
public struct BoundingBox(Vector3 min, Vector3 max)
{
    public Vector3 Min = min;
    public Vector3 Max = max;

    public readonly Vector3 Center => (Min + Max) * 0.5f;
    public readonly Vector3 Size => Max - Min;
}
