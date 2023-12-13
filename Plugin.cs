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
        public const string pluginVersion = "1.2";
        public string pluginPath = "";

        public ConfigEntry<string> objPath;
        public ConfigEntry<bool> importButton;
        public ConfigEntry<float> importScale;
        public ConfigEntry<float> shellThickness;
        public ConfigEntry<int> paintID;
        public ConfigEntry<int> blockID;

        private void Awake()
        {
            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");

            Harmony harmony = new Harmony(pluginGUID);
            harmony.PatchAll();

            objPath = Config.Bind("Import", "Obj Path", "", "");
            importButton = Config.Bind("Import", "Import", true, "[Button] Import obj from path");
            importScale = Config.Bind("Import", "Import Scale", 5.0f, "");
            shellThickness = Config.Bind("Properties", "Shell Thickness", 0.00001f, "");
            paintID = Config.Bind("Properties", "Paint ID", 1, "");
            blockID = Config.Bind("Properties", "Block ID", 1569, "");

            importButton.SettingChanged += ImportButton_SettingChanged;
            pluginPath = AppDomain.CurrentDomain.BaseDirectory + @"\BepInEx\plugins";
        }

        private void ImportButton_SettingChanged(object sender, EventArgs e)
        {
            //Check the file at the path.
            string filePath = (string)objPath.BoxedValue;

            if (File.Exists(filePath))
            {
                // Check if the extension is .obj
                string fileExtension = Path.GetExtension(filePath);

                if (fileExtension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        float _importScale = (float)importScale.BoxedValue;
                        float _shellThickness = (float)shellThickness.BoxedValue;
                        int _blockID = (int)blockID.BoxedValue;
                        int _paintID = (int)paintID.BoxedValue;

                        ObjFile objFile = new ObjFile(filePath);
                        objFile.ReadObj(_importScale);
                        objFile.CreateBlockCSV(_shellThickness, _paintID, _blockID);

                        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
                        string savePath = Path.Combine(pluginPath, fileNameWithoutExtension + ".zeeplevel");
                        File.WriteAllLines(savePath, objFile.lines);
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

    public class ObjFile
    {
        public string path;
        public List<Vector3> vertices;
        public List<int[]> faces;
        public List<Triangle> triangles;
        public List<string> lines;

        public ObjFile(string path)
        {
            this.path = path;
            vertices = new List<Vector3>();
            faces = new List<int[]>();
            triangles = new List<Triangle>();
            lines = new List<string>();
        }

        public void ReadObj(float renderScale)
        {
            vertices.Clear();
            faces.Clear();

            string[] lines = File.ReadAllLines(path);
            foreach (string line in lines)
            {
                string[] parts = line.Trim().Split(' ');
                switch (parts[0])
                {
                    case "": //Empty Line
                    case "#":  //Comment
                    case "vn": //Normal
                    case "vt": //Texture
                    case "g": //Graphics related
                    case "usemtl": //Material assignment
                    case "s": //Unknown
                        break;
                    case "v": //Vertices
                        //v  11.3345 20.3138 145.7801
                        Vector3 v = new Vector3();
                        float.TryParse(parts[1], out v.x);
                        float.TryParse(parts[2], out v.y);
                        float.TryParse(parts[3], out v.z);
                        vertices.Add(v * renderScale);
                        break;
                    case "f": //Face
                        int[] vertexIndices = new int[3] { 0, 0, 0 };
                        for (int i = 0; i < 3; i++)
                        {
                            //f 1/1/1 2/2/1 3/3/1 
                            //f #/./. #/./. #/./.

                            string[] indices = parts[i + 1].Split('/');
                            int.TryParse(indices[0], out vertexIndices[i]);
                            vertexIndices[i] -= vertexIndices[i] == 0 ? 0 : 1; //Obj indices are 1 based;
                        }
                        faces.Add(vertexIndices);
                        break;
                }
            }

            CreateTriangles();
        }

        private void CreateTriangles()
        {
            triangles.Clear();
            foreach (int[] face in faces)
            {
                bool valid = true;
                Vector3[] v_triangle = new Vector3[3];
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        v_triangle[i] = vertices[face[i]];
                    }
                    catch
                    {
                        valid = false;
                    }
                }

                if (!valid)
                {
                    continue;
                }

                Triangle t = new Triangle(v_triangle);

                if (t.IsRightTriangle())
                {
                    triangles.Add(t);
                }
                else
                {
                    // Vector BC
                    Vector3 vectorBC = t._leg.position - t._base.position;
                    // Vector AB
                    Vector3 vectorAP = t._corner.position - t._base.position;
                    // Project AP onto BC to find BD (vector from B to D)
                    Vector3 vectorBD = Vector3.Project(vectorAP, vectorBC);
                    // Point D is B + vector BD
                    Vector3 D = t._base.position + vectorBD;

                    triangles.Add(new Triangle(new Vector3[] { D, t._corner.position, t._base.position }));
                    triangles.Add(new Triangle(new Vector3[] { D, t._leg.position, t._corner.position }));
                }
            }
        }

        public void CreateBlockCSV(float shellScale = 0.0001f, int paintID = 1, int blockID = 1569)
        {
            lines.Clear();
            foreach (Triangle t in triangles)
            {
                Vector3 dirBaseCorner = t._base.position - t._corner.position;
                Vector3 dirLegCorner = t._leg.position - t._corner.position;
                Vector3 dirNormal = Vector3.Cross(dirLegCorner.normalized, dirBaseCorner.normalized);

                Vector3 position = t._base.position + dirLegCorner / 2;
                Vector3 euler = Quaternion.LookRotation(dirLegCorner.normalized, dirNormal.normalized).eulerAngles;
                Vector3 scale = new Vector3(-t._corner.distanceToBase / 8f, shellScale, -t._corner.distanceToLeg / 16f);

                lines.Add($"{blockID},{position.x},{position.y},{position.z},{euler.x},{euler.y},{euler.z},{scale.x},{scale.y},{scale.z},{paintID},0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0");
            }
        }
    }

    public class Triangle
    {
        public Vertice _corner;
        public Vertice _base;
        public Vertice _leg;

        public class Vertice
        {
            public enum Type { None, Corner, Base, Leg };
            public Type type;

            public Vector3 position;
            public float distanceToBase;
            public float distanceToLeg;
            public float distanceToCorner;
        }

        public Triangle(Vector3[] vertices)
        {
            float distance01 = Vector3.Distance(vertices[0], vertices[1]);
            float distance12 = Vector3.Distance(vertices[1], vertices[2]);
            float distance20 = Vector3.Distance(vertices[2], vertices[0]);

            Dictionary<string, float> distances = new Dictionary<string, float>()
            {
                {"01", distance01 },
                {"10", distance01 },
                {"12", distance12 },
                {"21", distance12 },
                {"20", distance20 },
                {"02", distance20 },
            };

            (float, int[]) side01 = (distance01, new int[] { 0, 1 });
            (float, int[]) side12 = (distance12, new int[] { 1, 2 });
            (float, int[]) side20 = (distance20, new int[] { 2, 0 });

            List<(float, int[])> sides = new List<(float, int[])>()
            {
                side01, side12,side20
            };

            //Sort sides by size
            sides.Sort((a, b) => a.Item1.CompareTo(b.Item1));

            //The corner is both in the shortest and middle line
            int i_corner = sides[0].Item2.Intersect(sides[1].Item2).ToArray()[0];
            //The base is both in the middle and the longest line
            int i_base = sides[0].Item2.Intersect(sides[2].Item2).ToArray()[0];
            //The leg is both in the shortest and the longest line
            int i_leg = sides[1].Item2.Intersect(sides[2].Item2).ToArray()[0];

            _corner = new Vertice();
            _corner.type = Vertice.Type.Corner;
            _corner.position = vertices[i_corner];
            _corner.distanceToBase = distances[(i_corner + "" + i_base)];
            _corner.distanceToLeg = distances[(i_corner + "" + i_leg)];

            _base = new Vertice();
            _base.type = Vertice.Type.Base;
            _base.position = vertices[i_base];
            _base.distanceToCorner = distances[(i_base + "" + i_corner)];
            _base.distanceToLeg = distances[(i_base + "" + i_leg)];

            _leg = new Vertice();
            _leg.type = Vertice.Type.Leg;
            _leg.position = vertices[i_leg];
            _leg.distanceToCorner = distances[(i_leg + "" + i_corner)];
            _leg.distanceToBase = distances[(i_leg + "" + i_base)];
        }

        public bool IsRightTriangle()
        {
            return Mathf.Approximately(Mathf.Pow(_corner.distanceToBase, 2) + Mathf.Pow(_corner.distanceToLeg, 2), Mathf.Pow(_base.distanceToLeg, 2));
        }
    }
}