using System.Collections.Generic;
using System.Reflection;
using RoadPathfinding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NOMapTools
{
    public class RoadTool : MapTool
    {
        public override string ToolName => "Road Tool";

        public RoadNetwork ActiveNetwork => activeNetwork;
        public HashSet<Road> SelectedRoads => selectedRoads;
        public Object NetworkOwner => networkOwner;
        
        private bool toolEnabled = true;
        private Object networkOwner;
        private RoadNetwork activeNetwork;
        private HashSet<Road> selectedRoads = new HashSet<Road>();

        private RoadEditorTool roadEditorTool;
        private MeshEditorTool meshEditorTool;
        
        private bool boxSelectMode;
        private bool networkDirty;

        private int tab;
        
        private bool isDraggingBox;
        private Vector2 boxStart;
	
	
        public override void OnEnable()
        {
            roadEditorTool ??= new RoadEditorTool(this);
            meshEditorTool ??= new MeshEditorTool(this);
        }

        public override void OnGUI()
        {
            toolEnabled = EditorGUILayout.Toggle("Tool Enabled", toolEnabled);

            EditorGUILayout.Space();
            GUILayout.Label("Network Selection", EditorStyles.boldLabel);

            if (GUILayout.Button("Use MapSettings Roads"))
            {
                MapSettings map = FindObjectInCurrentStage<MapSettings>();
                if (map != null)
                {
                    activeNetwork = map.RoadNetwork.RoadNetwork;
                    networkOwner = map.RoadNetwork;
                    selectedRoads.Clear();
                }
            }

            if (GUILayout.Button("Use MapSettings SeaLanes"))
            {
                MapSettings map = FindObjectInCurrentStage<MapSettings>();
                if (map != null)
                {
                    activeNetwork = map.SeaLanes.RoadNetwork;
                    networkOwner = map.SeaLanes;
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
            
            boxSelectMode = GUILayout.Toggle(boxSelectMode, "Box Selection Mode");
            if (GUILayout.Button("Clear Selection"))
                selectedRoads.Clear();
            
            EditorGUILayout.Space();
            
            tab = GUILayout.Toolbar(tab, new[] {"Road Tools", "Mesh Tools"});
            EditorGUILayout.Space();
            switch (tab)
            {
                case 0:
                    roadEditorTool.Draw();
                    break;
                case 1:
                    meshEditorTool.Draw();
                    break;
            }
        }

        public override void OnSceneGUI(SceneView sceneView)
        {
            if (!toolEnabled || activeNetwork == null || !activeNetwork.Exists())
                return;

            roadEditorTool?.OnSceneGUI(sceneView);
            
            DrawNetwork(sceneView);
            HandleBoxSelection(sceneView);
            

            if (networkDirty && Event.current.type == EventType.MouseUp)
            {
                activeNetwork.RegenerateNetwork();
                networkDirty = false;
            }
        }
	
        public void MarkNetworkDirty()
        {
            networkDirty = true;
        }
        
        private void DrawNetwork(SceneView sceneView)
        {
            Camera cam = sceneView.camera;
            Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);

            foreach (var road in activeNetwork.roads)
            {
                if (!IsVisible(road, planes)) continue;
                DrawRoadLines(road);

                if (!roadEditorTool.EditPointsMode)
                    DrawSelectionButton(road);
            }
            
            if (!roadEditorTool.EditPointsMode && selectedRoads.Count > 0)
            {
                switch (Tools.current)
                {
                    case Tool.Move:
                        DrawGroupMoveHandle();
                        break;
                    case Tool.Rotate:
                        DrawGroupRotationHandle();
                        break;
                }
            }
        }

        private void DrawGroupRotationHandle()
        {
            Vector3 pivot = GetGroupCenter();
    
            EditorGUI.BeginChangeCheck();
            
            Quaternion currentRotation = Quaternion.identity;
            Quaternion newRotation = Handles.RotationHandle(currentRotation, pivot);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(networkOwner, "Rotate Roads");
                
                Quaternion deltaRotation = newRotation * Quaternion.Inverse(currentRotation);

                foreach (var road in selectedRoads)
                {
                    for (int i = 0; i < road.points.Count; i++)
                    {
                        Vector3 worldPos = road.points[i].AsVector3();
                        
                        Vector3 relativePos = worldPos - pivot;
                        Vector3 rotatedPos = (deltaRotation * relativePos) + pivot;
                
                        road.points[i] = new GlobalPosition(rotatedPos);
                    }
            
                    road.UpdateBB();
                    road.CalcLength();
                }

                MarkNetworkDirty();
                EditorUtility.SetDirty(networkOwner);
            }
        }

        public static bool IsVisible(Road road, Plane[] planes)
        {
            FieldInfo boundsInfo = typeof(Road).GetField("bounds", BindingFlags.NonPublic | BindingFlags.Instance);

            if (boundsInfo != null)
            {
                Bounds bounds = (Bounds)boundsInfo.GetValue(road);
                return GeometryUtility.TestPlanesAABB(planes, bounds);
            }

            return false;
        }

        private void DrawRoadLines(Road road)
        {
            Handles.color = selectedRoads.Contains(road) ? Color.green : Color.white;
            for (int i = 0; i < road.points.Count - 1; i++)
            {
                Handles.DrawLine(road.points[i].AsVector3(), road.points[i + 1].AsVector3());
            }
        }

        private void DrawSelectionButton(Road road)
        {
            Vector3 center = GetRoadCenter(road);
            float size = HandleUtility.GetHandleSize(center) * 0.15f;

            if (Handles.Button(center, Quaternion.identity, size, size, Handles.SphereHandleCap))
            {
                if (Event.current.shift)
                {
                    if (!selectedRoads.Add(road)) selectedRoads.Remove(road);
                }
                else
                {
                    selectedRoads.Clear();
                    selectedRoads.Add(road);
                }
                Event.current.Use();
            }
        }

        private void DrawGroupMoveHandle()
        {
            Vector3 center = GetGroupCenter();
            EditorGUI.BeginChangeCheck();
            Vector3 newCenter = Handles.PositionHandle(center, Quaternion.identity);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(networkOwner, "Move Roads");
                Vector3 delta = newCenter - center;

                foreach (var road in selectedRoads)
                {
                    for (int i = 0; i < road.points.Count; i++) road.points[i] += delta;
                    road.UpdateBB();
                    road.CalcLength();
                }
                networkDirty = true;
                EditorUtility.SetDirty(networkOwner);
            }
        }
        
        private void HandleBoxSelection(SceneView sceneView)
        {
            if (!boxSelectMode) return;
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                isDraggingBox = true;
                boxStart = e.mousePosition;
                GUIUtility.hotControl = GUIUtility.GetControlID(FocusType.Passive);
                e.Use();
            }

            if (e.type == EventType.MouseUp && isDraggingBox)
            {
                Rect rect = GetScreenRect(boxStart, e.mousePosition);
                selectedRoads.Clear();
                foreach (var road in activeNetwork.roads)
                {
                    if (rect.Contains(HandleUtility.WorldToGUIPoint(GetRoadCenter(road))))
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
                EditorGUI.DrawRect(rect, new Color(0, 0.6f, 1f, 0.15f));
                Handles.EndGUI();
                sceneView.Repaint();
            }
        }

        private static Rect GetScreenRect(Vector2 p1, Vector2 p2) => Rect.MinMaxRect(Mathf.Min(p1.x, p2.x), Mathf.Min(p1.y, p2.y), Mathf.Max(p1.x, p2.x), Mathf.Max(p1.y, p2.y));

        public static Vector3 GetRoadCenter(Road road)
        {
            if (road.points == null || road.points.Count == 0) return Vector3.zero;
            Vector3 sum = Vector3.zero;
            foreach (var p in road.points) sum += p.AsVector3();
            return sum / road.points.Count;
        }

        private Vector3 GetGroupCenter()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (var road in selectedRoads)
            {
                foreach (var p in road.points) { sum += p.AsVector3(); count++; }
            }
            return count > 0 ? sum / count : Vector3.zero;
        }

        private T FindObjectInCurrentStage<T>() where T : Object
        {
            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            return prefabStage != null 
                ? prefabStage.prefabContentsRoot.GetComponentInChildren<T>(true) 
                : Object.FindObjectOfType<T>();
        }
    }
}