using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;

namespace NhxModFixes.Fixes
{
    // Effects shown on tamed creatures, which each player can hide on their own game:
    // - Burning: fire and spirit damage give a creature a burning status effect (SE_Burning: the game's "Burning", whose
    //   flames are orange, blue or green depending on the hit, and "Spirit"), shown by its start effects (vfx_Burning...,
    //   vfx_UndeadBurn: flames, sparks and a flickering light) attached to the creature and scaled to its size.
    // - Shields: SE_Shield (the Staff of Protection's, RtD's fairy and pixie shields, other mods' shields) shows a bubble
    //   (its start effects: a sphere, smoke and a light), a burst at each hit it absorbs and one when it breaks. RtD's
    //   fairies and pixies cast theirs with an attack whose status effect is the shield, with a burst of their own
    //   (fx_fae_protect_RtD).
    // These are network objects: the game controlling the creature creates them, every other game gets a copy. Status
    // effects' start effects and attacks' effects name their creature in their ZDO (EffectList.Create stores the ZDOID
    // it's given); a shield's hit and break bursts are created without it, so the game creating them names the creature
    // here (only games with this version do, so the others' bursts can't be told apart). A tame flying around the
    // player's head (RtD's fairies) can then fill most of the screen with fire or bubbles. With each player's own
    // settings, the renderers and lights of these effects are turned off on their game when the creature is tamed; the
    // sounds, the damage, what the shield absorbs and how long each lasts stay the game's. Checked every frame, so a
    // change (a setting, a creature tamed) shows at once.
    [HarmonyPatch]
    internal static class TameEffects
    {
        private enum Kind
        {
            None,
            Burning,
            Shield
        }

        // One effect's renderers and lights
        private sealed class Tracked
        {
            internal GameObject Effect;
            internal Kind Kind;
            internal ZDOID CreatureId;
            internal Character Creature;
            internal bool Hidden;
            // What was turned off here, to turn back on
            internal readonly List<Renderer> Renderers = new List<Renderer>();
            internal readonly List<Light> Lights = new List<Light>();
        }

        private static readonly List<Tracked> Effects = new List<Tracked>();

        // The effects' prefabs, by the hash a ZDO names its prefab with
        private static ObjectDB _db;
        private static ZNetScene _scene;
        private static int _statusEffectCount = -1;
        private static readonly Dictionary<int, Kind> KindByPrefab = new Dictionary<int, Kind>();

        // The shield creating its hit or break effects
        private static SE_Shield _shield;

        private static readonly HashSet<string> Logged = new HashSet<string>();

        // On the game creating an effect
        [HarmonyPostfix]
        [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
        private static void AfterCreate(EffectList __instance, GameObject[] __result, ZDOID gamepadEffectsExclusiveToPlayer)
        {
            if (__result == null || __result.Length == 0)
            {
                return;
            }
            SE_Shield shield = _shield;
            if (shield != null && (ReferenceEquals(__instance, shield.m_hitEffects) || ReferenceEquals(__instance, shield.m_breakEffects)))
            {
                NameShieldEffects(shield, __result);
                return;
            }
            if (gamepadEffectsExclusiveToPlayer == ZDOID.None)
            {
                return;
            }
            foreach (GameObject effect in __result)
            {
                if (effect == null)
                {
                    continue;
                }
                Kind kind = KindOf(PrefabHash(effect));
                if (kind != Kind.None)
                {
                    Track(effect, kind, gamepadEffectsExclusiveToPlayer, null);
                }
            }
        }

        // A shield's hit and break effects: SE_Shield.OnDamaged and IsDone create them
        [HarmonyPrefix]
        [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
        private static void BeforeShieldHit(SE_Shield __instance) => _shield = __instance;

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
        private static void AfterShieldHit() => _shield = null;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.IsDone))]
        private static void BeforeShieldBreak(SE_Shield __instance) => _shield = __instance;

