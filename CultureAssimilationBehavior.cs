using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace CulturalAssimilation
{
    /// <summary>
    /// Villages slowly adopt the culture of whoever holds their bound castle/town.
    /// Progress 0..100 per village; at 100 the village (and its notables) flip culture,
    /// which also changes militia and the recruit pool (works with modded cultures like Realm of Thrones).
    /// </summary>
    public class CultureAssimilationBehavior : CampaignBehaviorBase
    {
        // ---- Tunables (move to a config/MCM later) ----
        private const float GraceDays = 30f;          // days the castle/town must be held before conversion starts
        private const float BaseDaysToConvert = 90f;  // days to reach 100 at a neutral (1.0x) rate
        private const float MinLoyaltyToProgress = 25f;

        // village StringId -> progress (0..100)
        private Dictionary<string, float> _progress = new Dictionary<string, float>();
        // castle/town StringId -> days held by current owner
        private Dictionary<string, float> _heldDays = new Dictionary<string, float>();

        public override void RegisterEvents()
        {
            CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
            CampaignEvents.OnSettlementOwnerChangedEvent.AddNonSerializedListener(this, OnSettlementOwnerChanged);
        }

        public override void SyncData(IDataStore dataStore)
        {
            // Serialize to strings: safest across game versions.
            string progressStr = Serialize(_progress);
            string heldStr = Serialize(_heldDays);

            dataStore.SyncData("CA_progress", ref progressStr);
            dataStore.SyncData("CA_heldDays", ref heldStr);

            if (dataStore.IsLoading)
            {
                _progress = Deserialize(progressStr);
                _heldDays = Deserialize(heldStr);
            }
        }

        // ------------------------------------------------------------------
        // Events
        // ------------------------------------------------------------------

        private void OnSettlementOwnerChanged(
            Settlement settlement, bool openToClaim, Hero newOwner, Hero oldOwner,
            Hero capturerHero, ChangeOwnerOfSettlementAction.ChangeOwnerOfSettlementDetail detail)
        {
            if (settlement == null || !(settlement.IsCastle || settlement.IsTown)) return;

            // New owner: restart the clock and wipe progress so flipping hands never converts anything.
            _heldDays[settlement.StringId] = 0f;
            foreach (Village village in settlement.BoundVillages)
            {
                _progress.Remove(village.Settlement.StringId);
            }
        }

        private void OnDailyTickSettlement(Settlement settlement)
        {
            if (settlement == null || !(settlement.IsCastle || settlement.IsTown)) return;

            CultureObject ownerCulture = GetOwnerCulture(settlement);
            if (ownerCulture == null) return;

            // Track how long the current owner has held it.
            _heldDays.TryGetValue(settlement.StringId, out float held);
            held += 1f;
            _heldDays[settlement.StringId] = held;

            if (held < GraceDays) return;

            float loyalty = settlement.Town != null ? settlement.Town.Loyalty : 100f;
            if (loyalty < MinLoyaltyToProgress) return; // unrest stalls assimilation

            float rate = GetDailyRate(settlement, ownerCulture, loyalty);

            foreach (Village village in settlement.BoundVillages)
            {
                Settlement vs = village.Settlement;
                if (vs == null) continue;

                // Already the right culture: nothing to do.
                if (vs.Culture == ownerCulture)
                {
                    _progress.Remove(vs.StringId);
                    continue;
                }

                // Raided/looted villages make no progress.
                if (village.VillageState != Village.VillageStates.Normal) continue;

                _progress.TryGetValue(vs.StringId, out float p);
                p += rate;

                if (p >= 100f)
                {
                    _progress.Remove(vs.StringId);
                    ConvertVillage(vs, ownerCulture);
                }
                else
                {
                    _progress[vs.StringId] = p;
                }
            }
        }

        // ------------------------------------------------------------------
        // Rules
        // ------------------------------------------------------------------

        private static CultureObject GetOwnerCulture(Settlement settlement)
        {
            Clan clan = settlement.OwnerClan;
            if (clan == null) return null;
            return clan.Kingdom?.Culture ?? clan.Culture;
        }

        /// <summary>Progress points gained per day (base is 100 / BaseDaysToConvert).</summary>
        private static float GetDailyRate(Settlement castle, CultureObject ownerCulture, float loyalty)
        {
            float rate = 100f / BaseDaysToConvert;

            // Loyalty: 0.5x at 25 loyalty up to ~1.5x at 100.
            float loyaltyMult = 0.5f + (loyalty / 100f);

            // Governor: same-culture governor +10%, plus up to +15% from Charm.
            float governorMult = 1f;
            Hero governor = castle.Town?.Governor;
            if (governor != null)
            {
                if (governor.Culture == ownerCulture) governorMult += 0.10f;
                int charm = governor.GetSkillValue(DefaultSkills.Charm);
                governorMult += 0.15f * Math.Min(charm, 300) / 300f;
            }

            return rate * loyaltyMult * governorMult;
        }

        private void ConvertVillage(Settlement village, CultureObject newCulture)
        {
            CultureObject oldCulture = village.Culture;

            // 1. Village culture (drives militia troops and village identity).
            if (!TrySetCulture(village, newCulture))
            {
                Log("Could not set Settlement.Culture for " + village.Name);
                return;
            }

            // 2. Notables: their culture decides which troops they offer as volunteers.
            foreach (Hero notable in village.Notables)
            {
                TrySetCulture(notable, newCulture);

                // Drop already-generated old-culture recruits so the pool regenerates from the new culture.
                var volunteers = notable.VolunteerTypes;
                if (volunteers != null)
                {
                    for (int i = 0; i < volunteers.Length; i++) volunteers[i] = null;
                }
            }

            if (village.OwnerClan == Clan.PlayerClan ||
                (Clan.PlayerClan.Kingdom != null && village.OwnerClan?.Kingdom == Clan.PlayerClan.Kingdom))
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    village.Name + " has adopted " + newCulture.Name + " ways (was " + oldCulture?.Name + ").",
                    Colors.Green));
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>Culture setters are not public on every game version, so fall back to reflection.</summary>
        private static bool TrySetCulture(object target, CultureObject culture)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();

            try
            {
                PropertyInfo prop = type.GetProperty("Culture", flags);
                MethodInfo setter = prop?.GetSetMethod(true);
                if (setter != null)
                {
                    setter.Invoke(target, new object[] { culture });
                    return true;
                }

                FieldInfo field = type.GetField("_culture", flags);
                if (field != null)
                {
                    field.SetValue(target, culture);
                    return true;
                }
            }
            catch (Exception e)
            {
                Log("TrySetCulture failed on " + type.Name + ": " + e.Message);
            }
            return false;
        }

        private static string Serialize(Dictionary<string, float> dict)
        {
            var parts = new List<string>(dict.Count);
            foreach (var kv in dict)
                parts.Add(kv.Key + "=" + kv.Value.ToString("R", CultureInfo.InvariantCulture));
            return string.Join(";", parts);
        }

        private static Dictionary<string, float> Deserialize(string data)
        {
            var result = new Dictionary<string, float>();
            if (string.IsNullOrEmpty(data)) return result;

            foreach (string entry in data.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int idx = entry.LastIndexOf('=');
                if (idx <= 0) continue;
                if (float.TryParse(entry.Substring(idx + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    result[entry.Substring(0, idx)] = v;
            }
            return result;
        }

        private static void Log(string message)
        {
            InformationManager.DisplayMessage(new InformationMessage("[CulturalAssimilation] " + message, Colors.Red));
        }
    }
}
