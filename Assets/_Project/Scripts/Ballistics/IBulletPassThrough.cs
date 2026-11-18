namespace Pistol.Ballistics
{
    // A surface a bullet goes through (a cardboard target): BulletSystem asks it on every hit.
    // true = the bullet flies on: no hole mark is left (the surface shows its own hole) and the flight goes on behind it.
    // counts = false when the bullet flew through a hole that is already there: the hit is not reported and makes no sound.
    public interface IBulletPassThrough
    {
        bool PassesThrough(in BulletHit hit, out bool counts);
    }
}
