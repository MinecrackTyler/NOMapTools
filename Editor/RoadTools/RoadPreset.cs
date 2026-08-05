using UnityEngine;

namespace NOMapTools
{
	[CreateAssetMenu(fileName = "New Road Preset", menuName = "Map Tools/Road Preset")]
	public class RoadPreset : ScriptableObject
	{
		public string presetName;
		public float defaultWidth = 6f;
		public Material material;
		public float uvTilingPerMeter = 0.2f;
	}
}