using UnityEngine;

namespace Pistol.Core
{
    // Draws a Vector3 as three sliders instead of three number fields
    public class Vector3SliderAttribute : PropertyAttribute
    {
        public enum Kind { Position, Rotation }
        public readonly Kind kind;
        public readonly float limit;
        public Vector3SliderAttribute(Kind kind, float limit) { this.kind = kind; this.limit = limit; }
    }
}
