using System.Collections.Generic;
using UnityEngine;

namespace Pistol.Arms
{
    // Debug layer: live finger angles (from LiveHandReceiver) on top of the grip's finger pose.
    //
    // Angles are a model-independent hand pose, 20 per hand, degrees:
    //   thumb  across (base direction in the palm plane, + toward the little finger),
    //          away (out of the palm plane, + to the palm side), mcp, ip (flexion, + toward the palm)
    //   index, middle, ring, pinky: mcp, pip, dip (flexion, + toward the palm), spread (+ toward the thumb)
    // Zero flexion is a straight finger; spread and the thumb base are directions in the hand frame.
    //
    // Each hand's frame and its fingers' rest pose are measured from the skin's own rest geometry
    // (Z hand -> middle finger base, Y back of the hand: opposite to where the resting fingers curl,
    // X toward the little finger). Each frame the fingers are put to rest, every phalanx is turned to
    // point along the direction built from the angles, and the result is blended with the grip pose.
    //
    // Against tracking glitches: angles are clamped per joint, their speed is limited, and the layer
    // fades in and out (also when a hand is lost) instead of switching.
    public class LiveFingers
    {
        public class Settings
        {
            public float weight = 1f;
            public float fadeTime = 0.25f;
            public float sharpness = 20f;          // 1/s, how fast the angles follow
            public float maxSpeed = 600f;          // degrees per second per angle
        }

        // Per-angle limits, degrees (stream layout)
        static readonly Vector2 Flex = new Vector2(-30f, 110f);
        static readonly Vector2 Spread = new Vector2(-35f, 35f);
        static readonly Vector2[] ThumbLimits =
        {
            new Vector2(-90f, 60f),   // across
            new Vector2(-30f, 90f),   // away
            new Vector2(-30f, 90f),   // mcp
            new Vector2(-30f, 90f),   // ip
        };

        class Side
        {
            public Hand hand;
            public float[] angles = new float[LiveHandReceiver.AngleCount];
            public float blend;
            public bool hasPlacement;
            public Quaternion wrist = Quaternion.identity;   // anatomical hand (Z fingers, Y palm) in camera space
            public Vector3 offset;                           // meters, camera space
            public float placementBlend;
        }

        readonly Side right = new Side(), left = new Side();
        readonly Settings settings = new Settings();
        public Settings Config => settings;

        public LiveFingers(Transform rightHand, Transform leftHand, IReadOnlyList<ArmsRig.FingerJoint> joints)
        {
            right.hand = Hand.Build(rightHand, true, joints);
            left.hand = Hand.Build(leftHand, false, joints);
        }

        public bool Active(bool rightSide) => (rightSide ? right : left).blend > 0f;

        // The live wrist of a hand: anatomical rotation (Z fingers, Y palm) and offset in camera space,
        // with a 0..1 weight that fades in and out with tracking
        public bool Placement(bool rightSide, out Quaternion wrist, out Vector3 offset, out float weight)
        {
            var side = rightSide ? right : left;
            wrist = side.wrist;
            offset = side.offset;
            weight = side.hasPlacement ? side.placementBlend : 0f;
            return weight > 0f;
        }

        // Called once a frame before Apply: follows the received angles (or fades out)
        public void Update(LiveHandReceiver receiver, bool useRight, bool useLeft, float dt)
        {
            Follow(right, receiver != null && useRight ? receiver.GetHand(true) : null, dt);
            Follow(left, receiver != null && useLeft ? receiver.GetHand(false) : null, dt);
        }

