using UnityEngine;

namespace Snow.Aircraft
{
    /// <summary>
    /// One ray-suspension wheel (§9.1: no WheelCollider). Pure C#. The strut is a spring-damper along the aircraft's up
    /// axis with a bump stop past its travel; its force reaches the ground along the ground normal. The tyre holds the ground through an anchor point with a stiff spring and
    /// damper in the ground plane: it rolls along its heading, holds sideways until the friction circle is exceeded,
    /// then slides. Longitudinally only rolling resistance and brakes hold it, so a parked aircraft with the brakes set
    /// stays put in wind, and without brakes it rolls. Ground loops, weathervaning and nose-overs come from this plus the
    /// aircraft's geometry, not from scripts.
    /// </summary>
    public class WheelContact
    {
        public readonly float Radius, Travel, Spring, Damper, BumpStop, Grip;

        Vector3 _anchor;
        bool _anchored;
        float _lastCompression;

        /// <summary>m, 0 = strut fully extended.</summary>
        public float Compression { get; private set; }
        public bool Grounded { get; private set; }
        /// <summary>m/s along the wheel heading, + = forward.</summary>
        public float RollSpeed { get; private set; }
        public float NormalForce { get; private set; }
        public bool Sliding { get; private set; }

        public WheelContact(float radius, float travel, float spring, float damper, float bumpStop, float grip)
        {
            Radius = radius; Travel = travel; Spring = spring; Damper = damper; BumpStop = bumpStop; Grip = grip;
        }

        public void Lift()
        {
            Grounded = false;
            _anchored = false;
            Compression = 0f;
            _lastCompression = 0f;
            NormalForce = 0f;
            Sliding = false;
        }

        /// <summary>
        /// World force at the contact point. distance: wheel centre (strut fully extended) to the ground along -up, m.
        /// up: aircraft up. heading: wheel forward (steered). v: velocity of the contact point over the ground.
        /// brakeCapacity: brake friction coefficient × pedal (0 = free). tyreK, tyreDamping: tangential stiffness N/m and
        /// damping ratio. massShare: kg carried by this wheel, for the damping.
        /// </summary>
        public Vector3 Step(float distance, Vector3 contactPoint, Vector3 groundNormal, Vector3 up, Vector3 heading, Vector3 v,
                            float brakeCapacity, float rolling, float tyreK, float tyreDamping, float massShare, float dt)
        {
            float c = Radius - distance;
            if (c <= 0f)
            {
                Lift();
                RollSpeed = Vector3.Dot(v, heading);
                return Vector3.zero;
            }
            Grounded = true;
            Compression = c;
            float rate = (c - _lastCompression) / dt;
            _lastCompression = c;
            float spring = c <= Travel ? Spring * c : Spring * Travel + Spring * BumpStop * (c - Travel);
            float n = Mathf.Max(0f, spring + Damper * rate);
            NormalForce = n;

            Vector3 fwd = Vector3.ProjectOnPlane(heading, groundNormal).normalized;
            Vector3 side = Vector3.Cross(groundNormal, fwd).normalized;
            float vLon = Vector3.Dot(v, fwd), vLat = Vector3.Dot(v, side);
            RollSpeed = vLon;

            if (!_anchored) { _anchor = contactPoint; _anchored = true; }
            Vector3 d = contactPoint - _anchor;
            float eLon = Vector3.Dot(d, fwd), eLat = Vector3.Dot(d, side);
            float cDamp = 2f * tyreDamping * Mathf.Sqrt(tyreK * Mathf.Max(20f, massShare));

            float lat = -tyreK * eLat - cDamp * vLat;
            float lon = -tyreK * eLon - cDamp * vLon;
            float lonCap = (brakeCapacity + rolling) * n;
            Sliding = false;
            if (Mathf.Abs(lon) > lonCap)
            {
                lon = Mathf.Sign(lon) * lonCap;
                eLon = -(lon + cDamp * vLon) / tyreK;                  // the anchor rolls / skids along
            }
            float max = Grip * n;
            Vector2 f = new Vector2(lon, lat);
            if (f.magnitude > max)
            {
                f = f.normalized * max;
                lon = f.x; lat = f.y;
                eLon = -(lon + cDamp * vLon) / tyreK;
                eLat = -(lat + cDamp * vLat) / tyreK;
                Sliding = true;
            }
            // holding: the anchor stays; sliding or rolling: it follows so the spring carries exactly the limited force
            _anchor = contactPoint - fwd * eLon - side * eLat;
            // the ground pushes along its normal: along the strut axis the tail-down stance (model v5+, 10°) would push a
            // free-rolling aircraft backwards with sin 10° of its weight (И1 check I-04, 16.10)
            return groundNormal * n + fwd * lon + side * lat;
        }
    }
}
