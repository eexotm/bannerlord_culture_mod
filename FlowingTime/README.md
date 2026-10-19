# Flowing Time (Bannerlord mod, v0.4.0)

Single-player mod: campaign time no longer pauses on its own. You still pause whenever you want.

Built for Bannerlord **1.3.15**. Requires the **Harmony** module (Bannerlord.Harmony), which most
overhaul mods already use.

## What works so far (stage 1: the campaign map, settlement menus and your battle menu)
- **Town, castle and village menus:** time keeps running while you trade, recruit, manage the
  settlement, and so on. Space, the 1/2/3 speed keys and the time bar buttons work inside these
  menus too.
- **Your own field battle menu** (Attack / Send troops / Try to get away / Surrender): time keeps
  running. The game never fights your battle without you, so the enemy waits, but the world
  doesn't: every 15 in-game minutes, parties that have arrived nearby join, allies on your side and
  enemies on theirs, using the game's own joining rules. Enemies marching straight at you join
  when they reach you. A message tells you when either side grows, and the menu updates.
- **No arrival pause:** reaching your destination no longer stops the clock.
- **Routine stops are undone:** when the game stops time only because a menu opened, changed or
  closed, an encounter finished, or a wait ended, time resumes at normal speed. The mod never
  fast-forwards on its own: travel fast-forward (double click) ends when you arrive, as in vanilla,
  and only the keys or buttons fast-forward.
- **Siege or tournament changes:** if a siege starts on the town you're in, or its tournament ends,
  the menu switches to the right one, the way vanilla's wait menus do.

Time still stops, exactly as in vanilla, for:
- **Your own pause:** the Space key, the pause button, or the speed keys.
- **Loading a save:** you start paused until you press play.
- **The escape menu.**
- **Other battle situations:** raids, sieges, sally-outs, hideouts, naval battles, menus for
  joining someone else's battle, the menus after a battle, battles where you follow someone else's
  army, and captivity. Letting time pass there makes the menus go stale (crashes) or resolves the
  fight without you. Reinforcements still arrive *inside* the battle scene (see below).
- **Deliberate game and quest pauses:** a menu or quest that stops time on purpose (for example
  Revenue Farming events or the Rival Gang night meeting) stays paused until you press play.
- **Popups and locked screens:** popup decisions (peace offers, ransom, marriage, heir selection,
  incidents), cheats and army management.
- **Conversations and battle simulation.**

Vanilla "wait" menus (wait in town, sieges, captivity, waiting in an army) work as they always did.

## Reinforcements arriving mid-battle (field battles and relief battles)
When your battle starts, the mod looks at the parties marching nearby on the campaign map:
every party up to about four times vanilla's joining range that could join one side under the
game's own rules (war and alliance status, not busy in another battle, siege or town).

- **Arrival time** comes from each party's distance and speed: roughly one minute of battle per
  in-game hour of marching, never earlier than 30 seconds after the fighting starts. At most the
  4 nearest parties per side come.
- **They enter from the direction they came from:** a party that was behind the enemy on the map
  appears behind the enemy lines; one on your left comes in on your left flank. A message names
  them and says where they arrive.
- **They form their own formation and charge.** Yours appear in your order bar like any formation.
  Formations you already use are never re-ordered.
- **The battle size limit is always respected.** Arrivals only spawn into slots nobody else can
  claim: battle size, minus everyone alive, minus the reinforcements the other side still has coming
  under vanilla's rules. The rest wait at the edge and come in as casualties free room, like vanilla
  reinforcements. A side with arrivals still waiting isn't counted as wiped out.
- **Casualties, wounds, loot, experience and the battle result** go through vanilla's normal battle
  bookkeeping, because arrivals join the battle the way vanilla adds nearby parties.
- **Only real battles:** conversations held during an encounter, assaults on the walls, raids,
  hideouts and naval battles are left alone for now.

## Relieving a siege: flank the besiegers
When you attack an army besieging a town or castle from outside ("Assault the siege camp"), the
game fights it on the open field next to the settlement, and the garrison may join you (vanilla
decides that by comparing strength). In vanilla the garrison then simply stands next to you.

With Flowing Time the garrison and the lords inside **sally out from the settlement's side**, 15 to
90 seconds into the fight depending on how far the gate is. The besiegers are caught between your
army and the garrison, and a message says where the defenders come in. The same works the other way
round:
- **You are the besieger** and a relief army attacks your camp: if the garrison sallies to help it,
  the defenders come out behind your lines.
- **You join an assault from outside** to help the town: the town's defenders come from the town.
- **You sally out from inside the town:** your allies waiting outside come in from where they stand.

The garrison keeps its share of the battle size: it is left out of the opening deployment and fills
those slots when it comes out, so the limit is never exceeded. Parties marching nearby still arrive
mid-battle from their direction, as in field battles. Assaults on the walls themselves are not
changed yet.

## Planned
2. Time keeps running on other screens: inventory, party, character, clan, kingdom and
   encyclopedia, plus conversations.
3. Time keeps running inside scenes: town walks, taverns and battles.
4. Assaults on the walls: relief armies arriving behind the besiegers during the assault.

## Compatibility
The mod changes only *when* the clock runs. It doesn't reference any troop, culture, settlement or
map by name, so it works with Realm of Thrones and other total-conversion mods.

The core patches only work together. If any of them doesn't fit (another mod changed the same code,
or a game update did), the mod removes all of its patches, the game runs as vanilla, and a red
"[Flowing Time] Switched off ..." message names the reason. An unexpected error at runtime does the
same instead of crashing. Please report it when that happens.

## Settings
For now the switches are fixed values in `Settings.cs`: `FlowInMenus`, `FlowInOwnBattles`,
`JoinCheckIntervalHours`, `NoArrivalPause`, `FlowDuringPopups`, and for battles
`MidBattleArrivals`, `ArrivalRadiusMultiplier`, `BattleSecondsPerCampaignHour`, `MinArrivalSeconds`,
`MaxArrivalsPerSide`, `MaxSpawnPerWave`, and for relief battles `SiegeFlanking`, `SallyMinSeconds` and
`SallyMaxSeconds`.

## Performance check
Create an empty file named `perf.txt` in `Modules/FlowingTime/`. Every 10 seconds on the campaign map
a yellow line then shows how long the mod's own code takes per frame, the map frame rate, and the
current game speed. Delete the file to turn it off.

The mod's own code is meant to cost a few microseconds per frame. If the game still feels slower
than vanilla, it's because the world now keeps simulating in places where vanilla froze it (town
and battle menus, after you arrive somewhere). That costs about the same as playing unpaused on the
open map, more at fast-forward, and on big overhaul maps like Realm of Thrones that cost is real.

## Build
`dotnet build -c Release` builds against BUTR's `Bannerlord.ReferenceAssemblies` package, so no game
install is needed to compile. To build straight into your game, set `GameFolder` in
`FlowingTime.csproj`. Otherwise copy `bin/Release/FlowingTime.dll` to
`Modules/FlowingTime/bin/Win64_Shipping_Client/`, and `SubModule.xml` to `Modules/FlowingTime/`.
