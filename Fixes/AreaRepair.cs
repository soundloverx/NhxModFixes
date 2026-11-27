using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // The hammer's repair tool repairs the building piece the player aims at: free of materials, for one swing's stamina,
    // eitr and durability, when the piece's crafting station is in range (Player.CheckCanRemovePiece) and no ward keeps the
    // player out. Each repair asks the game controlling the piece to set it back to full health (WearNTear.Repair, at most
    // once a second per piece). While "Repair radius" isn't 0, this replaces Player.Repair: a click on a piece the player
    // may repair, damaged or not, repairs it and every damaged piece placed by a player within that many meters of them,
    // each with the same checks (skipped quietly when they fail), for one swing. The swing, the clicked piece's repair
    // effect and the messages are the game's, with "Repaired <n> pieces" when there are several. Only the local player's
    // game calls Player.Repair (from UpdatePlacement).
    [HarmonyPatch]
    internal static class AreaRepair
    {
        private static readonly List<WearNTear> Damaged = new List<WearNTear>();
        private static readonly Dictionary<string, bool> StationsInRange = new Dictionary<string, bool>();

        // Last, so that Jewelcrafting's prefix has already run: its Energetic gem lowers the stamina used while Repair runs
        [HarmonyPatch(typeof(Player), nameof(Player.Repair))]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static bool RepairAround(Player __instance, ItemDrop.ItemData toolItem)
        {
            float radius = Plugin.RepairRadius;
            if (radius <= 0f)
            {
                return true;
            }

            // The game's checks for the piece aimed at, with its messages
            if (!__instance.InPlaceMode())
            {
                return false;
            }
            Piece clicked = __instance.GetHoveringPiece();
            if (!clicked || !__instance.CheckCanRemovePiece(clicked) || !PrivateArea.CheckAccess(clicked.transform.position))
            {
                return false;
            }

            int repaired = 0;
            int skipped = 0;
            Piece onlyRepaired = null;
            WearNTear clickedWearNTear = clicked.GetComponent<WearNTear>();
            if (clickedWearNTear && clickedWearNTear.Repair())
            {
                repaired++;
                onlyRepaired = clicked;
            }

            Vector3 center = __instance.transform.position;
            float radiusSquared = radius * radius;
            Damaged.Clear();
            StationsInRange.Clear();
            try
            {
                // Collected first: a repair runs the game's code at once for pieces this game controls
                foreach (WearNTear wearNTear in WearNTear.GetAllInstances())
                {
                    if (wearNTear != clickedWearNTear && (wearNTear.transform.position - center).sqrMagnitude <= radiusSquared && NeedsRepair(wearNTear))
                    {
                        Damaged.Add(wearNTear);
                    }
                }

                foreach (WearNTear wearNTear in Damaged)
                {
                    Piece piece = wearNTear ? wearNTear.m_piece : null;
                    if (piece == null || !piece.IsPlacedByPlayer())
                    {
                        continue;
                    }
                    if (!MayRepair(__instance, piece, center))
                    {
                        skipped++;
                        continue;
                    }
                    if (wearNTear.Repair())
                    {
                        repaired++;
                        onlyRepaired = repaired == 1 ? piece : null;
                    }
                }
            }
            finally
            {
                Damaged.Clear();
            }

            if (repaired == 0)
            {
                __instance.Message(MessageHud.MessageType.TopLeft, clicked.m_name + " $msg_doesnotneedrepair");
                return false;
            }

            // The game's swing, for one repair
            __instance.FaceLookDirection();
            __instance.m_zanim.SetTrigger(toolItem.m_shared.m_attack.m_attackAnimation);
            clicked.m_placeEffect.Create(clicked.transform.position, clicked.transform.rotation, null, 1f, -1, __instance.GetZDOID());
            string message = onlyRepaired != null
                ? Localization.instance.Localize("$msg_repaired", onlyRepaired.m_name)
                : $"Repaired {repaired} pieces";
            __instance.Message(MessageHud.MessageType.TopLeft, message);
            __instance.UseStamina(__instance.GetBuildStamina());
            __instance.UseEitr(toolItem.m_shared.m_attack.m_attackEitr);
            if (toolItem.m_shared.m_useDurability)
            {
                toolItem.m_durability -= toolItem.m_shared.m_useDurabilityDrain * Game.m_durabilityRate;
            }

            if (repaired > 1 || skipped > 0)
            {
                string skippedText = skipped > 0 ? $", skipped {skipped} (crafting station out of range, or a ward)" : "";
                Plugin.Log.LogInfo($"Repaired {repaired} building pieces within {radius:0.#} m{skippedText}.");
            }
            return false;
        }

        // WearNTear.Repair's own test, without its once-a-second limit
        private static bool NeedsRepair(WearNTear wearNTear)
        {
            ZNetView nview = wearNTear.m_nview;
            return nview != null && nview.IsValid() && nview.GetZDO().GetFloat(ZDOVars.s_health, wearNTear.m_health) < wearNTear.m_health;
        }

        // The game's checks for a piece, without their messages: the station first (Player.CheckCanRemovePiece's own test,
        // which shows "missing station" when it fails), then the game's method (so other mods' patches on it apply), then
        // the ward, without flashing it
        private static bool MayRepair(Player player, Piece piece, Vector3 center)
        {
            CraftingStation station = piece.m_craftingStation;
            if (station != null && !player.m_noPlacementCost && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench))
            {
                if (!StationsInRange.TryGetValue(station.m_name, out bool inRange))
                {
                    inRange = CraftingStation.HaveBuildStationInRange(station.m_name, center) != null;
                    StationsInRange[station.m_name] = inRange;
                }
                if (!inRange)
                {
                    return false;
                }
            }
            return player.CheckCanRemovePiece(piece) && PrivateArea.CheckAccess(piece.transform.position, 0f, false);
        }
    }
}
