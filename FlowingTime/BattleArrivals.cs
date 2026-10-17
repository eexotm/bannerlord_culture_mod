using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.AgentOrigins;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.TroopSuppliers;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace FlowingTime
{
    /// <summary>
    /// Parties marching toward the player's field battle arrive while it is being fought, and their troops
    /// enter the battlefield from the edge in the direction they came from on the campaign map.
    ///
    /// The campaign stays paused during the battle scene (vanilla), so arrivals are planned when the battle
    /// starts: every party within reach that could join one side under vanilla's rules gets an arrival time
    /// from its distance and speed. When it arrives it is added to the battle the same way vanilla adds
    /// nearby parties (so casualties, loot and results are handled by vanilla), and its troops spawn as
    /// their own formation at the battlefield edge and charge. The battle size limit is always respected:
    /// troops only spawn into free slots and the rest follow as casualties free room.
    /// </summary>
    internal sealed class BattleArrivalsLogic : MissionLogic
    {
        private sealed class Arrival
        {
            public MobileParty Party;
            public BattleSideEnum Side;
            public float DueSeconds;
            public bool Started;
            public List<IAgentOriginBase> Waiting = new List<IAgentOriginBase>();
            public Vec2 SpawnPoint;
            public WorldPosition SpawnPosition;
            public Vec2 Facing;
            public bool HasSpawnPoint;
            // The formation each (team, troop class) of this arrival fights in, reused by later waves.
            public Dictionary<(Team, FormationClass), Formation> Formations = new Dictionary<(Team, FormationClass), Formation>();
        }

        private readonly List<Arrival> _arrivals = new List<Arrival>();
        private MapEvent _battle;
        private float _battleStartTime = -1f;
        private float _nextCheckTime;
        private bool _disabled;

        /// <summary>Adds this logic to campaign field battles only.</summary>
        public static void TryAttach(Mission mission)
        {
            if (!Settings.MidBattleArrivals || !Reflected.Available || Campaign.Current == null) return;
            MapEvent battle = MapEvent.PlayerMapEvent;
            if (battle == null || !battle.IsFieldBattle || battle.IsNavalMapEvent || battle.MapEventSettlement != null) return;
            // Only the battle scene itself (it has the troop spawner), not e.g. a conversation held at the
            // encounter while the battle exists.
            if (mission.GetMissionBehavior<MissionAgentSpawnLogic>() == null) return;
            mission.AddMissionBehavior(new BattleArrivalsLogic());
        }

        /// <summary>The logic of the battle being fought, if any.</summary>
        public static BattleArrivalsLogic Current { get; private set; }

        public override void AfterStart()
        {
            base.AfterStart();
            Current = this;
            Run(PlanArrivals);
        }

        protected override void OnEndMission()
        {
            if (Current == this) Current = null;
            base.OnEndMission();
        }

        /// <summary>
        /// Arrived troops still waiting for room under the battle size limit keep their side in the fight,
        /// the way vanilla's own unspawned reserves do (see SideDepletedPatch).
        /// </summary>
        public bool HasWaitingTroops(BattleSideEnum side)
        {
            if (_disabled) return false;
            foreach (Arrival arrival in _arrivals)
                if (arrival.Side == side && arrival.Started && arrival.HasSpawnPoint && arrival.Waiting.Count > 0) return true;
            return false;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (_disabled || _arrivals.Count == 0) return;
            Run(Tick);
        }

        private void Run(Action action)
        {
            if (_disabled) return;
            try
            {
                action();
            }
            catch (Exception e)
            {
                // Never take the battle down: stop arrivals for the rest of this battle.
                _disabled = true;
                Log.Error("Mid-battle arrivals stopped for this battle: " + e.Message);
            }
        }

        // ------------------------------------------------------------------
        // Planning (battle start, campaign paused)
        // ------------------------------------------------------------------

        private void PlanArrivals()
        {
            _battle = MapEvent.PlayerMapEvent;
            if (_battle == null) { _disabled = true; return; }

            float joinRadius = Campaign.Current.Models.EncounterModel.GetEncounterJoiningRadius;
            float radius = joinRadius * Settings.ArrivalRadiusMultiplier;
            Vec2 center = _battle.Position.ToVec2();

            var candidates = new List<Arrival>();
            LocatableSearchData<MobileParty> search = MobileParty.StartFindingLocatablesAroundPosition(center, radius);
            for (MobileParty party = MobileParty.FindNextLocatable(ref search); party != null; party = MobileParty.FindNextLocatable(ref search))
            {
                if (!IsEligible(party)) continue;

                bool canJoinPlayerSide = _battle.CanPartyJoinBattle(party.Party, _battle.PlayerSide);
                bool canJoinEnemySide = _battle.CanPartyJoinBattle(party.Party, _battle.PlayerSide == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker);
                if (canJoinPlayerSide == canJoinEnemySide) continue; // neither, or ambiguous

                float distance = party.Position.ToVec2().Distance(center);
                if (distance <= joinRadius) continue; // vanilla already pulled these in at the start

                // Party speed is in map units per campaign time step (TickMapTime: 4320 game seconds = 1.2 h).
                float hours = distance / Math.Max(party.Speed, 0.5f) * 1.2f;
                float seconds = Math.Max(Settings.MinArrivalSeconds, hours * Settings.BattleSecondsPerCampaignHour);

                candidates.Add(new Arrival
                {
                    Party = party,
                    Side = canJoinPlayerSide ? _battle.PlayerSide : (_battle.PlayerSide == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker),
                    DueSeconds = seconds,
                });
            }

            // The nearest few per side.
            foreach (var side in candidates.GroupBy(a => a.Side))
                _arrivals.AddRange(side.OrderBy(a => a.DueSeconds).Take(Settings.MaxArrivalsPerSide));
        }

        private bool IsEligible(MobileParty party)
        {
            // Same filter vanilla uses for parties joining the player's battle (DefaultEncounterModel).
            return party != MobileParty.MainParty
                && party.IsActive
                && party.MapEvent == null
                && party.SiegeEvent == null
                && party.CurrentSettlement == null
                && party.AttachedTo == null
                && !party.IsInRaftState
                && !party.IsCurrentlyAtSea
                && !party.ShouldBeIgnored
                && party.MemberRoster.TotalHealthyCount > 0
                && (party.IsLordParty || party.IsBandit || party.IsPatrolParty || party.ShouldJoinPlayerBattles);
        }

        // ------------------------------------------------------------------
        // During the battle
        // ------------------------------------------------------------------

        private void Tick()
        {
            if (Mission.MissionEnded || Mission.Mode == MissionMode.Deployment) return;
            if (_battleStartTime < 0f) _battleStartTime = Mission.CurrentTime;
            if (Mission.CurrentTime < _nextCheckTime) return;
            _nextCheckTime = Mission.CurrentTime + 1f;

            float elapsed = Mission.CurrentTime - _battleStartTime;
            foreach (Arrival arrival in _arrivals)
            {
                if (!arrival.Started && elapsed >= arrival.DueSeconds) StartArrival(arrival);
                if (arrival.Started && arrival.Waiting.Count > 0) SpawnWave(arrival);
            }
        }

        private void StartArrival(Arrival arrival)
        {
            arrival.Started = true;
            MobileParty party = arrival.Party;
            if (_battle.IsFinalized || !IsEligible(party) || !_battle.CanPartyJoinBattle(party.Party, arrival.Side)) return;

            MissionAgentSpawnLogic spawnLogic = Mission.GetMissionBehavior<MissionAgentSpawnLogic>();
            if (spawnLogic == null || !(Reflected.GetSupplier(spawnLogic, arrival.Side) is PartyGroupTroopSupplier supplier)) return;

            // Find where they enter first: a party is only added to the battle if its troops can take the field.
            ChooseSpawnPoint(arrival);
            if (!arrival.HasSpawnPoint) return;

            MapEventSide side = _battle.GetMapEventSide(arrival.Side);

            // Join the battle the way vanilla adds parties that are near the player's battle.
            var joining = new List<MobileParty> { party };
            joining.AddRange(party.AttachedParties.Where(p => p.MapEvent == null && p.IsActive));
            foreach (MobileParty p in joining) Reflected.AddNearbyParty(side, p);

            int sizeOfSide = 0;
            foreach (MapEventParty p in side.Parties) sizeOfSide += p.Party.NumberOfHealthyMembers;

            foreach (MobileParty p in joining)
            {
                MapEventParty battleParty = side.Parties.FirstOrDefault(x => x.Party == p.Party);
                if (battleParty == null) continue;

                // Queue its healthy troops the way the battle does at mission start, then take exactly those.
                Reflected.MakeReadyParty(side, battleParty, sizeOfSide);
                List<UniqueTroopDescriptor> descriptors = null;
                side.AllocateTroops(ref descriptors, -1, (descriptor, owner) => owner == battleParty);
                foreach (UniqueTroopDescriptor descriptor in descriptors)
                    arrival.Waiting.Add(Reflected.CreateOrigin(supplier, descriptor));
            }

            if (arrival.Waiting.Count == 0) return;
            Announce(arrival);
            SpawnWave(arrival);
        }

        /// <summary>Spawns as many waiting troops as the battle size limit allows right now.</summary>
        private void SpawnWave(Arrival arrival)
        {
            if (!arrival.HasSpawnPoint) return;
            MissionAgentSpawnLogic spawnLogic = Mission.GetMissionBehavior<MissionAgentSpawnLogic>();
            if (spawnLogic == null) return;

            int alive = 0;
            foreach (Agent agent in Mission.Agents)
                if (agent.IsHuman && agent.IsActive()) alive++;
            int room = Math.Min(spawnLogic.BattleSize - alive, Settings.MaxSpawnPerWave);
            if (room <= 0) return;

            bool isPlayerSide = arrival.Side == Mission.PlayerTeam.Side;
            bool withHorses = spawnLogic.GetSpawnHorses(arrival.Side);
            List<IAgentOriginBase> batch = arrival.Waiting.Take(room).ToList();
            arrival.Waiting.RemoveRange(0, batch.Count);

            Vec2 right = new Vec2(arrival.Facing.y, -arrival.Facing.x);
            int spawned = 0;
            foreach (var group in batch.GroupBy(o => (Team: Mission.GetAgentTeam(o, isPlayerSide), Class: Mission.GetAgentTroopClass(arrival.Side, o.Troop))))
            {
                Team team = group.Key.Team;
                List<IAgentOriginBase> troops = group.ToList();

                // Later waves of the same arrival join the formation its first wave formed.
                if (!arrival.Formations.TryGetValue(group.Key, out Formation formation) || formation.CountOfUnits == 0)
                {
                    formation = PickFormation(team, group.Key.Class);
                    arrival.Formations[group.Key] = formation;
                }
                bool ownFormation = formation.CountOfUnits == 0;

                if (ownFormation)
                {
                    bool mounted = withHorses && troops.Count(t => t.Troop.HasMount()) * 2 > troops.Count;
                    formation.BeginSpawn(troops.Count, mounted);
                    formation.SetPositioning(arrival.SpawnPosition, arrival.Facing, null);
                    formation.HasBeenPositioned = true;
                }

                for (int i = 0; i < troops.Count; i++)
                {
                    // A loose column at the battlefield edge.
                    Vec2 offset = right * ((i % 10) - 4.5f) * 1.6f - arrival.Facing * (i / 10) * 1.8f;
                    Vec2 p = arrival.SpawnPoint + offset;
                    Vec3 position = new Vec3(p.x, p.y, Mission.Scene.GetGroundHeightAtPosition(new Vec3(p.x, p.y, 0f), BodyFlags.CommonCollisionExcludeFlags));
                    Mission.SpawnTroop(troops[i], isPlayerSide, hasFormation: true, withHorses, isReinforcement: true, troops.Count, i,
                        isAlarmed: true, wieldInitialWeapons: true, forceDismounted: false, position, arrival.Facing, null, null, formation.FormationIndex);
                    spawned++;
                }

                if (formation.IsSpawning) formation.EndSpawn();
                // Fresh formations charge in from their flank; never overrides orders of a formation already in use.
                if (ownFormation) formation.SetMovementOrder(MovementOrder.MovementOrderCharge);
                team.QuerySystem.Expire();
            }

            // Keep the spawn logic's "troops alive on this side" count right, so it never thinks the side is
            // wiped out while arrivals fight (their casualties are reported through the same troop supplier).
            Reflected.AddSpawnedTroops(spawnLogic, arrival.Side, spawned);
        }

        /// <summary>An empty formation, preferring the extra slots, so arrivals form their own group.</summary>
        private static Formation PickFormation(Team team, FormationClass troopClass)
        {
            FormationClass[] preferred = { FormationClass.HeavyInfantry, FormationClass.Skirmisher, FormationClass.LightCavalry, FormationClass.HeavyCavalry };
            foreach (FormationClass fc in preferred)
            {
                Formation f = team.GetFormation(fc);
                if (f != null && f.CountOfUnits == 0) return f;
            }
            for (int i = 0; i < 8; i++)
            {
                Formation f = team.GetFormation((FormationClass)i);
                if (f != null && f.CountOfUnits == 0) return f;
            }
            return team.GetFormation(troopClass); // all in use: join the matching one
        }

        // ------------------------------------------------------------------
        // Direction: campaign map bearing -> battlefield edge
        // ------------------------------------------------------------------

        private void ChooseSpawnPoint(Arrival arrival)
        {
            if (!TeamCenter(Mission.PlayerTeam, out Vec2 playerCenter) || !TeamCenter(Mission.PlayerEnemyTeam, out Vec2 enemyCenter)) return;
            Vec2 axis = enemyCenter - playerCenter;
            if (axis.LengthSquared < 1f) return;
            axis = axis.Normalized();

            // Bearing of the arriving party relative to the player -> enemy line on the campaign map.
            float angle = BearingAngle(arrival);
            Vec2 direction = Rotate(axis, angle);

            // Walk out from the player's army to just inside the battlefield boundary.
            float distance = 30f;
            for (float r = 30f; r < 2000f; r += 10f)
            {
                if (!Mission.IsPositionInsideBoundaries(playerCenter + direction * r)) break;
                distance = r;
            }
            Vec2 point = playerCenter + direction * Math.Max(30f, distance - 20f);

            WorldPosition ground = GroundPosition(point);
            if (ground.GetNavMesh() == UIntPtr.Zero)
            {
                float penalty = 0f;
                ground = Mission.GetAlternatePositionForNavmeshlessOrOutOfBoundsPosition(-direction, ground, ref penalty);
            }

            arrival.SpawnPosition = ground;
            arrival.SpawnPoint = ground.AsVec2;
            Vec2 target = arrival.Side == Mission.PlayerTeam.Side ? enemyCenter : playerCenter;
            Vec2 facing = target - arrival.SpawnPoint;
            arrival.Facing = facing.LengthSquared > 0.01f ? facing.Normalized() : -direction;
            arrival.HasSpawnPoint = true;
        }

        /// <summary>Signed angle (radians, counter-clockwise positive) from the player->enemy line to the party.</summary>
        private float BearingAngle(Arrival arrival)
        {
            Vec2 player = MobileParty.MainParty.Position.ToVec2();
            BattleSideEnum enemySide = _battle.PlayerSide == BattleSideEnum.Attacker ? BattleSideEnum.Defender : BattleSideEnum.Attacker;
            PartyBase enemyLeader = _battle.GetMapEventSide(enemySide).LeaderParty;
            Vec2 reference = enemyLeader != null ? enemyLeader.Position.ToVec2() - player : Vec2.Zero;
            Vec2 toParty = arrival.Party.Position.ToVec2() - player;

            if (toParty.LengthSquared < 1e-6f) return 0f;
            if (reference.LengthSquared < 1e-6f)
            {
                // Armies on the same spot: allies come from behind the player, enemies from behind the enemy.
                return arrival.Side == _battle.PlayerSide ? MathF.PI : 0f;
            }
            return MathF.Atan2(reference.x * toParty.y - reference.y * toParty.x, reference.x * toParty.x + reference.y * toParty.y);
        }

        private void Announce(Arrival arrival)
        {
            float a = BearingAngle(arrival);
            float deg = Math.Abs(a) * 180f / MathF.PI;
            string from = deg < 45f ? "from beyond the enemy lines"
                : deg > 135f ? "from behind your lines"
                : a > 0f ? "on your left flank" : "on your right flank";
            bool ally = arrival.Side == _battle.PlayerSide;
            string text = arrival.Party.Name + (ally ? " arrives to support you, " : " joins the enemy, ") + from + "!";
            InformationManager.DisplayMessage(new InformationMessage(text, ally ? Colors.Green : Colors.Red));
        }

        private bool TeamCenter(Team team, out Vec2 center)
        {
            center = Vec2.Zero;
            if (team == null) return false;
            int count = 0;
            foreach (Agent agent in team.ActiveAgents)
            {
                center += agent.Position.AsVec2;
                count++;
            }
            if (count == 0) return false;
            center *= 1f / count;
            return true;
        }

        private WorldPosition GroundPosition(Vec2 p)
        {
            float z = Mission.Scene.GetGroundHeightAtPosition(new Vec3(p.x, p.y, 0f), BodyFlags.CommonCollisionExcludeFlags);
            return new WorldPosition(Mission.Scene, UIntPtr.Zero, new Vec3(p.x, p.y, z), false);
        }

        private static Vec2 Rotate(Vec2 v, float radians)
        {
            float c = MathF.Cos(radians), s = MathF.Sin(radians);
            return new Vec2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        // ------------------------------------------------------------------
        // Game internals used (checked once at startup; the feature switches off if any is missing)
        // ------------------------------------------------------------------

        internal static class Reflected
        {
            private static readonly MethodInfo AddNearbyPartyMethod = AccessTools.Method(typeof(MapEventSide), "AddNearbyPartyToPlayerMapEvent", new[] { typeof(MobileParty) });
            private static readonly MethodInfo MakeReadyPartyMethod = AccessTools.Method(typeof(MapEventSide), "MakeReadyParty", new[] { typeof(MapEventParty), typeof(FlattenedTroopRoster), typeof(bool), typeof(int) });
            private static readonly ConstructorInfo OriginConstructor = AccessTools.Constructor(typeof(PartyGroupAgentOrigin), new[] { typeof(PartyGroupTroopSupplier), typeof(UniqueTroopDescriptor), typeof(int) });
            private static readonly FieldInfo MissionSidesField = AccessTools.Field(typeof(MissionAgentSpawnLogic), "_missionSides");
            private static readonly Type MissionSideType = AccessTools.Inner(typeof(MissionAgentSpawnLogic), "MissionSide");
            private static readonly FieldInfo SupplierField = MissionSideType == null ? null : AccessTools.Field(MissionSideType, "_troopSupplier");
            private static readonly FieldInfo SpawnedField = MissionSideType == null ? null : AccessTools.Field(MissionSideType, "_numSpawnedTroops");
            private static int _rank = 1_000_000;

            public static bool Available =>
                AddNearbyPartyMethod != null && MakeReadyPartyMethod != null && OriginConstructor != null
                && MissionSidesField != null && SupplierField != null && SpawnedField != null;

            public static void AddNearbyParty(MapEventSide side, MobileParty party) => AddNearbyPartyMethod.Invoke(side, new object[] { party });

            public static void MakeReadyParty(MapEventSide side, MapEventParty party, int sizeOfSide) =>
                MakeReadyPartyMethod.Invoke(side, new object[] { party, null, true, sizeOfSide });

            public static IAgentOriginBase CreateOrigin(PartyGroupTroopSupplier supplier, UniqueTroopDescriptor descriptor) =>
                (IAgentOriginBase)OriginConstructor.Invoke(new object[] { supplier, descriptor, _rank++ });

            public static IMissionTroopSupplier GetSupplier(MissionAgentSpawnLogic logic, BattleSideEnum side) =>
                SupplierField.GetValue(MissionSide(logic, side)) as IMissionTroopSupplier;

            public static void AddSpawnedTroops(MissionAgentSpawnLogic logic, BattleSideEnum side, int count)
            {
                object missionSide = MissionSide(logic, side);
                SpawnedField.SetValue(missionSide, (int)SpawnedField.GetValue(missionSide) + count);
            }

            private static object MissionSide(MissionAgentSpawnLogic logic, BattleSideEnum side) =>
                ((Array)MissionSidesField.GetValue(logic)).GetValue((int)side);
        }
    }
}
