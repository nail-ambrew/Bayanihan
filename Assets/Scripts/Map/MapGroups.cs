using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// How the built Mabuhay map is sorted under its root (BarangayMock), by what things are (MapFinisher, the last step of the
/// build). Paths are for <c>Transform.Find</c> on the root.
/// </summary>
public static class MapGroups
{
    public const string Terrain = "Terrain";
    public const string Roads = "Roads";
    public const string Residential = "Residential";
    public const string Commercial = "Commercial";
    public const string Landmarks = "Landmarks";

    /// <summary>The ground's chunks.</summary>
    public const string Ground = Terrain + "/Ground";
    /// <summary>The concrete under houses on a slope.</summary>
    public const string Foundations = Terrain + "/Foundations";
    public const string StreetFurniture = Roads + "/Street Furniture";
    public const string CompoundWalls = Landmarks + "/Compound Walls";

    /// <summary>Every house and shop of the rows, residential and commercial, in that order.</summary>
    public static List<Transform> Houses(Transform root)
    {
        var list = new List<Transform>();
        foreach (var name in new[] { Residential, Commercial })
        {
            var g = root.Find(name);
            if (g == null) continue;
            foreach (Transform t in g) if (t.name.StartsWith("House ")) list.Add(t);
        }
        return list;
    }
}
