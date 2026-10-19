using System;
using System.Collections;
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
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.TroopSuppliers;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace FlowingTime
{
    /// <summary>
    /// Parties marching toward the player's battle arrive while it is being fought, and their troops enter
    /// the battlefield from the edge in the direction they came from on the campaign map.
    ///
    /// The campaign stays paused during the battle scene (vanilla), so arrivals are planned when the battle
    /// starts: every party within reach that could join one side under vanilla's rules gets an arrival time
    /// from its distance and speed. When it arrives it is added to the battle the same way vanilla adds
    /// nearby parties (so casualties, loot and results are handled by vanilla), and its troops spawn as
    /// their own formation at the battlefield edge and charge.
    ///
    /// Relief battles at a besieged settlement (vanilla fights them on the open field next to it) also get
    /// the flank: troops that start the battle inside the walls (garrison, lords inside) while their side's
    /// army attacks from outside are held back from the opening deployment and sally out from the
    /// settlement's side, so the besiegers end up between the two. When the army fights from inside instead,
    /// its allies waiting outside come in from where they stand.
    ///
    /// The battle size limit is always respected: troops only spawn into slots that neither side's vanilla
    /// reinforcements can still claim, and the rest follow as casualties free room.
    /// </summary>
    internal sealed class BattleArrivalsLogic : MissionLogic
    {
        private sealed class Arrival
        {
            // A party marching in from the map, joined to the battle when it arrives.
            public MobileParty Party;
            // Or: parties already in the battle that start somewhere else than their side's army.
            public List<MapEventParty> Members;
            public bool FromSettlement;

            public string Name;
            public Vec2 Origin; // campaign map position they come from
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

            public bool IsHeldBack => Members != null;
        }

        private readonly List<Arrival> _arrivals = new List<Arrival>();
        private MapEvent _battle;
        private Settlement _besieged;
        private float _battleStartTime = -1f;
        private float _nextCheckTime;
        private bool _disabled;

        /// <summary>Adds this logic to campaign field battles and to relief battles at a besieged settlement.</summary>
        public static void TryAttach(Mission mission)
        {
            if (!Settings.MidBattleArrivals || !Reflected.Available || Campaign.Current == null) return;
            MapEvent battle = MapEvent.PlayerMapEvent;
            if (battle == null || battle.IsNavalMapEvent) return;
            bool fieldBattle = battle.IsFieldBattle && battle.MapEventSettlement == null;
            // Attacking the besiegers' camp, with or without the garrison: vanilla fights these on the open
            // field next to the settlement (assaults on the walls are left alone).
            bool reliefBattle = battle.IsSiegeOutside || battle.IsSallyOut;
            if (!fieldBattle && !reliefBattle) return;
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
            // The team AI type is set by now: stay out of anything fought on a siege scene.
            if (!Mission.IsFieldBattle)
            {
                _disabled = true;
                return;
            }
            Run(PlanArrivals);
        }

        protected override void OnEndMission()
        {
            if (Current == this) Current = null;
            base.OnEndMission();
        }

        /// <summary>
        /// Troops still to arrive keep their side in the fight, the way vanilla's own unspawned reserves do
        /// (see SideDepletedPatch): arrived ones waiting for room, and sallies that haven't come out yet.
        /// </summary>
        public bool HasWaitingTroops(BattleSideEnum side)
        {
            if (_disabled) return false;
            foreach (Arrival arrival in _arrivals)
            {
                if (arrival.Side != side || arrival.Waiting.Count == 0) continue;
                if (arrival.Started ? arrival.HasSpawnPoint : arrival.IsHeldBack) return true;
            }
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

            if (Settings.SiegeFlanking && (_battle.IsSiegeOutside || _battle.IsSallyOut))
            {
                _besieged = BesiegedSettlement(_battle);
                if (_besieged != null && Reflected.PhasesAvailable)
                {
                    PlanHeldBack(BattleSideEnum.Defender);
                    PlanHeldBack(BattleSideEnum.Attacker);
                }
            }

            PlanMarchingParties();
        }

        private void PlanMarchingParties()
        {
            float joinRadius = Campaign.Current.Models.EncounterModel.GetEncounterJoiningRadius;
            float radius = joinRadius * Settings.ArrivalRadiusMultiplier;
            Vec2 center = _battle.Position.ToVec2();

            var candidates = new List<Arrival>();
            LocatableSearchData<MobileParty> search = MobileParty.StartFindingLocatablesAroundPosition(center, radius);
            for (MobileParty party = MobileParty.FindNextLocatable(ref search); party != null; party = MobileParty.FindNextLocatable(ref search))
            {
                if (!IsEligible(party)) continue;

                bool canJoinPlayerSide = _battle.CanPartyJoinBattle(party.Party, _battle.PlayerSide);
                bool canJoinEnemySide = _battle.CanPartyJoinBattle(party.Party, _battle.PlayerSide.GetOppositeSide());
                if (canJoinPlayerSide == canJoinEnemySide) continue; // neither, or ambiguous

                float distance = party.Position.ToVec2().Distance(center);
                if (distance <= joinRadius) continue; // vanilla already pulled these in at the start

                candidates.Add(new Arrival
                {
                    Party = party,
                    Name = party.Name.ToString(),
                    Origin = party.Position.ToVec2(),
                    Side = canJoinPlayerSide ? _battle.PlayerSide : _battle.PlayerSide.GetOppositeSide(),
                    DueSeconds = Math.Max(Settings.MinArrivalSeconds, MarchSeconds(distance, party.Speed)),
                });
            }

            // The nearest few per side.
            foreach (var side in candidates.GroupBy(a => a.Side))
                _arrivals.AddRange(side.OrderBy(a => a.DueSeconds).Take(Settings.MaxArrivalsPerSide));
        }

        private bool IsEligible(MobileParty party)
        {
            // Same filter vanilla uses for parties joining the player's battle (DefaultEncounterModel). It
            // also keeps out the besiegers and everyone inside a settlement.
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

        /// <summary>Battle seconds for a march of this many map units.</summary>
        private static float MarchSeconds(float distance, float speed)
        {
            // Party speed is in map units per campaign time step (TickMapTime: 4320 game seconds = 1.2 h).
            float hours = distance / Math.Max(speed, 0.5f) * 1.2f;
            return hours * Settings.BattleSecondsPerCampaignHour;
        }

        // ------------------------------------------------------------------
        // Relief battles: the part of a side that starts elsewhere comes in from there
        // ------------------------------------------------------------------

        private static Settlement BesiegedSettlement(MapEvent battle)
        {
            if (battle.MapEventSettlement != null) return battle.MapEventSettlement;
            foreach (BattleSideEnum side in new[] { BattleSideEnum.Defender, BattleSideEnum.Attacker })
                foreach (MapEventParty p in battle.GetMapEventSide(side).Parties)
                    if (p.Party.MobileParty?.BesiegedSettlement != null) return p.Party.MobileParty.BesiegedSettlement;
            return null;
        }

        private bool IsInside(PartyBase party) =>
            party.IsSettlement ? party.Settlement == _besieged : party.MobileParty?.CurrentSettlement == _besieged;

        /// <summary>
        /// The parties of a side that start on the other side of the walls from the side's own army: the
        /// garrison and lords inside when the army attacks from outside, or the allies waiting outside when
        /// it sallies from inside. They are taken out of the opening deployment, keeping their share of the
        /// battle size, and come in from where they are.
        /// </summary>
        private void PlanHeldBack(BattleSideEnum side)
        {
            MapEventSide battleSide = _battle.GetMapEventSide(side);
            PartyBase anchor = side == _battle.PlayerSide ? PartyBase.MainParty : battleSide.LeaderParty;
            if (anchor == null || anchor.NumberOfHealthyMembers <= 0) return;
            bool anchorInside = IsInside(anchor);

            var away = battleSide.Parties
                .Where(p => p.Party != PartyBase.MainParty && p.Party != anchor && IsInside(p.Party) != anchorInside && p.Party.NumberOfHealthyMembers > 0)
                .ToList();
            if (away.Count == 0) return;

            MissionAgentSpawnLogic spawnLogic = Mission.GetMissionBehavior<MissionAgentSpawnLogic>();
            if (!(Reflected.GetSupplier(spawnLogic, side) is PartyGroupTroopSupplier supplier)) return;
            if (!Reflected.TryGetPhase(spawnLogic, side, out int total, out int initial, out _)) return;

            // The rest of the side must still deploy. Count generously: an exact count needs the allocation.
            int upperBound = away.Sum(p => p.Party.MemberRoster.TotalManCount);
            if (total - upperBound < 1 || initial < 2) return;

            // Outside the walls everyone sallies from the gate together; outside parties come per army.
            var groups = anchorInside
                ? away.GroupBy(p => p.Party.MobileParty?.Army?.LeaderParty ?? p.Party.MobileParty).Select(g => g.ToList()).ToList()
                : new List<List<MapEventParty>> { away };

            // Take their troops now, before the spawner reserves the opening deployment.
            int held = 0;
            var planned = new List<(List<MapEventParty> members, List<UniqueTroopDescriptor> troops)>();
            foreach (List<MapEventParty> members in groups)
            {
                List<UniqueTroopDescriptor> troops = null;
                battleSide.AllocateTroops(ref troops, -1, (descriptor, owner) => members.Contains(owner));
                held += troops.Count;
                planned.Add((members, troops));
            }
            if (held == 0) return;
            // Straight after the allocation, so the spawner never expects troops it can't get.
            Reflected.HoldBack(spawnLogic, side, held);

            Vec2 battlePosition = _battle.Position.ToVec2();
            foreach (var (members, troops) in planned)
            {
                if (troops.Count == 0) continue;
                PartyBase lead = members.OrderByDescending(p => p.Party.NumberOfHealthyMembers).First().Party;
                Vec2 origin = anchorInside ? lead.Position.ToVec2() : _besieged.GatePosition.ToVec2();
                float speed = members.Where(p => p.Party.IsMobile).Select(p => p.Party.MobileParty.Speed).DefaultIfEmpty(0f).Min();
                float seconds = MarchSeconds(origin.Distance(battlePosition), speed);

                var arrival = new Arrival
                {
                    Members = members,
                    FromSettlement = !anchorInside,
                    Name = anchorInside ? lead.Name.ToString() : _besieged.Name.ToString(),
                    Origin = origin,
                    Side = side,
                    DueSeconds = MathF.Clamp(seconds, Settings.SallyMinSeconds, Settings.SallyMaxSeconds),
                };
                foreach (UniqueTroopDescriptor descriptor in troops)
                    arrival.Waiting.Add(Reflected.CreateOrigin(supplier, descriptor));
                _arrivals.Add(arrival);
            }
        }

        // ------------------------------------------------------------------
        // During the battle
        // ------------------------------------------------------------------

        private void Tick()
        {
            if (Mission.MissionEnded || Mission.Mode == MissionMode.Deployment) return;
            // Nothing comes in before the opening deployment is on the field: the battle size count needs it.
            MissionAgentSpawnLogic spawnLogic = Mission.GetMissionBehavior<MissionAgentSpawnLogic>();
            if (spawnLogic == null || !spawnLogic.IsDeploymentOver) return;
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
            if (arrival.IsHeldBack)
            {
                // Already part of the battle: they only need a way onto the field.
                ChooseSpawnPoint(arrival);
                if (!arrival.HasSpawnPoint) UseReinforcementEntry(arrival);
                if (!arrival.HasSpawnPoint) return;
                Announce(arrival);
                SpawnWave(arrival);
                return;
            }

            MobileParty party = arrival.Party;
            if (_battle.IsFinalized || !IsEligible(party) || !_battle.CanPartyJoinBattle(party.Party, arrival.Side)) return;

            MissionAgentSpawnLogic spawnLogic = Mission.GetMissionBehavior<MissionAgentSpawnLogic>();
            if (spawnLogic == null || !(Reflected.GetSupplier(spawnLogic, arrival.Side) is PartyGroupTroopSupplier supplier)) return;

            // Find where they enter first: a party is only added to the battle if its troops can take the field.
            ChooseSpawnPoint(arrival);
            if (!arrival.HasSpawnPoint) return;

            MapEventSide side = _battle.GetMapEventSide(arrival.Side);

            // Join the battle the way vanilla adds parties that are near the player's battle. None of these is
            // a garrison (they come from outside settlements), so a relief battle keeps its type.
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

            int room = Math.Min(FreeSlots(spawnLogic, arrival.Side), Settings.MaxSpawnPerWave);
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
            // wiped out while arrivals fight (their casualties are reported through the same troop supplier),
            // and so the side's own reinforcements count them.
            Reflected.AddSpawnedTroops(spawnLogic, arrival.Side, spawned);
        }

        /// <summary>
        /// Slots under the battle size limit this side can fill now without the total ever going over it:
        /// besides everyone alive, the other side's vanilla reinforcements may still bring it back up to its
        /// opening strength, so those slots stay theirs. This side's own vanilla reinforcements only refill
        /// what these troops leave free (they count towards its strength).
        /// </summary>
        private int FreeSlots(MissionAgentSpawnLogic spawnLogic, BattleSideEnum side)
        {
            BattleSideEnum other = side.GetOppositeSide();
            int aliveHere = 0, aliveThere = 0;
            foreach (Agent agent in Mission.Agents)
            {
                if (!agent.IsHuman || !agent.IsActive() || agent.Team == null) continue;
                if (agent.Team.Side == side) aliveHere++;
                else if (agent.Team.Side == other) aliveThere++;
            }

            int remainingThere = other == BattleSideEnum.Defender ? spawnLogic.NumberOfRemainingDefenderTroops : spawnLogic.NumberOfRemainingAttackerTroops;
            int refillThere = remainingThere; // unknown refill rule: assume all of it can come
            MissionSpawnSettings.ReinforcementSpawnMethod method = spawnLogic.ReinforcementSpawnSettings.ReinforcementTroopsSpawnMethod;
            bool refillsToOpening = method == MissionSpawnSettings.ReinforcementSpawnMethod.Wave || method == MissionSpawnSettings.ReinforcementSpawnMethod.Balanced;
            if (refillsToOpening && Reflected.TryGetPhase(spawnLogic, other, out _, out _, out int openingThere))
            {
                int activeThere = other == BattleSideEnum.Defender ? spawnLogic.NumberOfActiveDefenderTroops : spawnLogic.NumberOfActiveAttackerTroops;
                refillThere = Math.Max(0, Math.Min(openingThere - activeThere, remainingThere));
            }

            int room = spawnLogic.BattleSize - aliveHere - aliveThere - refillThere;
            // The engine's own agent limit; a rider takes two agents.
            int agentRoom = (MissionAgentSpawnLogic.MaxNumberOfAgentsForMission - spawnLogic.NumberOfAgents) / 2;
            return Math.Min(room, agentRoom);
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
            BattleSideEnum playerSide = Mission.PlayerTeam.Side;
            if (!SideCenter(playerSide, out Vec2 playerCenter) || !SideCenter(playerSide.GetOppositeSide(), out Vec2 enemyCenter)) return;
            Vec2 axis = enemyCenter - playerCenter;
            if (axis.LengthSquared < 1f) return;
            axis = axis.Normalized();

            // Bearing of where they come from, relative to the player -> enemy line on the campaign map.
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
            Vec2 target = arrival.Side == playerSide ? enemyCenter : playerCenter;
            Vec2 facing = target - arrival.SpawnPoint;
            arrival.Facing = facing.LengthSquared > 0.01f ? facing.Normalized() : -direction;
            arrival.HasSpawnPoint = true;
        }

        /// <summary>Troops already in the battle that can't use their own direction come in at vanilla's reinforcement entry.</summary>
        private void UseReinforcementEntry(Arrival arrival)
        {
            try
            {
                Team team = Mission.GetAgentTeam(arrival.Waiting[0], arrival.Side == Mission.PlayerTeam.Side);
                Mission.GetFormationSpawnFrame(team, FormationClass.Infantry, true, out WorldPosition position, out Vec2 direction);
                if (!position.IsValid) return;
                arrival.SpawnPosition = position;
                arrival.SpawnPoint = position.AsVec2;
                arrival.Facing = direction.LengthSquared > 0.01f ? direction.Normalized() : Vec2.Forward;
                arrival.HasSpawnPoint = true;
            }
            catch
            {
                // No plan for that team: they stay out of the fight.
            }
        }

        /// <summary>Signed angle (radians, counter-clockwise positive) from the player->enemy line to the arrival's origin.</summary>
        private float BearingAngle(Arrival arrival)
        {
            Vec2 player = MobileParty.MainParty.Position.ToVec2();
            Vec2 reference = EnemyPosition() - player;
            Vec2 toOrigin = arrival.Origin - player;

            if (toOrigin.LengthSquared < 1e-6f) return arrival.Side == _battle.PlayerSide ? MathF.PI : 0f;
            if (reference.LengthSquared < 1e-6f)
            {
                // Armies on the same spot: allies come from behind the player, enemies from behind the enemy.
                return arrival.Side == _battle.PlayerSide ? MathF.PI : 0f;
            }
            return MathF.Atan2(reference.x * toOrigin.y - reference.y * toOrigin.x, reference.x * toOrigin.x + reference.y * toOrigin.y);
        }

        /// <summary>Where the enemy stands on the campaign map: its leader, else the middle of its parties.</summary>
        private Vec2 EnemyPosition()
        {
            MapEventSide enemy = _battle.GetMapEventSide(_battle.PlayerSide.GetOppositeSide());
            Vec2 player = MobileParty.MainParty.Position.ToVec2();
            if (enemy.LeaderParty != null && enemy.LeaderParty.Position.ToVec2().DistanceSquared(player) > 1e-6f)
                return enemy.LeaderParty.Position.ToVec2();
            Vec2 sum = Vec2.Zero;
            int count = 0;
            foreach (MapEventParty p in enemy.Parties)
            {
                sum += p.Party.Position.ToVec2();
                count++;
            }
            return count > 0 ? sum * (1f / count) : player;
        }

        private void Announce(Arrival arrival)
        {
            float a = BearingAngle(arrival);
            float deg = Math.Abs(a) * 180f / MathF.PI;
            string from = deg < 45f ? "from beyond the enemy lines"
                : deg > 135f ? "from behind your lines"
                : a > 0f ? "on your left flank" : "on your right flank";
            bool ally = arrival.Side == _battle.PlayerSide;
            string text = arrival.FromSettlement
                ? "The defenders of " + arrival.Name + (ally ? " sally out to join you, " : " sally out against you, ") + from + "!"
                : arrival.Name + (ally ? " arrives to support you, " : " joins the enemy, ") + from + "!";
            InformationManager.DisplayMessage(new InformationMessage(text, ally ? Colors.Green : Colors.Red));
        }

        /// <summary>The middle of one side's soldiers on the battlefield.</summary>
        private bool SideCenter(BattleSideEnum side, out Vec2 center)
        {
            center = Vec2.Zero;
            int count = 0;
            foreach (Agent agent in Mission.Agents)
            {
                if (!agent.IsHuman || !agent.IsActive() || agent.Team == null || agent.Team.Side != side) continue;
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
            // The spawner's per-side plan: how many troops in total, in the opening deployment, still to come.
            private static readonly FieldInfo PhasesField = AccessTools.Field(typeof(MissionAgentSpawnLogic), "_phases");
            private static readonly Type SpawnPhaseType = AccessTools.Inner(typeof(MissionAgentSpawnLogic), "SpawnPhase");
            private static readonly FieldInfo TotalField = SpawnPhaseType == null ? null : AccessTools.Field(SpawnPhaseType, "TotalSpawnNumber");
            private static readonly FieldInfo InitialField = SpawnPhaseType == null ? null : AccessTools.Field(SpawnPhaseType, "InitialSpawnNumber");
            private static readonly FieldInfo InitialSpawnedField = SpawnPhaseType == null ? null : AccessTools.Field(SpawnPhaseType, "InitialSpawnedNumber");
            private static readonly FieldInfo RemainingField = SpawnPhaseType == null ? null : AccessTools.Field(SpawnPhaseType, "RemainingSpawnNumber");
            private static int _rank = 1_000_000;

            public static bool Available =>
                AddNearbyPartyMethod != null && MakeReadyPartyMethod != null && OriginConstructor != null
                && MissionSidesField != null && SupplierField != null && SpawnedField != null;

            /// <summary>Needed for the sally from the walls and for the exact battle size count.</summary>
            public static bool PhasesAvailable =>
                PhasesField != null && TotalField != null && InitialField != null && InitialSpawnedField != null && RemainingField != null;

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

            /// <summary>
            /// The side's spawn plan: total troops, troops in the opening deployment (before it happens), and
            /// how many the opening deployment actually put on the field (after it).
            /// </summary>
            public static bool TryGetPhase(MissionAgentSpawnLogic logic, BattleSideEnum side, out int total, out int initial, out int initialSpawned)
            {
                total = initial = initialSpawned = 0;
                object phase = PhasesAvailable ? ActivePhase(logic, side) : null;
                if (phase == null) return false;
                total = (int)TotalField.GetValue(phase);
                initial = (int)InitialField.GetValue(phase);
                initialSpawned = (int)InitialSpawnedField.GetValue(phase);
                return true;
            }

            /// <summary>
            /// Takes troops the mod spawns itself out of the side's spawn plan, keeping their share of the
            /// opening deployment free for them. Called before the opening deployment.
            /// </summary>
            public static void HoldBack(MissionAgentSpawnLogic logic, BattleSideEnum side, int held)
            {
                object phase = ActivePhase(logic, side);
                int total = (int)TotalField.GetValue(phase);
                int initial = (int)InitialField.GetValue(phase);
                int newTotal = total - held;

                // Their proportional share of the opening slots, but the rest of the side must fill what is left
                // of the opening deployment with troops it really has, and still deploy at least one.
                int share = (int)Math.Round((double)initial * held / Math.Max(1, total));
                int reserved = Math.Max(Math.Max(0, initial - newTotal), Math.Min(share, Math.Min(held, initial - 1)));
                int newInitial = initial - reserved;

                TotalField.SetValue(phase, newTotal);
                InitialField.SetValue(phase, newInitial);
                RemainingField.SetValue(phase, newTotal - newInitial);
            }

            private static object ActivePhase(MissionAgentSpawnLogic logic, BattleSideEnum side)
            {
                IList phases = ((Array)PhasesField.GetValue(logic)).GetValue((int)side) as IList;
                return phases != null && phases.Count == 1 ? phases[0] : null;
            }

            private static object MissionSide(MissionAgentSpawnLogic logic, BattleSideEnum side) =>
                ((Array)MissionSidesField.GetValue(logic)).GetValue((int)side);
        }
    }
}