        void Follow(Side side, LiveHandReceiver.Hand msg, float dt)
        {
            if (side.hand == null) return;
            bool ok = msg != null && msg.Valid;
            bool appeared = ok && side.blend <= 0f;
            float fade = settings.fadeTime > 0f ? dt / settings.fadeTime : 1f;
            side.blend = Mathf.MoveTowards(side.blend, ok ? 1f : 0f, fade);
            bool placed = ok && msg.HasPlacement;
            side.placementBlend = Mathf.MoveTowards(side.placementBlend, placed ? 1f : 0f, fade);
            if (!ok) return;

            float k = 1f - Mathf.Exp(-settings.sharpness * dt);
            if (placed)
            {
                // Ready values from the tool; only interpolated here to cover the gap between packets
                Vector3 fingers = new Vector3(msg.viewFingers[0], msg.viewFingers[1], msg.viewFingers[2]);
                Vector3 back = new Vector3(msg.viewBack[0], msg.viewBack[1], msg.viewBack[2]);
                Vector3 offset = new Vector3(msg.offset[0], msg.offset[1], msg.offset[2]);
                if (fingers.sqrMagnitude > 1e-6f && back.sqrMagnitude > 1e-6f)
                {
                    Quaternion wrist = Quaternion.LookRotation(fingers, -back);
                    bool first = !side.hasPlacement || side.placementBlend <= fade;
                    side.wrist = first ? wrist : Quaternion.Slerp(side.wrist, wrist, k);
                    side.offset = first ? offset : Vector3.Lerp(side.offset, offset, k);
                    side.hasPlacement = true;
                }
            }
            float step = settings.maxSpeed * dt;
            for (int i = 0; i < LiveHandReceiver.AngleCount; i++)
            {
                float target = Clamp(i, msg.angles[i]);
                if (appeared) { side.angles[i] = target; continue; }
                float smooth = Mathf.Lerp(side.angles[i], target, k);
                side.angles[i] = Mathf.MoveTowards(side.angles[i], smooth, step);
            }
        }

        static float Clamp(int i, float value)
        {
            Vector2 r = i < 4 ? ThumbLimits[i] : (i % 4 == 3 ? Spread : Flex);
            return Mathf.Clamp(value, r.x, r.y);
        }

        // Called after the grip has posed the fingers: blends the live pose over it
        public void Apply()
        {
            Apply(right);
            Apply(left);
        }

        void Apply(Side side)
        {
            if (side.hand == null || side.blend <= 0f) return;
            float w = side.blend * Mathf.Clamp01(settings.weight);
            if (w <= 0f) return;
            side.hand.Pose(side.angles, w);
        }

        // ---------- One hand of the skin ----------
        class Hand
        {
            const int Thumb = 0;
            Transform hand;
            readonly Transform[][] chains = new Transform[5][];   // 3 joints + the tip
            readonly Quaternion[][] rest = new Quaternion[5][];
            readonly Quaternion[][] grip = new Quaternion[5][];
            Vector3 axisX, axisY, axisZ;                          // in the hand bone's space
            float flexSign;

            public static Hand Build(Transform handBone, bool rightSide, IReadOnlyList<ArmsRig.FingerJoint> joints)
            {
                if (handBone == null) return null;
                var h = new Hand { hand = handBone };
                for (int f = 0; f < 5; f++)
                {
                    var bones = new List<ArmsRig.FingerJoint>();
                    foreach (var j in joints) if (j.right == rightSide && j.finger == f && j.bone != null) bones.Add(j);
                    if (bones.Count != 3) return null;
                    Transform last = bones[2].bone;
                    Transform tip = last.childCount > 0 ? last.GetChild(0) : null;
                    if (tip == null) return null;
                    h.chains[f] = new[] { bones[0].bone, bones[1].bone, last, tip };
                    h.rest[f] = new[] { bones[0].restLocal, bones[1].restLocal, bones[2].restLocal };
                    h.grip[f] = new Quaternion[3];
                }
                // Frame from the rest pose (the skin is at rest when the arms are created)
                h.RestToBones();
                Vector3 wrist = handBone.position;
                Vector3 z = (h.chains[2][0].position - wrist).normalized;
                Vector3 y = -Perp(h.chains[2][2].position - h.chains[2][0].position, z).normalized;
                Vector3 x = Perp(Perp(h.chains[4][0].position - h.chains[1][0].position, z), y).normalized;
                if (z.sqrMagnitude < 0.5f || y.sqrMagnitude < 0.5f || x.sqrMagnitude < 0.5f) return null;
                Quaternion inv = Quaternion.Inverse(handBone.rotation);
                h.axisX = inv * x;
                h.axisY = inv * y;
                h.axisZ = inv * z;
                h.flexSign = Vector3.Dot(Vector3.Cross(x, z), -y) >= 0f ? 1f : -1f;
                return h;
            }

