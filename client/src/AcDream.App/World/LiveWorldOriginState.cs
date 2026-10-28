using System.Globalization;
using System.Numerics;

namespace AcDream.App.World;

internal sealed class LiveWorldOriginState
{
    public int CenterX { get; private set; }
    public int CenterY { get; private set; }
    public bool IsKnown { get; private set; }

    public void SetPlaceholder(int centerX, int centerY)
    {
        CenterX = centerX;
        CenterY = centerY;
        IsKnown = false;
    }

    public bool TryInitialize(int centerX, int centerY)
    {
        if (IsKnown)
            return false;

        CenterX = centerX;
        CenterY = centerY;
        IsKnown = true;
        return true;
    }

    public void Recenter(int centerX, int centerY)
    {
        CenterX = centerX;
        CenterY = centerY;
    }

    public (int X, int Y) GetCenter() => (CenterX, CenterY);

    public void EnsureAgreesWithRuntimeFrame(
        uint runtimeCenterLandblockId,
        uint projectingLandblockId) =>
        TryEnsureAgreesWithRuntimeFrame(
            runtimeCenterLandblockId,
            projectingLandblockId,
            transitInFlight: false);

    public bool TryEnsureAgreesWithRuntimeFrame(
        uint runtimeCenterLandblockId,
        uint projectingLandblockId,
        bool transitInFlight)
    {
        if (!IsKnown || runtimeCenterLandblockId == 0u)
            return true;

        int runtimeCenterX = (int)((runtimeCenterLandblockId >> 24) & 0xFFu);
        int runtimeCenterY = (int)((runtimeCenterLandblockId >> 16) & 0xFFu);
        if (runtimeCenterX == CenterX && runtimeCenterY == CenterY)
            return true;

        if (transitInFlight)
            return false;

        // DroneCam fix: the Runtime frame can recentre (cross-landblock teleport or walk)
        // a few frames before the streamed origin follows. Throwing here killed the client;
        // skip this projection and let the caller retry once the origin catches up.
        if (++_disagreeLogCount <= 5)
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"[world-frame] defer projection: runtime ({runtimeCenterX},{runtimeCenterY}) vs origin ({CenterX},{CenterY}) lb=0x{projectingLandblockId:X8}"));
        return false;
#pragma warning disable CS0162
        throw new InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"World-frame owners disagree: Runtime centre "
            + $"({runtimeCenterX},{runtimeCenterY}) vs streamed origin "
            + $"({CenterX},{CenterY}) while projecting landblock "
            + $"0x{projectingLandblockId:X8}. That offsets the entity by "
            + $"({(runtimeCenterX - CenterX) * 192f:F0}m,"
            + $"{(runtimeCenterY - CenterY) * 192f:F0}m) from its geometry."));
#pragma warning restore CS0162
    }
    private int _disagreeLogCount;

    public Vector3 CellLocalForSeed(Vector3 worldPosition, uint cellId)
    {
        int landblockX = (int)((cellId >> 24) & 0xFFu);
        int landblockY = (int)((cellId >> 16) & 0xFFu);
        var origin = new Vector3(
            (landblockX - CenterX) * 192f,
            (landblockY - CenterY) * 192f,
            0f);
        return worldPosition - origin;
    }

    public void Reset() => IsKnown = false;
}
