using UnityEngine;

namespace Pistol.Weapons.Revolver
{
    // Names of the revolver Animator parameters and states, in one place.
    // The generator (RevolverAnimationSettings) builds the controller from these names,
    // the game talks to the Animator through the precomputed hashes (Hash.*).
    public static class RevolverAnimatorIds
    {
        // Trigger parameters
        public const string Fire = "Fire", Open = "Open", Close = "Close", Spin = "Spin";
        // Float parameters: playback speed of the matching state
        public const string FireSpeed = "FireSpeed", OpenSpeed = "OpenSpeed", CloseSpeed = "CloseSpeed", SpinSpeed = "SpinSpeed";
        // States (layer 0)
        public const string IdleState = "Idle", FireState = "Fire", OpenState = "Open",
                            OpenIdleState = "OpenIdle", SpinState = "Spin", CloseState = "Close";

        public static readonly string[] Triggers = { Fire, Open, Close, Spin };
        public static readonly string[] SpeedParams = { FireSpeed, OpenSpeed, CloseSpeed, SpinSpeed };
        public static readonly string[] States = { IdleState, FireState, OpenState, OpenIdleState, SpinState, CloseState };

        // Which speed parameter drives which state
        public static string SpeedParamFor(string state) => state switch
        {
            FireState => FireSpeed,
            OpenState => OpenSpeed,
            CloseState => CloseSpeed,
            SpinState => SpinSpeed,
            _ => null
        };

        public static class Hash
        {
            public static readonly int Fire = Animator.StringToHash(RevolverAnimatorIds.Fire);
            public static readonly int Open = Animator.StringToHash(RevolverAnimatorIds.Open);
            public static readonly int Close = Animator.StringToHash(RevolverAnimatorIds.Close);
            public static readonly int Spin = Animator.StringToHash(RevolverAnimatorIds.Spin);

            public static readonly int FireSpeed = Animator.StringToHash(RevolverAnimatorIds.FireSpeed);
            public static readonly int OpenSpeed = Animator.StringToHash(RevolverAnimatorIds.OpenSpeed);
            public static readonly int CloseSpeed = Animator.StringToHash(RevolverAnimatorIds.CloseSpeed);
            public static readonly int SpinSpeed = Animator.StringToHash(RevolverAnimatorIds.SpinSpeed);

            public static readonly int IdleState = Animator.StringToHash(RevolverAnimatorIds.IdleState);
            public static readonly int OpenIdleState = Animator.StringToHash(RevolverAnimatorIds.OpenIdleState);
            public static readonly int SpinState = Animator.StringToHash(RevolverAnimatorIds.SpinState);
        }
    }
}
