using UnityEngine;

/// <summary>Can one point see another across the town? Shared by the boat's lookout and by residents spotting the boat.</summary>
public static class LineOfSight
{
    /// <summary>
    /// True if nothing fixed stands between the two points. Things that float and move (the boat, the swimmer,
    /// drifting debris) are too small to hide a person, so they do not block the view; nor do tree trunks, whose
    /// colliders are on the Ignore Raycast layer (the boat bumps into them, eyes see past them).
    /// </summary>
    public static bool Clear(Vector3 from, Vector3 to)
    {
        if (!Physics.Linecast(from, to, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.attachedRigidbody != null;
    }
}
