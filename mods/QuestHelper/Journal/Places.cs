using UnityEngine;

namespace QuestHelper
{
    // Place names for the "Received" line (spec §5.2): the nearest landmark the player has discovered on the same map,
    // read from MapController.mapInfos (the map's own saved list), so nothing undiscovered is ever named.
    internal static class Places
    {
        internal static string Nearest(string mapId, Vector3 pos, float radius)
        {
            var mc = MapController.instance;
            if (mc == null || string.IsNullOrEmpty(mapId) || mc.mapInfos == null) return null;
            string best = null;
            float bestD = radius * radius;
            foreach (var info in mc.mapInfos)
            {
                if (info == null || info.taskItemId != mapId || info.landmarkInfos == null) continue;
                foreach (var li in info.landmarkInfos)
                {
                    if (li == null || li.landmark == null || string.IsNullOrEmpty(li.landmark.title)) continue;
                    var p = li.GetPosition();
                    float dx = p.x - pos.x, dz = p.z - pos.z;
                    float d = dx * dx + dz * dz;
                    if (d <= bestD) { bestD = d; best = li.landmark.title; }
                }
            }
            return best;
        }
    }
}
