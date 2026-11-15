using System;
using System.Text;

// Состояние каждой каморы барабана
public enum RoundState { Empty, Live, Spent }

// Логика барабана: что лежит в каждой из 6 камор и какая камора под курком.
// Обычный C#-класс (не MonoBehaviour): только данные и правила, без визуала.
// Визуал (RevolverCylinder) и стрельба (RevolverFire) подписываются на события.
public class CylinderModel
{
    public const int Count = 6;

    readonly RoundState[] states = new RoundState[Count];
    readonly AmmoType[] ammo = new AmmoType[Count];

    // Камора, которая сейчас стоит напротив ствола и под курком
    public int UnderHammer { get; private set; }

    public event Action<int> ChamberChanged;          // камора i изменилась
    public event Action<int, AmmoType> Fired;         // выстрел из каморы i
    public event Action<int, RoundState> DryFired;    // курок ударил по пустой каморе или гильзе
    public event Action<int> Advanced;                // барабан провернулся, новая камора под курком

    public static int Wrap(int i) => ((i % Count) + Count) % Count;
    public RoundState GetState(int i) => states[Wrap(i)];
    public AmmoType GetAmmo(int i) => ammo[Wrap(i)];

    public int CountOf(RoundState s)
    {
        int n = 0;
        foreach (var st in states) if (st == s) n++;
        return n;
    }

    // Барабан проворачивается (при нажатии на спуск — на одну камору)
    public void Advance(int steps = 1)
    {
        UnderHammer = Wrap(UnderHammer + steps);
        Advanced?.Invoke(UnderHammer);
    }

    // Курок падает на камору под ним
    public bool TryFire(out AmmoType firedAmmo)
    {
        int i = UnderHammer;
        if (states[i] == RoundState.Live)
        {
            states[i] = RoundState.Spent;
            firedAmmo = ammo[i];
            ChamberChanged?.Invoke(i);
            Fired?.Invoke(i, firedAmmo);
            return true;
        }
        firedAmmo = null;
        DryFired?.Invoke(i, states[i]);
        return false;
    }

    // Вставить патрон в пустую камору
    public bool Insert(int i, AmmoType type)
    {
        i = Wrap(i);
        if (states[i] != RoundState.Empty) return false;
        states[i] = RoundState.Live;
        ammo[i] = type;
        ChamberChanged?.Invoke(i);
        return true;
    }

    // Вынуть стреляную гильзу
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

    // Для Inspector: «[●] ● ○ ◌ ● ●» — в скобках камора под курком
    // ● патрон, ○ стреляная гильза, ◌ пусто
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
