# Cultural Assimilation (Bannerlord mod, v0.1)

Villages slowly adopt the culture of whoever holds their bound castle or town.

## How it works
- Every village has hidden progress (0-100) toward its owner's culture.
- After a castle/town is captured, progress waits 30 days (`GraceDays`). Holdings that were
  already owned when the mod was added (or at campaign start) count as established and
  start progressing right away.
- Base conversion takes about 90 days (`BaseDaysToConvert`), scaled by:
  - castle/town loyalty (stalls below 25, up to ~1.5x at 100)
  - governor (+10% if same culture as the owner, up to +15% from Charm)
- Raided or looted villages make no progress.
- When the castle/town changes owner, progress and the hold timer reset.
- At 100: the village's culture changes, its notables' culture changes, and their
  stored old-culture volunteers are cleared so recruits regenerate from the new culture.
  Militia follows `Settlement.Culture`, so it changes too.

## Adding to / removing from an ongoing save
- **Adding:** safe. Load your save with the mod enabled; assimilation starts from 0 progress
  for every village, with no grace period for settlements already held.
- **Removing:** the mod only stores plain strings in the save (no custom saveable types),
  so a save made with it should still load without it; its data is simply ignored.
  Villages that already converted keep their new culture.

## Realm of Thrones
Troops in RoT come from each culture's troop tree, so flipping the culture is what
changes who you can recruit. Two things to set up:
1. In `SubModule.xml`, uncomment the `RealmOfThrones` DependedModule line and make sure the
   Id matches the folder name in `Modules/`. This makes your mod load after RoT.
2. Place this mod below RoT in the launcher load order.

## Build
1. Open `CulturalAssimilation.csproj` and set `GameFolder` to your Bannerlord install path.
2. `dotnet build -c Release` (or open in Visual Studio / Rider).
3. Output lands in `Modules/CulturalAssimilation/bin/Win64_Shipping_Client/`, and
   `SubModule.xml` is copied to `Modules/CulturalAssimilation/`.
4. Enable the mod in the launcher.

Written against Bannerlord 1.2.x. If you are on a different version, namespaces for
`CultureObject`, `DefaultSkills` or `InformationManager` may need a `using` tweak.

## Things to verify in game
- Cheat: set a village's culture/owner and fast-forward ~120 days; check the green
  "has adopted ... ways" message appears.
- Open the village's notables and confirm their volunteers match the new culture.
- Save, reload, and confirm progress persists.
- Load a save made *without* the mod, enable it, and confirm it loads cleanly and villages
  under long-held castles start converting without the 30-day wait.

## Roadmap
Loyalty unrest from culture mismatch, assimilate/tolerate/integrate policies,
Cultural Reclamation casus belli, village envoys/quests.