        [HarmonyFinalizer]
        [HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.IsDone))]
        private static void AfterShieldBreak() => _shield = null;

        // On the other games, which create their copy from its ZDO
        [HarmonyPostfix]
        [HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.CreateObject))]
        private static void AfterCreateObject(ZDO zdo, GameObject __result)
        {
            if ((object)__result == null)
            {
                return;
            }
            Kind kind = KindOf(zdo.GetPrefab());
            if (kind == Kind.None)
            {
                return;
            }
            long userId = zdo.GetLong(ZDOVars.s_ownerZDOUserId, 0L);
            long id = zdo.GetLong(ZDOVars.s_ownerZDOId, 0L);
            if (userId != 0L || id != 0L)
            {
                Track(__result, kind, new ZDOID(userId, (uint)id), null);
            }
        }

        // From Plugin.Update
        internal static void Update()
        {
            for (int i = Effects.Count - 1; i >= 0; i--)
            {
                Tracked tracked = Effects[i];
                if (tracked.Effect == null)
                {
                    Effects.RemoveAt(i);
                    continue;
                }
                Apply(tracked);
            }
        }

        // Names the shield's creature in its bursts' ZDOs, as EffectList.Create does for start effects (whatever the
        // settings here: the other games need it), and tracks them
        private static void NameShieldEffects(SE_Shield shield, GameObject[] effects)
        {
            Character creature = shield.m_character;
            if (creature == null)
            {
                return;
            }
            ZDOID creatureId = creature.GetZDOID();
            if (creatureId == ZDOID.None)
            {
                return;
            }
            foreach (GameObject effect in effects)
            {
                if (effect == null)
                {
                    continue;
                }
                ZNetView view = effect.GetComponent<ZNetView>();
                if (view != null && view.IsValid() && view.IsOwner())
                {
                    ZDO zdo = view.GetZDO();
                    zdo.Set(ZDOVars.s_ownerZDOUserId, creatureId.UserID);
                    zdo.Set(ZDOVars.s_ownerZDOId, (long)creatureId.ID);
                }
                Track(effect, Kind.Shield, creatureId, creature);
            }
        }

        private static int PrefabHash(GameObject effect)
        {
            ZNetView view = effect.GetComponent<ZNetView>();
            ZDO zdo = view != null ? view.GetZDO() : null;
            return zdo != null ? zdo.GetPrefab() : Utils.GetPrefabName(effect).GetStableHashCode();
        }

        private static Kind KindOf(int prefab)
        {
            ObjectDB db = ObjectDB.instance;
            ZNetScene scene = ZNetScene.instance;
            if ((object)db == null || (object)scene == null)
            {
                return Kind.None;
            }
            // Read again in a new world session, or when mods added status effects
            if (!ReferenceEquals(db, _db) || !ReferenceEquals(scene, _scene) || db.m_StatusEffects.Count != _statusEffectCount)
            {
                ReadKinds(db, scene);
            }
            return KindByPrefab.TryGetValue(prefab, out Kind kind) ? kind : Kind.None;
        }

        private static void ReadKinds(ObjectDB db, ZNetScene scene)
        {
            _db = db;
            _scene = scene;
            _statusEffectCount = db.m_StatusEffects.Count;
            KindByPrefab.Clear();

            foreach (StatusEffect statusEffect in db.m_StatusEffects)
            {
                if (statusEffect is SE_Burning)
                {
                    Add(statusEffect.m_startEffects, Kind.Burning);
                }
                else if (statusEffect is SE_Shield shield)
                {
                    Add(shield.m_startEffects, Kind.Shield);
                    Add(shield.m_hitEffects, Kind.Shield);
                    Add(shield.m_breakEffects, Kind.Shield);
                }
            }

            // Attacks that give a shield (RtD's fairies' and pixies' protection): what they show while casting
            foreach (GameObject prefab in scene.m_namedPrefabs.Values)
            {
                ItemDrop item = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                ItemDrop.ItemData.SharedData shared = item != null ? item.m_itemData?.m_shared : null;
                if (shared == null || !(shared.m_attackStatusEffect is SE_Shield))
                {
                    continue;
                }
                Add(shared.m_startEffect, Kind.Shield);
                Add(shared.m_triggerEffect, Kind.Shield);
                Add(shared.m_trailStartEffect, Kind.Shield);
                foreach (Attack attack in new[] { shared.m_attack, shared.m_secondaryAttack })
                {
                    if (attack != null)
                    {
                        Add(attack.m_startEffect, Kind.Shield);
                        Add(attack.m_triggerEffect, Kind.Shield);
                        Add(attack.m_trailStartEffect, Kind.Shield);
                    }
                }
            }
        }

        private static void Add(EffectList effects, Kind kind)
        {
            if (effects?.m_effectPrefabs == null)
            {
                return;
            }
            foreach (EffectList.EffectData effect in effects.m_effectPrefabs)
            {
                if (effect?.m_prefab != null)
                {
                    int hash = effect.m_prefab.name.GetStableHashCode();
                    if (!KindByPrefab.ContainsKey(hash))
                    {
                        KindByPrefab[hash] = kind;
                    }
                }
            }
        }

        private static void Track(GameObject effect, Kind kind, ZDOID creatureId, Character creature)
        {
            if (effect == null || creatureId == ZDOID.None)
            {
                return;
            }
            foreach (Tracked tracked in Effects)
            {
                if (ReferenceEquals(tracked.Effect, effect))
                {
                    return;
                }
            }
            // Only what can be seen (some are sounds)
            if (effect.GetComponentInChildren<Renderer>(true) == null && effect.GetComponentInChildren<Light>(true) == null)
            {
                return;
            }
            Tracked added = new Tracked { Effect = effect, Kind = kind, CreatureId = creatureId, Creature = creature };
            Effects.Add(added);
            Apply(added);
        }

        private static void Apply(Tracked tracked)
        {
            bool wanted = tracked.Kind == Kind.Burning ? Plugin.DisableTamedOnFire : Plugin.DisableTamedShields;
            bool hide = wanted && IsTamed(tracked);
            if (hide == tracked.Hidden)
            {
                return;
            }
            tracked.Hidden = hide;

            if (hide)
            {
                foreach (Renderer renderer in tracked.Effect.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.enabled)
                    {
                        renderer.enabled = false;
                        tracked.Renderers.Add(renderer);
                    }
                }
                foreach (Light light in tracked.Effect.GetComponentsInChildren<Light>(true))
                {
                    if (light.enabled)
                    {
                        light.enabled = false;
                        tracked.Lights.Add(light);
                    }
                }

                string what = tracked.Kind == Kind.Burning ? "burning" : "shield";
                string effectName = Utils.GetPrefabName(tracked.Effect);
                string creatureName = Utils.GetPrefabName(tracked.Creature.gameObject);
                if (Logged.Add(effectName + "/" + creatureName))
                {
                    Plugin.Log.LogInfo($"Hid a {what} effect ({effectName}) on a tamed {creatureName}, for this player only.");
                }
                return;
            }

            foreach (Renderer renderer in tracked.Renderers)
            {
                if (renderer != null)
                {
                    renderer.enabled = true;
                }
            }
            foreach (Light light in tracked.Lights)
            {
                if (light != null)
                {
                    light.enabled = true;
                }
            }
            tracked.Renderers.Clear();
            tracked.Lights.Clear();
        }

        // The creature can load after its effect's copy, or unload before it
        private static bool IsTamed(Tracked tracked)
        {
            if (tracked.Creature == null)
            {
                ZNetScene scene = ZNetScene.instance;
                GameObject creature = scene != null ? scene.FindInstance(tracked.CreatureId) : null;
                tracked.Creature = creature != null ? creature.GetComponent<Character>() : null;
                if (tracked.Creature == null)
                {
                    return false;
                }
            }
            return tracked.Creature.IsTamed();
        }
    }
}
