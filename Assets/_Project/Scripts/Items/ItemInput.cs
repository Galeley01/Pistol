using UnityEngine;
using Pistol.Player;

namespace Pistol.Items
{
    // Turns the player's input into intents for the item in the hands:
    //   Gameplay mode: Fire -> Use, Reload -> Reload, Inspect (Alt) -> Inspect.
    //   Inspect mode:  Exit (Alt / Esc) -> ExitInspect, Reload -> ExitInspect + ReloadFromInspect.
    // Only the active action map gives presses, so a click that recaptures the cursor never fires.
    //
    // Goes next to ItemHolderWithPose.
    [RequireComponent(typeof(ItemHolderWithPose))]
    [DisallowMultipleComponent]
    public class ItemInput : MonoBehaviour
    {
        ItemHolderWithPose holder;

        void Awake() => holder = GetComponent<ItemHolderWithPose>();

        void Update()
        {
            var item = holder.Current;
            var player = holder.Context?.Player;
            if (item == null || player == null) return;

            switch (player.Mode)
            {
                case ControlMode.Gameplay:
                    if (player.ReloadPressed) item.Reload();
                    if (player.FirePressed) item.Use();
                    if (player.InspectPressed) item.Inspect();
                    break;
                case ControlMode.Inspect:
                    if (player.InspectReloadPressed) { item.ExitInspect(); item.ReloadFromInspect(); }
                    else if (player.InspectExitPressed) item.ExitInspect();
                    break;
            }
        }
    }
}
