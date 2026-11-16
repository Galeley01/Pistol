using System.Collections.Generic;
using UnityEngine;
using Pistol.Player;

namespace Pistol.Items
{
    // Everything pose layers may need from outside the item. ItemHolderWithPose fills it and hands it
    // to each layer once (Bind), so items never search their parents or the scene for it.
    public sealed class PoseContext
    {
        public Camera Camera { get; }
        public PlayerMovement Movement { get; }
        public CharacterController Controller { get; }
        public PlayerContext Player { get; }
        public ItemHolderWithPose Holder { get; }

        public PoseContext(Camera camera, PlayerMovement movement, CharacterController controller,
                           PlayerContext player, ItemHolderWithPose holder)
        {
            Camera = camera;
            Movement = movement;
            Controller = controller;
            Player = player;
            Holder = holder;
        }
    }

    // Where a layer runs in the stack: lower runs first
    public static class PoseOrder
    {
        public const int Base = 0;      // hip / aim pose of the item root
        public const int State = 100;   // layers that only publish weights (inspect)
        public const int Hand = 200;    // sway, sprint, inspect pose: rotation around the wrist
        public const int Recoil = 300;  // shot recoil in the item's own axes
    }

    // A piece of the held item pose. Layers never write the item transform themselves:
    // they add their part to the frame and ItemHolderWithPose writes the result once.
    public interface IPoseLayer
    {
        int PoseOrder { get; }
        void Bind(PoseContext context);
        void ApplyPose(ref PoseFrame frame);
    }

    // One frame of the held item pose, built by the layers in order and composed in three steps:
    //   1. Base   - the item root in holder space (hip / aim).
    //   2. Hand   - angles and offsets from the layers are summed first and applied as ONE rotation around
    //               HandPivot (item-local), then HandOffset (holder space). Summing first keeps big poses
    //               (sprint, inspect) exactly as they were with a single rotation.
    //   3. Recoil - rotation in the item's own axes around RecoilPivot (item-local), then RecoilOffset along the item's axes.
    public struct PoseFrame
    {
        public float DeltaTime;

        // Weights published by layers for the layers after them (0..1)
        public float Aim;
        public float Inspect;

        public Vector3 BasePosition;
        public Quaternion BaseRotation;

        public Vector3 HandEuler;
        public Vector3 HandOffset;
        public Vector3 HandPivot;

        public Vector3 RecoilEuler;
        public Vector3 RecoilOffset;
        public Vector3 RecoilPivot;

        public static PoseFrame Begin(float deltaTime, Vector3 restPosition, Quaternion restRotation) => new PoseFrame
        {
            DeltaTime = deltaTime,
            BasePosition = restPosition,
            BaseRotation = restRotation,
        };

        public void Compose(out Vector3 position, out Quaternion rotation)
        {
            // 2. Hand: rotate the base pose around the pivot point, then shift
            Quaternion hand = Quaternion.Euler(HandEuler);
            Vector3 handPoint = BasePosition + BaseRotation * HandPivot;
            Vector3 p = handPoint + hand * (BasePosition - handPoint) + HandOffset;
            Quaternion q = hand * BaseRotation;

            // 3. Recoil: rotation in the item's axes around the grip, then a shift along the item's axes
            Quaternion kick = q * Quaternion.Euler(RecoilEuler) * Quaternion.Inverse(q);
            Vector3 recoilPoint = p + q * RecoilPivot;
            position = recoilPoint + kick * (p - recoilPoint) + q * RecoilOffset;
            rotation = kick * q;
        }
    }

    // The ordered list of layers of one held item
    public sealed class PoseStack
    {
        readonly List<IPoseLayer> layers = new List<IPoseLayer>();

        public int Count => layers.Count;

        public void Collect(GameObject item, PoseContext context)
        {
            layers.Clear();
            item.GetComponents(layers);
            // Stable sort by order: layers with the same order keep their component order
            for (int i = 1; i < layers.Count; i++)
            {
                var layer = layers[i];
                int j = i - 1;
                while (j >= 0 && layers[j].PoseOrder > layer.PoseOrder) { layers[j + 1] = layers[j]; j--; }
                layers[j + 1] = layer;
            }
            foreach (var layer in layers) layer.Bind(context);
        }

        public void Apply(ref PoseFrame frame)
        {
            foreach (var layer in layers)
            {
                // A disabled component (or one that disabled itself, e.g. no profile) adds nothing
                if (layer is Behaviour b && !b.isActiveAndEnabled) continue;
                layer.ApplyPose(ref frame);
            }
        }
    }
}
