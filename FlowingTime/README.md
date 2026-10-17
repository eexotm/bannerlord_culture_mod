# Flowing Time (Bannerlord mod, v0.1.2)

Single-player mod: campaign time no longer pauses on its own. You still pause whenever you want.

Built for Bannerlord **1.3.15**. Requires the **Harmony** module (Bannerlord.Harmony), which most
overhaul mods already use.

## What works so far (stage 1: the campaign map and settlement menus)
- **Town, castle and village menus:** time keeps running while you trade, recruit, manage the
  settlement, and so on. Space, the 1/2/3 speed keys and the time bar buttons work inside these
  menus too.
- **No arrival pause:** reaching your destination no longer stops the clock.
- **Routine stops are undone:** when the game stops time only because a menu opened, changed or
  closed, an encounter finished, or a wait ended, time resumes at the speed you had chosen.
- **Siege or tournament changes:** if a siege starts on the town you're in, or its tournament ends,
  the menu switches to the right one, the way vanilla's wait menus do.

Time still stops, exactly as in vanilla, for:
- **Your own pause:** the Space key, the pause button, or the speed keys.
- **Loading a save:** you start paused until you press play.
- **The escape menu.**
- **Anything involving a battle:** encounter menus (attack / surrender / leave), menus for joining
  someone else's battle, raids, sieges and sally-outs, and captivity. The menus stay valid this way,
  and your troops never fight without you. Parties joining a battle mid-fight is planned for the
  battle stages below.
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
For now the switches are fixed values in `Settings.cs`: `FlowInMenus`, `NoArrivalPause` and
`FlowDuringPopups`.

## Build
`dotnet build -c Release` builds against BUTR's `Bannerlord.ReferenceAssemblies` package, so no game
install is needed to compile. To build straight into your game, set `GameFolder` in
`FlowingTime.csproj`. Otherwise copy `bin/Release/FlowingTime.dll` to
`Modules/FlowingTime/bin/Win64_Shipping_Client/`, and `SubModule.xml` to `Modules/FlowingTime/`.
