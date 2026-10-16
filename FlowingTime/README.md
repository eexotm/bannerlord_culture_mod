# Flowing Time (Bannerlord mod, v0.0.1)

Single-player mod: campaign time no longer pauses on its own. You still pause whenever you want.

Built for Bannerlord **1.3.15**. Requires the **Harmony** module (Bannerlord.Harmony), which most
overhaul mods already use.

## What works so far (stage 1: the campaign map and menus)
- **Town, castle and village menus:** time keeps running while you trade, recruit, manage the
  settlement, and so on. Space and the 1/2/3 speed keys work inside these menus too.
- **Encounter menus** (attack / surrender / leave): time keeps running, so other parties keep moving
  and can join the fight. The game never auto-resolves your own battle while it waits for you.
- **No arrival pause:** reaching your destination no longer stops the clock.
- **After the game stops time by itself** (entering or leaving a settlement, closing a menu, finishing
  an encounter or a wait), time resumes at the speed you had chosen.

Time still stops for:
- **Your own pause:** the Space key, the pause button, or the speed keys.
- **Loading a save:** you start paused, as in vanilla, until you press play.
- **The escape menu.**
- **Popup decisions** (peace offers, ransom, marriage, heir selection, incidents), cheats and army
  management. These lock the clock in vanilla and are left alone.
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

Each game patch is applied on its own. If one doesn't fit (another mod changed the same code, or a
game update did), only that feature switches off and a red "[Flowing Time] ... disabled" message
appears in the log. Please report it when that happens.

## Settings
For now the switches are constants in `Settings.cs` (`FlowInMenus`, `FlowDuringEncounters`,
`NoArrivalPause`, `FlowDuringPopups`).

## Build
`dotnet build -c Release` builds against BUTR's `Bannerlord.ReferenceAssemblies` package, so no game
install is needed to compile. To build straight into your game, set `GameFolder` in
`FlowingTime.csproj`. Otherwise copy `bin/Release/FlowingTime.dll` to
`Modules/FlowingTime/bin/Win64_Shipping_Client/`, and `SubModule.xml` to `Modules/FlowingTime/`.
