CARMONA ROADSIDE FLORA - low-poly stylized foliage pack
45 assets modeled from Google Street View references (Carmona / Trece Martires roadsides, Cavite).
15 Trees (T01-T15), 15 Tall Grass (G01-G15), 15 Foliage (F01-F15).

UNITY IMPORT
- Drag the FBX folder into Assets/. Scale is 1 unit = 1 m (Scale Factor 1), pivot at the base, Y-up, no root rotation.
- Model tab: Normals = Import (meshes are flat-shaded on purpose). Turn off "Generate Lightmap UVs" unless you bake.
- Materials tab: Location = "Use External Materials (Legacy)" or click "Extract Materials..." once into a shared folder.
  All assets reuse the same M_* materials, so they collapse into one shared set (see Palette.png).
- Colors in the FBX are written as sRGB values so the imported Base Color matches the hex codes below.
  For URP, convert the extracted materials to URP/Lit (Edit > Rendering > Materials > Convert...) or set them to
  URP/Simple Lit with Smoothness ~0.1 for a matte stylized look.
- Grass blades and leaves are double-sided geometry, so the default shader works with back-face culling on.
- For terrain painting: add the FBX prefabs as Terrain "Trees" (trees, bananas, bushes) or as Detail Mesh (grass).

ASSET LIST  (code | name | height m | footprint m | tris)
T01 | Mango Tree, Large              |  8.50 | 6.3 x 6.7 |  1079 | T01_MangoTreeLarge.fbx
T02 | Mango Tree, Medium             |  5.96 | 4.9 x 4.8 |   896 | T02_MangoTreeMedium.fbx
T03 | Rain Tree, Wide Canopy         |  6.58 | 9.7 x 10.5 |  1056 | T03_RainTreeWideCanopy.fbx
T04 | Rain Tree, Young               |  4.29 | 6.8 x 6.9 |   873 | T04_RainTreeYoung.fbx
T05 | Ipil-Ipil, Tall                |  6.55 | 4.0 x 3.9 |   744 | T05_IpilIpilTall.fbx
T06 | Ipil-Ipil, Twin Trunk          |  5.54 | 3.5 x 4.3 |  1057 | T06_IpilIpilTwinTrunk.fbx
T07 | Ipil-Ipil Cluster              |  4.72 | 4.2 x 3.5 |  1273 | T07_IpilIpilCluster.fbx
T08 | Columnar Roadside Tree         |  6.46 | 3.2 x 4.0 |   875 | T08_ColumnarRoadsideTree.fbx
T09 | Tiered Talisay-Style Tree      |  8.47 | 9.5 x 9.6 |  2113 | T09_TieredTalisayStyleTree.fbx
T10 | Vine-Draped Tree               |  8.12 | 6.5 x 6.8 |  1879 | T10_VineDrapedTree.fbx
T11 | Vine-Smothered Thicket         |  7.89 | 7.3 x 7.0 |  2861 | T11_VineSmotheredThicket.fbx
T12 | Sapling                        |  2.47 | 1.6 x 1.5 |   407 | T12_Sapling.fbx
T13 | Bamboo Clump                   |  7.91 | 7.3 x 7.1 |  4350 | T13_BambooClump.fbx
T14 | Old Buttress Tree              |  9.39 | 9.1 x 10.3 |  1198 | T14_OldButtressTree.fbx
T15 | Dry-Season Sparse Tree         |  5.46 | 4.5 x 4.0 |   323 | T15_DrySeasonSparseTree.fbx
G01 | Talahib Clump, Small           |  1.21 | 1.7 x 1.6 |   220 | G01_TalahibClumpSmall.fbx
G02 | Talahib Clump, Medium          |  1.77 | 2.0 x 2.6 |   320 | G02_TalahibClumpMedium.fbx
G03 | Talahib Clump, Tall            |  2.56 | 3.4 x 3.4 |   400 | G03_TalahibClumpTall.fbx
G04 | Talahib in Bloom               |  2.60 | 2.2 x 2.2 |   784 | G04_TalahibInBloom.fbx
G05 | Talahib in Bloom, Tall         |  3.33 | 2.9 x 2.7 |  1224 | G05_TalahibInBloomTall.fbx
G06 | Cogon Tuft                     |  0.83 | 0.7 x 0.7 |   260 | G06_CogonTuft.fbx
G07 | Cogon Patch                    |  0.94 | 1.9 x 1.8 |   588 | G07_CogonPatch.fbx
G08 | Wind-Swept Clump               |  1.13 | 1.8 x 1.4 |   340 | G08_WindSweptClump.fbx
G09 | Dry-Tipped Clump               |  1.37 | 1.9 x 1.9 |   300 | G09_DryTippedClump.fbx
G10 | Mixed Green & Dry Clump        |  1.29 | 1.9 x 2.1 |   360 | G10_MixedGreenDryClump.fbx
G11 | Roadside Grass Strip (3 m)     |  0.90 | 3.4 x 1.4 |   660 | G11_RoadsideGrassStrip3M.fbx
G12 | Sparse Weedy Tuft              |  0.53 | 0.9 x 0.7 |   402 | G12_SparseWeedyTuft.fbx
G13 | Grass with Creeper             |  1.07 | 1.3 x 1.6 |   460 | G13_GrassWithCreeper.fbx
G14 | Dense Grass Mound              |  1.00 | 2.0 x 2.4 |   320 | G14_DenseGrassMound.fbx
G15 | Trampled Grass                 |  0.91 | 2.2 x 2.0 |   180 | G15_TrampledGrass.fbx
F01 | Banana, Young                  |  1.68 | 1.8 x 1.5 |   294 | F01_BananaYoung.fbx
F02 | Banana, Medium                 |  3.11 | 3.4 x 2.8 |   394 | F02_BananaMedium.fbx
F03 | Banana, Tall & Torn            |  4.43 | 3.8 x 3.8 |   444 | F03_BananaTallTorn.fbx
F04 | Banana Clump                   |  4.21 | 4.0 x 4.0 |  1082 | F04_BananaClump.fbx
F05 | Banana with Dry Leaves         |  3.66 | 3.0 x 3.2 |   428 | F05_BananaWithDryLeaves.fbx
F06 | Round Bush, Small              |  0.77 | 1.2 x 1.2 |   320 | F06_RoundBushSmall.fbx
F07 | Round Bush, Medium             |  1.35 | 1.9 x 1.9 |   480 | F07_RoundBushMedium.fbx
F08 | Bush Cluster, Large            |  2.24 | 3.9 x 2.4 |   960 | F08_BushClusterLarge.fbx
F09 | Vine Mound                     |  2.54 | 5.2 x 4.2 |  1796 | F09_VineMound.fbx
F10 | Vine Curtain Wall (4 m)        |  3.33 | 5.3 x 1.4 |  2600 | F10_VineCurtainWall4M.fbx
F11 | Ground Creeper Patch           |  0.29 | 1.8 x 2.5 |   620 | F11_GroundCreeperPatch.fbx
F12 | Gabi (Taro) Plant              |  0.89 | 2.0 x 1.6 |   336 | F12_GabiTaroPlant.fbx
F13 | Fern Clump                     |  0.76 | 1.2 x 1.4 |   803 | F13_FernClump.fbx
F14 | Hagonoy Weed Shrub             |  1.40 | 1.5 x 1.6 |  1216 | F14_HagonoyWeedShrub.fbx
F15 | Dry Undergrowth                |  1.49 | 2.0 x 2.1 |   767 | F15_DryUndergrowth.fbx

