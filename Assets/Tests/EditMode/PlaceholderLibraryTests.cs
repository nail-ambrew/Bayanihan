using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The stand-ins the placeholder map is built from: that every kind of landmark has a style, that every house is a real
/// baked prefab (with its body marker attached, one mesh, every material present), that the houses are house-sized and
/// come in every type, that the hall, the market and the school blocks are baked, and that the settings point at the
/// library and at a scene that is not Gameplay. A model dropped in later has to pass the same checks to be fitted.
/// </summary>
public class PlaceholderLibraryTests
{
    const string LibraryPath = "Assets/Settings/Maps/PlaceholderLibrary.asset";
    const string SettingsPath = "Assets/Settings/Maps/BarangayMockSettings.asset";

    static PlaceholderLibrary Library()
    {
        var lib = AssetDatabase.LoadAssetAtPath<PlaceholderLibrary>(LibraryPath);
        Assert.IsNotNull(lib, "there is no placeholder library at " + LibraryPath + ". Run Bayanihan > Make Placeholder Prefabs.");
        return lib;
    }

    // ------------------------------------------------------------------ measuring a model

    [Test]
    public void A_prefab_is_measured_from_its_meshes_or_from_its_body_marker()
    {
        var root = new GameObject("Model");
        try
        {
            root.transform.position = new Vector3(100f, 5f, -40f);          // where the root sits does not matter
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(root.transform, false);
            cube.transform.localScale = new Vector3(4f, 2f, 6f);
            cube.transform.localPosition = new Vector3(1f, 1f, 3f);

            Vector3 measured = PlaceholderLibrary.Measure(root);
            Assert.AreEqual(4f, measured.x, 0.01f, "width");
            Assert.AreEqual(2f, measured.y, 0.01f, "height");
            Assert.AreEqual(6f, measured.z, 0.01f, "depth");

            root.AddComponent<PlaceholderBody>().size = new Vector3(1f, 2f, 3f);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), PlaceholderLibrary.Measure(root), "a baked body says how big it is, eaves and all aside");
            Assert.AreEqual(Vector3.zero, PlaceholderLibrary.Measure(null));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    // ------------------------------------------------------------------ houses

    [Test]
    public void The_library_has_every_type_of_house()
    {
        var lib = Library();
        Assert.GreaterOrEqual(lib.houses.Count, 20, "the slum map has 24 houses");
        foreach (HouseType type in Enum.GetValues(typeof(HouseType)))
            Assert.IsTrue(lib.houses.Any(h => h.type == type), "no " + type);
        Assert.AreEqual(lib.houses.Count, lib.houses.Select(h => h.name).Distinct().Count(), "two houses share a name");
    }

    [Test]
    public void Every_house_is_a_baked_prefab_with_its_body_marker_one_mesh_and_every_material()
    {
        foreach (var h in Library().houses)
        {
            Assert.IsNotNull(h.prefab, h.name + " has no prefab");
            var body = h.prefab.GetComponent<PlaceholderBody>();
            Assert.IsNotNull(body, h.name + " has lost its body marker (a MISSING script on the prefab means the class and its file do not match)");
            Assert.Greater(body.size.sqrMagnitude, 1f, h.name + " has no size");
            Assert.AreEqual(body.size.x, h.size.x, 0.01f, h.name + " width in the library");
            Assert.AreEqual(body.size.z, h.size.z, 0.01f, h.name + " depth in the library");

            var filter = h.prefab.GetComponent<MeshFilter>();
            var renderer = h.prefab.GetComponent<MeshRenderer>();
            Assert.IsNotNull(filter, h.name + " has no mesh filter");
            Assert.IsNotNull(renderer, h.name + " has no renderer");
            Assert.Greater(filter.sharedMesh.vertexCount, 100, h.name + " has no mesh");
            Assert.AreEqual(filter.sharedMesh.subMeshCount, renderer.sharedMaterials.Length, h.name + ": one material for each submesh");
            foreach (var m in renderer.sharedMaterials) Assert.IsNotNull(m, h.name + " has a missing material");
            Assert.AreEqual(1, h.prefab.GetComponents<MonoBehaviour>().Length, h.name + " should carry only the body marker");
        }
    }

    [Test]
    public void Houses_are_house_sized_and_towers_are_taller_than_shops()
    {
        var lib = Library();
        foreach (var h in lib.houses)
        {
            Assert.That(h.size.x, Is.InRange(3f, 9f), h.name + " width");
            Assert.That(h.size.z, Is.InRange(4f, 9.5f), h.name + " depth");
            Assert.That(h.size.y, Is.InRange(3f, 15f), h.name + " height");
            Assert.GreaterOrEqual(h.storeys, 1, h.name + " storeys");
        }
        float tallestShop = lib.houses.Where(h => h.type == HouseType.Shop).Max(h => h.size.y);
        float shortestTower = lib.houses.Where(h => h.type == HouseType.Tower).Min(h => h.size.y);
        Assert.Less(tallestShop, shortestTower);
    }

    // ------------------------------------------------------------------ landmarks

    [Test]
    public void Every_kind_of_landmark_has_exactly_one_style()
    {
        var lib = Library();
        foreach (LandmarkCategory category in Enum.GetValues(typeof(LandmarkCategory)))
            Assert.AreEqual(1, lib.landmarks.Count(s => s.category == category), category + " should have one style");
    }

