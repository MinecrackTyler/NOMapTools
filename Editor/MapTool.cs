using UnityEditor;

namespace NOMapTools
{
	public abstract class MapTool
	{
		public virtual string ToolName => "";
	
		public abstract void OnEnable();
		public abstract void OnGUI();
		public abstract void OnSceneGUI(SceneView sceneView);

	}
}