            void RestToBones()
            {
                for (int f = 0; f < 5; f++)
                    for (int j = 0; j < 3; j++) chains[f][j].localRotation = rest[f][j];
            }

            // Turns the fingers to the angles and blends the result with the current (grip) pose
            public void Pose(float[] a, float weight)
            {
                for (int f = 0; f < 5; f++)
                    for (int j = 0; j < 3; j++) grip[f][j] = chains[f][j].localRotation;
                RestToBones();

                Vector3 x = hand.rotation * axisX, y = hand.rotation * axisY, z = hand.rotation * axisZ;

                // Thumb: base direction, then two flexions toward the palm and the little finger
                {
                    float c = a[0] * Mathf.Deg2Rad, w = a[1] * Mathf.Deg2Rad;
                    Vector3 d1 = Mathf.Cos(w) * Mathf.Sin(c) * x - Mathf.Sin(w) * y + Mathf.Cos(w) * Mathf.Cos(c) * z;
                    Vector3 q = (-y + x).normalized;
                    Vector3 d2 = Rotate(d1, Vector3.Cross(d1, q).normalized, a[2]);
                    Vector3 d3 = Rotate(d2, Vector3.Cross(d2, q).normalized, a[3]);
                    Point(Thumb, d1, d2, d3);
                }
                // Four fingers: directions in the hand frame, so the skin's knuckle fan doesn't matter
                for (int f = 1; f < 5; f++)
                {
                    float s = a[4 * f + 3] * Mathf.Deg2Rad;
                    Vector3 v = Mathf.Cos(s) * z - Mathf.Sin(s) * x;
                    Vector3 d1 = Rotate(v, flexSign * x, a[4 * f]);
                    Vector3 d2 = Rotate(d1, flexSign * Perp(x, d1).normalized, a[4 * f + 1]);
                    Vector3 d3 = Rotate(d2, flexSign * Perp(x, d2).normalized, a[4 * f + 2]);
                    Point(f, d1, d2, d3);
                }

                if (weight < 1f)
                    for (int f = 0; f < 5; f++)
                        for (int j = 0; j < 3; j++)
                            chains[f][j].localRotation = Quaternion.Slerp(grip[f][j], chains[f][j].localRotation, weight);
            }

            void Point(int f, Vector3 d1, Vector3 d2, Vector3 d3)
            {
                var c = chains[f];
                Aim(c[0], c[1], d1);
                Aim(c[1], c[2], d2);
                Aim(c[2], c[3], d3);
            }

            static void Aim(Transform bone, Transform child, Vector3 direction)
            {
                Vector3 current = child.position - bone.position;
                if (current.sqrMagnitude < 1e-12f || direction.sqrMagnitude < 1e-12f) return;
                bone.rotation = Quaternion.FromToRotation(current, direction) * bone.rotation;
            }

            // Rotation of v around a unit axis, degrees; with axis = v x q it turns v toward q
            static Vector3 Rotate(Vector3 v, Vector3 axis, float degrees)
            {
                float r = degrees * Mathf.Deg2Rad, cos = Mathf.Cos(r), sin = Mathf.Sin(r);
                return v * cos + Vector3.Cross(axis, v) * sin + axis * (Vector3.Dot(axis, v) * (1f - cos));
            }

            static Vector3 Perp(Vector3 v, Vector3 axis) => v - Vector3.Dot(v, axis) * axis;
        }
    }
}
