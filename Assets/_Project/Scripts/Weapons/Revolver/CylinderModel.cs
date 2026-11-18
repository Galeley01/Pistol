using System;
using System.Text;
using Pistol.Ballistics;

namespace Pistol.Weapons.Revolver
{
    // State of each cylinder chamber
    public enum RoundState { Empty, Live, Spent }

    // Cylinder logic: what is in each of the 6 chambers and which chamber is under the hammer.
    // Plain C# class (not a MonoBehaviour): data and rules only, no visuals.
    // The view (RevolverCylinder) and firing (RevolverFire) subscribe to its events.
    public class CylinderModel
    {
        public const int Count = 6;

        readonly RoundState[] states = new RoundState[Count];
        readonly AmmoType[] ammo = new AmmoType[Count];

        // The chamber currently aligned with the barrel, under the hammer
        public int UnderHammer { get; private set; }

        public event Action<int> ChamberChanged;          // chamber i changed
        public event Action<int, AmmoType> Fired;         // shot fired from chamber i
        public event Action<int, RoundState> DryFired;    // hammer fell on an empty chamber or a spent case
        public event Action<int> Advanced;                // cylinder rotated, new chamber under the hammer

        public static int Wrap(int i) => ((i % Count) + Count) % Count;
        public RoundState GetState(int i) => states[Wrap(i)];
        public AmmoType GetAmmo(int i) => ammo[Wrap(i)];

        public int CountOf(RoundState s)
        {
            int n = 0;
            foreach (var st in states) if (st == s) n++;
            return n;
        }

        // Rotate the cylinder (one chamber per trigger pull)
        public void Advance(int steps = 1)
        {
            UnderHammer = Wrap(UnderHammer + steps);
            Advanced?.Invoke(UnderHammer);
        }

        // The hammer falls on the chamber under it
        // keepRound (debug: infinite ammo): the round isn't spent, and an empty chamber fires a round of the
        // kind loaded elsewhere in the cylinder
        public bool TryFire(out AmmoType firedAmmo, bool keepRound = false)
        {
            int i = UnderHammer;
            if (states[i] == RoundState.Live)
            {
                firedAmmo = ammo[i];
                if (!keepRound)
                {
                    states[i] = RoundState.Spent;
                    ChamberChanged?.Invoke(i);
                }
                Fired?.Invoke(i, firedAmmo);
                return true;
            }
            if (keepRound)
            {
                foreach (var a in ammo)
                    if (a != null) { firedAmmo = a; Fired?.Invoke(i, firedAmmo); return true; }
            }
            firedAmmo = null;
            DryFired?.Invoke(i, states[i]);
            return false;
        }

        // Insert a round into an empty chamber
        public bool Insert(int i, AmmoType type)
        {
            i = Wrap(i);
            if (states[i] != RoundState.Empty) return false;
            states[i] = RoundState.Live;
            ammo[i] = type;
            ChamberChanged?.Invoke(i);
            return true;
        }

        // Remove a spent case
        public bool RemoveSpent(int i)
        {
            i = Wrap(i);
            if (states[i] != RoundState.Spent) return false;
            states[i] = RoundState.Empty;
            ammo[i] = null;
            ChamberChanged?.Invoke(i);
            return true;
        }

        public void Set(int i, RoundState state, AmmoType type)
        {
            i = Wrap(i);
            states[i] = state;
            ammo[i] = state == RoundState.Empty ? null : type;
            ChamberChanged?.Invoke(i);
        }

        public void FillAll(AmmoType type) { for (int i = 0; i < Count; i++) Set(i, RoundState.Live, type); }
        public void SpendAll() { for (int i = 0; i < Count; i++) if (states[i] != RoundState.Empty) Set(i, RoundState.Spent, ammo[i]); }
        public void EmptyAll() { for (int i = 0; i < Count; i++) Set(i, RoundState.Empty, null); }

        // For the Inspector: "[●] ● ○ ◌ ● ●", brackets mark the chamber under the hammer
        // ● live round, ○ spent case, ◌ empty
        public string Describe()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Count; i++)
            {
                char c = states[i] == RoundState.Live ? '●' : states[i] == RoundState.Spent ? '○' : '◌';
                if (i == UnderHammer) sb.Append('[').Append(c).Append(']');
                else sb.Append(' ').Append(c).Append(' ');
            }
            return sb.ToString();
        }
    }
}
