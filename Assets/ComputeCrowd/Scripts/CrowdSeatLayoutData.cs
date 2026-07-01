using System;
using UnityEngine;

[Serializable]
public sealed class CrowdSeatLayoutData
{
    public int formatVersion = 1;
    public string layoutName;
    public string layoutType;
    public string sourceObjectName;
    public bool positionsRelativeToSourceCenter;
    public CrowdSeatLayoutBounds sourceBounds;
    public Vector3 sourceBoundsCenter;
    public Vector3 sourceBoundsSize;
    public CrowdSeatLayoutAisleConfig aisleConfig;
    public CrowdSeatLayoutBounds bounds;
    public CrowdSeatLayoutRowSection[] rowSections;
    public CrowdSeatLayoutSeat[] seats;

    public int SeatCount => seats?.Length ?? 0;
    public bool HasSeats => seats != null && seats.Length > 0;
    public int RowSectionCount => rowSections?.Length ?? 0;
}

[Serializable]
public struct CrowdSeatLayoutBounds
{
    public Vector3 min;
    public Vector3 max;
}

[Serializable]
public struct CrowdSeatLayoutAisleConfig
{
    public int aisleCount;
    public float aisleWidth;
    public int cornerSegments;
    public bool cornerAislesExcluded;
    public bool sectionsAreExplicit;
}

[Serializable]
public struct CrowdSeatLayoutRowSection
{
    public int blockIndex;
    public int floorIndex;
    public int rowIndex;
    public int sectionIndex;
    public int loopSectionIndex;
    public string sectionKind;
    public int sideIndex;
    public int cornerIndex;
    public float localT0;
    public float localT1;
    public int seatCount;
    public Vector3 centerStart;
    public Vector3 centerEnd;
    public Vector3 centerMid;
}

[Serializable]
public struct CrowdSeatLayoutSeat
{
    public Vector3 position;
    public Vector3 forward;
    public int blockIndex;
    public int floorIndex;
    public int rowIndex;
    public int sectionIndex;
    public int seatIndex;
    public int loopSectionIndex;
    public string sectionKind;
    public int sideIndex;
    public int cornerIndex;
    public float sectionLocalT;
    public float sectionLocalT0;
    public float sectionLocalT1;
    public float rowHeight;
    public float seatSurfaceHeight;
    public float anchorHeight;
}

public static class CrowdSeatLayoutUtility
{
    public static bool TryParse(TextAsset asset, out CrowdSeatLayoutData layout, out string error)
    {
        layout = null;
        error = null;

        if (asset == null)
        {
            error = "No seat layout asset is assigned.";
            return false;
        }

        try
        {
            layout = JsonUtility.FromJson<CrowdSeatLayoutData>(asset.text);
        }
        catch (Exception exception)
        {
            error = $"JSON parse failed: {exception.Message}";
            return false;
        }

        if (layout == null)
        {
            error = "Parsed layout is null.";
            return false;
        }

        if (!layout.HasSeats)
        {
            error = "Layout does not contain any seats.";
            return false;
        }

        return true;
    }
}
