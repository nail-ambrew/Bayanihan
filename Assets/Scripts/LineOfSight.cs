using UnityEngine;

/// <summary>Can one point see another across the town? Shared by the boat's lookout and by residents spotting the boat.</summary>
public static class LineOfSight
{
    /// <summary>
    /// True if nothing fixed stands between the two points. Things that float and move (the boat, the swimmer,
    /// drifting debris) are too small to hide a person, so they do not block the view.
    /// </summary>
    public static bool Clear(Vector3 from, Vector3 to)
    {
        if (!Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.attachedRigidbody != null;
    }
}
