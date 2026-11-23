using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Demo.Editor
{
    /// <summary>生成可重复使用的几何体场景、共用道具和角色动画；不依赖游戏运行时。</summary>
    public static class DemoGeometryBuilder
    {
        private const string Root = "Assets/Demo/Asb";
        private const string Items = Root + "/StaticSceneItem";
        private const string Characters = Root + "/Character";
        private const string Scenes = Root + "/StaticScene";
        private const string Preview = "Library/DemoGeometryPreview";
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly Dictionary<string, GameObject> Props = new Dictionary<string, GameObject>();
        private static Mesh _cone;
        private static Mesh _roof;

        [MenuItem("DEGF/Demo/Generate Geometry Assets")]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().isDirty)
            {
                throw new InvalidOperationException("Save the current scene and exit Play Mode before generating Demo assets.");
            }
            Directory.CreateDirectory(Preview);
            Materials.Clear();
            Props.Clear();
            foreach (string path in new[] { Items + "/Materials", Items + "/Meshes", Items + "/Prefabs",
                Scenes })
            {
                Directory.CreateDirectory(path);
            }
            foreach (string role in new[] { "Warrior", "Archer", "Mage", "Merchant" })
            {
                foreach (string folder in new[] { "Animations", "Materials", "Meshes" })
                {
                    Directory.CreateDirectory(Characters + "/" + role + "/" + folder);
                }
            }
            AssetDatabase.Refresh();
            Configure3DUrp();
            CreateMaterials();
            CreateMeshes();
            if (string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Runtime.unity", OpenSceneMode.Single);
            }
            var original = SceneManager.GetActiveScene();
            var work = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(work);
            try
            {
                CreateProps();
                CreateCharacters();
                CreateWorld(true);
                CreateWorld(false);
                CreateCharacterPreview();
            }
            finally
            {
                SceneManager.SetActiveScene(original);
                EditorSceneManager.CloseScene(work, true);
            }
            ConvertSceneTo3D("Assets/Scenes/Runtime.unity");
            ConvertSceneTemplate();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Demo geometry generated: City (SpaceNo 1), Outskirts (SpaceNo 2), Warrior, Archer, Mage, Merchant.");
        }

        private static void Configure3DUrp()
        {
            const string path = "Assets/Settings/Renderer3D.asset";
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
            if (renderer == null)
            {
                renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, path);
            }
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
            ResourceReloader.ReloadAllNullIn(renderer, "Packages/com.unity.render-pipelines.universal");
            EditorUtility.SetDirty(renderer);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/UniversalRP.asset");
            var settings = new SerializedObject(pipeline);
            var list = settings.FindProperty("m_RendererDataList");
            // Keep the old 2D renderer as an optional renderer for existing 2D content.
            bool found = false;
            for (int index = 0; index < list.arraySize; index++)
            {
                found |= list.GetArrayElementAtIndex(index).objectReferenceValue == renderer;
            }
            if (!found)
            {
                int index = list.arraySize++;
                list.GetArrayElementAtIndex(index).objectReferenceValue = renderer;
            }
            for (int index = 0; index < list.arraySize; index++)
            {
                if (list.GetArrayElementAtIndex(index).objectReferenceValue == renderer)
                {
                    settings.FindProperty("m_DefaultRendererIndex").intValue = index;
                }
            }
            settings.FindProperty("m_ShadowDistance").floatValue = 150;
            settings.FindProperty("m_ShadowCascadeCount").intValue = 4;
            settings.FindProperty("m_SoftShadowsSupported").boolValue = true;
            settings.FindProperty("m_MSAA").intValue = 4;
            settings.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode3D;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.in2DMode = false;
            }
            EditorUtility.SetDirty(pipeline);
        }

        private static void CreateMaterials()
        {
            string[] names = { "Stone", "StoneDark", "Plaster", "RoofRed", "RoofTeal", "Wood", "WoodLight", "Metal",
                "Gold", "Grass", "GrassLight", "Leaves", "LeavesLight", "Dirt", "Water", "ClothRed", "ClothBlue",
                "ClothGreen", "ClothPurple", "Skin", "Body", "Boots", "Eyes", "Glow" };
            string[] colors = { "B9AEA0", "59646A", "F0DCC0", "A95943", "427C7C", "634B37", "AA7B4D", "B3C5CB",
                "E8B950", "6C9360", "91AC6D", "3B6750", "638453", "BB9C6F", "489DA8", "BE6556", "527E9F",
                "648954", "8870A8", "F0D1A5", "DADACB", "414D53", "24323A", "87DCE0" };
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                throw new InvalidOperationException("URP Lit shader is missing.");
            }
            for (int index = 0; index < names.Length; index++)
            {
                var material = new Material(shader) { name = names[index], enableInstancing = true };
                ColorUtility.TryParseHtmlString("#" + colors[index], out var color);
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Smoothness", names[index] == "Water" ? 0.65f : 0.18f);
                material.SetFloat("_Metallic", names[index] == "Metal" || names[index] == "Gold" ? 0.35f : 0);
                Materials[names[index]] = Save(material, Items + "/Materials/" + names[index] + ".mat");
            }
        }

        private static T Save<T>(T asset, string path) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return asset;
            }
            EditorUtility.CopySerialized(asset, existing);
            Object.DestroyImmediate(asset);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void CreateMeshes()
        {
            var triangles = new List<Vector3>();
            const int sides = 8;
            for (int index = 0; index < sides; index++)
            {
                float a = index * Mathf.PI * 2 / sides;
                float b = (index + 1) * Mathf.PI * 2 / sides;
                var first = new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f);
                var second = new Vector3(Mathf.Cos(b) * 0.5f, 0, Mathf.Sin(b) * 0.5f);
                triangles.AddRange(new[] { first, Vector3.up, second, Vector3.zero, first, second });
            }
            _cone = Save(FlatMesh("Cone", triangles), Items + "/Meshes/Cone.asset");
            Vector3[] v = { new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, -0.5f), new Vector3(0, 1, -0.5f),
                new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0, 1, 0.5f) };
            int[] indices = { 0, 2, 1, 3, 4, 5, 0, 3, 5, 0, 5, 2, 1, 2, 5, 1, 5, 4, 0, 1, 4, 0, 4, 3 };
            _roof = Save(FlatMesh("GableRoof", indices.Select(index => v[index]).ToList()), Items + "/Meshes/GableRoof.asset");
        }

        private static Mesh FlatMesh(string name, List<Vector3> vertices)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Transform Node(string name, Transform parent, Vector3 position)
        {
            var node = new GameObject(name).transform;
            node.SetParent(parent, false);
            node.localPosition = position;
            return node;
        }

        private static GameObject Shape(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale,
            string material, bool collider = false)
        {
            var shape = GameObject.CreatePrimitive(type);
            shape.name = name;
            shape.transform.SetParent(parent, false);
            shape.transform.localPosition = position;
            shape.transform.localScale = scale;
            shape.GetComponent<Renderer>().sharedMaterial = Materials[material];
            if (!collider)
            {
                Object.DestroyImmediate(shape.GetComponent<Collider>());
            }
            return shape;
        }

        private static GameObject Box(string name, Transform parent, Vector3 position, Vector3 scale, string material,
            bool collider = false)
        {
            return Shape(name, PrimitiveType.Cube, parent, position, scale, material, collider);
        }

        private static GameObject MeshShape(string name, Mesh mesh, Transform parent, Vector3 position, Vector3 scale, string material)
        {
            var shape = Node(name, parent, position).gameObject;
            shape.transform.localScale = scale;
            shape.AddComponent<MeshFilter>().sharedMesh = mesh;
            shape.AddComponent<MeshRenderer>().sharedMaterial = Materials[material];
            return shape;
        }

        private static void SaveProp(Transform root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>())
            {
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject,
                    StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            }
            Props[root.name] = PrefabUtility.SaveAsPrefabAsset(root.gameObject, Items + "/Prefabs/" + root.name + ".prefab");
            Object.DestroyImmediate(root.gameObject);
        }

        private static void CreateProps()
        {
            foreach (string roofColor in new[] { "RoofRed", "RoofTeal" })
            {
                var house = Node(roofColor == "RoofRed" ? "HouseWarm" : "HouseTeal", null, Vector3.zero);
                Box("Foundation", house, new Vector3(0, 0.2f, 0), new Vector3(8.6f, 0.4f, 7.6f), "StoneDark", true);
                Box("Walls", house, new Vector3(0, 2.8f, 0), new Vector3(8, 5.2f, 7), "Plaster", true);
                MeshShape("Roof", _roof, house, new Vector3(0, 5.4f, 0), new Vector3(9, 2.5f, 8), roofColor);
                Box("Door", house, new Vector3(0, 1.5f, -3.53f), new Vector3(1.5f, 2.6f, 0.15f), "Wood");
                for (int side = -1; side <= 1; side += 2)
                {
                    Box("Window", house, new Vector3(side * 2.6f, 3.3f, -3.56f), new Vector3(1.2f, 1.5f, 0.15f), "RoofTeal");
                    Box("WindowFrame", house, new Vector3(side * 2.6f, 3.3f, -3.66f), new Vector3(0.12f, 1.6f, 0.12f), "Wood");
                    Box("Beam", house, new Vector3(side * 3.85f, 2.8f, -3.57f), new Vector3(0.25f, 5.2f, 0.15f), "Wood");
                }
                Box("Chimney", house, new Vector3(2, 6.5f, 1.8f), new Vector3(0.9f, 3, 0.9f), "Stone");
                SaveProp(house);
            }
            var tree = Node("Tree", null, Vector3.zero);
            Shape("Trunk", PrimitiveType.Cylinder, tree, new Vector3(0, 1.4f, 0), new Vector3(0.65f, 1.4f, 0.65f), "Wood", true);
            MeshShape("CrownLower", _cone, tree, new Vector3(0, 1.8f, 0), new Vector3(4.4f, 3.6f, 4.4f), "Leaves");
            MeshShape("CrownUpper", _cone, tree, new Vector3(0, 3.6f, 0), new Vector3(3.2f, 3.1f, 3.2f), "LeavesLight");
            SaveProp(tree);
            var rock = Node("Rock", null, Vector3.zero);
            MeshShape("RockMesh", _cone, rock, Vector3.zero, new Vector3(2.8f, 1.6f, 2.3f), "Stone");
            rock.gameObject.AddComponent<BoxCollider>().center = new Vector3(0, 0.55f, 0);
            rock.GetComponent<BoxCollider>().size = new Vector3(1.8f, 1.1f, 1.5f);
            SaveProp(rock);
            var bench = Node("Bench", null, Vector3.zero);
            Box("Seat", bench, new Vector3(0, 0.65f, 0), new Vector3(2.5f, 0.18f, 0.8f), "WoodLight", true);
            Box("Back", bench, new Vector3(0, 1.1f, 0.34f), new Vector3(2.5f, 0.7f, 0.14f), "WoodLight", true);
            foreach (float x in new[] { -0.9f, 0.9f })
            {
                Box("Leg", bench, new Vector3(x, 0.3f, 0), new Vector3(0.18f, 0.6f, 0.65f), "Metal");
            }
            SaveProp(bench);
            var lamp = Node("Lamp", null, Vector3.zero);
            Shape("Base", PrimitiveType.Cylinder, lamp, new Vector3(0, 0.15f, 0), new Vector3(0.7f, 0.15f, 0.7f), "StoneDark", true);
            Shape("Post", PrimitiveType.Cylinder, lamp, new Vector3(0, 2, 0), new Vector3(0.18f, 2, 0.18f), "Metal");
            Box("Lantern", lamp, new Vector3(0, 4, 0), Vector3.one * 0.65f, "Gold");
            MeshShape("Cap", _cone, lamp, new Vector3(0, 4.33f, 0), new Vector3(1, 0.45f, 1), "RoofTeal");
            SaveProp(lamp);
            var crate = Node("Crate", null, Vector3.zero);
            Box("Box", crate, new Vector3(0, 0.6f, 0), Vector3.one * 1.2f, "WoodLight", true);
            foreach (float x in new[] { -0.43f, 0.43f })
            {
                Box("Band", crate, new Vector3(x, 0.6f, 0), new Vector3(0.12f, 1.23f, 1.23f), "Wood");
            }
            SaveProp(crate);
            var fence = Node("Fence", null, Vector3.zero);
            foreach (float x in new[] { -2f, 2f })
            {
                Box("Post", fence, new Vector3(x, 0.7f, 0), new Vector3(0.24f, 1.4f, 0.24f), "Wood", true);
            }
            foreach (float y in new[] { 0.45f, 1.05f })
            {
                Box("Rail", fence, new Vector3(0, y, 0), new Vector3(4.2f, 0.2f, 0.18f), "WoodLight", true);
            }
            SaveProp(fence);
            var stall = Node("MarketStall", null, Vector3.zero);
            Box("Counter", stall, new Vector3(0, 0.65f, 0), new Vector3(3, 1.3f, 1.7f), "WoodLight", true);
            foreach (float x in new[] { -1.4f, 1.4f })
            {
                Box("Post", stall, new Vector3(x, 1.7f, 0.6f), new Vector3(0.15f, 3.4f, 0.15f), "Wood");
            }
            for (int index = 0; index < 6; index++)
            {
                Box("Awning", stall, new Vector3(-1.25f + index * 0.5f, 3, 0), new Vector3(0.5f, 0.15f, 2.5f),
                    index % 2 == 0 ? "ClothRed" : "Plaster");
            }
            for (int index = 0; index < 5; index++)
            {
                Shape("Produce", PrimitiveType.Sphere, stall, new Vector3(-1 + index * 0.5f, 1.5f, 0), Vector3.one * 0.35f,
                    index % 2 == 0 ? "Gold" : "ClothGreen");
            }
            SaveProp(stall);
            var fountain = Node("Fountain", null, Vector3.zero);
            Shape("Base", PrimitiveType.Cylinder, fountain, new Vector3(0, 0.2f, 0), new Vector3(7, 0.2f, 7), "StoneDark", true);
            Shape("Basin", PrimitiveType.Cylinder, fountain, new Vector3(0, 0.5f, 0), new Vector3(6.3f, 0.15f, 6.3f), "Stone");
            Shape("Water", PrimitiveType.Cylinder, fountain, new Vector3(0, 0.67f, 0), new Vector3(5.7f, 0.025f, 5.7f), "Water");
            Shape("Pillar", PrimitiveType.Cylinder, fountain, new Vector3(0, 1.6f, 0), new Vector3(0.8f, 1, 0.8f), "Stone");
            Shape("UpperBowl", PrimitiveType.Cylinder, fountain, new Vector3(0, 2.5f, 0), new Vector3(2.8f, 0.13f, 2.8f), "Stone");
            Shape("Top", PrimitiveType.Sphere, fountain, new Vector3(0, 3.1f, 0), Vector3.one * 0.8f, "Gold");
            SaveProp(fountain);
            var bridge = Node("Bridge", null, Vector3.zero);
            for (int index = 0; index < 14; index++)
            {
                Box("Plank", bridge, new Vector3(0, 0.35f, -6.5f + index), new Vector3(6, 0.3f, 0.92f), "WoodLight", true);
            }
            foreach (float x in new[] { -2.8f, 2.8f })
            {
                Box("HandRail", bridge, new Vector3(x, 1.5f, 0), new Vector3(0.2f, 0.2f, 14), "Wood", true);
                foreach (float z in new[] { -6f, -2f, 2f, 6f })
                {
                    Box("Post", bridge, new Vector3(x, 0.9f, z), new Vector3(0.22f, 1.8f, 0.22f), "Wood", true);
                }
            }
            SaveProp(bridge);
        }

        private static Transform Place(string prop, Transform parent, float x, float z, float yaw = 0, float scale = 1)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(Props[prop], parent);
            instance.transform.localPosition = new Vector3(x, 0, z);
            instance.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            instance.transform.localScale = Vector3.one * scale;
            return instance.transform;
        }

        private static void ClearWorkScene()
        {
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreateWorld(bool city)
        {
            ClearWorkScene();
            var root = Node(city ? "City_SpaceNo_1" : "Outskirts_SpaceNo_2", null, Vector3.zero);
            Box("Ground_100x100m", root, new Vector3(0, -0.5f, 0), new Vector3(100, 1, 100), city ? "GrassLight" : "Grass", true);
            if (city)
            {
                BuildCity(root);
            }
            else
            {
                BuildOutskirts(root);
            }
            Node("SpawnPoint", root, new Vector3(0, 0.05f, -12));
            var camera = EnvironmentCamera(new Vector3(76, 90, -92), new Vector3(0, 0, 0), 44);
            PrefabUtility.SaveAsPrefabAsset(root.gameObject, Scenes + "/" + (city ? "City" : "Outskirts") + ".prefab");
            Capture(camera, city ? "City" : "Outskirts", 1600, 1200);
        }

        private static void BuildCity(Transform root)
        {
            var streets = Node("StreetsAndPlaza", root, Vector3.zero);
            Box("NorthSouthStreet", streets, new Vector3(0, 0.025f, 0), new Vector3(9, 0.05f, 100), "Dirt");
            Box("EastWestStreet", streets, new Vector3(0, 0.03f, 0), new Vector3(100, 0.05f, 9), "Dirt");
            Box("Plaza", streets, new Vector3(0, 0.07f, 0), new Vector3(29, 0.12f, 29), "Stone");
            for (int index = -4; index <= 4; index++)
            {
                Box("PavingJoint", streets, new Vector3(index * 3, 0.136f, 0), new Vector3(0.06f, 0.01f, 28), "StoneDark");
                Box("PavingJoint", streets, new Vector3(0, 0.137f, index * 3), new Vector3(28, 0.01f, 0.06f), "StoneDark");
            }
            Place("Fountain", root, 0, 0).localPosition += Vector3.up * 0.14f;
            var buildings = Node("Buildings", root, Vector3.zero);
            foreach (float side in new[] { -1f, 1f })
            {
                for (int index = 0; index < 4; index++)
                {
                    float z = -34 + index * 22;
                    Place(index % 2 == 0 ? "HouseWarm" : "HouseTeal", buildings, side * 24, z, side < 0 ? -90 : 90,
                        index % 2 == 0 ? 1 : 1.12f);
                }
                Place("HouseTeal", buildings, side * 12, 34, 0, 1.25f);
                Place("HouseWarm", buildings, side * 12, -34, 180);
                for (int index = 0; index < 5; index++)
                {
                    Place("Tree", root, side * 40, -36 + index * 18, index * 31, 0.8f + index % 2 * 0.2f);
                }
                for (int index = 0; index < 4; index++)
                {
                    Place("Lamp", root, side * 6, -30 + index * 20);
                }
                Place("Bench", root, side * 10, 7, side * 90);
                Place("Bench", root, side * 10, -7, side * 90);
                Place("MarketStall", root, side * 10, 21, side < 0 ? 0 : 20);
                Place("Crate", root, side * 13, 23, 15);
                Place("Crate", root, side * 14, 21, 30, 0.8f);
            }
            var walls = Node("BoundaryAndGate", root, Vector3.zero);
            foreach (float side in new[] { -1f, 1f })
            {
                foreach (float center in new[] { -28f, 28f })
                {
                    Box("BoundaryWall", walls, new Vector3(center, 1.1f, side * 48.5f), new Vector3(40, 2.2f, 1), "Stone", true);
                }
                Box("SideWall", walls, new Vector3(side * 48.5f, 1.1f, 0), new Vector3(1, 2.2f, 97), "Stone", true);
                foreach (float x in new[] { -7f, 7f })
                {
                    Box("GateTower", walls, new Vector3(x, 3.5f, side * 47), new Vector3(3.5f, 7, 3.5f), "Plaster", true);
                    MeshShape("TowerRoof", _cone, walls, new Vector3(x, 7, side * 47), new Vector3(5, 2, 5), "RoofTeal");
                }
            }
        }

        private static void BuildOutskirts(Transform root)
        {
            var path = Node("CountryPath", root, Vector3.zero);
            Vector3[] points = { new Vector3(0, 0, -50), new Vector3(-8, 0, -30), new Vector3(-3, 0, -12),
                new Vector3(0, 0, 5), new Vector3(0, 0, 27), new Vector3(9, 0, 49) };
            for (int index = 1; index < points.Length; index++)
            {
                var start = points[index - 1];
                var end = points[index];
                var segment = Box("Path", path, (start + end) * 0.5f + Vector3.up * 0.025f,
                    new Vector3(6.5f, 0.05f, Vector3.Distance(start, end) + 1.5f), "Dirt");
                segment.transform.rotation = Quaternion.LookRotation(end - start);
            }
            Box("River", root, new Vector3(0, 0.06f, 20), new Vector3(100, 0.12f, 10), "Water");
            foreach (float z in new[] { 14.2f, 25.8f })
            {
                Box("RiverBank", root, new Vector3(0, 0.1f, z), new Vector3(100, 0.2f, 1.5f), "Dirt");
            }
            Place("Bridge", root, 0, 20);
            var forest = Node("Forest", root, Vector3.zero);
            var random = new System.Random(2718);
            for (int index = 0; index < 65; index++)
            {
                float x = (index % 2 == 0 ? -1 : 1) * (17 + (float)random.NextDouble() * 28);
                float z = -44 + (float)random.NextDouble() * 88;
                if (z > 11 && z < 29 || x < -15 && z > -30 && z < -5)
                {
                    continue;
                }
                Place("Tree", forest, x, z, (float)random.NextDouble() * 360, 0.75f + (float)random.NextDouble() * 0.6f);
            }
            for (int index = 0; index < 20; index++)
            {
                float x = (index % 2 == 0 ? -1 : 1) * (11 + (float)random.NextDouble() * 31);
                float z = -44 + (float)random.NextDouble() * 88;
                Place("Rock", root, x, z, index * 57, 0.6f + index % 3 * 0.35f);
            }
            var farm = Node("Farmstead", root, Vector3.zero);
            Place("HouseWarm", farm, -28, -18, -30, 0.85f);
            for (int row = 0; row < 5; row++)
            {
                Box("SoilBed", farm, new Vector3(-28, 0.04f, -5 + row * 1.5f), new Vector3(13, 0.08f, 0.8f), "Dirt");
                for (int column = 0; column < 10; column++)
                {
                    MeshShape("Crop", _cone, farm, new Vector3(-33.5f + column * 1.2f, 0.08f, -5 + row * 1.5f),
                        new Vector3(0.5f, 0.8f, 0.5f), row % 2 == 0 ? "Gold" : "ClothGreen");
                }
            }
            for (int index = 0; index < 4; index++)
            {
                Place("Fence", farm, -35 + index * 4.3f, -9);
            }
            var camp = Node("Camp", root, Vector3.zero);
            MeshShape("Tent", _roof, camp, new Vector3(19, 0, -25), new Vector3(6, 3.5f, 6), "ClothBlue");
            Box("TentDoor", camp, new Vector3(19, 1, -28.02f), new Vector3(1.4f, 2, 0.03f), "Wood");
            Place("Crate", camp, 23, -25);
            Place("Bench", camp, 15, -22, -45);
            for (int index = 0; index < 8; index++)
            {
                float angle = index * Mathf.PI / 4;
                Shape("FireRingStone", PrimitiveType.Sphere, camp,
                    new Vector3(17 + Mathf.Cos(angle) * 1.2f, 0.2f, -18 + Mathf.Sin(angle) * 1.2f), new Vector3(0.6f, 0.4f, 0.6f), "Stone");
            }
            for (int index = 0; index < 3; index++)
            {
                var log = Box("Firewood", camp, new Vector3(17, 0.25f + index * 0.12f, -18), new Vector3(1.8f, 0.25f, 0.3f), "Wood");
                log.transform.localRotation = Quaternion.Euler(0, index * 60, 0);
            }
        }

        private static Camera EnvironmentCamera(Vector3 position, Vector3 target, float fieldOfView)
        {
            var environment = Node("PreviewEnvironment", null, Vector3.zero);
            var sun = Node("Directional Light", environment, Vector3.zero).gameObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1, 0.94f, 0.83f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48, -35, 0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.75f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.43f, 0.49f, 0.51f);
            RenderSettings.ambientGroundColor = new Color(0.29f, 0.3f, 0.28f);
            RenderSettings.skybox = null;
            var camera = Node("Main Camera", environment, position).gameObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.LookAt(target);
            camera.fieldOfView = fieldOfView;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.74f, 0.82f, 0.85f);
            camera.GetUniversalAdditionalCameraData().SetRenderer(-1);
            return camera;
        }

        private static void Capture(Camera camera, string name, int width, int height)
        {
            var texture = new RenderTexture(width, height, 24);
            var previous = RenderTexture.active;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                texture.Create();
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Preview + "/" + name + ".png", image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                texture.Release();
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(image);
            }
        }

        private static void ConvertSceneTemplate()
        {
            const string oldScene = "Assets/Settings/Scenes/URP2DSceneTemplate.unity";
            const string newScene = "Assets/Settings/Scenes/URP3DSceneTemplate.unity";
            const string oldTemplate = "Assets/Settings/Lit2DSceneTemplate.scenetemplate";
            const string newTemplate = "Assets/Settings/Lit3DSceneTemplate.scenetemplate";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(oldScene) != null)
            {
                AssetDatabase.MoveAsset(oldScene, newScene);
            }
            if (AssetDatabase.LoadMainAssetAtPath(oldTemplate) != null)
            {
                AssetDatabase.MoveAsset(oldTemplate, newTemplate);
            }
            ConvertSceneTo3D(newScene);
            var template = AssetDatabase.LoadMainAssetAtPath(newTemplate);
            var serialized = new SerializedObject(template);
            serialized.FindProperty("templateName").stringValue = "Lit 3D (URP)";
            serialized.FindProperty("description").stringValue = "Perspective camera and shadow-casting directional light for 3D URP scenes.";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(template);
        }

        private static void ConvertSceneTo3D(string path)
        {
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }
            SceneManager.SetActiveScene(scene);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.65f, 0.75f, 0.84f);
            RenderSettings.ambientEquatorColor = new Color(0.43f, 0.49f, 0.51f);
            RenderSettings.ambientGroundColor = new Color(0.29f, 0.3f, 0.28f);
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var light in root.GetComponentsInChildren<Light2D>(true))
                {
                    var go = light.gameObject;
                    Object.DestroyImmediate(light);
                    go.name = "Directional Light";
                    var sun = go.AddComponent<Light>();
                    sun.type = LightType.Directional;
                    sun.intensity = 1.2f;
                    sun.shadows = LightShadows.Soft;
                    go.transform.rotation = Quaternion.Euler(48, -35, 0);
                    RenderSettings.sun = sun;
                }
                foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                {
                    camera.orthographic = false;
                    camera.fieldOfView = 50;
                    camera.farClipPlane = 500;
                    camera.transform.position = new Vector3(0, 16, -20);
                    camera.transform.LookAt(Vector3.zero);
                    camera.GetUniversalAdditionalCameraData().SetRenderer(-1);
                }
            }
            EditorSceneManager.SaveScene(scene);
            SceneManager.SetActiveScene(previous);
            if (opened)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void CreateCharacters()
        {
            foreach (string role in new[] { "Warrior", "Archer", "Mage", "Merchant" })
            {
                var root = CreateCharacter(role);
                LocalizeCharacterResources(root, role);
                var clips = new Dictionary<string, AnimationClip>();
                foreach (string action in new[] { "Idle", "Run", "Hit", "Jump", "Death", "Attack" })
                {
                    clips[action] = CreateClip(action, role);
                }
                string path = Characters + "/" + role + "/" + role + ".controller";
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null)
                {
                    controller = AnimatorController.CreateAnimatorControllerAtPath(path);
                }
                var machine = controller.layers[0].stateMachine;
                foreach (var state in machine.states)
                {
                    machine.RemoveState(state.state);
                }
                controller.parameters = Array.Empty<AnimatorControllerParameter>();
                controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
                foreach (string trigger in new[] { "Attack", "Hit", "Jump", "Death" })
                {
                    controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
                }
                var states = new Dictionary<string, AnimatorState>();
                int index = 0;
                foreach (var clip in clips)
                {
                    var state = machine.AddState(clip.Key, new Vector3(index % 3 * 240, index / 3 * 100));
                    state.motion = clip.Value;
                    state.writeDefaultValues = false;
                    states.Add(clip.Key, state);
                    index++;
                }
                machine.defaultState = states["Idle"];
                var run = states["Idle"].AddTransition(states["Run"]);
                run.hasExitTime = false;
                run.duration = 0.12f;
                run.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                var idle = states["Run"].AddTransition(states["Idle"]);
                idle.hasExitTime = false;
                idle.duration = 0.12f;
                idle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");
                foreach (string action in new[] { "Attack", "Hit", "Jump", "Death" })
                {
                    // Death has no outgoing transitions; action triggers cannot revive a dead character.
                    foreach (var source in states.Where(pair => pair.Key != "Death" && pair.Key != action))
                    {
                        var transition = source.Value.AddTransition(states[action]);
                        transition.hasExitTime = false;
                        transition.duration = action == "Death" ? 0.05f : 0.08f;
                        transition.AddCondition(AnimatorConditionMode.If, 0, action);
                    }
                    if (action != "Death")
                    {
                        var back = states[action].AddTransition(states["Idle"]);
                        back.hasExitTime = true;
                        back.exitTime = 1;
                        back.duration = 0.1f;
                    }
                }
                var animator = root.gameObject.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var collider = root.gameObject.AddComponent<CapsuleCollider>();
                collider.center = Vector3.up;
                collider.height = 2;
                collider.radius = 0.4f;
                PrefabUtility.SaveAsPrefabAsset(root.gameObject, Characters + "/" + role + "/" + role + ".prefab");
                Object.DestroyImmediate(root.gameObject);
                EditorUtility.SetDirty(controller);
            }
        }

        private static void LocalizeCharacterResources(Transform root, string role)
        {
            string directory = Characters + "/" + role;
            var meshes = new Dictionary<Mesh, Mesh>();
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var source = filter.sharedMesh;
                if (!meshes.TryGetValue(source, out var mesh))
                {
                    mesh = Object.Instantiate(source);
                    mesh.name = source.name;
                    mesh = Save(mesh, directory + "/Meshes/" + source.name + ".asset");
                    meshes.Add(source, mesh);
                }
                filter.sharedMesh = mesh;
            }
            var materials = new Dictionary<Material, Material>();
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.sharedMaterials = renderer.sharedMaterials.Select(source =>
                {
                    if (!materials.TryGetValue(source, out var material))
                    {
                        material = Save(new Material(source) { name = source.name }, directory + "/Materials/" + source.name + ".mat");
                        materials.Add(source, material);
                    }
                    return material;
                }).ToArray();
            }
        }

        private static Transform CreateCharacter(string role)
        {
            var root = Node(role, null, Vector3.zero);
            var visual = Node("Visual", root, Vector3.zero);
            var body = Node("BodyPivot", visual, new Vector3(0, 1.1f, 0));
            Shape("Body", PrimitiveType.Capsule, body, Vector3.zero, new Vector3(0.65f, 0.53f, 0.48f), "Body");
            Shape("Head", PrimitiveType.Sphere, body, new Vector3(0, 0.67f, 0), Vector3.one * 0.46f, "Skin");
            foreach (float x in new[] { -0.085f, 0.085f })
            {
                Shape("Eye", PrimitiveType.Sphere, body, new Vector3(x, 0.7f, 0.207f), Vector3.one * 0.055f, "Eyes");
            }
            Box("Belt", body, new Vector3(0, -0.3f, 0), new Vector3(0.64f, 0.12f, 0.49f), "Wood");
            string accent = role == "Warrior" ? "ClothRed" : role == "Archer" ? "ClothGreen" : role == "Mage" ? "ClothPurple" : "ClothBlue";
            Box("RoleTabard", body, new Vector3(0, -0.05f, 0.24f), new Vector3(0.35f, 0.5f, 0.035f), accent);
            foreach (int side in new[] { -1, 1 })
            {
                var arm = Node(side < 0 ? "LeftArm" : "RightArm", body, new Vector3(side * 0.45f, 0.25f, 0));
                Shape("Sleeve", PrimitiveType.Capsule, arm, new Vector3(0, -0.17f, 0), new Vector3(0.18f, 0.18f, 0.18f), "Body");
                Shape("Hand", PrimitiveType.Sphere, arm, new Vector3(0, -0.4f, 0), Vector3.one * 0.25f, "Skin");
                var leg = Node(side < 0 ? "LeftLeg" : "RightLeg", visual, new Vector3(side * 0.2f, 0.65f, 0));
                Shape("Leg", PrimitiveType.Capsule, leg, new Vector3(0, -0.2f, 0), new Vector3(0.19f, 0.21f, 0.19f), "Body");
                Box("Foot", leg, new Vector3(0, -0.51f, 0.08f), new Vector3(0.27f, 0.28f, 0.5f), "Boots");
            }
            var right = body.Find("RightArm");
            var left = body.Find("LeftArm");
            if (role == "Warrior")
            {
                Box("SwordGrip", right, new Vector3(0, -0.4f, 0.12f), new Vector3(0.09f, 0.1f, 0.3f), "Wood");
                Box("SwordGuard", right, new Vector3(0, -0.4f, 0.3f), new Vector3(0.4f, 0.1f, 0.08f), "Gold");
                Box("SwordBlade", right, new Vector3(0, -0.4f, 0.8f), new Vector3(0.16f, 0.07f, 0.9f), "Metal");
                var shield = Shape("Shield", PrimitiveType.Cylinder, left, new Vector3(-0.13f, -0.3f, 0.15f),
                    new Vector3(0.65f, 0.07f, 0.65f), "ClothRed");
                shield.transform.localRotation = Quaternion.Euler(90, 0, 0);
                Box("ShieldBoss", left, new Vector3(-0.13f, -0.3f, 0.24f), new Vector3(0.1f, 0.5f, 0.05f), "Gold");
                Shape("Helmet", PrimitiveType.Sphere, body, new Vector3(0, 0.8f, -0.02f), new Vector3(0.48f, 0.26f, 0.48f), "Metal");
            }
            else if (role == "Archer")
            {
                var bow = Node("Bow", left, new Vector3(0, -0.35f, 0.22f));
                for (int index = 0; index < 6; index++)
                {
                    float a = (-80 + index * 160f / 6) * Mathf.Deg2Rad;
                    float b = (-80 + (index + 1) * 160f / 6) * Mathf.Deg2Rad;
                    Rod("BowLimb", bow, new Vector3(0, Mathf.Sin(a) * 0.65f, Mathf.Cos(a) * 0.3f),
                        new Vector3(0, Mathf.Sin(b) * 0.65f, Mathf.Cos(b) * 0.3f), 0.055f, "WoodLight");
                }
                Rod("BowString", bow, new Vector3(0, -0.64f, 0.05f), new Vector3(0, 0.64f, 0.05f), 0.012f, "Plaster");
                Shape("Quiver", PrimitiveType.Cylinder, body, new Vector3(0.19f, 0.1f, -0.35f), new Vector3(0.22f, 0.4f, 0.22f), "Wood");
                for (int index = 0; index < 3; index++)
                {
                    Rod("Arrow", body, new Vector3(0.1f + index * 0.08f, 0.3f, -0.35f),
                        new Vector3(0.1f + index * 0.08f, 0.7f, -0.35f), 0.025f, "WoodLight");
                }
                MeshShape("Cap", _cone, body, new Vector3(0, 0.81f, 0), new Vector3(0.58f, 0.25f, 0.58f), "ClothGreen");
            }
            else if (role == "Mage")
            {
                Shape("HatBrim", PrimitiveType.Cylinder, body, new Vector3(0, 0.83f, 0), new Vector3(0.72f, 0.035f, 0.72f), "ClothPurple");
                MeshShape("Hat", _cone, body, new Vector3(0, 0.86f, 0), new Vector3(0.49f, 0.45f, 0.49f), "ClothPurple");
                Rod("Staff", right, new Vector3(0, -1.1f, 0.15f), new Vector3(0, 0.55f, 0.15f), 0.085f, "WoodLight");
                Shape("StaffOrb", PrimitiveType.Sphere, right, new Vector3(0, 0.64f, 0.15f), Vector3.one * 0.3f, "Glow");
                Box("SpellBook", left, new Vector3(0, -0.4f, 0.12f), new Vector3(0.32f, 0.12f, 0.4f), "ClothBlue");
            }
            else
            {
                Box("Backpack", body, new Vector3(0, 0, -0.38f), new Vector3(0.6f, 0.68f, 0.35f), "WoodLight");
                Box("PackStrap", body, new Vector3(0, 0, -0.57f), new Vector3(0.12f, 0.7f, 0.04f), "Wood");
                Shape("BedRoll", PrimitiveType.Cylinder, body, new Vector3(0, 0.42f, -0.4f), new Vector3(0.25f, 0.38f, 0.25f), "ClothBlue")
                    .transform.localRotation = Quaternion.Euler(0, 0, 90);
                Box("Satchel", right, new Vector3(0, -0.6f, 0.08f), new Vector3(0.35f, 0.38f, 0.25f), "WoodLight");
                Shape("Coin", PrimitiveType.Cylinder, right, new Vector3(0, -0.6f, 0.22f), new Vector3(0.16f, 0.025f, 0.16f), "Gold")
                    .transform.localRotation = Quaternion.Euler(90, 0, 0);
                Shape("Cap", PrimitiveType.Sphere, body, new Vector3(0, 0.82f, -0.03f), new Vector3(0.5f, 0.21f, 0.5f), "ClothBlue");
            }
            return root;
        }

        private static void Rod(string name, Transform parent, Vector3 start, Vector3 end, float thickness, string material)
        {
            var rod = Shape(name, PrimitiveType.Cylinder, parent, (start + end) * 0.5f,
                new Vector3(thickness, Vector3.Distance(start, end) * 0.5f, thickness), material);
            rod.transform.localRotation = Quaternion.FromToRotation(Vector3.up, end - start);
        }

        private static AnimationClip CreateClip(string action, string role)
        {
            float duration = action == "Run" ? 0.64f : action == "Hit" ? 0.45f : action == "Death" ? 1.2f : action == "Idle" ? 2 : 0.9f;
            var clip = new AnimationClip { name = role + "_" + action, frameRate = 30 };
            string[] paths = { "Visual", "Visual/BodyPivot", "Visual/BodyPivot/LeftArm", "Visual/BodyPivot/RightArm",
                "Visual/LeftLeg", "Visual/RightLeg" };
            Vector3[] positions = { Vector3.zero, new Vector3(0, 1.1f, 0), new Vector3(-0.45f, 0.25f, 0),
                new Vector3(0.45f, 0.25f, 0), new Vector3(-0.2f, 0.65f, 0), new Vector3(0.2f, 0.65f, 0) };
            for (int index = 0; index < paths.Length; index++)
            {
                foreach (string axis in new[] { "x", "y", "z" })
                {
                    float value = axis == "x" ? positions[index].x : axis == "y" ? positions[index].y : positions[index].z;
                    Curve(clip, paths[index], "m_LocalPosition." + axis, duration, value, value);
                    Curve(clip, paths[index], "localEulerAnglesRaw." + axis, duration, 0, 0);
                }
            }
            const string body = "Visual/BodyPivot";
            const string left = "Visual/BodyPivot/LeftArm";
            const string right = "Visual/BodyPivot/RightArm";
            if (action == "Idle")
            {
                Curve(clip, body, "m_LocalPosition.y", duration, 1.1f, 1.12f, 1.1f);
            }
            else if (action == "Run")
            {
                Curve(clip, "Visual/LeftLeg", "localEulerAnglesRaw.x", duration, 32, 0, -32, 0, 32);
                Curve(clip, "Visual/RightLeg", "localEulerAnglesRaw.x", duration, -32, 0, 32, 0, -32);
                Curve(clip, left, "localEulerAnglesRaw.x", duration, -30, 0, 30, 0, -30);
                Curve(clip, right, "localEulerAnglesRaw.x", duration, 30, 0, -30, 0, 30);
                Curve(clip, "Visual", "m_LocalPosition.y", duration, 0, 0.08f, 0, 0.08f, 0);
                Curve(clip, body, "localEulerAnglesRaw.x", duration, 8, 8);
            }
            else if (action == "Hit")
            {
                Curve(clip, body, "localEulerAnglesRaw.x", duration, 0, -22, -8, 0);
                Curve(clip, "Visual", "m_LocalPosition.z", duration, 0, -0.16f, -0.08f, 0);
                Curve(clip, left, "localEulerAnglesRaw.z", duration, 0, -24, -12, 0);
                Curve(clip, right, "localEulerAnglesRaw.z", duration, 0, 24, 12, 0);
            }
            else if (action == "Jump")
            {
                Curve(clip, "Visual", "m_LocalPosition.y", duration, 0, -0.12f, 0.75f, 1, 0.75f, -0.1f, 0);
                Curve(clip, "Visual/LeftLeg", "localEulerAnglesRaw.x", duration, 0, -20, -40, -45, -30, 12, 0);
                Curve(clip, "Visual/RightLeg", "localEulerAnglesRaw.x", duration, 0, -20, -40, -45, -30, 12, 0);
                Curve(clip, left, "localEulerAnglesRaw.z", duration, 0, -15, -60, -75, -60, -15, 0);
                Curve(clip, right, "localEulerAnglesRaw.z", duration, 0, 15, 60, 75, 60, 15, 0);
            }
            else if (action == "Death")
            {
                Curve(clip, "Visual", "localEulerAnglesRaw.x", duration, 0, -12, -55, -90, -90);
                Curve(clip, "Visual", "m_LocalPosition.y", duration, 0, 0, 0.1f, 0.3f, 0.3f);
                Curve(clip, left, "localEulerAnglesRaw.z", duration, 0, -35, -50, -50, -50);
                Curve(clip, right, "localEulerAnglesRaw.z", duration, 0, 35, 50, 50, 50);
            }
            else if (role == "Archer")
            {
                Curve(clip, left, "localEulerAnglesRaw.x", duration, 0, -90, -90, -90, 0);
                Curve(clip, right, "localEulerAnglesRaw.x", duration, 0, -85, -95, -85, 0);
                Curve(clip, right, "m_LocalPosition.z", duration, 0, 0.1f, -0.3f, 0.15f, 0);
                Curve(clip, body, "localEulerAnglesRaw.y", duration, 0, -20, -20, 0, 0);
            }
            else if (role == "Mage")
            {
                Curve(clip, right, "localEulerAnglesRaw.x", duration, 0, -20, -45, 20, 0);
                Curve(clip, left, "localEulerAnglesRaw.x", duration, 0, -55, -75, -40, 0);
                Curve(clip, body, "localEulerAnglesRaw.x", duration, 0, -10, -5, 12, 0);
            }
            else
            {
                Curve(clip, right, "localEulerAnglesRaw.x", duration, 0, -95, -120, 45, 0);
                Curve(clip, body, "localEulerAnglesRaw.y", duration, 0, -25, -30, 30, 0);
                Curve(clip, left, "localEulerAnglesRaw.x", duration, 0, -25, -35, -20, 0);
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = action == "Run" || action == "Idle";
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return Save(clip, Characters + "/" + role + "/Animations/" + role + "_" + action + ".anim");
        }

        private static void Curve(AnimationClip clip, string path, string property, float duration, params float[] values)
        {
            var keys = new Keyframe[values.Length];
            for (int index = 0; index < values.Length; index++)
            {
                keys[index] = new Keyframe(duration * index / (values.Length - 1), values[index]);
            }
            var curve = new AnimationCurve(keys);
            for (int index = 0; index < keys.Length; index++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.ClampedAuto);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, typeof(Transform), property), curve);
        }

        private static void CreateCharacterPreview()
        {
            ClearWorkScene();
            Box("Stage", null, new Vector3(0, -0.12f, 0), new Vector3(15, 0.24f, 8), "Stone");
            string[] roles = { "Warrior", "Archer", "Mage", "Merchant" };
            var actors = new List<GameObject>();
            for (int index = 0; index < roles.Length; index++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Characters + "/" + roles[index] + "/" + roles[index] + ".prefab");
                var actor = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                actor.transform.position = new Vector3(-4.5f + index * 3, 0, 0);
                actors.Add(actor);
            }
            var camera = EnvironmentCamera(new Vector3(4, 4, 11), new Vector3(0, 1, 0), 43);
            Capture(camera, "Characters", 1600, 900);
            foreach (string action in new[] { "Run", "Attack", "Hit", "Jump", "Death" })
            {
                for (int index = 0; index < actors.Count; index++)
                {
                    string clipPath = Characters + "/" + roles[index] + "/Animations/" + roles[index] + "_" + action + ".anim";
                    var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                    clip.SampleAnimation(actors[index], clip.length * (action == "Death" ? 1 : 0.5f));
                }
                Capture(camera, "Characters_" + action, 1600, 900);
            }
        }
    }
}
