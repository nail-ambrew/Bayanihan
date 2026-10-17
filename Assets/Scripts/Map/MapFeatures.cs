using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>What kind of road a line is, from a footpath up to a national highway. Ordered from minor to major, so a
/// filter can keep everything at or above a class.</summary>
public enum RoadClass
{
    /// <summary>Footpaths, steps and alleys too narrow for a vehicle.</summary>
    Footway,
    /// <summary>Unpaved farm or construction tracks.</summary>
    Track,
    /// <summary>Driveways, parking aisles and lanes inside compounds.</summary>
    Service,
    /// <summary>Streets inside the barangay and its subdivisions.</summary>
    Residential,
    /// <summary>Minor through roads, such as estate access roads.</summary>
    Unclassified,
    Tertiary,
    Secondary,
    Primary,
    /// <summary>National highways: Governor's Drive.</summary>
    Trunk
}

/// <summary>What kind of water a line is. Ordered from minor to major, like RoadClass.</summary>
public enum WaterwayKind { Ditch, Drain, Stream, Canal, River }

public enum AreaKind { Residential, Commercial, Industrial, Institutional, Green, Farmland, Cemetery, Water, Other }

/// <summary>What a place is, as far as a flood response is concerned.</summary>
public enum LandmarkCategory
{
    BarangayHall,
    CityHall,
    /// <summary>A purpose-built evacuation centre.</summary>
    EvacuationCentre,
    School,
    /// <summary>A basketball court or similar, often roofed: a common evacuation site.</summary>
    Court,
    /// <summary>A gymnasium or a large covered court.</summary>
    SportsHall,
    CommunityCentre,
    Hospital,
    HealthCentre,
    Pharmacy,
    FireStation,
    Police,
    /// <summary>The barangay outpost: where the tanod keep watch and a flood response checks in.</summary>
    BarangayOutpost,
    Government,
    /// <summary>Water tanks, towers and the water district.</summary>
    WaterSupply,
    Dam,
    AssemblyPoint,
    /// <summary>A gate kept for emergency vehicles.</summary>
    EmergencyAccess,
    Market,
    /// <summary>Supermarkets, groceries and convenience stores: where supplies are.</summary>
    Grocery,
    Bank,
    Pawnshop,
    Fuel,
    Church,
    Other
}

/// <summary>Whether a place is meant to take in evacuees.</summary>
public enum EvacuationRole
{
    None,
    /// <summary>Likely, from the sources given, but nobody official has said so.</summary>
    Candidate,
    /// <summary>Named by the barangay or Carmona CDRRMO as an evacuation site.</summary>
    Designated
}

/// <summary>A closed outline. The last point joins back to the first; it is not repeated.</summary>
[Serializable]
public class MapRing
{
    public List<Vector2> points = new List<Vector2>();
}

[Serializable]
public class MapRoad
{
    [Tooltip("OpenStreetMap id (w = way). A road cut in two by the clip gets .1, .2 on the end.")]
    public string osmId;
    public string name;
    public RoadClass roadClass;
    public bool oneWay;
    [Tooltip("The road crosses water or a gully here: a place a flood can cut off.")]
    public bool bridge;
    [Tooltip("Centre line, in real metres east (x) and north (y) of the layout's origin.")]
    public List<Vector2> points = new List<Vector2>();
}

[Serializable]
public class MapWaterway
{
    public string osmId;
    public string name;
    public WaterwayKind kind;
    [Tooltip("Centre line, in real metres east (x) and north (y) of the layout's origin.")]
    public List<Vector2> points = new List<Vector2>();
}

[Serializable]
public class MapArea
{
    public string osmId;
    public string name;
    public AreaKind kind;
    [Tooltip("Outline, in real metres east (x) and north (y) of the layout's origin.")]
    public List<Vector2> outline = new List<Vector2>();
}

/// <summary>
/// One place on the map that matters when the water comes: where people can shelter, who can help, what can fail.
/// </summary>
[Serializable]
public class MapLandmark
{
    [Tooltip("OpenStreetMap id (n = node, w = way, r = relation), or x-... for a place OpenStreetMap does not have.")]
    public string id;
    public string name;
    public LandmarkCategory category;
    [Tooltip("False for a place we know exists but not where (it has no position, and is not drawn).")]
    public bool positionKnown = true;
    [Tooltip("Real metres east (x) and north (y) of the layout's origin.")]
    public Vector2 position;
    [Tooltip("The building's outline, if OpenStreetMap has one. Real metres.")]
    public List<Vector2> footprint = new List<Vector2>();
    [Tooltip("Inside the barangay's boundary. Worked out from the boundary when the layout is rebuilt.")]
    public bool insideBarangay;

    [Header("Evacuation")]
    public EvacuationRole evacuationRole;
    [Tooltip("Confirmed by the barangay or Carmona CDRRMO. Until then a site is only a candidate.")]
    public bool verified;
    [Tooltip("Where the evacuation role comes from.")]
    [TextArea(1, 4)] public string source;
    [TextArea(1, 3)] public string note;
    [Tooltip("The OpenStreetMap tag it was found by, e.g. amenity=school.")]
    public string osmTag;

    public bool IsEvacuationSite => evacuationRole != EvacuationRole.None;

    /// <summary>A place a flood response leans on: shelter, command, rescue, health or water.</summary>
    public bool Essential => IsEvacuationSite || IsEssential(category);

    /// <summary>A shop or service a level will want as a landmark, though a flood response does not lean on it.</summary>
    public bool Business =>
        category == LandmarkCategory.Grocery || category == LandmarkCategory.Bank ||
        category == LandmarkCategory.Pawnshop || category == LandmarkCategory.Market;

    /// <summary>The name, or what it is when OpenStreetMap gives it no name ("Court", "Water supply").</summary>
    public string Label
    {
        get
        {
            if (!string.IsNullOrEmpty(name)) return name;
            string what = System.Text.RegularExpressions.Regex.Replace(category.ToString(), "(?<=[a-z])([A-Z])", " $1");
            return what.Substring(0, 1) + what.Substring(1).ToLowerInvariant();
        }
    }

    public static bool IsEssential(LandmarkCategory category)
    {
        switch (category)
        {
            case LandmarkCategory.BarangayHall:
            case LandmarkCategory.CityHall:
            case LandmarkCategory.EvacuationCentre:
            case LandmarkCategory.School:
            case LandmarkCategory.Court:
            case LandmarkCategory.SportsHall:
            case LandmarkCategory.CommunityCentre:
            case LandmarkCategory.Hospital:
            case LandmarkCategory.HealthCentre:
            case LandmarkCategory.FireStation:
            case LandmarkCategory.Police:
            case LandmarkCategory.BarangayOutpost:
            case LandmarkCategory.WaterSupply:
            case LandmarkCategory.Dam:
            case LandmarkCategory.AssemblyPoint:
            case LandmarkCategory.EmergencyAccess:
                return true;
            default:
                return false;
        }
    }
}
