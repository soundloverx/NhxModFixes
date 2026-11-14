using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace NhxModFixes.Diagnostics
{
    // Picking up an item another game controls (ItemDrop.Interact or Player.AutoPickup) first asks that game for it
    // (RequestOwn, a routed RPC through the server, sent again after 0.2, 0.4, 0.8... s). That game hands it over, and
    // the handover comes back through the server with the item's other updates. Only then does CanPickup say yes. So the
    // wait is the network's: logged when it's a second or more, counted for the session report. Diagnostics only,
    // while the report is on.
    [HarmonyPatch(typeof(ItemDrop))]
    internal static class PickupWaits
    {
        private const float SlowWait = 1f;
        private const float ForgetAfter = 60f;
        private const int PruneAbove = 50;

        private struct Request
        {
            public float Start;
            public int Times;
            public long Owner;
        }

        private static readonly Dictionary<ItemDrop, Request> Requests = new Dictionary<ItemDrop, Request>();
        private static int _count;
        private static float _total;
        private static float _longest;

        // Counts only the calls that send a request: the game skips the others until its retry delay has passed
        [HarmonyPrefix]
        [HarmonyPatch(nameof(ItemDrop.RequestOwn))]
        private static void BeforeRequest(ItemDrop __instance)
        {
            ZNetView view = __instance.m_nview;
            if (Plugin.ReportMinutes <= 0 || view == null || !view.IsValid() || view.IsOwner() ||
                Time.time - __instance.m_lastOwnerRequest < __instance.m_ownerRetryTimeout)
            {
                return;
            }

            if (Requests.TryGetValue(__instance, out Request request))
            {
                request.Times++;
                Requests[__instance] = request;
                return;
            }
            if (Requests.Count > PruneAbove)
            {
                Prune();
            }
            Requests[__instance] = new Request { Start = Time.time, Times = 1, Owner = view.GetZDO().GetOwner() };
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(ItemDrop.CanPickup))]
        private static void AfterCheck(ItemDrop __instance, bool __result)
        {
            if (!__result || Requests.Count == 0 || !Requests.TryGetValue(__instance, out Request request))
            {
                return;
            }
            ZNetView view = __instance.m_nview;
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                return;
            }
            Requests.Remove(__instance);

            float wait = Time.time - request.Start;
            _count++;
            _total += wait;
            _longest = Mathf.Max(_longest, wait);
            if (wait >= SlowWait)
            {
                string item = Localization.instance.Localize(__instance.m_itemData.m_shared.m_name);
                Plugin.Log.LogInfo($"Waited {wait:0.0} s for {OwnerName(request.Owner)} to hand over {item} before picking it up (asked {request.Times} times).");
            }
        }

        // Items someone else picked up, or left where they were
        private static void Prune()
        {
            List<ItemDrop> old = new List<ItemDrop>();
            foreach (KeyValuePair<ItemDrop, Request> entry in Requests)
            {
                if (entry.Key == null || Time.time - entry.Value.Start > ForgetAfter)
                {
                    old.Add(entry.Key);
                }
            }
            foreach (ItemDrop item in old)
            {
                Requests.Remove(item);
            }
        }

        private static string OwnerName(long owner)
        {
            ZNet net = ZNet.instance;
            if (net == null)
            {
                return "another game";
            }
            foreach (ZNet.PlayerInfo player in net.GetPlayerList())
            {
                if (player.m_characterID.UserID == owner)
                {
                    return player.m_name + "'s game";
                }
            }
            ZNetPeer server = net.GetServerPeer();
            return server != null && server.m_uid == owner ? "the server" : "another game";
        }

        internal static void Reset()
        {
            Requests.Clear();
            _count = 0;
            _total = 0f;
            _longest = 0f;
        }

        // For the session report, then the counts start over
        internal static string Report()
        {
            string report = _count == 0
                ? "no pickups from other games"
                : $"{_count} pickups from other games, waited {_total / _count:0.0} s on average, {_longest:0.0} s at most";
            _count = 0;
            _total = 0f;
            _longest = 0f;
            return report;
        }
    }
}
