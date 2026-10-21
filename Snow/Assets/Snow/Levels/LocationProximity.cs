using SkyIsland;
using Unity.Netcode;
using UnityEngine;

namespace Snow.Levels
{
    /// <summary>
    /// Building interiors of a location (Town: stair cores, flats and their openable parts) on in a networked game. The
    /// core's SkyIslandProximityGroup looks at its own camera only: a host far away would keep a client's building off,
    /// and the server's reach check (InteractionHost.InReach -> Collider.ClosestPoint) would run against a disabled zone.
    /// Here every 0.25 s a group's children are on while this peer's camera is inside the group's bounds grown by its
    /// margin, and on the server also while any connected player is. Before a session starts: the own camera only.
    /// The groups' own components are switched off by the level builder (their Start would hide the children).
    /// </summary>
    public class LocationProximity : MonoBehaviour
    {
        [Tooltip("The location's interior groups (their SkyIslandProximityGroup components are disabled)")]
        public SkyIslandProximityGroup[] groups = new SkyIslandProximityGroup[0];
        [Tooltip("Seconds between checks")] public float interval = 0.25f;

        bool[] _on;
        Vector3[] _viewers = new Vector3[8];
        float _next;

        void Start()
        {
            _on = new bool[groups.Length];
            for (int i = 0; i < groups.Length; i++) _on[i] = true;      // as saved in the scene: everything on
            Check();                                                    // at once, not a frame of every interior
        }

        void Update()
        {
            if (Time.time < _next) return;
            Check();
        }

        void Check()
        {
            _next = Time.time + interval;
            int n = Viewers();
            for (int i = 0; i < groups.Length; i++)
            {
                SkyIslandProximityGroup g = groups[i];
                if (g == null) continue;
                Bounds b = g.bounds;
                b.Expand(g.margin * 2f);
                bool want = false;
                for (int v = 0; v < n && !want; v++) want = b.Contains(_viewers[v]);
                if (want == _on[i]) continue;
                _on[i] = want;
                g.SetChildren(want);
            }
        }

        /// <summary>This peer's camera; on the server also every connected player.</summary>
        int Viewers()
        {
            int n = 0;
            Camera cam = Camera.main;
            if (cam != null) _viewers[n++] = cam.transform.position;
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return n;
            var clients = nm.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)                    // index loop: no enumerator boxed every check
            {
                NetworkClient c = clients[i];
                if (c.PlayerObject == null) continue;
                if (n == _viewers.Length) System.Array.Resize(ref _viewers, n * 2);
                _viewers[n++] = c.PlayerObject.transform.position;
            }
            return n;
        }
    }
}
