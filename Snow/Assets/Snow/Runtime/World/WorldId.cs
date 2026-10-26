using UnityEngine;

namespace Snow.World
{
    /// <summary>
    /// A placed object's id for the Unity ↔ Blender exchange (docs/gdd/WORLD_SYNC_CONTRACT.md): "&lt;group&gt;/&lt;model&gt;#&lt;n&gt;",
    /// n counting same-named objects of the group in the package's order. The Forest integration writes it, with the pose the
    /// build put the object at; a hand move in the editor is the difference to that pose (Editor/WorldSync.cs keeps it in
    /// Levels/Forest/ForestMoves.json, so a rebuild puts the object back where it was moved). Not used in play.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldId : MonoBehaviour
    {
        public string id;
        /// <summary>The pose after the integration's own steps, before the hand moves; false until the integration ran.</summary>
        public bool placed;
        public Vector3 placedPosition;
        public Quaternion placedRotation = Quaternion.identity;
    }
}
