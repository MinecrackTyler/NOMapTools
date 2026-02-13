using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoadPathfinding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NOMapTools
{
    public class RoadEditorTool : EditorWindow
    {
        private static bool toolEnabled = true;
        
        private static RoadEditorTool instance;

        private static Object networkOwner;
        private static RoadNetwork activeNetwork;
        private static HashSet<Road> selectedRoads = new HashSet<Road>();

        private static bool editPointsMode;
        private static bool showAllPoints;
        private static bool boxSelectMode;

        private static bool networkDirty;

        private static bool isDraggingBox;
        private static Vector2 boxStart;
        
        private static readonly FieldInfo roadBoundsField =
            typeof(Road).GetField("bounds",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        
        [MenuItem("Window/Road Editor")]
        public static void OpenWindow()
        {
            instance = GetWindow<RoadEditorTool>();
            instance.titleContent = new GUIContent("Road Editor");
            instance.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }
        

        private void OnGUI()
        {
            GUILayout.Label("Road Editor", EditorStyles.boldLabel);

            toolEnabled = EditorGUILayout.Toggle("Tool Enabled", toolEnabled);

            EditorGUILayout.Space();

            GUILayout.Label("Network Selection", EditorStyles.boldLabel);

            if (GUILayout.Button("Use MapSettings Roads"))
            {
                MapSettings map = FindObjectInCurrentStage<MapSettings>();
                if (map != null)
                {
                    activeNetwork = map.RoadNetwork;
                    networkOwner = map;
                    selectedRoads.Clear();
                }
            }
            
            if (GUILayout.Button("Use MapSettings SeaLanes"))
            {
                MapSettings map = FindObjectInCurrentStage<MapSettings>();
                if (map != null)
                {
                    activeNetwork = map.SeaLanes;
                    networkOwner = map;
                    selectedRoads.Clear();
                }
            }

            if (GUILayout.Button("Use Selected Airbase TaxiNetwork"))
            {
                Airbase ab = Selection.activeGameObject != null
                    ? Selection.activeGameObject.GetComponent<Airbase>()
                    : null;

                if (ab != null)
                {
                    activeNetwork = ab.GetTaxiNetwork();
                    networkOwner = ab;
                    selectedRoads.Clear();
                }
            }

            if (GUILayout.Button("Deselect Network"))
            {
                activeNetwork = null;
                networkOwner = null;
                selectedRoads.Clear();
            }

            EditorGUILayout.Space();

            if (activeNetwork != null)
            {
                GUILayout.Label("Road Management", EditorStyles.boldLabel);
                if (GUILayout.Button("Create New Road"))
                {
                    AddNewRoad();
                }
                
                EditorGUILayout.Space();
                
                GUILayout.Label("Editing Modes", EditorStyles.boldLabel);

                if (!editPointsMode)
                {
                    if (GUILayout.Button("Enter Point Edit Mode"))
                        editPointsMode = true;
                }
                else
                {
                    if (GUILayout.Button("Exit Point Edit Mode"))
                        editPointsMode = false;
                }

                showAllPoints = GUILayout.Toggle(showAllPoints, "Show All Points");
                boxSelectMode = GUILayout.Toggle(boxSelectMode, "Box Selection Mode");
                
                EditorGUILayout.HelpBox("Point Editing Shortcuts:\n" +
                                        "• Shift+Click: Add point to end\n" +
                                        "• Ctrl+Click Point: Delete point\n" +
                                        "• Alt+Click Line: Insert point", MessageType.Info);

                EditorGUILayout.Space();

                if (GUILayout.Button("Clear Selection"))
                    selectedRoads.Clear();

                EditorGUILayout.HelpBox($"Active Roads: {activeNetwork.roads.Count}", MessageType.None);
            }
            else
            {
                EditorGUILayout.HelpBox("No active network selected.", MessageType.Info);
            }
            
            if (GUI.changed)
            {
                SceneView.RepaintAll();
            }
        }

        private void AddNewRoad()
        {
            Undo.RecordObject(networkOwner, "Add new Road");
            Road newRoad = new Road();
            newRoad.points = new List<GlobalPosition>();
            activeNetwork.roads.Add(newRoad);
            
            selectedRoads.Clear();
            selectedRoads.Add(newRoad);
            editPointsMode = true;
            
            EditorUtility.SetDirty(networkOwner);
        }


        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!toolEnabled)
                return;

            if (activeNetwork == null || !activeNetwork.Exists())
                return;

            if (editPointsMode)
            {
                int controlID = GUIUtility.GetControlID(FocusType.Passive);
                HandleUtility.AddDefaultControl(controlID);
            }
            
            DrawNetwork(sceneView);
            HandleBoxSelection(sceneView);

            HandlePointInput();
            
            if (networkDirty && Event.current.type == EventType.MouseUp)
            {
                activeNetwork.RegenerateNetwork();
                networkDirty = false;
            }
            
            if (GUI.changed)
                SceneView.RepaintAll();
        }

        private static void HandlePointInput()
        {
            Event e = Event.current;
            if (selectedRoads.Count != 1 || !editPointsMode) return;

            Road road = selectedRoads.First();

            if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                var scene = (networkOwner as Component)?.gameObject.scene 
                            ?? (networkOwner as GameObject)?.scene 
                            ?? default;

                if (scene.IsValid())
                {
                    var pScene = scene.GetPhysicsScene();
                    if (pScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
                    {
                        Undo.RecordObject(networkOwner, "Add Point");
                        road.AddPoint(new GlobalPosition(hit.point));
                        FinalizeRoadUpdate(road);
                        e.Use();
                    }
                }

                
            }

            if (e.type == EventType.MouseDown && e.button == 0 && e.alt)
            {
                HandleInsertion(road, e);
            }
        }
        
        private static void HandleInsertion(Road road, Event e)
        {
            if (e.type == EventType.MouseDown && e.button == 0 && e.alt)
            {
                for (int i = 0; i < road.points.Count - 1; i++)
                {
                    Vector3 a = road.points[i].AsVector3();
                    Vector3 b = road.points[i+1].AsVector3();
                    
                    float dist = HandleUtility.DistanceToLine(a, b);
                    if (dist < 10f)
                    {
                        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                        var scene = (networkOwner as Component)?.gameObject.scene 
                                    ?? (networkOwner as GameObject)?.scene 
                                    ?? default;

                        if (scene.IsValid())
                        {
                            var pScene = scene.GetPhysicsScene();
                            if (pScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
                            {
                                Undo.RecordObject(networkOwner, "Insert Point");
                                road.points.Insert(i + 1, new GlobalPosition(hit.point));
                                FinalizeRoadUpdate(road);
                                e.Use();
                                return;
                            }
                        }
                    }
                }
            }
        }

        private static void DrawNetwork(SceneView sceneView)
        {
            Camera cam = sceneView.camera;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);

            foreach (var road in activeNetwork.roads)
            {
                if (!IsVisible(road, planes))
                    continue;

                DrawRoadLines(road);

                if (!editPointsMode)
                {
                    DrawSelectionButton(road);
                }
                else
                {
                    if (showAllPoints || selectedRoads.Contains(road))
                        DrawPointHandles(road);
                }
                
            }

            if (!editPointsMode && selectedRoads.Count > 0)
                DrawGroupMoveHandle();
        }

        private static bool IsVisible(Road road, Plane[] planes)
        {
            if (roadBoundsField == null)
                return true;

            Bounds bounds = (Bounds)roadBoundsField.GetValue(road);
            return GeometryUtility.TestPlanesAABB(planes, bounds);
        }

        private static void DrawRoadLines(Road road)
        {
            Handles.color = selectedRoads.Contains(road) ? Color.green : Color.white;

            for (int i = 0; i < road.points.Count - 1; i++)
            {
                Handles.DrawLine(
                    road.points[i].AsVector3(),
                    road.points[i + 1].AsVector3());
            }
        }

        private static void DrawSelectionButton(Road road)
        {
            Vector3 center = GetRoadCenter(road);
            float size = HandleUtility.GetHandleSize(center) * 0.15f;

            if (Handles.Button(center, Quaternion.identity, size, size, Handles.SphereHandleCap))
            {
                if (Event.current.shift)
                {
                    if (!selectedRoads.Add(road))
                        selectedRoads.Remove(road);
                }
                else
                {
                    selectedRoads.Clear();
                    selectedRoads.Add(road);
                }

                Event.current.Use();
            }
        }

        private static void DrawGroupMoveHandle()
        {
            Vector3 center = GetGroupCenter();

            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(networkOwner, "Move Roads");
                EditorUtility.SetDirty(networkOwner);

                Vector3 delta = newCenter - center;

                foreach (var road in selectedRoads)
                {
                    for (int i = 0; i < road.points.Count; i++)
                        road.points[i] += delta;

                    road.UpdateBB();
                    road.CalcLength();
                }

                networkDirty = true;
            }
        }

        private static void DrawPointHandles(Road road)
        {
            for (int i = 0; i < road.points.Count; i++)
            {
                Vector3 pos = road.points[i].AsVector3();
                float size = HandleUtility.GetHandleSize(pos) * 0.1f;
                float screenDistance = Vector2.Distance(Event.current.mousePosition, HandleUtility.WorldToGUIPoint(pos));

                if (Event.current.control && Event.current.type == EventType.MouseDown)
                {
                    if (Event.current.control && Event.current.type == EventType.MouseDown && screenDistance < 10f)
                    {
                        Undo.RecordObject(networkOwner, "Remove Point");
                        road.points.RemoveAt(i);
                        FinalizeRoadUpdate(road);
                        Event.current.Use();
                        break;
                    }
                }
                
                EditorGUI.BeginChangeCheck();
                Vector3 newPos = Handles.PositionHandle(pos, Quaternion.identity);

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(networkOwner, "Move Road Point");
                    EditorUtility.SetDirty(networkOwner);

                    road.points[i] = new GlobalPosition(newPos);
                    FinalizeRoadUpdate(road);
                }

                Handles.color = Color.yellow;
                Handles.SphereHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);
                Handles.color = Color.white;
            }
        }
        
        private static void FinalizeRoadUpdate(Road road)
        {
            road.UpdateBB();
            road.CalcLength();
            networkDirty = true;
            EditorUtility.SetDirty(networkOwner);
        }
        

        private static void HandleBoxSelection(SceneView sceneView)
        {
            if (!boxSelectMode)
                return;

            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                isDraggingBox = true;
                boxStart = e.mousePosition;

                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                e.Use();
            }

            if (e.type == EventType.MouseDrag && isDraggingBox)
            {
                e.Use();
            }
            

            if (e.type == EventType.MouseUp && isDraggingBox)
            {
                Rect rect = GetScreenRect(boxStart, e.mousePosition);

                selectedRoads.Clear();

                foreach (var road in activeNetwork.roads)
                {
                    Vector2 guiPoint =
                        HandleUtility.WorldToGUIPoint(GetRoadCenter(road));

                    if (rect.Contains(guiPoint))
                        selectedRoads.Add(road);
                }

                isDraggingBox = false;

                GUIUtility.hotControl = 0;
                e.Use();
            }

            if (isDraggingBox)
            {
                Rect rect = GetScreenRect(boxStart, e.mousePosition);

                Handles.BeginGUI();

                DrawScreenRect(rect, new Color(0, 0.6f, 1f, 0.15f));
                DrawScreenRectBorder(rect, 2, Color.cyan);

                Handles.EndGUI();

                sceneView.Repaint();
            }
        }

        private static Rect GetScreenRect(Vector2 p1, Vector2 p2)
        {
            return Rect.MinMaxRect(
                Mathf.Min(p1.x, p2.x),
                Mathf.Min(p1.y, p2.y),
                Mathf.Max(p1.x, p2.x),
                Mathf.Max(p1.y, p2.y));
        }

        private static void DrawScreenRect(Rect rect, Color color)
        {
            EditorGUI.DrawRect(rect, color);
        }

        private static void DrawScreenRectBorder(Rect rect, float thickness, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }
        

        private static Vector3 GetRoadCenter(Road road)
        {
            Vector3 sum = Vector3.zero;
            foreach (var p in road.points)
                sum += p.AsVector3();
            return sum / road.points.Count;
        }

        private static Vector3 GetGroupCenter()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;

            foreach (var road in selectedRoads)
            {
                foreach (var p in road.points)
                {
                    sum += p.AsVector3();
                    count++;
                }
            }

            return count > 0 ? sum / count : Vector3.zero;
        }
        
        private static T FindObjectInCurrentStage<T>() where T : Object
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();

            if (prefabStage != null)
            {
                return prefabStage.prefabContentsRoot.GetComponentInChildren<T>(true);
            }
            else
            {
                return Object.FindObjectOfType<T>();
            }
        }
    }
}