using BepInEx;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System;
using System.Linq;
using BepInEx.Configuration;

namespace ObjImport
{
    [BepInPlugin(pluginGUID, pluginName, pluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string pluginGUID = "com.metalted.zeepkist.objimport";
        public const string pluginName = "ObjImport";
        public const string pluginVersion = "1.1";
        public string pluginPath = ""; 

        public ConfigEntry<string> objPath;
        public ConfigEntry<bool> importButton;
        public ConfigEntry<float> importScale;

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            Harmony harmony = new Harmony(pluginGUID);
            harmony.PatchAll();

            objPath = Config.Bind("Import", "Obj Path", "", "");
            importButton = Config.Bind("Import", "Import", true, "[Button] Import obj from path");
            importScale = Config.Bind("Import", "Import Scale", 5.0f, "");
            importButton.SettingChanged += ImportButton_SettingChanged;
            pluginPath = AppDomain.CurrentDomain.BaseDirectory + @"\BepInEx\plugins";
        }

        private void ImportButton_SettingChanged(object sender, EventArgs e)
        {
            //Check the file at the path.
            string filePath = (string)objPath.BoxedValue;

            if(File.Exists(filePath))
            {
                // Check if the extension is .obj
                string fileExtension = Path.GetExtension(filePath);

                if (fileExtension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        float scalingFactor = (float)importScale.BoxedValue;
                        ZeepObj myObj = new ZeepObj(filePath, scalingFactor);
                        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                        string savePath = Path.Combine(pluginPath, fileNameWithoutExtension + ".zeeplevel");
                        File.WriteAllLines(savePath, myObj.zeeplines);
                        PlayerManager.Instance.messenger.Log("ObjImport: Imported " + fileNameWithoutExtension + "! :)", 2f);
                    }
                    catch
                    {
                        PlayerManager.Instance.messenger.Log("ObjImport: Failed to import .obj! :(", 2f);
                    }
                }
                else
                {
                    PlayerManager.Instance.messenger.Log("ObjImport: Path is not an .obj file! :|", 2f);
                }
            }
            else
            {
                PlayerManager.Instance.messenger.Log("ObjImport: File doesn't exist! :S", 2f);
            }
        }
    }

    public class ZeepObj
    {
        public string path;
        public List<string> zeeplines = new List<string>();

        public ZeepObj(string path, float scale)
        {
            this.path = path;

            //A list containing arrays with 3 vertices belonging to 1 triangle.
            List<Vector3[]> triVerts = new List<Vector3[]>();
            //A list containing vertices from the file.
            List<Vector3> vertices = new List<Vector3>();
            //Read the lines from the file
            string[] lines = File.ReadAllLines(path);

            // First pass to collect all vertices
            foreach (string line in lines)
            {
                string[] parts = line.Trim().Split(' ');

                //Vertices
                if (parts[0] == "v")
                {
                    float x = float.Parse(parts[1]);
                    float y = float.Parse(parts[2]);
                    float z = float.Parse(parts[3]);
                    vertices.Add(new Vector3(x, y, z) * scale);
                }
            }

            // Second pass to process faces
            foreach (string line in lines)
            {
                string[] parts = line.Trim().Split(' ');

                //Faces
                if (parts[0] == "f")
                {
                    int[] vertexIndices = new int[3];

                    for (int i = 0; i < 3; i++)
                    {
                        string[] indices = parts[i + 1].Split('/');
                        vertexIndices[i] = int.Parse(indices[0]) - 1; // OBJ indices are 1-based
                    }

                    Vector3[] triangle = new Vector3[3];
                    for (int i = 0; i < 3; i++)
                    {
                        triangle[i] = vertices[vertexIndices[i]];
                    }

                    triVerts.Add(triangle);
                }
            }

            //Process the triangles vertices
            foreach (Vector3[] tvarr in triVerts)
            {
                List<(float, int[])> triangleSides = new List<(float, int[])> { (Vector3.Distance(tvarr[0], tvarr[1]), new int[] { 0, 1 }), (Vector3.Distance(tvarr[1], tvarr[2]), new int[] { 1, 2 }), (Vector3.Distance(tvarr[2], tvarr[0]), new int[] { 2, 0 }) };
                //0 shortest, 2 longest
                triangleSides.Sort((a, b) => a.Item1.CompareTo(b.Item1));

                int cornerVertex = triangleSides[0].Item2.Intersect(triangleSides[1].Item2).ToArray()[0];
                int baseVertex = triangleSides[0].Item2.Intersect(triangleSides[2].Item2).ToArray()[0];
                int legVertex = triangleSides[1].Item2.Intersect(triangleSides[2].Item2).ToArray()[0];

                bool isRightTriangle = Mathf.Approximately(Mathf.Pow(triangleSides[0].Item1, 2) + Mathf.Pow(triangleSides[1].Item1, 2), Mathf.Pow(triangleSides[2].Item1, 2));
                if (isRightTriangle)
                {
                    zeeplines.Add(CreateZeepTriangle(tvarr[cornerVertex], tvarr[baseVertex], tvarr[legVertex]));
                }
                else
                {

                    Vector3 pointD = CalculatePointD(tvarr[cornerVertex], tvarr[baseVertex], tvarr[legVertex]);
                    zeeplines.Add(CreateZeepTriangle(pointD, tvarr[cornerVertex], tvarr[baseVertex]));
                    zeeplines.Add(CreateZeepTriangle(pointD, tvarr[legVertex], tvarr[cornerVertex]));
                }
            }
        }

        private Vector3 CalculatePointD(Vector3 A, Vector3 B, Vector3 C)
        {
            // Vector BC
            Vector3 vectorBC = C - B;

            // Vector AB
            Vector3 vectorAP = A - B;

            // Project AP onto BC to find BD (vector from B to D)
            Vector3 vectorBD = Vector3.Project(vectorAP, vectorBC);

            // Point D is B + vector BD
            Vector3 D = B + vectorBD;

            return D;
        }

        private string CreateZeepTriangle(Vector3 vCorner, Vector3 v1, Vector3 v2)
        {
            float dist_v1 = Vector3.Distance(vCorner, v1);
            float dist_v2 = Vector3.Distance(vCorner, v2);
            bool v1IsBase = dist_v1 < dist_v2;

            Vector3 vBase = v1IsBase ? v1 : v2;
            Vector3 vLeg = v1IsBase ? v2 : v1;

            //Calculate the base direction
            Vector3 dirBase = vBase - vCorner;
            //Calculate the leg direction
            Vector3 dirLeg = vLeg - vCorner;
            //Calculate the position of the zeep triangle
            Vector3 position = (vBase + dirLeg / 2);
            //Calculate the surface normal direction
            Vector3 dirSurface = Vector3.Cross(dirLeg.normalized, dirBase.normalized);
            //Create a rotation using the surface and leg vector.
            Quaternion rotation = Quaternion.LookRotation(dirLeg.normalized, dirSurface.normalized);
            //Get the euler angles
            Vector3 euler = rotation.eulerAngles;
            //Calculate the scale

            float baseScale = v1IsBase ? dist_v1 : dist_v2;
            float legScale = v1IsBase ? dist_v2 : dist_v1;

            baseScale /= 8f;
            legScale /= 16f;

            //Create the string
            return $"1569,{position.x},{position.y},{position.z},{euler.x},{euler.y},{euler.z},{-baseScale},0.0001,{-legScale},1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0";
        }
    }
}