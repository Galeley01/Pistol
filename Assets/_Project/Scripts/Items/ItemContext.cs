using UnityEngine;
using Pistol.Audio;
using Pistol.Ballistics;
using Pistol.Core;
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
        public BulletSystem Bullets { get; }    // may be null: then weapons can't shoot
        public DebugSettings Debug { get; }     // may be null: then no debug output
        public ItemHolderWithPose Holder { get; }

        public ItemContext(Camera camera, PlayerMovement movement, CharacterController controller, PlayerContext player,
                           CameraEffects effects, GameAudio audio, BulletSystem bullets, DebugSettings debug,
                           ItemHolderWithPose holder)
        {
            Bullets = bullets;
            Debug = debug;
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
    //
    // Unbind is called when the item leaves the hands (Unequip). The part must give back everything it changed
    // outside the item (camera FOV, Free-Aim, screen effects, control mode), drop the context and reset its
    // motion, so the item can lie in the world or be taken again (a new Bind) as if it was never held.
    public interface IItemContextUser
    {
        void Bind(ItemContext context);
        void Unbind() { }
    }
}
