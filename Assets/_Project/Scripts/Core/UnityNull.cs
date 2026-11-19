namespace Pistol.Core
{
    // An interface reference found with GetComponent: a destroyed component is "null" only by Unity's own check
    public static class UnityNull
    {
        public static bool Alive(object o) => o is UnityEngine.Object u ? u != null : o != null;
    }
}
