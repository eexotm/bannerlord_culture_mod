# Flowing Time (Bannerlord mod, v0.2.1)

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
  fight without you. Parties joining *inside* a battle scene, mid-fight, is planned for the battle
  stages below.
- **Deliberate game and quest pauses:** a menu or quest that stops time on purpose (for example
  Revenue Farming events or the Rival Gang night meeting) stays paused until you press play.
- **Popups and locked screens:** popup decisions (peace offers, ransom, marriage, heir selection,
  incidents), cheats and army management.
- **Conversations and battle simulation.**

Vanilla "wait" menus (wait in town, sieges, captivity, waiting in an army) work as they always did.

## Planned
2. Time keeps running on other screens: inventory, party, character, clan, kingdom and
   encyclopedia, plus conversations.
3. Time keeps running inside scenes: town walks, taverns and battles.
4. Parties that join your battle on the map arrive mid-fight as reinforcements, from the direction
   they came.

## Compatibility
The mod changes only *when* the clock runs. It doesn't reference any troop, culture, settlement or
map by name, so it works with Realm of Thrones and other total-conversion mods.

The core patches only work together. If any of them doesn't fit (another mod changed the same code,
or a game update did), the mod removes all of its patches, the game runs as vanilla, and a red
"[Flowing Time] Switched off ..." message names the reason. An unexpected error at runtime does the
same instead of crashing. Please report it when that happens.

## Settings
For now the switches are fixed values in `Settings.cs`: `FlowInMenus`, `FlowInOwnBattles`,
`JoinCheckIntervalHours`, `NoArrivalPause` and `FlowDuringPopups`.

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
