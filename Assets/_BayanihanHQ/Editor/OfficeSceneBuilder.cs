using System.Collections.Generic;
using System.IO;
using TMPro;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BayanihanHQ.Editor
{
    /// <summary>
    /// Builds Assets/_BayanihanHQ/Scenes/BayanihanHQ.unity from code: a low-poly Barangay Mabuhay DRRM operations centre
    /// with ten desks, the map table, the whiteboard, the PAGASA signal board and a kubo / sari-sari break corner. Like the
    /// game's own scenes it is rebuilt, not hand-edited. It is never added to Build Settings.
    ///
    /// Layout (metres, +Z is the back wall, the camera looks in from the front left):
    ///   back wall z = 8: whiteboard (left), DRRM banner over the lead's big desk, signal board (right), door (far right)
    ///   right wall x = 12: capiz windows
    ///   desks face the back wall so their monitors read from the camera; the map table is in the middle
    ///   break corner front right (kubo, sari-sari counter, monobloc chairs)
    /// </summary>
    public static class OfficeSceneBuilder
    {
        const string Root = "Assets/_BayanihanHQ";
        const string MatDir = Root + "/Generated/Materials";

        /// <summary>Desk slot → floor position of the desk centre. Slot 0 is the lead's big desk.</summary>
        static readonly Vector3[] DeskSpots =
        {
            new Vector3(0f, 0, 5.4f),
            new Vector3(-8.5f, 0, 3.4f), new Vector3(-4.8f, 0, 3.4f), new Vector3(4.8f, 0, 3.4f), new Vector3(8.5f, 0, 3.4f),
            new Vector3(-8.5f, 0, -0.6f), new Vector3(8.5f, 0, -0.6f),
            new Vector3(-8.5f, 0, -4.4f), new Vector3(-4.8f, 0, -4.4f), new Vector3(4.8f, 0, -4.4f),
        };

        static readonly Vector3[] BreakSpots =
        {
            new Vector3(7.6f, 0, -5.4f), new Vector3(9.4f, 0, -5.2f), new Vector3(10.6f, 0, -6.6f),
            new Vector3(8.2f, 0, -7.1f), new Vector3(10.4f, 0, -3.7f), new Vector3(6.6f, 0, -7.0f),
        };

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        public static void Build()
        {
            mats.Clear();
            Directory.CreateDirectory(MatDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Lighting();
            var hq = new GameObject("Bayanihan HQ");
            var director = hq.AddComponent<HqDirector>();
            var debug = hq.AddComponent<HqDebugPanel>();
            debug.visible = false;
            hq.AddComponent<HqOverlay>();

            var room = new GameObject("Office").transform;
            room.SetParent(hq.transform, false);
            var office = room.gameObject.AddComponent<HqOffice>();

            Shell(room);
            office.door = Marker("Door", room, new Vector3(10.5f, 0, 7.1f), Quaternion.Euler(0, 180, 0));
            var desks = new List<DeskStation>();
            for (int i = 0; i < DeskSpots.Length; i++) desks.Add(Desk(room, i, DeskSpots[i], big: i == 0));
            office.desks = desks.ToArray();
            office.mapTable = MapTableAt(room, new Vector3(0, 0, 0.4f));
            WhiteboardAt(room, new Vector3(-5.6f, 1.7f, 7.86f));
            SignalBoardAt(room, new Vector3(5.6f, 1.75f, 7.86f));
            Banner(room);
            BreakCorner(room);
            var spots = new List<Transform>();
            foreach (var p in BreakSpots) spots.Add(Marker("Break Spot", room, p, Quaternion.identity));
            office.breakSpots = spots.ToArray();
            office.waitingPing = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/UI/question_002.ogg");
            Props(room);

            Camera();
            BakeNavMesh(room.gameObject);

            Directory.CreateDirectory(Root + "/Scenes");
            EditorSceneManager.SaveScene(scene, HqMenu.ScenePath);
            // Save only the HQ's own assets. AssetDatabase.SaveAssets would also write anything else Unity happens to
            // hold dirty, such as a teammate's font atlas, and the HQ must never touch the game's files.
            foreach (var m in mats.Values) AssetDatabase.SaveAssetIfDirty(m);
            foreach (var guid in AssetDatabase.FindAssets("", new[] { Root + "/Generated" }))
            {
                var asset = AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
            }
            AssetDatabase.Refresh();
            Debug.Log("[Bayanihan HQ] Built " + HqMenu.ScenePath + " (not in Build Settings).");
        }

        // ---------------------------------------------------------------- room

        static void Lighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.58f, 0.52f);
            RenderSettings.ambientEquatorColor = new Color(0.46f, 0.38f, 0.30f);
            RenderSettings.ambientGroundColor = new Color(0.22f, 0.17f, 0.12f);
            var sun = new GameObject("Afternoon Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.86f, 0.66f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.65f;
            sun.transform.rotation = Quaternion.Euler(42, -62, 0);
        }

        static void Shell(Transform room)
        {
            var wood = Mat("Floor Planks", new Color(0.62f, 0.44f, 0.28f), 0.3f);
            var plywood = Mat("Plywood Wall", new Color(0.93f, 0.83f, 0.66f));
            var wainscot = Mat("Wainscot", new Color(0.55f, 0.33f, 0.18f));
            var trim = Mat("Dark Trim", new Color(0.30f, 0.19f, 0.11f));

            var floor = Box("Floor", room, new Vector3(0, -0.1f, 0), new Vector3(24.4f, 0.2f, 16.4f), wood, keepCollider: true);
            floor.isStatic = true;
            // A few darker boards so the floor reads as planks from above.
            for (int i = -11; i <= 11; i += 2) Box("Board Seam", room, new Vector3(i + 0.5f, 0.001f, 0), new Vector3(0.03f, 0.002f, 16.2f), trim);

            // Back wall with a door gap at x 9.6..11.4, and the right wall. The front and left stay open (cutaway).
            const float h = 3.2f;
            Box("Back Wall", room, new Vector3(-1.2f, h / 2, 8.1f), new Vector3(21.6f, h, 0.2f), plywood);
            Box("Back Wall", room, new Vector3(11.75f, h / 2, 8.1f), new Vector3(0.7f, h, 0.2f), plywood);
            Box("Lintel", room, new Vector3(10.5f, 2.75f, 8.1f), new Vector3(1.8f, 0.9f, 0.2f), plywood);
            Box("Door Frame", room, new Vector3(9.55f, 1.15f, 8.0f), new Vector3(0.12f, 2.3f, 0.3f), trim);
            Box("Door Frame", room, new Vector3(11.45f, 1.15f, 8.0f), new Vector3(0.12f, 2.3f, 0.3f), trim);
            Box("Door Frame", room, new Vector3(10.5f, 2.3f, 8.0f), new Vector3(2.0f, 0.12f, 0.3f), trim);
            Box("Outside", room, new Vector3(10.5f, 1.15f, 8.6f), new Vector3(2f, 2.3f, 0.05f), Mat("Daylight", new Color(1f, 0.95f, 0.8f), 0, new Color(1f, 0.9f, 0.7f) * 1.2f));
            Box("Right Wall", room, new Vector3(12.1f, h / 2, 0), new Vector3(0.2f, h, 16.4f), plywood);
            Box("Wainscot Back", room, new Vector3(-1.2f, 0.5f, 7.99f), new Vector3(21.6f, 1.0f, 0.03f), wainscot);
            Box("Wainscot Right", room, new Vector3(11.99f, 0.5f, 0), new Vector3(0.03f, 1.0f, 16.2f), wainscot);
            Box("Rail Back", room, new Vector3(-1.2f, 1.02f, 7.97f), new Vector3(21.6f, 0.06f, 0.06f), trim);
            Box("Rail Right", room, new Vector3(11.97f, 1.02f, 0), new Vector3(0.06f, 0.06f, 16.2f), trim);
            Box("Kick Front", room, new Vector3(0, 0.06f, -8.15f), new Vector3(24.4f, 0.12f, 0.1f), trim);
            Box("Kick Left", room, new Vector3(-12.15f, 0.06f, 0), new Vector3(0.1f, 0.12f, 16.4f), trim);

            // Capiz shell windows: a grid of small pearly panes that glow with the afternoon light.
            var capiz = Mat("Capiz", new Color(0.98f, 0.95f, 0.86f), 0.6f, new Color(1f, 0.92f, 0.75f) * 0.9f);
            foreach (float z in new[] { -4.5f, 0f, 4.5f })
            {
                Box("Window Frame", room, new Vector3(11.98f, 1.9f, z), new Vector3(0.06f, 1.5f, 2.3f), trim);
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 6; col++)
                        Box("Capiz", room, new Vector3(11.94f, 1.33f + row * 0.36f, z - 0.95f + col * 0.38f), new Vector3(0.02f, 0.32f, 0.34f), capiz);
            }
        }

        static void Banner(Transform room)
        {
            var navy = Mat("Banner Navy", new Color(0.10f, 0.16f, 0.36f));
            var gold = Mat("Banner Gold", new Color(0.98f, 0.78f, 0.22f), 0.2f);
            var b = Box("DRRM Banner", room, new Vector3(0, 2.62f, 7.97f), new Vector3(6.4f, 0.8f, 0.04f), navy);
            Box("Banner Trim", room, new Vector3(0, 2.23f, 7.95f), new Vector3(6.4f, 0.05f, 0.03f), gold);
            var anchor = Anchor("Banner Text", room, new Vector3(0, 2.62f, 7.94f));
            Text(anchor, "BARANGAY MABUHAY DRRM OPERATIONS CENTER\n<size=55%>BAYANIHAN HQ · CARMONA, CAVITE</size>", 2.6f, new Color(0.99f, 0.86f, 0.35f), new Vector2(6.2f, 0.75f), heading: true);
        }

        // ---------------------------------------------------------------- desks

        static DeskStation Desk(Transform room, int slot, Vector3 at, bool big)
        {
            var wood = Mat("Desk Wood", new Color(0.74f, 0.55f, 0.36f), 0.35f);
            var metal = Mat("Desk Metal", new Color(0.25f, 0.24f, 0.23f), 0.5f);
            var fabric = Mat("Chair Fabric", new Color(0.20f, 0.22f, 0.26f));
            var bezel = Mat("Monitor Bezel", new Color(0.09f, 0.09f, 0.1f), 0.6f);
            var plate = Mat("Nameplate", new Color(0.97f, 0.93f, 0.84f));
            var bulb = Mat("Lamp Bulb", new Color(1f, 0.9f, 0.7f), 0.2f, new Color(1f, 0.85f, 0.6f));

            var desk = new GameObject(big ? "Desk 0 (Lead)" : "Desk " + slot).transform;
            desk.SetParent(room, false);
            desk.localPosition = at;
            float w = big ? 2.6f : 1.5f;
            float d = big ? 0.95f : 0.75f;

            Box("Top", desk, new Vector3(0, 0.74f, 0), new Vector3(w, 0.05f, d), wood);
            Box("Side", desk, new Vector3(-w / 2 + 0.04f, 0.36f, 0), new Vector3(0.05f, 0.72f, d - 0.05f), metal);
            Box("Side", desk, new Vector3(w / 2 - 0.04f, 0.36f, 0), new Vector3(0.05f, 0.72f, d - 0.05f), metal);
            Box("Modesty Panel", desk, new Vector3(0, 0.45f, d / 2 - 0.04f), new Vector3(w - 0.1f, 0.5f, 0.03f), metal);
            if (big) Box("Drawers", desk, new Vector3(0.85f, 0.36f, 0), new Vector3(0.5f, 0.72f, d - 0.1f), wood);

            // Monitor: stand, bezel, and a screen quad facing the chair (-Z) that the station tints and writes on.
            Box("Monitor Stand", desk, new Vector3(0, 0.83f, 0.2f), new Vector3(0.08f, 0.16f, 0.08f), metal);
            Box("Monitor Bezel", desk, new Vector3(0, 1.1f, 0.2f), new Vector3(0.68f, 0.44f, 0.04f), bezel);
            var screen = Quad("Screen", desk, new Vector3(0, 1.1f, 0.177f), new Vector2(0.62f, 0.38f), Quaternion.identity,
                Mat("Screen", new Color(0.08f, 0.12f, 0.16f), 0.8f, new Color(0.05f, 0.08f, 0.1f)));
            var screenAnchor = Anchor("Screen Text", desk, new Vector3(0, 1.1f, 0.172f));
            if (big)
            {
                Box("Monitor Bezel 2", desk, new Vector3(-0.78f, 1.1f, 0.15f), new Vector3(0.68f, 0.44f, 0.04f), bezel, rotation: Quaternion.Euler(0, -18, 0));
                Box("Monitor Stand 2", desk, new Vector3(-0.78f, 0.83f, 0.15f), new Vector3(0.08f, 0.16f, 0.08f), metal);
                Quad("Screen 2", desk, new Vector3(-0.776f, 1.1f, 0.128f), new Vector2(0.62f, 0.38f), Quaternion.Euler(0, -18, 0),
                    Mat("Screen Map", new Color(0.15f, 0.35f, 0.3f), 0.8f, new Color(0.1f, 0.3f, 0.25f) * 0.8f));
            }
            Box("Keyboard", desk, new Vector3(0, 0.775f, -0.12f), new Vector3(0.44f, 0.02f, 0.14f), bezel);
            Cyl("Mug", desk, new Vector3(-0.5f, 0.82f, -0.05f), new Vector3(0.08f, 0.05f, 0.08f), Mat("Mug", new Color(0.95f, 0.95f, 0.92f)));
            Box("Folder", desk, new Vector3(0.42f, 0.775f, 0.05f), new Vector3(0.24f, 0.02f, 0.32f), Mat("Manila Folder", new Color(0.93f, 0.8f, 0.5f)), rotation: Quaternion.Euler(0, 12, 0));

            // Nameplate on a little easel at the desk's front right, readable from the camera side.
            var plateBox = Box("Nameplate", desk, new Vector3(w / 2 - 0.36f, 0.86f, -d / 2 + 0.08f), new Vector3(0.66f, 0.2f, 0.03f), plate, rotation: Quaternion.Euler(-12, 0, 0));
            var stripe = Box("Nameplate Stripe", desk, new Vector3(w / 2 - 0.36f, 0.76f, -d / 2 + 0.06f), new Vector3(0.66f, 0.035f, 0.035f), Mat("Stripe Default", Color.gray));
            var nameAnchor = Anchor("Nameplate Text", desk, new Vector3(w / 2 - 0.36f, 0.862f, -d / 2 + 0.06f));
            nameAnchor.localRotation = Quaternion.Euler(-12, 0, 0);

            // Desk lamp with its own small point light (it turns yellow or red with the responder's status).
            Cyl("Lamp Base", desk, new Vector3(-w / 2 + 0.2f, 0.78f, 0.22f), new Vector3(0.14f, 0.015f, 0.14f), metal);
            Box("Lamp Arm", desk, new Vector3(-w / 2 + 0.2f, 0.98f, 0.18f), new Vector3(0.025f, 0.4f, 0.025f), metal, rotation: Quaternion.Euler(-12, 0, 0));
            var bulbGo = Sphere("Lamp Bulb", desk, new Vector3(-w / 2 + 0.2f, 1.17f, 0.1f), 0.09f, bulb);
            var lamp = new GameObject("Lamp Light", typeof(Light)).GetComponent<Light>();
            lamp.transform.SetParent(desk, false);
            lamp.transform.localPosition = new Vector3(-w / 2 + 0.2f, 1.12f, 0.02f);
            lamp.type = LightType.Point;
            lamp.range = 2.6f;
            lamp.intensity = 1f;
            lamp.color = new Color(1f, 0.8f, 0.55f);
            lamp.shadows = LightShadows.None;

            // Chair on the camera side; the responder sits facing the monitor (+Z).
            var chairZ = -d / 2 - 0.42f;
            Box("Chair Seat", desk, new Vector3(0, 0.44f, chairZ), new Vector3(0.48f, 0.07f, 0.46f), fabric);
            Box("Chair Back", desk, new Vector3(0, 0.78f, chairZ - 0.24f), new Vector3(0.46f, 0.55f, 0.06f), fabric, rotation: Quaternion.Euler(-8, 0, 0));
            Cyl("Chair Post", desk, new Vector3(0, 0.22f, chairZ), new Vector3(0.06f, 0.2f, 0.06f), metal);
            Box("Chair Foot", desk, new Vector3(0, 0.03f, chairZ), new Vector3(0.55f, 0.04f, 0.06f), metal);
            Box("Chair Foot", desk, new Vector3(0, 0.03f, chairZ), new Vector3(0.06f, 0.04f, 0.55f), metal);
            var seat = Marker("Seat", desk, new Vector3(0, 0, chairZ + 0.02f), Quaternion.identity, local: true);

            var station = desk.gameObject.AddComponent<DeskStation>();
            station.slot = slot;
            station.seat = seat;
            station.seatHeight = 0.48f;
            station.screen = screen.GetComponent<Renderer>();
            station.screenAnchor = screenAnchor;
            station.screenSize = new Vector2(0.58f, 0.35f);
            station.nameplateAnchor = nameAnchor;
            station.nameplateStripe = stripe.GetComponent<Renderer>();
            station.lamp = lamp;
            station.lampBulb = bulbGo.GetComponent<Renderer>();
            if (big) station.screenSize = new Vector2(0.58f, 0.35f);
            plateBox.name = "Nameplate";
            return station;
        }

        // ---------------------------------------------------------------- features

        static Transform MapTableAt(Transform room, Vector3 at)
        {
            var wood = Mat("Table Wood", new Color(0.48f, 0.31f, 0.18f), 0.3f);
            var t = new GameObject("Map Table").transform;
            t.SetParent(room, false);
            t.localPosition = at;
            Box("Table Top", t, new Vector3(0, 0.86f, 0), new Vector3(4.4f, 0.1f, 3.4f), wood);
            Box("Table Rim", t, new Vector3(0, 0.92f, 0), new Vector3(4.5f, 0.03f, 3.5f), Mat("Dark Trim", new Color(0.30f, 0.19f, 0.11f)));
            foreach (var x in new[] { -1.9f, 1.9f })
                foreach (var z in new[] { -1.4f, 1.4f })
                    Box("Table Leg", t, new Vector3(x, 0.4f, z), new Vector3(0.12f, 0.8f, 0.12f), wood);
            var map = Quad("Map", t, new Vector3(0, 0.94f, 0), new Vector2(3.2f, 3.2f), Quaternion.Euler(90, 0, 0), Mat("Map Paper", new Color(0.93f, 0.86f, 0.70f), 0.1f));
            var table = t.gameObject.AddComponent<MapTable>();
            table.top = map.GetComponent<Renderer>();
            // Pins, a radio and a coffee cup: an operations table, not a museum piece.
            foreach (var (x, z, c) in new[] { (-1.0f, 0.6f, new Color(0.85f, 0.2f, 0.15f)), (0.7f, -0.4f, new Color(0.15f, 0.55f, 0.2f)), (0.2f, 1.1f, new Color(0.95f, 0.75f, 0.2f)) })
                Cyl("Pin", t, new Vector3(x, 1.0f, z), new Vector3(0.06f, 0.06f, 0.06f), Mat("Pin " + ColorUtility.ToHtmlStringRGB(c), c));
            PlaceProp("Assets/Cutscenes/Loadout/Models/Item_Radio.glb", t, new Vector3(1.9f, 0.91f, -1.2f), 0.3f, Quaternion.Euler(0, -30, 0));
            return t;
        }

        static void WhiteboardAt(Transform room, Vector3 at)
        {
            var frame = Mat("Aluminium", new Color(0.72f, 0.74f, 0.76f), 0.6f);
            var white = Mat("Whiteboard", new Color(0.97f, 0.97f, 0.95f), 0.7f);
            var t = new GameObject("Whiteboard").transform;
            t.SetParent(room, false);
            t.localPosition = at;
            Box("Frame", t, Vector3.zero, new Vector3(3.7f, 2.1f, 0.05f), frame);
            Box("Surface", t, new Vector3(0, 0, -0.03f), new Vector3(3.55f, 1.95f, 0.01f), white);
            Box("Marker Tray", t, new Vector3(0, -1.08f, -0.06f), new Vector3(3.0f, 0.04f, 0.1f), frame);
            foreach (var (x, c) in new[] { (-0.4f, new Color(0.1f, 0.2f, 0.6f)), (-0.25f, new Color(0.8f, 0.15f, 0.1f)), (-0.1f, new Color(0.1f, 0.5f, 0.15f)) })
                Box("Marker", t, new Vector3(x, -1.05f, -0.07f), new Vector3(0.12f, 0.025f, 0.025f), Mat("Marker " + ColorUtility.ToHtmlStringRGB(c), c));
            var surface = Anchor("Board Text", t, new Vector3(0, 0, -0.04f));
            var wb = t.gameObject.AddComponent<Whiteboard>();
            wb.surface = surface;
            wb.size = new Vector2(3.35f, 1.85f);
        }

        static void SignalBoardAt(Transform room, Vector3 at)
        {
            var t = new GameObject("PAGASA Signal Board").transform;
            t.SetParent(room, false);
            t.localPosition = at;
            var backing = Box("Backing", t, Vector3.zero, new Vector3(1.8f, 2.0f, 0.06f), Mat("Signal Blue", new Color(0.1f, 0.2f, 0.45f)));
            Box("Frame", t, new Vector3(0, 0, 0.02f), new Vector3(1.92f, 2.12f, 0.04f), Mat("Signal Frame", new Color(0.95f, 0.95f, 0.95f)));
            // The pennant on top: PAGASA's warning flags are red triangles.
            Box("Pennant", t, new Vector3(0, 1.22f, -0.01f), new Vector3(0.4f, 0.4f, 0.03f), Mat("Pennant Red", new Color(0.85f, 0.15f, 0.12f)), rotation: Quaternion.Euler(0, 0, 45));
            var lamps = new List<Renderer>();
            for (int i = 0; i < 5; i++)
                lamps.Add(Sphere("Lamp " + (i + 1), t, new Vector3(-0.6f + i * 0.3f, -0.8f, -0.05f), 0.16f, Mat("Signal Lamp", new Color(0.2f, 0.18f, 0.16f), 0.5f, Color.black)).GetComponent<Renderer>());
            var face = Anchor("Signal Text", t, new Vector3(0, 0.1f, -0.04f));
            var board = t.gameObject.AddComponent<SignalBoard>();
            board.face = face;
            board.lamps = lamps.ToArray();
            board.backing = backing.GetComponent<Renderer>();
        }

        static void BreakCorner(Transform room)
        {
            var bamboo = Mat("Bamboo", new Color(0.78f, 0.66f, 0.38f), 0.3f);
            var nipa = Mat("Nipa Thatch", new Color(0.62f, 0.48f, 0.26f));
            var k = new GameObject("Kubo Lounge").transform;
            k.SetParent(room, false);
            k.localPosition = new Vector3(8.9f, 0, -5.9f);
            foreach (var x in new[] { -1.5f, 1.5f })
                foreach (var z in new[] { -1.1f, 1.1f })
                    Cyl("Bamboo Post", k, new Vector3(x, 1.15f, z), new Vector3(0.12f, 1.15f, 0.12f), bamboo);
            var roof = new GameObject("Nipa Roof", typeof(MeshFilter), typeof(MeshRenderer));
            roof.transform.SetParent(k, false);
            roof.transform.localPosition = new Vector3(0, 2.3f, 0);
            roof.GetComponent<MeshFilter>().sharedMesh = Pyramid(3.8f, 3.0f, 1.1f);
            roof.GetComponent<MeshRenderer>().sharedMaterial = nipa;
            Box("Papag", k, new Vector3(0, 0.22f, 0.3f), new Vector3(2.2f, 0.08f, 0.9f), bamboo);
            foreach (var x in new[] { -1f, 1f }) Box("Papag Leg", k, new Vector3(x, 0.1f, 0.3f), new Vector3(0.08f, 0.2f, 0.8f), bamboo);
            Cyl("Low Table", k, new Vector3(0, 0.35f, -0.6f), new Vector3(0.7f, 0.04f, 0.7f), bamboo);
            Cyl("Thermos", k, new Vector3(0.15f, 0.48f, -0.6f), new Vector3(0.12f, 0.1f, 0.12f), Mat("Thermos Red", new Color(0.8f, 0.2f, 0.15f)));
            Sphere("Pandesal Basket", k, new Vector3(-0.15f, 0.42f, -0.55f), 0.22f, Mat("Basket", new Color(0.7f, 0.5f, 0.25f)));
            PlaceProp("Assets/Cutscenes/Intro/Prefabs/Rubbish_PlasticChair.prefab", k, new Vector3(-1.1f, 0, -1.5f), 0.85f, Quaternion.Euler(0, 30, 0));
            PlaceProp("Assets/Cutscenes/Intro/Prefabs/Rubbish_PlasticChair.prefab", k, new Vector3(0.9f, 0, -1.6f), 0.85f, Quaternion.Euler(0, -25, 0));

            // Sari-sari counter against the right wall: wire-mesh front, shelves of colourful goods, snacks hanging in strips.
            var s = new GameObject("Sari-sari Corner").transform;
            s.SetParent(room, false);
            s.localPosition = new Vector3(11.2f, 0, -2.9f);
            s.localRotation = Quaternion.Euler(0, -90, 0);
            var counterWood = Mat("Counter Wood", new Color(0.55f, 0.36f, 0.2f));
            Box("Counter", s, new Vector3(0, 0.5f, 0), new Vector3(2.0f, 1.0f, 0.6f), counterWood);
            Box("Shelf Back", s, new Vector3(0, 1.4f, 0.55f), new Vector3(2.0f, 1.6f, 0.08f), counterWood);
            for (int row = 0; row < 3; row++)
            {
                Box("Shelf", s, new Vector3(0, 0.95f + row * 0.45f, 0.4f), new Vector3(2.0f, 0.04f, 0.3f), counterWood);
                for (int i = 0; i < 9; i++)
                {
                    var c = Color.HSVToRGB(((i * 37 + row * 91) % 100) / 100f, 0.65f, 0.95f);
                    Box("Goods", s, new Vector3(-0.88f + i * 0.22f, 1.07f + row * 0.45f, 0.4f), new Vector3(0.16f, 0.2f, 0.16f), Mat("Goods " + (i + row * 9) % 7, c));
                }
            }
            for (int i = 0; i < 8; i++)
                Box("Snack Strip", s, new Vector3(-0.85f + i * 0.24f, 2.05f, -0.25f), new Vector3(0.12f, 0.6f, 0.01f), Mat("Snack " + i % 4, Color.HSVToRGB((i * 0.23f) % 1f, 0.7f, 1f)));
            Box("Sign", s, new Vector3(0, 2.55f, -0.05f), new Vector3(2.0f, 0.36f, 0.05f), Mat("Sign Yellow", new Color(0.98f, 0.82f, 0.2f)));
            var signText = Anchor("Sign Text", s, new Vector3(0, 2.55f, -0.085f));
            Text(signText, "SARI-SARI STORE", 2.2f, new Color(0.7f, 0.12f, 0.1f), new Vector2(1.9f, 0.33f), heading: true);
        }

        static void Props(Transform room)
        {
            PlaceProp("Assets/Cutscenes/Intro/Prefabs/Prop_RescueGear.prefab", room, new Vector3(8.4f, 0, 7.3f), 1.2f, Quaternion.Euler(0, 180, 0));
            PlaceProp("Assets/Cutscenes/Loadout/Models/Item_Lifebuoy.glb", room, new Vector3(11.92f, 1.8f, 2.25f), 0.7f, Quaternion.Euler(0, -90, 90));
            PlaceProp("Assets/Cutscenes/Loadout/Models/Item_FirstAid.glb", room, new Vector3(-11.4f, 0.95f, 7.4f), 0.45f, Quaternion.identity);
            Box("Supply Cabinet", room, new Vector3(-11.3f, 0.45f, 7.4f), new Vector3(1.2f, 0.9f, 0.6f), Mat("Cabinet Green", new Color(0.35f, 0.48f, 0.35f)));
            PlaceProp("Assets/Prefabs/Props/Bucket_blue_new.prefab", room, new Vector3(11.3f, 0, 6.9f), 0.45f, Quaternion.identity);
            PlaceProp("Assets/Prefabs/Props/Bucket_yellow_new.prefab", room, new Vector3(11.0f, 0, 7.5f), 0.45f, Quaternion.identity);
            foreach (var p in new[] { new Vector3(-11.3f, 0, -7.3f), new Vector3(2.8f, 0, 7.4f), new Vector3(-2.8f, 0, 7.4f) })
            {
                Cyl("Plant Pot", room, p + new Vector3(0, 0.22f, 0), new Vector3(0.5f, 0.22f, 0.5f), Mat("Clay Pot", new Color(0.7f, 0.38f, 0.22f)));
                PlaceProp("Assets/Prefabs/Props/LP09_LowPolyShrub.prefab", room, p + new Vector3(0, 0.42f, 0), 1.0f, Quaternion.Euler(0, p.x * 40, 0));
            }
        }

        static void Camera()
        {
            var go = new GameObject("Office Camera", typeof(Camera), typeof(AudioListener));
            go.tag = "MainCamera";
            var cam = go.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.11f, 0.08f);
            cam.orthographic = true;
            go.AddComponent<OfficeCamera>();
            // Place it once here too, so the Scene and Game views look right before Play.
            var rotation = Quaternion.Euler(38f, 32f, 0);
            go.transform.SetPositionAndRotation(new Vector3(0, 0.6f, 0.5f) - rotation * Vector3.forward * 40f, rotation);
            cam.orthographicSize = 9.5f;
            cam.farClipPlane = 100f;
        }

        static void BakeNavMesh(GameObject room)
        {
            var surface = room.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();
            if (surface.navMeshData == null) { Debug.LogWarning("[Bayanihan HQ] NavMesh bake failed; responders will slide instead of walking."); return; }
            Directory.CreateDirectory(Root + "/Generated");
            const string path = Root + "/Generated/OfficeNavMesh.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>A URP/Lit material saved under Generated/Materials (scene objects need materials that are assets).</summary>
        static Material Mat(string name, Color colour, float smoothness = 0.2f, Color? emission = null)
        {
            if (mats.TryGetValue(name, out var cached)) return cached;
            var path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", colour);
            m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", emission.Value);
            }
            else m.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material m, Quaternion? rotation, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rotation ?? Quaternion.identity;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material m, Quaternion? rotation = null, bool keepCollider = false) =>
            Prim(PrimitiveType.Cube, name, parent, pos, size, m, rotation, keepCollider);

        static GameObject Cyl(string name, Transform parent, Vector3 pos, Vector3 scale, Material m) =>
            Prim(PrimitiveType.Cylinder, name, parent, pos, scale, m, null, false);

        static GameObject Sphere(string name, Transform parent, Vector3 pos, float diameter, Material m) =>
            Prim(PrimitiveType.Sphere, name, parent, pos, Vector3.one * diameter, m, null, false);

        static GameObject Quad(string name, Transform parent, Vector3 pos, Vector2 size, Quaternion rotation, Material m) =>
            Prim(PrimitiveType.Quad, name, parent, pos, new Vector3(size.x, size.y, 1), m, rotation, false);

        static Transform Anchor(string name, Transform parent, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        static Transform Marker(string name, Transform parent, Vector3 pos, Quaternion rotation, bool local = true)
        {
            var t = Anchor(name, parent, pos);
            t.localRotation = rotation;
            return t;
        }

        /// <summary>A sign whose text is made in Play (see WorldLabel); no TextMeshPro is created in the Editor.</summary>
        static WorldLabel Text(Transform anchor, string text, float size, Color colour, Vector2 box, bool heading = false)
        {
            var label = anchor.gameObject.AddComponent<WorldLabel>();
            label.text = text;
            label.size = size;
            label.colour = colour;
            label.box = box;
            label.heading = heading;
            return label;
        }

        /// <summary>
        /// Puts one of the game's own props in the office, scaled so its largest side is <paramref name="maxSize"/> metres
        /// and resting on <paramref name="pos"/>. Missing assets are skipped; the room doesn't depend on them.
        /// </summary>
        static GameObject PlaceProp(string assetPath, Transform parent, Vector3 pos, float maxSize, Quaternion rotation)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (asset == null) { Debug.Log("[Bayanihan HQ] Prop not found, skipped: " + assetPath); return null; }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = rotation;
            go.transform.localScale = Vector3.one;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return go;
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            float largest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (largest > 0.0001f) go.transform.localScale = Vector3.one * (maxSize / largest);
            // Rest the scaled prop's bottom on the target point.
            b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            var target = parent.TransformPoint(pos);
            go.transform.position += new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
            return go;
        }

        /// <summary>A four-sided roof: a square base and an apex. Saved as an asset so the scene can reference it.</summary>
        static Mesh Pyramid(float width, float depth, float height)
        {
            const string path = Root + "/Generated/KuboRoof.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = "Kubo Roof" };
            float x = width / 2, z = depth / 2;
            var apex = new Vector3(0, height, 0);
            var corners = new[] { new Vector3(-x, 0, -z), new Vector3(x, 0, -z), new Vector3(x, 0, z), new Vector3(-x, 0, z) };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < 4; i++)
            {
                // Faces wound so they point outwards; flat-shaded for the low-poly look.
                verts.Add(corners[i]);
                verts.Add(apex);
                verts.Add(corners[(i + 1) % 4]);
                tris.AddRange(new[] { verts.Count - 3, verts.Count - 2, verts.Count - 1 });
            }
            int b0 = verts.Count;
            verts.AddRange(corners);
            tris.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 });   // underside, seen from below
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (isNew) AssetDatabase.CreateAsset(mesh, path);
            else EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
