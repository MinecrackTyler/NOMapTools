using System.Collections.Generic;
using System.Linq;
using RoadPathfinding;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements.UIR;

namespace NOMapTools;

public class RoadEditorTool(RoadEditorWindow window)
{
	public bool EditPointsMode => editPointsMode;
	
	private bool editPointsMode;
	private bool showAllPoints;

	public void Draw()
	{
		if (window.ActiveNetwork != null)
		{
			GUILayout.Label("Road Management", EditorStyles.boldLabel);
			if (GUILayout.Button("Create New Road")) AddNewRoad();

			EditorGUILayout.Space();
			GUILayout.Label("Editing Modes", EditorStyles.boldLabel);

			editPointsMode = GUILayout.Toggle(editPointsMode, "Edit Points Mode", "Button");
			showAllPoints = GUILayout.Toggle(showAllPoints, "Show All Points");
			

			EditorGUILayout.HelpBox("Point Editing Shortcuts:\n" +
			                        "• Shift+Click: Add point to end\n" +
			                        "• Ctrl+Click Point: Delete point\n" +
			                        "• Alt+Click Line: Insert point", MessageType.Info);

			EditorGUILayout.HelpBox($"Active Roads: {window.ActiveNetwork.roads.Count}", MessageType.None);
		}
		else
		{
			EditorGUILayout.HelpBox("No active network selected.", MessageType.Info);
		}
	}
	
	private void AddNewRoad()
	{
		Undo.RecordObject(window.NetworkOwner, "Add new Road");
		Road newRoad = new Road();
		newRoad.points = new List<GlobalPosition>();
		window.ActiveNetwork.roads.Add(newRoad);

		window.SelectedRoads.Clear();
		window.SelectedRoads.Add(newRoad);
		editPointsMode = true;

		EditorUtility.SetDirty(window.NetworkOwner);
	}

	public void OnSceneGUI(SceneView sceneView)
	{
		if (editPointsMode)
		{
			int controlID = GUIUtility.GetControlID(FocusType.Passive);
			HandleUtility.AddDefaultControl(controlID);
		}
		HandlePointInput();
		
		foreach (var road in window.ActiveNetwork.roads)
		{
			Camera cam = sceneView.camera;
			Plane[] planes = GeometryUtility.CalculateFrustumPlanes(cam);
			
			if (!RoadEditorWindow.IsVisible(road, planes)) continue;


			if (!editPointsMode) continue;
			if (showAllPoints || window.SelectedRoads.Contains(road))
				DrawPointHandles(road);
		}
	}
	
	private void HandlePointInput()
	{
		Event e = Event.current;
		if (window.SelectedRoads.Count != 1 || !editPointsMode) return;

		Road road = window.SelectedRoads.First();

		if (e.type == EventType.MouseDown && e.button == 0 && e.shift)
		{
			Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
			var scene = (window.NetworkOwner as Component)?.gameObject.scene
			            ?? (window.NetworkOwner as GameObject)?.scene
			            ?? default;

			if (scene.IsValid())
			{
				var pScene = scene.GetPhysicsScene();
				if (pScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
				{
					Undo.RecordObject(window.NetworkOwner, "Add Point");
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
	
	private void FinalizeRoadUpdate(Road road)
	{
		road.UpdateBB();
		road.CalcLength();
		window.MarkNetworkDirty();
		EditorUtility.SetDirty(window.NetworkOwner);
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
				var scene = (window.NetworkOwner as Component)?.gameObject.scene
				            ?? (window.NetworkOwner as GameObject)?.scene
				            ?? default;

				if (scene.IsValid())
				{
					var pScene = scene.GetPhysicsScene();
					if (pScene.Raycast(ray.origin, ray.direction, out RaycastHit hit))
					{
						Undo.RecordObject(window.NetworkOwner, "Insert Point");
						road.points.Insert(i + 1, new GlobalPosition(hit.point));
						FinalizeRoadUpdate(road);
						e.Use();
						return;
					}
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
				Undo.RecordObject(window.NetworkOwner, "Remove Point");
				road.points.RemoveAt(i);
				FinalizeRoadUpdate(road);
				Event.current.Use();
				break;
			}

			EditorGUI.BeginChangeCheck();
			Vector3 newPos = Handles.PositionHandle(pos, Quaternion.identity);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(window.NetworkOwner, "Move Road Point");
				road.points[i] = new GlobalPosition(newPos);
				FinalizeRoadUpdate(road);
			}

			Handles.color = Color.yellow;
			Handles.SphereHandleCap(0, pos, Quaternion.identity, size, EventType.Repaint);
		}
	}
}