Total triangles, all 45: 40,342

PALETTE (material | sRGB hex)
M_Bark             #5E4E42
M_BarkLight        #8C8073
M_BarkDark         #45382F
M_LeafDeep         #24442A
M_LeafDark         #2F5B2C
M_LeafMid          #4A7F31
M_LeafLight        #74A83C
M_LeafYellow       #A2B544
M_DryLeaf          #B89A55
M_VineDark         #2B5527
M_VineMid          #3F7A2E
M_VineLight        #64A23A
M_GrassDark        #4B7A2C
M_GrassMid         #6E9E38
M_GrassLight       #9CC04E
M_GrassDry         #C7B068
M_GrassStraw       #DCCB8E
M_Plume            #ECE6D6
M_BananaLeaf       #5A9A35
M_BananaUnder      #8FC04F
M_BananaStem       #7E9F45
M_BananaDry        #9C7E4A
M_BananaStemDark   #66703A
M_TaroLeaf         #3F7F3A
M_TaroUnder        #7FB25A
M_TaroStem         #6F9A4C
M_Fern             #4F8B33
M_Bamboo           #93A548
M_BambooNode       #6B7A30
M_Pod              #7A5A38

SOURCE
Source/ holds the procedural generator (Blender 5.x, bpy). Edit assets.py (seeds, sizes, counts) and run
  python build.py --out <folder>
to regenerate meshes, renders and FBX files. Previews/ has a 560 px render of every asset.
