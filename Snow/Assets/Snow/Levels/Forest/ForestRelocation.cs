using UnityEngine;

namespace Snow.Levels.Forest
{
    /// <summary>
    /// The checkpoint moved out along the road (user decision 21.10: "КПП должен быть намного дальше от базы"): one rigid move
    /// of the booth, the barrier and everything of the opening that belongs to them — keys and log in the booth, the padlock,
    /// the phone and its bell. The Forest integration places the models and sets this component; at run time the opening and
    /// the item spawner pass their data positions through <see cref="Point"/> / <see cref="Rotation"/>, so the data files stay
    /// as the package made them. Positions farther than <see cref="radius"/> from the old pivot are left alone.
    /// </summary>
    public class ForestRelocation : MonoBehaviour
    {
        [Tooltip("The barrier's place in the package (the pivot of the move)")] public Vector3 oldPivot;
        [Tooltip("Where the pivot stands now")] public Vector3 newPivot;
        [Tooltip("Turn about Y (deg) to follow the road")] public float yaw;
        [Tooltip("m around the old pivot that moves")] public float radius = 22f;
        [Header("Yard drawn in (ForestYardPlan): each moved structure carries what lies within its radius")]
        public Vector3[] yardFrom = new Vector3[0];
        public Vector3[] yardTo = new Vector3[0];
        public float[] yardRadius = new float[0];

        public static ForestRelocation Instance { get; private set; }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public bool Moves(Vector3 p) => isActiveAndEnabled && new Vector2(p.x - oldPivot.x, p.z - oldPivot.z).sqrMagnitude <= radius * radius;

        /// <summary>A data position after the move (unchanged outside the moved group).</summary>
        public static Vector3 Point(Vector3 p)
        {
            ForestRelocation r = Instance;
            if (r == null) return p;
            if (r.Moves(p)) return r.newPivot + Quaternion.Euler(0f, r.yaw, 0f) * (p - r.oldPivot);
            int y = r.Yard(p);
            return y >= 0 ? p + (r.yardTo[y] - r.yardFrom[y]) : p;
        }

        /// <summary>The moved yard structure a data point belongs to (the nearest within its radius), or -1.</summary>
        int Yard(Vector3 p)
        {
            if (!isActiveAndEnabled || yardFrom == null) return -1;
            int best = -1;
            float bestD = float.MaxValue;
            for (int i = 0; i < yardFrom.Length && i < yardTo.Length && i < yardRadius.Length; i++)
            {
                float d = new Vector2(p.x - yardFrom[i].x, p.z - yardFrom[i].z).sqrMagnitude;
                if (d <= yardRadius[i] * yardRadius[i] && d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>A data rotation after the move (<paramref name="p"/> is its data position).</summary>
        public static Quaternion Rotation(Vector3 p, Quaternion q)
        {
            ForestRelocation r = Instance;
            return r == null || !r.Moves(p) ? q : Quaternion.Euler(0f, r.yaw, 0f) * q;
        }
    }
}
