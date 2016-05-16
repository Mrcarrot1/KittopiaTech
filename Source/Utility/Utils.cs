/*=======================================================================================================*\
 * This code is partitially by Kragrathea (GPL) and by the Kopernicus-Team (LGPL). Used with permission. *
\*=======================================================================================================*/

using Kopernicus.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace Kopernicus
{
    namespace UI
    {
        // Collection of Utillity-functions
        public class Utils
        {
            public static void Generate(CelestialBody body, Mesh meshinput)
            {
                PQS bodyPQS = body.pqsController;
                Single joolScaledRad = 1000f;
                Single joolRad = 6000000f;
                Single scale = (float) bodyPQS.radius/joolScaledRad;

                Vector3[] vertices = new Vector3[meshinput.vertices.Count()];

                // One could use pqs.radiusMin and pqs.radiusMax to determine minimum and maximum height.
                // But to be safe, the height limit values will be determined manually.
                Single radiusMin = 0;
                Single radiusMax = 0;

                bodyPQS.isBuildingMaps = true;
                for (Int32 i = 0; i < meshinput.vertices.Count(); i++)
                {
                    Vector3 vertex = meshinput.vertices[i];
                    Single rootrad = (float) Math.Sqrt(vertex.x*vertex.x +
                                                       vertex.y*vertex.y +
                                                       vertex.z*vertex.z);
                    Single localRadius = (float) bodyPQS.GetSurfaceHeight(vertex)/scale;
                    vertices[i] = vertex*(localRadius/rootrad);

                    if (i == 0)
                    {
                        radiusMin = radiusMax = localRadius;
                    }
                    else
                    {
                        if (radiusMin > localRadius) radiusMin = localRadius;
                        if (radiusMax < localRadius) radiusMax = localRadius;
                    }
                }
                bodyPQS.isBuildingMaps = false;

                // Adjust the mesh so the maximum radius has 1000 unit in scaled space.
                // (so the planets will fit in the science archive list)
                Single r = radiusMax/1000;
                for (Int32 i = 0; i < vertices.Count(); i++)
                {
                    vertices[i] /= r;
                }

                // Use the lowest radius as collision radius.
                Single radius = radiusMin/r;

                // Calculate the local scale.
                Vector3 localScale = Vector3.one*((float) bodyPQS.radius/joolRad)*r;

                // Apply the mesh to ScaledSpace
                MeshFilter meshfilter = body.scaledBody.GetComponent<MeshFilter>();
                SphereCollider collider = body.scaledBody.GetComponent<SphereCollider>();
                meshfilter.sharedMesh.vertices = vertices;
                meshfilter.sharedMesh.RecalculateNormals();
                Utility.RecalculateTangents(meshfilter.sharedMesh);
                collider.radius = radius;
                body.scaledBody.transform.localScale = localScale;
            }

            /// <summary>
            /// Returns the CelestialBody Component for a Body
            /// </summary>
            public static CelestialBody FindCB(string name)
            {
                return PSystemManager.Instance.localBodies.Find(b => b.name == name);
            }

            /// <summary>
            /// Loads a Texture from GameDatabase or from the Game-Assets
            /// </summary>
            public static Texture2D LoadTexture(string path)
            {
                Texture2DParser parser = new Texture2DParser();
                parser.SetFromString(path);
                return parser.value;
            }

            /// <summary>
            /// Updates the Atmosphere-Ramp in ScaledSpace for a body
            /// </summary>
            public static void UpdateAtmosphereRamp(CelestialBody body, Texture2D texture)
            {
                GameObject scaledVersion = body.scaledBody;
                MeshRenderer meshRenderer = scaledVersion.GetComponentInChildren<MeshRenderer>();
                meshRenderer.material.SetTexture("_rimColorRamp", texture);
            }

            /// <summary>
            /// [HACK] Spawn a new body from the PSystem-Prefab
            /// </summary>
            public static void Instantiate(PSystemBody template, string name)
            {
                // Fix Templates
                if (template == null)
                {
                    ScreenMessages.PostScreenMessage("You need a valid Template!", 3f, ScreenMessageStyle.UPPER_CENTER);
                    return;
                }

                // Spawn Message
                ScreenMessages.PostScreenMessage("Created new Planet " + name + ", based on " + template.name + "!", 5f, ScreenMessageStyle.UPPER_CENTER);
                ScreenMessages.PostScreenMessage("This tool is meant to be used by modders, it can break mods!", 5f, ScreenMessageStyle.UPPER_CENTER);

                // Clone the Template
                GameObject bodyObject = UnityEngine.Object.Instantiate(template.gameObject);
                PSystemBody body = bodyObject.GetComponent<PSystemBody>();

                // Alter it's name and flight-Number
                body.name = name;
                body.celestialBody.bodyName = name;
                body.celestialBody.transform.name = name;
                body.celestialBody.bodyTransform.name = name;
                body.scaledVersion.name = name;
                if (body.pqsVersion != null)
                {
                    body.pqsVersion.name = name;
                    body.pqsVersion.gameObject.name = name;
                    body.pqsVersion.transform.name = name;
                    foreach (PQS p in body.pqsVersion.GetComponentsInChildren(typeof (PQS), true))
                        p.name = p.name.Replace(template.celestialBody.bodyName, name);
                }
                body.flightGlobalsIndex = PSystemManager.Instance.localBodies.Last().flightGlobalsIndex + 1;

                // Change it's Orbit
                body.orbitDriver.orbit = Orbit.CreateRandomOrbitAround(PSystemManager.Instance.localBodies.First(), 4000000000, 60000000000);
                body.orbitDriver.referenceBody = PSystemManager.Instance.localBodies.First();
                body.orbitDriver.orbit.referenceBody = body.orbitDriver.referenceBody;
                body.orbitRenderer.lowerCamVsSmaRatio = template.orbitRenderer.lowerCamVsSmaRatio;
                body.orbitRenderer.upperCamVsSmaRatio = template.orbitRenderer.upperCamVsSmaRatio;

                // Clear it's childs
                body.children = new List<PSystemBody>();

                // Add it to the System-Prefab
                body.transform.parent = PSystemManager.Instance.systemPrefab.transform;
                PSystemManager.Instance.systemPrefab.rootBody.children.Add(body);

                // Hack^6 - Hack the PSystemManager to spawn this thing
                MethodInfo spawnBody = typeof (PSystemManager).GetMethod("SpawnBody", BindingFlags.NonPublic | BindingFlags.Instance);
                spawnBody.Invoke(PSystemManager.Instance, new object[] {PSystemManager.Instance.localBodies.First(), body});
                CelestialBody cBody = PSystemManager.Instance.localBodies.Last();

                // Add the body to FlightGlobals.Bodies
                FlightGlobals.fetch.bodies.Add(cBody);

                // Start the CelestialBody
                typeof (CelestialBody).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cBody, null);

                // Start the OrbitDriver
                if (cBody.orbitDriver != null)
                    typeof (OrbitDriver).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cBody.orbitDriver, null);

                // Fix and start the OrbitRenderer
                if (Resources.FindObjectsOfTypeAll<OrbitRenderer>().Count(r => r.name == cBody.name) == 1)
                {
                    OrbitRenderer renderer = Resources.FindObjectsOfTypeAll<OrbitRenderer>().Where(r => r.name == cBody.name).First();
                    renderer.driver = cBody.orbitDriver;
                    renderer.celestialBody = cBody;
                    typeof (OrbitRenderer).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(renderer, null);
                }

                // Force the start of the PQS-Spheres
                foreach (PQS p in cBody.GetComponentsInChildren<PQS>(true))
                    p.ForceStart();

                // Fix the ScaledVersion
                if (cBody.scaledBody.GetComponents<ScaledSpaceFader>().Length == 1)
                    cBody.scaledBody.GetComponent<ScaledSpaceFader>().celestialBody = cBody;
                if (cBody.scaledBody.GetComponents<AtmosphereFromGround>().Length == 1)
                    cBody.scaledBody.GetComponent<AtmosphereFromGround>().planet = cBody;
            }

            /// <summary>
            /// Generate the scaled space Textures using PQS in a Coroutine
            /// </summary>
            public static void GeneratePQSMaps(CelestialBody body)
            {
                // Get time
                DateTime now = DateTime.Now;

                // Get PQS
                PQS pqs = body.pqsController;
                pqs.SetupExternalRender();

                // Get the Mod-Building Methods, because I'm lazy :P 
                MethodInfo modOnVertexBuildHeight = typeof (PQS).GetMethod("Mod_OnVertexBuildHeight", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo modOnVertexBuild = typeof (PQS).GetMethod("Mod_OnVertexBuild", BindingFlags.Instance | BindingFlags.NonPublic);

                // Arrays
                Color[] colorMapValues = new Color[pqs.mapFilesize*(pqs.mapFilesize/2)];
                Color[] heightMapValues = new Color[pqs.mapFilesize*(pqs.mapFilesize/2)];

                // Create the Textures
                Texture2D colorMap = new Texture2D(pqs.mapFilesize, pqs.mapFilesize/2, TextureFormat.ARGB32, true);
                Texture2D heightMap = new Texture2D(pqs.mapFilesize, pqs.mapFilesize/2, TextureFormat.RGB24, true);
                
                // Stuff
                ScreenMessage message = ScreenMessages.PostScreenMessage("Generating Planet-Maps", Single.MaxValue, ScreenMessageStyle.UPPER_CENTER);

                // Loop through the pixels
                for (int y = 0; y < (pqs.mapFilesize/2); y++)
                {
                    for (int x = 0; x < pqs.mapFilesize; x++)
                    {
                        // Update Message
                        Double percent = ((double) ((y*pqs.mapFilesize) + x)/((pqs.mapFilesize/2)*pqs.mapFilesize))*100;
                        while (CanvasUpdateRegistry.IsRebuildingLayout()) Thread.Sleep(10);
                        message.textInstance.text.text = "Generating Planet-Maps: " + percent.ToString("0.00") + "%";

                        // Create a VertexBuildData
                        PQS.VertexBuildData data = new PQS.VertexBuildData
                        {
                            directionFromCenter = (QuaternionD.AngleAxis((360d/pqs.mapFilesize)*x, Vector3d.up)*QuaternionD.AngleAxis(90d - (180d/(pqs.mapFilesize/2))*y, Vector3d.right))*Vector3d.forward,
                            vertHeight = pqs.radius
                        };

                        // Build from the Mods 
                        modOnVertexBuildHeight.Invoke(pqs, new[] {data});
                        modOnVertexBuild.Invoke(pqs, new[] {data});

                        // Adjust the height
                        double height = (data.vertHeight - pqs.radius)*(1d/pqs.mapMaxHeight);
                        if (height < 0)
                            height = 0;
                        else if (height > 1)
                            height = 1;

                        // Adjust the Color
                        Color color = data.vertColor;
                        color.a = 1f;
                        if (pqs.mapOcean && height <= pqs.mapOceanHeight)
                            color = pqs.mapOceanColor;

                        // Set the Pixels
                        colorMapValues[(y*pqs.mapFilesize) + x] = color;
                        heightMapValues[(y*pqs.mapFilesize) + x] = new Color((Single) height, (Single) height, (Single) height);
                    }
                }

                // Apply the maps
                colorMap.SetPixels(colorMapValues);
                colorMap.Apply();
                heightMap.SetPixels(heightMapValues);

                // Close the Renderer
                pqs.CloseExternalRender();

                // Bump to Normal Map
                Texture2D normalMap = Utility.BumpToNormalMap(heightMap, UIController.NormalStrength);

                // Serialize them to disk
                string path = KSPUtil.ApplicationRootPath + "/GameData/KittopiaTech/Textures/" + body.name + "/";
                Directory.CreateDirectory(path);
                File.WriteAllBytes(path + body.name + "_Color.png", colorMap.EncodeToPNG());
                File.WriteAllBytes(path + body.name + "_Height.png", heightMap.EncodeToPNG());
                File.WriteAllBytes(path + body.name + "_Normal.png", normalMap.EncodeToPNG());

                // Apply them to the ScaledVersion
                body.scaledBody.GetComponent<MeshRenderer>().material.SetTexture("_MainTex", colorMap);
                body.scaledBody.GetComponent<MeshRenderer>().material.SetTexture("_BumpMap", normalMap);

                // Declare that we're done
                ScreenMessages.RemoveMessage(message);
                ScreenMessages.PostScreenMessage("Operation completed in: " + (DateTime.Now - now).TotalMilliseconds + " ms", 2f, ScreenMessageStyle.UPPER_CENTER);
            }

            /// <summary>
            /// Searches for a node in a Kopernicus-Body definition
            /// </summary>
            public static ConfigNode SearchNode(string name, string planet, ConfigNode node = null)
            {
                // Get the Body-Node
                if (node == null)
                {
                    ConfigNode root = GameDatabase.Instance.GetConfigNodes("Kopernicus").First();
                    IEnumerable<ConfigNode> bodies = root.GetNodes().Where(n => n.HasValue("name") && n.GetValue("name") == planet);

                    // If we didn't found a body, abort
                    if (!bodies.Any())
                        return null;

                    node = bodies.First();
                }

                // Are we already the searched node?
                if (node.name == name)
                    return node;

                // Loop through the nodes, to find the node
                return (from ConfigNode subNode in node.nodes select SearchNode(name, planet, subNode)).FirstOrDefault(result => result != null);
            }

            /// <summary>
            /// Returns the Default Ring-Texture
            /// </summary>
            public static Texture2D defaultRing
            {
                get { return LoadTexture("KittopiaTech/Textures/ring"); }
            }

            /// <summary>
            /// Returns the Types that are supported by the Renderer
            /// </summary>
            public static Type[] supportedTypes
            {
                get
                {
                    return new Type[]
                    {
                        typeof(string),
                        typeof(bool),
                        typeof(int),
                        typeof(float),
                        typeof(double),
                        typeof(Color),
                        typeof(Vector3),
                        typeof(Vector3d),
                        typeof(Vector2),
                        typeof(Vector2d),
                        typeof(PQSLandControl.LandClass[]),
                        typeof(PQSLandControl.LerpRange),
                        typeof(PQSMod_VertexPlanet.LandClass[]),
                        typeof(PQSMod_HeightColorMap.LandClass[]),
                        typeof(PQS),
                        typeof(PQSMod_VertexPlanet.SimplexWrapper),
                        typeof(PQSMod_VertexPlanet.NoiseModWrapper),
                        typeof(MapSO),
                        typeof(CBAttributeMapSO),
                        typeof(Texture2D),
                        typeof(Texture),
                        typeof(Material),
                        typeof(CelestialBody),
                        typeof(FloatCurve),
                        typeof(AnimationCurve),
                        typeof(Mesh)
                    };
                }
            }

            /// <summary>
            /// Returns all supported Field/Property Infos from an Object
            /// </summary>
            public static object[] GetInfos<T>(object o)
            {
                // Get the Type of the Object
                Type t = o.GetType();

                // Array to store the Infos
                object[] infos = new object[0];

                // If T is a FieldInfo, get all the Fields
                if (typeof(T) == typeof(FieldInfo))
                {
                    infos = t.GetFields().Where(f => supportedTypes.Contains(f.FieldType) && !f.IsLiteral).ToArray();
                }

                // If T is a PropertyInfo, get all the Properties
                if (typeof(T) == typeof(PropertyInfo))
                {
                    infos = t.GetProperties().Where(p => supportedTypes.Contains(p.PropertyType) && p.CanRead && p.CanWrite).ToArray();
                }

                // Return the array
                return infos;
            }

            /// <summary>
            /// Returns the Size for a Scrollbar
            /// </summary>
            public static int GetScrollSize(object[] infos)
            {
                // Integer to store the values
                int scrollSize = 0;

                // Get the count of the array
                scrollSize += infos.Length * 25;

                // Handle special things
                if (infos[0] is FieldInfo)
                {
                    scrollSize += infos.Where(o => (o as FieldInfo).FieldType == typeof(MapSO) || (o as FieldInfo).FieldType == typeof(CBAttributeMapSO)).ToArray().Length * 25;
                }
                else if (infos[0] is PropertyInfo)
                {
                    scrollSize += infos.Where(o => (o as PropertyInfo).PropertyType == typeof(MapSO) || (o as PropertyInfo).PropertyType == typeof(CBAttributeMapSO)).ToArray().Length * 25;
                }

                // Return the Scroll-Size
                return scrollSize;
            }

            /// <summary>
            /// Renders a generic Editor for Planetary Params
            /// </summary>
            public static void RenderSelection<T>(object[] infos, ref object obj, ref int offset)
            {
                // Use an empty int for mapDepth
                int mapDepth = 0;

                // Render the Editor
                RenderSelection<T>(infos, ref obj, ref offset, ref mapDepth);
            }

            /// <summary>
            /// Renders a generic Editor for Planetary Params
            /// </summary>
            public static void RenderSelection<T>(object[] infos, ref object obj, ref int offset, ref int mapDepth)
            {
                if (typeof(T) == typeof(FieldInfo))
                {
                    // Loop through all fields and display them
                    foreach (FieldInfo key in (infos as FieldInfo[]))
                    {
                        if (key.FieldType == typeof(string))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, GUI.TextField(new Rect(200, offset, 170, 20), "" + key.GetValue(obj)));
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(bool))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, GUI.Toggle(new Rect(200, offset, 170, 20), (bool)key.GetValue(obj), "Bool"));
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(int))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, Int32.Parse(GUI.TextField(new Rect(200, offset, 170, 20), "" + key.GetValue(obj))));
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(float))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, Single.Parse(GUI.TextField(new Rect(200, offset, 170, 20), "" + key.GetValue(obj))));
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(double))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, Double.Parse(GUI.TextField(new Rect(200, offset, 170, 20), "" + key.GetValue(obj))));
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(Color))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 50, 20), "Edit"))
                                ColorPicker.SetEditedObject(key, (Color)key.GetValue(obj), obj);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(Vector3))
                        {
                            GUI.Label(new Rect(20, offset, 200, 20), "" + key.Name);
                            Vector3 value = (Vector3)key.GetValue(obj);
                            value.x = Single.Parse(GUI.TextField(new Rect(200, offset, 50, 20), "" + value.x));
                            value.y = Single.Parse(GUI.TextField(new Rect(260, offset, 50, 20), "" + value.y));
                            value.z = Single.Parse(GUI.TextField(new Rect(320, offset, 50, 20), "" + value.z));
                            key.SetValue(obj, value);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(Vector3d))
                        {
                            GUI.Label(new Rect(20, offset, 200, 20), "" + key.Name);
                            Vector3d value = (Vector3d)key.GetValue(obj);
                            value.x = Double.Parse(GUI.TextField(new Rect(200, offset, 50, 20), "" + value.x));
                            value.y = Double.Parse(GUI.TextField(new Rect(260, offset, 50, 20), "" + value.y));
                            value.z = Double.Parse(GUI.TextField(new Rect(320, offset, 50, 20), "" + value.z));
                            key.SetValue(obj, value);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(Vector2))
                        {
                            GUI.Label(new Rect(20, offset, 200, 20), "" + key.Name);
                            Vector2 value = (Vector2)key.GetValue(obj);
                            value.x = Single.Parse(GUI.TextField(new Rect(200, offset, 75, 20), "" + value.x));
                            value.y = Single.Parse(GUI.TextField(new Rect(285, offset, 75, 20), "" + value.y));
                            key.SetValue(obj, value);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(Vector2d))
                        {
                            GUI.Label(new Rect(20, offset, 200, 20), "" + key.Name);
                            Vector2d value = (Vector2d)key.GetValue(obj);
                            value.x = Double.Parse(GUI.TextField(new Rect(200, offset, 75, 20), "" + value.x));
                            value.y = Double.Parse(GUI.TextField(new Rect(285, offset, 75, 20), "" + value.y));
                            key.SetValue(obj, value);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(CBAttributeMapSO))
                        {
                            // Load the MapSO
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 80, 20), "Load"))
                            {
                                UIController.Instance.isFileBrowser = !UIController.Instance.isFileBrowser;
                                FileBrowser.type = key.FieldType;
                                FileBrowser.location = "";
                            }

                            // Apply the new MapSO
                            if (GUI.Button(new Rect(290, offset, 80, 20), "Apply"))
                            {
                                if (!FileBrowser.builtin)
                                {
                                    string path = FileBrowser.location.Replace(Path.Combine(Directory.GetCurrentDirectory(), "GameData") + Path.DirectorySeparatorChar, "");
                                    Texture2D texture = Utility.LoadTexture(path, false, false, false);
                                    texture.name = path.Replace("\\", "/");
                                    CBAttributeMapSO mapSO = ScriptableObject.CreateInstance<CBAttributeMapSO>();
                                    mapSO.exactSearch = false;
                                    mapSO.nonExactThreshold = 0.05f;
                                    mapSO.CreateMap(MapSO.MapDepth.RGB, texture);
                                    mapSO.Attributes = (key.GetValue(obj) as CBAttributeMapSO).Attributes;
                                    mapSO.name = path.Replace("\\", "/");
                                    key.SetValue(obj, mapSO);
                                }
                                else
                                {
                                    key.SetValue(obj, FileBrowser.value);
                                }
                            }
                            offset += 25;

                            // Edit the Biome-Definitions
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Mod Biomes"))
                            {
                                FieldInfo attributes = typeof(CBAttributeMapSO).GetField("Attributes");
                                object att = attributes.GetValue(key.GetValue(obj));
                                BiomeModifier.SetEditedObject(att as CBAttributeMapSO.MapAttribute[], attributes, key.GetValue(obj));
                            }
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(Texture2D) || key.FieldType == typeof(Texture))
                        {
                            // Load the Texture
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 80, 20), "Load"))
                            {
                                UIController.Instance.isFileBrowser = !UIController.Instance.isFileBrowser;
                                FileBrowser.type = key.FieldType;
                                FileBrowser.location = "";
                            }

                            // Apply the new Texture
                            if (GUI.Button(new Rect(290, offset, 80, 20), "Apply"))
                            {
                                if (!FileBrowser.builtin)
                                {
                                    string path = FileBrowser.location.Replace(Path.Combine(Directory.GetCurrentDirectory(), "GameData") + Path.DirectorySeparatorChar, "");
                                    Texture2D texture = Utility.LoadTexture(path, false, false, false);
                                    texture.name = path.Replace("\\", "/");
                                    key.SetValue(obj, texture);
                                }
                                else
                                {
                                    key.SetValue(obj, FileBrowser.value);
                                }
                            }
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(PQSLandControl.LandClass[]))
                        {
                            if (GUI.Button(new Rect(20, offset, 178, 20), "Mod Land Classes"))
                                LandClassModifier.SetEditedObject((PQSLandControl.LandClass[])key.GetValue(obj), key, obj);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(PQSMod_VertexPlanet.LandClass[]))
                        {
                            if (GUI.Button(new Rect(20, offset, 178, 20), "Mod Land Classes"))
                                LandClassModifier.SetEditedObject((PQSMod_VertexPlanet.LandClass[])key.GetValue(obj), key, obj);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(PQSMod_HeightColorMap.LandClass[]))
                        {
                            if (GUI.Button(new Rect(20, offset, 178, 20), "Mod Land Classes"))
                                LandClassModifier.SetEditedObject((PQSMod_HeightColorMap.LandClass[])key.GetValue(obj), key, obj);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(PQSLandControl.LerpRange))
                        {
                            if (GUI.Button(new Rect(20, offset, 178, 20), "" + key.Name))
                                LerpRange.SetEditedObject((PQSLandControl.LerpRange)key.GetValue(obj));
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(PQSMod_VertexPlanet.SimplexWrapper))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Simplex Wrapper"))
                                SimplexWrapper.SetEditedObject((PQSMod_VertexPlanet.SimplexWrapper)key.GetValue(obj));
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(PQSMod_VertexPlanet.NoiseModWrapper))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit NoiseMod Wrapper"))
                                NoiseModWrapper.SetEditedObject((PQSMod_VertexPlanet.NoiseModWrapper)key.GetValue(obj));
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(MapSO))
                        {
                            // Stuff
                            if (mapDepth == 5 && key.GetValue(obj) != null)
                                mapDepth = (int)(key.GetValue(obj) as MapSO).Depth;
                            else if (mapDepth == 5 && key.GetValue(obj) == null)
                                mapDepth = 0;

                            // Load the MapSO
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 80, 20), "Load"))
                            {
                                UIController.Instance.isFileBrowser = !UIController.Instance.isFileBrowser;
                                FileBrowser.type = key.FieldType;
                                FileBrowser.location = "";
                            }

                            // Apply the new MapSO
                            if (GUI.Button(new Rect(290, offset, 80, 20), "Apply"))
                            {
                                if (!FileBrowser.builtin)
                                {
                                    string path = FileBrowser.location.Replace(Path.Combine(Directory.GetCurrentDirectory(), "GameData") + Path.DirectorySeparatorChar, "");
                                    Texture2D texture = Utility.LoadTexture(path, false, false, false);
                                    MapSO mapSO = ScriptableObject.CreateInstance<MapSO>();
                                    mapSO.CreateMap((MapSO.MapDepth)mapDepth, texture);
                                    mapSO.name = path.Replace("\\", "/");
                                    key.SetValue(obj, mapSO);
                                }
                                else
                                {
                                    key.SetValue(obj, FileBrowser.value);
                                }
                            }
                            offset += 25;
                            mapDepth = GUI.SelectionGrid(new Rect(20, offset, 350, 20), mapDepth, new string[] { "Greyscale", "HeightAlpha", "RGB", "RGBA" }, 4);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(PQS))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Sphere"))
                                PQSBrowser.SetEditedObject(key, obj);
                            offset += 25;
                        }
                        else if (key.GetValue(obj) is Material) // Kopernicus creates Wrappers for the Materials, so key.FieldType == typeof(Material) would return false. :/
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Material"))
                                MaterialEditor.SetEditedObject(key.GetValue(obj) as Material, key, obj);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(FloatCurve))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Curve"))
                                CurveWindow.SetEditedObject(key.GetValue(obj) as FloatCurve, key, obj, false);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(AnimationCurve))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Curve"))
                                CurveWindow.SetEditedObject(new FloatCurve((key.GetValue(obj) as AnimationCurve).keys), key, obj, true);
                            offset += 25;
                        }
                        else if (key.FieldType == typeof(Mesh))
                        {
                            // Load the Texture
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 80, 20), "Load"))
                            {
                                UIController.Instance.isFileBrowser = !UIController.Instance.isFileBrowser;
                                FileBrowser.type = key.FieldType;
                                FileBrowser.location = "";
                            }

                            // Apply the new Texture
                            if (GUI.Button(new Rect(290, offset, 80, 20), "Apply"))
                            {
                                if (!FileBrowser.builtin)
                                {
                                    string path = FileBrowser.location.Replace(Path.Combine(Directory.GetCurrentDirectory(), "GameData") + Path.DirectorySeparatorChar, "");
                                    MeshParser parser = new MeshParser(key.GetValue(obj) as Mesh);
                                    parser.SetFromString(path);
                                    parser.value.name = path.Replace("\\", "/");
                                    key.SetValue(obj, parser.value);
                                }
                                else
                                {
                                    key.SetValue(obj, FileBrowser.value);
                                }
                            }
                            offset += 25;
                        }
                    }
                }
                else if (typeof(T) == typeof(PropertyInfo))
                {
                    // Loop through all fields and display them
                    foreach (PropertyInfo key in (infos as PropertyInfo[]))
                    {
                        if (key.PropertyType == typeof(string))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, GUI.TextField(new Rect(200, offset, 170, 20), "" + key.GetValue(obj, null)), null);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(bool))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, GUI.Toggle(new Rect(200, offset, 170, 20), (bool)key.GetValue(obj, null), "Bool"), null);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(int))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, Int32.Parse(GUI.TextField(new Rect(200, offset, 170, 20), "" + key.GetValue(obj, null))), null);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(float))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, Single.Parse(GUI.TextField(new Rect(200, offset, 170, 20), "" + key.GetValue(obj, null))), null);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(double))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            key.SetValue(obj, Double.Parse(GUI.TextField(new Rect(200, offset, 170, 20), "" + key.GetValue(obj, null))), null);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(Color))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 50, 20), "Edit"))
                                ColorPicker.SetEditedObject(key, (Color)key.GetValue(obj, null), obj);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(Vector3))
                        {
                            GUI.Label(new Rect(20, offset, 200, 20), "" + key.Name);
                            Vector3 value = (Vector3)key.GetValue(obj, null);
                            value.x = Single.Parse(GUI.TextField(new Rect(200, offset, 50, 20), "" + value.x));
                            value.y = Single.Parse(GUI.TextField(new Rect(260, offset, 50, 20), "" + value.y));
                            value.z = Single.Parse(GUI.TextField(new Rect(320, offset, 50, 20), "" + value.z));
                            key.SetValue(obj, value, null);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(Vector3d))
                        {
                            GUI.Label(new Rect(20, offset, 200, 20), "" + key.Name);
                            Vector3d value = (Vector3d)key.GetValue(obj, null);
                            value.x = Double.Parse(GUI.TextField(new Rect(200, offset, 50, 20), "" + value.x));
                            value.y = Double.Parse(GUI.TextField(new Rect(260, offset, 50, 20), "" + value.y));
                            value.z = Double.Parse(GUI.TextField(new Rect(320, offset, 50, 20), "" + value.z));
                            key.SetValue(obj, value, null);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(Vector2))
                        {
                            GUI.Label(new Rect(20, offset, 200, 20), "" + key.Name);
                            Vector2 value = (Vector2)key.GetValue(obj, null);
                            value.x = Single.Parse(GUI.TextField(new Rect(200, offset, 75, 20), "" + value.x));
                            value.y = Single.Parse(GUI.TextField(new Rect(285, offset, 75, 20), "" + value.y));
                            key.SetValue(obj, value, null);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(Vector2d))
                        {
                            GUI.Label(new Rect(20, offset, 200, 20), "" + key.Name);
                            Vector2d value = (Vector2d)key.GetValue(obj, null);
                            value.x = Double.Parse(GUI.TextField(new Rect(200, offset, 75, 20), "" + value.x));
                            value.y = Double.Parse(GUI.TextField(new Rect(285, offset, 75, 20), "" + value.y));
                            key.SetValue(obj, value, null);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(CBAttributeMapSO))
                        {
                            // Load the MapSO
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 80, 20), "Load"))
                            {
                                UIController.Instance.isFileBrowser = !UIController.Instance.isFileBrowser;
                                FileBrowser.type = key.PropertyType;
                                FileBrowser.location = "";
                            }

                            // Apply the new MapSO
                            if (GUI.Button(new Rect(290, offset, 80, 20), "Apply"))
                            {
                                if (!FileBrowser.builtin)
                                {
                                    string path = FileBrowser.location.Replace(Path.Combine(Directory.GetCurrentDirectory(), "GameData") + Path.DirectorySeparatorChar, "");
                                    Texture2D texture = Utility.LoadTexture(path, false, false, false);
                                    texture.name = path.Replace("\\", "/");
                                    CBAttributeMapSO mapSO = ScriptableObject.CreateInstance<CBAttributeMapSO>();
                                    mapSO.exactSearch = false;
                                    mapSO.nonExactThreshold = 0.05f;
                                    mapSO.CreateMap(MapSO.MapDepth.RGB, texture);
                                    mapSO.Attributes = (key.GetValue(obj, null) as CBAttributeMapSO).Attributes;
                                    key.SetValue(obj, mapSO, null);
                                }
                                else
                                {
                                    key.SetValue(obj, FileBrowser.value, null);
                                }
                            }
                            offset += 25;

                            // Edit the Biome-Definitions
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Mod Biomes"))
                            {
                                FieldInfo attributes = typeof(CBAttributeMapSO).GetField("Attributes");
                                object att = attributes.GetValue(key.GetValue(obj, null));
                                BiomeModifier.SetEditedObject(att as CBAttributeMapSO.MapAttribute[], attributes, key.GetValue(obj, null));
                            }
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(Texture2D) || key.PropertyType == typeof(Texture))
                        {
                            // Load the Texture
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 80, 20), "Load"))
                            {
                                UIController.Instance.isFileBrowser = !UIController.Instance.isFileBrowser;
                                FileBrowser.type = key.PropertyType;
                                FileBrowser.location = "";
                            }

                            // Apply the new Texture
                            if (GUI.Button(new Rect(290, offset, 80, 20), "Apply"))
                            {
                                if (!FileBrowser.builtin)
                                {
                                    string path = FileBrowser.location.Replace(Path.Combine(Directory.GetCurrentDirectory(), "GameData") + Path.DirectorySeparatorChar, "");
                                    Texture2D texture = Utility.LoadTexture(path, false, false, false);
                                    texture.name = path.Replace("\\", "/");
                                    key.SetValue(obj, texture, null);
                                }
                                else
                                {
                                    key.SetValue(obj, FileBrowser.value, null);
                                }
                            }
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(PQSLandControl.LandClass[]))
                        {
                            if (GUI.Button(new Rect(20, offset, 178, 20), "Mod Land Classes"))
                                LandClassModifier.SetEditedObject((PQSLandControl.LandClass[])key.GetValue(obj, null), key, obj);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(PQSMod_VertexPlanet.LandClass[]))
                        {
                            if (GUI.Button(new Rect(20, offset, 178, 20), "Mod Land Classes"))
                                LandClassModifier.SetEditedObject((PQSMod_VertexPlanet.LandClass[])key.GetValue(obj, null), key, obj);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(PQSMod_HeightColorMap.LandClass[]))
                        {
                            if (GUI.Button(new Rect(20, offset, 178, 20), "Mod Land Classes"))
                                LandClassModifier.SetEditedObject((PQSMod_HeightColorMap.LandClass[])key.GetValue(obj, null), key, obj);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(PQSLandControl.LerpRange))
                        {
                            if (GUI.Button(new Rect(20, offset, 178, 20), "" + key.Name))
                                LerpRange.SetEditedObject((PQSLandControl.LerpRange)key.GetValue(obj, null));
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(PQSMod_VertexPlanet.SimplexWrapper))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Simplex Wrapper"))
                                SimplexWrapper.SetEditedObject((PQSMod_VertexPlanet.SimplexWrapper)key.GetValue(obj, null));
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(PQSMod_VertexPlanet.NoiseModWrapper))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit NoiseMod Wrapper"))
                                NoiseModWrapper.SetEditedObject((PQSMod_VertexPlanet.NoiseModWrapper)key.GetValue(obj, null));
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(MapSO))
                        {
                            // Stuff
                            if (mapDepth == 5 && key.GetValue(obj, null) != null)
                                mapDepth = (int)(key.GetValue(obj, null) as MapSO).Depth;
                            else if (mapDepth == 5 && key.GetValue(obj, null) == null)
                                mapDepth = 0;

                            // Load the MapSO
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 80, 20), "Load"))
                            {
                                UIController.Instance.isFileBrowser = !UIController.Instance.isFileBrowser;
                                FileBrowser.type = key.PropertyType;
                                FileBrowser.location = "";
                            }

                            // Apply the new MapSO
                            if (GUI.Button(new Rect(290, offset, 80, 20), "Apply"))
                            {
                                if (!FileBrowser.builtin)
                                {
                                    string path = FileBrowser.location.Replace(Path.Combine(Directory.GetCurrentDirectory(), "GameData") + Path.DirectorySeparatorChar, "");
                                    Texture2D texture = Utility.LoadTexture(path, false, false, false);
                                    MapSO mapSO = ScriptableObject.CreateInstance<MapSO>();
                                    mapSO.CreateMap((MapSO.MapDepth)mapDepth, texture);
                                    key.SetValue(obj, mapSO, null);
                                }
                                else
                                {
                                    key.SetValue(obj, FileBrowser.value, null);
                                }
                            }
                            offset += 25;
                            mapDepth = GUI.SelectionGrid(new Rect(20, offset, 350, 20), mapDepth, new string[] { "Greyscale", "HeightAlpha", "RGB", "RGBA" }, 4);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(PQS))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Sphere"))
                                PQSBrowser.SetEditedObject(key, obj);
                            offset += 25;
                        }
                        else if (key.GetValue(obj, null) is Material) // Kopernicus creates Wrappers for the Materials, so key.FieldType == typeof(Material) would return false. :/
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Material"))
                                MaterialEditor.SetEditedObject(key.GetValue(obj, null) as Material, key, obj);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(CelestialBody))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Body"))
                            {
                                CBBrowser.SetEditedObject(key, obj, false);
                            }
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(FloatCurve))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Curve"))
                                CurveWindow.SetEditedObject(key.GetValue(obj, null) as FloatCurve, key, obj, false);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(AnimationCurve))
                        {
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 170, 20), "Edit Curve"))
                                CurveWindow.SetEditedObject(new FloatCurve((key.GetValue(obj, null) as AnimationCurve).keys), key, obj, true);
                            offset += 25;
                        }
                        else if (key.PropertyType == typeof(Mesh))
                        {
                            // Load the Texture
                            GUI.Label(new Rect(20, offset, 178, 20), "" + key.Name);
                            if (GUI.Button(new Rect(200, offset, 80, 20), "Load"))
                            {
                                UIController.Instance.isFileBrowser = !UIController.Instance.isFileBrowser;
                                FileBrowser.type = key.PropertyType;
                                FileBrowser.location = "";
                            }

                            // Apply the new Texture
                            if (GUI.Button(new Rect(290, offset, 80, 20), "Apply"))
                            {
                                if (!FileBrowser.builtin)
                                {
                                    string path = FileBrowser.location.Replace(Path.Combine(Directory.GetCurrentDirectory(), "GameData") + Path.DirectorySeparatorChar, "");
                                    MeshParser parser = new MeshParser(key.GetValue(obj, null) as Mesh);
                                    parser.SetFromString(path);
                                    parser.value.name = path.Replace("\\", "/");
                                    key.SetValue(obj, parser.value, null);
                                }
                                else
                                {
                                    key.SetValue(obj, FileBrowser.value, null);
                                }
                            }
                            offset += 25;
                        }
                    }
                }
            }
        }
    }
}