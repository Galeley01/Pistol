using UnityEngine;
using Pistol.Audio;
using Pistol.Player;
using Pistol.PostFx;

namespace Pistol.Items
{
    // Everything parts of a held item may need from outside it: the player, the camera and the services.
    // ItemHolderWithPose fills it and hands it to each part once (Bind), so items never search
    // their parents or the scene.
    public sealed class ItemContext
    {
        public Camera Camera { get; }
        public PlayerMovement Movement { get; }
        public CharacterController Controller { get; }
        public PlayerContext Player { get; }
        public CameraEffects Effects { get; }   // may be null: then items show no screen effects
        public GameAudio Audio { get; }         // may be null: then items are silent
        public ItemHolderWithPose Holder { get; }

        public ItemContext(Camera camera, PlayerMovement movement, CharacterController controller, PlayerContext player,
                           CameraEffects effects, GameAudio audio, ItemHolderWithPose holder)
        {
            Camera = camera;
            Movement = movement;
            Controller = controller;
            Player = player;
            Effects = effects;
            Audio = audio;
            Holder = holder;
        }
    }

    // A component of a held item that needs the context. ItemHolderWithPose calls Bind once, in Awake,
    // before any Start: use the context from Start on.
    public interface IItemContextUser
    {
        void Bind(ItemContext context);
    }
}
