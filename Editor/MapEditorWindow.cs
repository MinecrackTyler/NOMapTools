using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RoadPathfinding;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NOMapTools
{
    public class MapEditorWindow : EditorWindow
    {
        private List<MapTool> tools;
        private MapTool activeTool;
        private int activeToolIndex;
        
        [MenuItem("Window/Map Tools")]
        public static void OpenWindow()
        {
            var window = GetWindow<MapEditorWindow>();
            window.titleContent = new GUIContent("Map Tools");
            window.Show();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SceneView.duringSceneGui += OnSceneGUI;
            
            tools ??= new  List<MapTool>();
            
            TryAddTool<RoadTool>();
            
            tools.ForEach(tool => tool.OnEnable());
            activeTool = tools[0];
        }

        private void TryAddTool<T>() where T : MapTool, new()
        {
            if (tools.All(t => t.GetType() != typeof(T))) tools.Add(new T());
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
        }

        private void OnGUI()
        {
            GUILayout.Label("Map Tool", EditorStyles.boldLabel);
            string[] names = tools.Select(tool => tool.ToolName).ToArray();
            activeToolIndex = GUILayout.Toolbar(activeToolIndex, names);
            activeTool = tools[activeToolIndex];
            
            activeTool?.OnGUI();
            
            if (GUI.changed) SceneView.RepaintAll();
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            activeTool?.OnSceneGUI(sceneView);

            if (GUI.changed) sceneView.Repaint();
        }
    }
}