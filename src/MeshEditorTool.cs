using System.Collections.Generic;
using RoadPathfinding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NOMapTools;

public class MeshEditorTool(RoadEditorWindow window)
{
	private RoadPreset selectedPreset;
	private float widthOverride;
	private bool useWidthOverride;
	private Transform roadsTransform;
	
	private Dictionary<Road, GameObject> roads = new Dictionary<Road, GameObject>();

	public void Draw()
	{
		selectedPreset = (RoadPreset)EditorGUILayout.ObjectField("Preset", selectedPreset, typeof(RoadPreset), false);
		useWidthOverride = EditorGUILayout.Toggle("Use Width Override", useWidthOverride);
		
		if (useWidthOverride)
			widthOverride = EditorGUILayout.FloatField("Width Override", widthOverride);

		if (GUILayout.Button("Generate Mesh"))
		{
			GenerateMeshes();
		}

		if (GUILayout.Button("Clear Selected Mesh"))
		{
			ClearSelectedMeshes();
		}
		
		EditorGUILayout.Space();
		
		if (GUILayout.Button("Clear All Meshes"))
		{
			ClearAllMeshes();
		}
	}

	private void FindRoadTransform()
	{
		if (roadsTransform) return;
		
		var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
		var newRoadsTransform = prefabStage.prefabContentsRoot.transform.Find("GeneratedRoads");
		if (newRoadsTransform == null)
		{
			var roadsGO = new GameObject("GeneratedRoads");
			roadsGO.transform.SetParent(prefabStage.prefabContentsRoot.transform);
			newRoadsTransform = roadsGO.transform;
		}
		roadsTransform = newRoadsTransform;
	}

	private void GenerateMeshes()
	{
		if (window.ActiveNetwork == null || selectedPreset == null)
			return;

		FindRoadTransform();
		
		if (roadsTransform == null) return;

		ClearSelectedMeshes(); // optional but recommended

		foreach (var road in window.SelectedRoads)
		{
			Mesh mesh = GenerateRoadMesh(road);
			if (mesh == null) continue;

			string prefabPath = PrefabStageUtility.GetCurrentPrefabStage().assetPath;
			string prefabDirectory = System.IO.Path.GetDirectoryName(prefabPath);

			string meshFolder = prefabDirectory + "/GeneratedMeshes";

			if (!AssetDatabase.IsValidFolder(meshFolder))
			{
				AssetDatabase.CreateFolder(prefabDirectory, "GeneratedMeshes");
			}

			string meshPath = meshFolder + "/" + road.GetHashCode() + "_RoadMesh.asset";

			AssetDatabase.CreateAsset(mesh, meshPath);
			AssetDatabase.SaveAssets();
			
			GameObject go = new GameObject("RoadMesh");
			Undo.RegisterCreatedObjectUndo(go, "Create Road Mesh");

			go.transform.SetParent(roadsTransform, false);

			go.transform.position = RoadEditorWindow.GetRoadCenter(road);

			var mf = go.AddComponent<MeshFilter>();
			var mr = go.AddComponent<MeshRenderer>();
			var mc = go.AddComponent<MeshCollider>();

			mf.sharedMesh = mesh;
			mr.sharedMaterial = selectedPreset.material;
			mc.sharedMesh = mesh;
			
			roads.Add(road, go);
			
			EditorUtility.SetDirty(go);
			EditorUtility.SetDirty(roadsTransform.gameObject);
		}
	}
	
	private void ClearSelectedMeshes()
	{
		FindRoadTransform();

		if (roadsTransform == null) return;

		foreach (var road in window.SelectedRoads)
		{
			if (roads.TryGetValue(road, out var road1))
			{
				Undo.DestroyObjectImmediate(road1);
				roads.Remove(road);
			}
		}
	}
	
	private void ClearAllMeshes()
	{
		FindRoadTransform();

		if (roadsTransform == null) return;

		for (int i = roadsTransform.childCount - 1; i >= 0; i--)
		{
			Undo.DestroyObjectImmediate(roadsTransform.GetChild(i).gameObject);
			roads.Clear();
		}
	}
	
	private Mesh GenerateRoadMesh(Road road)
	{
		if (road.points == null || road.points.Count < 2) return null;
		float width = useWidthOverride ? widthOverride : selectedPreset.defaultWidth;
		float halfWidth = width / 2;
		
		List<Vector3> vertices = new List<Vector3>();
		List<int> triangles = new List<int>();
		List<Vector2> uvs = new List<Vector2>();
		
		float length = 0f;

		for (int i = 0; i < road.points.Count; i++)
		{
			Vector3 current = road.points[i].AsVector3();

			Vector3 forward;

			if (i == 0)
			{
				forward = (road.points[i+1].AsVector3() - current).normalized;
			}
			else if (i == road.points.Count - 1)
			{
				forward = (current - road.points[i - 1].AsVector3()).normalized;
			}
			else
			{
				Vector3 prevDir = (current - road.points[i - 1].AsVector3()).normalized;
				Vector3 nextDir = (road.points[i+1].AsVector3() - current).normalized;
				forward = (prevDir + nextDir).normalized;
			}

			Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
			
			Vector3 leftPos = current - right * halfWidth;
			Vector3 rightPos = current + right * halfWidth;

			vertices.Add(leftPos);
			vertices.Add(rightPos);

			float v = length * selectedPreset.uvTilingPerMeter;
			
			uvs.Add(new Vector2(1, v));
			uvs.Add(new Vector2(0, v));

			if (i < road.points.Count - 1)
			{
				length += Vector3.Distance(current, road.points[i + 1].AsVector3());
			}
		}

		Vector3 center = RoadEditorWindow.GetRoadCenter(road);

		for (int i = 0; i < vertices.Count; i++)
		{
			vertices[i] -= center;
		}
		
		for (int i = 1; i < road.points.Count; i++)
		{
			int baseIndex = i * 2;
			triangles.Add(baseIndex - 2);
			triangles.Add(baseIndex);
			triangles.Add(baseIndex - 1);

			triangles.Add(baseIndex);
			triangles.Add(baseIndex + 1);
			triangles.Add(baseIndex - 1);
		}

		Mesh mesh = new Mesh();
		mesh.name = "RoadMesh";
		
		mesh.SetVertices(vertices);
		mesh.SetTriangles(triangles, 0);
		mesh.SetUVs(0, uvs);
		
		mesh.RecalculateNormals();
		mesh.RecalculateTangents();
		mesh.RecalculateBounds();
		
		return mesh;
	}
}