    [Test]
    public void The_hall_the_market_and_the_school_blocks_are_baked_models()
    {
        var lib = Library();

        var hall = lib.StyleFor(LandmarkCategory.BarangayHall);
        Assert.AreEqual(LandmarkPlacement.Prefab, hall.placement);
        Assert.IsNotNull(hall.model.prefab, "the hall model");
        Assert.That(hall.model.size.x, Is.InRange(20f, 32f), "the hall is about 26 m wide");

        var market = lib.StyleFor(LandmarkCategory.Market);
        Assert.AreEqual(LandmarkPlacement.Prefab, market.placement);
        Assert.IsNotNull(market.model.prefab, "the market model");
        Assert.Greater(market.model.size.x, 40f);

        var school = lib.StyleFor(LandmarkCategory.School);
        Assert.AreEqual(LandmarkPlacement.Parts, school.placement);
        Assert.AreEqual(3, school.parts.Count, "the three school blocks");
        foreach (var part in school.parts)
        {
            Assert.IsNotNull(part.prefab, part.name + " has no prefab");
            Assert.IsNotNull(part.prefab.GetComponent<PlaceholderBody>(), part.name + " has lost its body marker");
            Assert.Greater(part.size.x, 10f, part.name + " width");
        }
    }

    [Test]
    public void Shops_get_shop_houses_courts_get_a_roof_and_everything_else_is_a_block()
    {
        var lib = Library();
        foreach (var c in new[] { LandmarkCategory.Bank, LandmarkCategory.Grocery, LandmarkCategory.Pharmacy, LandmarkCategory.Pawnshop })
            Assert.AreEqual(LandmarkPlacement.ShopHouse, lib.StyleFor(c).placement, c.ToString());
        Assert.AreEqual(LandmarkPlacement.Court, lib.StyleFor(LandmarkCategory.Court).placement);
        foreach (var c in new[] { LandmarkCategory.Hospital, LandmarkCategory.Church, LandmarkCategory.Police, LandmarkCategory.FireStation, LandmarkCategory.BarangayOutpost })
            Assert.AreEqual(LandmarkPlacement.Block, lib.StyleFor(c).placement, c.ToString());
        foreach (var s in lib.landmarks)
        {
            Assert.GreaterOrEqual(s.blockHeight, 1f, s.category + " block height");
            Assert.Less(s.minScale, s.maxScale, s.category + " scale limits");
        }
    }

    // ------------------------------------------------------------------ the numbers

    [Test]
    public void Roads_are_widest_for_highways_and_water_for_rivers()
    {
        var lib = Library();
        var w = lib.roadWidths;
        Assert.Greater(w.trunk, w.secondary);
        Assert.Greater(w.secondary, w.tertiary);
        Assert.Greater(w.tertiary, w.residential);
        Assert.Greater(w.residential, w.service);
        Assert.Greater(w.service, w.footway);
        Assert.That(w.residential, Is.InRange(4f, 8f), "a residential street fits two cars");
        Assert.Greater(lib.WaterWidth(WaterwayKind.River), lib.WaterWidth(WaterwayKind.Stream));
        Assert.Greater(lib.WaterWidth(WaterwayKind.Stream), lib.WaterWidth(WaterwayKind.Ditch));
        Assert.Less(lib.minStretch, 1f);
        Assert.Greater(lib.maxStretch, 1f);
    }

    // ------------------------------------------------------------------ the settings

    [Test]
    public void The_settings_use_the_library_and_a_scene_of_their_own_never_Gameplay()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BarangayMockSettings>(SettingsPath);
        Assert.IsNotNull(settings, "there are no settings at " + SettingsPath + ". Run Bayanihan > Make Placeholder Prefabs.");
        Assert.AreEqual(Library(), settings.library, "the settings should point at the library");
        StringAssert.EndsWith(".unity", settings.scenePath);
        StringAssert.DoesNotContain("Gameplay", settings.scenePath, "the mock must never be built in the Gameplay scene");
        Assert.AreEqual(MockRegion.FocusArea, settings.region, "the default is the focus area");
        Assert.AreEqual(1f, settings.scale, "the default is real size");
        Assert.IsFalse(settings.colliders, "colliders are off unless asked for");
    }

    [Test]
    public void The_mock_scene_is_built_and_says_what_it_was_built_from()
    {
        var settings = AssetDatabase.LoadAssetAtPath<BarangayMockSettings>(SettingsPath);
        Assert.IsNotNull(settings);
        var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(settings.scenePath);
        Assert.IsNotNull(scene, "the mock scene is not at " + settings.scenePath + ". Run Bayanihan > Build Barangay Mock.");

        var bundle = AssetDatabase.LoadAssetAtPath<MockMeshBundle>(settings.generatedFolder + "/BarangayMock_Meshes.asset");
        Assert.IsNotNull(bundle, "the generated meshes are missing. Run Bayanihan > Build Barangay Mock.");
        var meshes = AssetDatabase.LoadAllAssetRepresentationsAtPath(settings.generatedFolder + "/BarangayMock_Meshes.asset").OfType<Mesh>().ToList();
        Assert.Greater(meshes.Count, 10, "roads, ground, blocks and so on");
        foreach (var m in meshes) Assert.Greater(m.vertexCount, 0, m.name + " is empty");
    }
}
