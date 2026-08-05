using System.Collections.Generic;
using System.Linq;
using RoadPathfinding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements.UIR;

namespace NOMapTools
{
	public class RoadEditorTool
	{
		public bool EditPointsMode => editPointsMode;
	
		private bool editPointsMode;
		private bool showAllPoints;
		private bool seaMode = true;
		private float seaLevel = 0f;
		private readonly RoadTool tool;

		public RoadEditorTool(RoadTool tool)
		{
			this.tool = tool;
		}

		public void Draw()
		{
			if (tool.ActiveNetwork != null)
			{
				GUILayout.Label("Road Management", EditorStyles.boldLabel);
				if (GUILayout.Button("Create New Road")) AddNewRoad();

				EditorGUILayout.Space();
				GUILayout.Label("Editing Modes", EditorStyles.boldLabel);

				editPointsMode = GUILayout.Toggle(editPointsMode, "Edit Points Mode", "Button");
				showAllPoints = GUILayout.Toggle(showAllPoints, "Show All Points");
				seaMode = GUILayout.Toggle(seaMode, "Sea Mode");
				if (seaMode)
				{
					seaLevel = EditorGUILayout.FloatField("Sea Level", seaLevel);
				}
			
			

				EditorGUILayout.HelpBox("Point Editing Shortcuts:\n" +
				                        "- Shift+Click: Add point to end\n" +
				                        "- Ctrl+Click Point: Delete point\n" +
				                        "- Alt+Click Line: Insert point", MessageType.Info);

				EditorGUILayout.HelpBox($"Active Roads: {tool.ActiveNetwork.roads.Count}", MessageType.None);
			}
			else
			{
				EditorGUILayout.HelpBox("No active network selected.", MessageType.Info);
			}
		}

		private bool Raycast(Ray ray, out RaycastHit hit)
		{
			hit = new RaycastHit();
		
			Scene scene = (tool.NetworkOwner as Component)?.gameObject.scene
			              ?? (tool.NetworkOwner as GameObject)?.scene
			              ?? PrefabStageUtility.GetCurrentPrefabStage()?.scene
			              ?? SceneManager.GetActiveScene();

			bool hitPhysics = scene.IsValid() 
				? scene.GetPhysicsScene().Raycast(ray.origin, ray.direction, out hit) 
				: Physics.Raycast(ray, out hit);
		
			if (seaMode)
			{
				if (!hitPhysics || hit.point.y < seaLevel)
				{
					Plane seaPlane = new Plane(Vector3.up, new Vector3(0f, seaLevel, 0f));
             
					if (seaPlane.Raycast(ray, out float enter))
					{
						hit.point = ray.GetPoint(enter);
						hit.normal = Vector3.up;
						hit.distance = enter;
						return true;
					}
				}
			}

			return hitPhysics;
		}
	
		private void AddNewRoad()
		{
			Undo.RecordObject(tool.NetworkOwner, "Add new Road");
			Road newRoad = new Road();
			newRoad.points = new List<GlobalPosition>();
			tool.ActiveNetwork.roads.Add(newRoad);

			tool.SelectedRoads.Clear();
			tool.SelectedRoads.Add(newRoad);
			editPointsMode = true;

			EditorUtility.SetDirty(tool.NetworkOwner);
		}

		public void OnSceneGUI(SceneView sceneView)
		{
			if (editPointsMode)
			{
				int controlID = GUIUtility.GetControlID(FocusType.Passive);
				HandleUtility.AddDefaultControl(controlID);
			}
			HandlePointInput();
		
			foreach (var road in tool.ActiveNetwork.roads)
			{
				Camera cam = sceneView.camera;
				Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
			
				if (!RoadTool.IsVisible(road, planes)) continue;


				if (!editPointsMode) continue;
				if (showAllPoints || tool.SelectedRoads.Contains(road))
					DrawPointHandles(road);
			}
		}
	
		private void HandlePointInput()
		{
			Event e = Event.current;
			if (tool.SelectedRoads.Count != 1 || !editPointsMode) return;

			Road road = tool.SelectedRoads.First();

			if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
			{
				Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

				if (Raycast(ray, out RaycastHit hit))
				{
					Undo.RecordObject(tool.NetworkOwner, "Add Point");
					road.AddPoint(new GlobalPosition(hit.point));
					FinalizeRoadUpdate(road);
					e.Use();
				}
			}

			if (e.type == EventType.MouseDown && e.button == 0 && e.alt)
			{
				HandleInsertion(road, e);
			}
		}
	
		private void FinalizeRoadUpdate(Road road)
		{
			road.UpdateBB();
			road.CalcLength();
			tool.MarkNetworkDirty();
			EditorUtility.SetDirty(tool.NetworkOwner);
		}
	
		private void HandleInsertion(Road road, Event e)
		{
			for (int i = 0; i < road.points.Count - 1; i++)
			{
				Vector3 a = road.points[i].AsVector3();
				Vector3 b = road.points[i + 1].AsVector3();

				if (HandleUtility.DistanceToLine(a, b) < 10f)
				{
					Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
				
					if (Raycast(ray, out RaycastHit hit))
					{
						Undo.RecordObject(tool.NetworkOwner, "Insert Point");
						road.points.Insert(i + 1, new GlobalPosition(hit.point));
						FinalizeRoadUpdate(road);
						e.Use();
						return;
					}
				}
			}
		}
	
		private void DrawPointHandles(Road road)
		{
			for (int i = 0; i < road.points.Count; i++)
			{
				Vector3 pos = road.points[i].AsVector3();
				float size = HandleUtility.GetHandleSize(pos) * 0.1f;
				float screenDist = Vector2.Distance(Event.current.mousePosition, HandleUtility.WorldToGUIPoint(pos));

				if (Event.current.control && Event.current.type == EventType.MouseDown && screenDist < 10f)
				{
					Undo.RecordObject(tool.NetworkOwner, "Remove Point");
					road.points.RemoveAt(i);
					FinalizeRoadUpdate(road);
					Event.current.Use();
					break;
				}

				EditorGUI.BeginChangeCheck();
				Vector3 newPos = Handles.PositionHandle(pos, Quaternion.identity);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(tool.NetworkOwner, "Move Road Point");
					road.points[i] = new GlobalPosition(newPos);
					FinalizeRoadUpdate(road);
				}

				Handles.color = Color.yellow;
				Handles.SphereHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);
			}
		}
	}
}