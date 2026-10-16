# Monetisation: free Meadow, one-time unlock for the rest

Agreed with the developer on 2026-10-15. Status (2026-10-16): code written and tested in the editor with the fake store (locked planks, unlock screen from a locked section and from Continue, Cancel, successful Buy → continues into the section, unsolved Meadow levels come before the unlock screen). Not tested: purchase failure (needs the `IAP_FAKE_STORE_DEVELOPER_USER` define), pending payment, and anything on a phone. `com.unity.purchasing` 5.4.4 in the manifest; `Assets/ScriptsShared/Entitlements.cs` (store, cache, purchase states); gating and the unlock screen in `GameFlow` (built at runtime from copies of the main menu planks); locked sections read "LOCKED" on their planks (the Blender lock badge is still to do); test menu items under "Blocks Escape/Testing"; `privacy-policy.md` updated. Still to do: compile + fake-store tests, lock badge, `listing.md`, `play-console-checklist.md`.

## Goal

No ads. The whole game is free to install and play, but only the first section (Meadow, levels 1–200) is playable for free. A single Google Play purchase unlocks the other five sections (Orchard, Pond, Barnyard, Hayloft, Hilltop: levels 201–1200). The purchase belongs to the player's Google account, so it comes back after a reinstall or on another device.

## Decisions (fixed)

| Topic | Decision |
|---|---|
| Product | One **non-consumable** in-app product, ID **`unlock_all_levels`** (cannot be changed once created in Play Console) |
| Billing | Google Play Billing through Unity's **In-App Purchasing** package (`com.unity.purchasing`); it also gives a fake store for editor testing |
| Server | None. Ownership comes from Play at startup (the package fetches existing purchases); the unlock is also cached on the device for offline play |
| Restore | Automatic on Android when the store initialises; no "Restore purchases" button needed |
| Unlock screen | A wooden sign in the game's style, e.g. "Unlock all 1,000 extra levels", the localised price from Play, and Buy / Not now buttons |
| Locked sections | The five non-Meadow planks in the level picker show a lock; tapping one opens the unlock screen |
| After level 200 | While locked, `NextLevel` after level 200 moves on to the next **unsolved Meadow level**; the unlock screen appears only when every Meadow level (1–200) is solved |
| Continue (locked) | `ContinueLevel` searches only levels 1–200 while locked; with all of them solved, Continue opens the unlock screen |
| Purchase effect | All five sections unlock at once, immediately |
| Pending purchases | The sign shows "Purchase pending"; levels unlock when Play confirms the payment |
| Unlock sign | Reuse the existing `Sign` / `MenuPlank` props; only a small lock badge is modelled new in Blender |
| "Extra levels" text | Computed from the catalog size (catalog count − 200), not hard-coded |
| Existing players | None: the game is not public and has no testers yet (2026-10-15), so no grandfathering is needed |
| Price | Set by the developer in Play Console |

## Where things live (Unity project `Game/BlocksEscape`)

- `Assets/Prefabs/Menu/GameFlow.cs`: the single-scene flow. Screens Menu, Settings, Sections, Page, Level; every switch is an animated coroutine (`Switch`, `Pop`, `PopTransition`); `Back()` steps Page → Sections → Menu. `StartLevel(index)`, `ShowLevelPicker()`, `ShowSection(section)`, `NextLevel()` (runs after a level is solved; goes to `ToLevel(_level + 1)`), Continue uses `Progress.ContinueLevel`.
- `Assets/Prefabs/Menu/LevelPicker.cs`: section planks (`SectionProps()`), 200 levels per section, 5×5 tile pages, `SectionPicked` / `LevelPicked` / `PageTurned` events.
- `Assets/ScriptsShared/Progress.cs`: PlayerPrefs progress (solved bitset, last played). A cached `Unlocked` flag fits here or in a new `Entitlements` class.
- In-world UI: props are `ScreenAnchor`s (viewport placement) popped by `PopTransition`; buttons are `WorldButton`; text is `WorldLabel` / `WorldText` (TextMeshPro, Fredoka font). The level sign (`LevelSign`) has a `Caption`. Menu pages are GameObjects whose children are planks laid out top to bottom.
- New props (a lock badge for planks, the unlock sign) are modelled in `Game/BlocksEscape/Blender/scenery.py` (add a builder to `BUILDERS`, export headless with Blender), then "Blocks Escape/Build Scenery From Blender" in Unity. That menu rewrites internal IDs in every scenery prefab; restore the untouched ones from git afterwards. Reusing `MenuPlank` / `Sign` may be enough.

## Work

1. Add `com.unity.purchasing` (current version), initialise the store at startup with `unlock_all_levels` as non-consumable; handle init failure and offline start (fall back to the cached flag).
2. Entitlement: `IsUnlocked` (cached in PlayerPrefs, refreshed from the store's owned purchases), an `Unlocked` event, purchase / cancel / failure / pending handling. Never lock out a player who once had the unlock because the store is unreachable.
3. Gate levels ≥ 200 (catalog index) in `GameFlow.StartLevel`, `NextLevel`, Continue, and section picking in the picker. While locked, `NextLevel` and `Progress.ContinueLevel` treat the level count as 200 (wrap within the Meadow) and open the unlock screen only when all 200 are solved.
   Use the current IAP v5 API (Unity 6000.6), not the deprecated `IStoreListener`.
4. Locked look for sections 2–6 (lock badge or dimmed plank with a lock), refreshed when the unlock arrives.
5. Unlock screen: a new GameFlow screen with the wooden sign, price text from the store (fallback text when the price is unknown), Buy and Not now; Back closes it. After a successful purchase, continue to where the player was heading.
6. Test in the editor with the fake store (success, cancel, failure, already owned), then on a phone via the Play internal track with a license tester.

## Play Console (developer)

1. Set up a **payments profile** (merchant account), required before selling anything.
2. Upload a build that includes billing (adds `com.android.vending.BILLING`) to **Internal testing**; Play only allows creating in-app products after that.
3. Monetize › Products › In-app products: create `unlock_all_levels`, set the price, activate it.
4. Add tester accounts under **License testing** to buy without being charged.

## Documents to update afterwards (`Game/BlocksEscape/StoreListing/`)

- `listing.md`: remove "no in-app purchases"; describe the free Meadow and the one-time unlock (the listing will carry the "In-app purchases" label).
- `privacy-policy.md`: purchases are handled by Google Play; the game itself still collects no personal data.
- `play-console-checklist.md`: Data safety review for purchase handling; payments profile and product steps above.

## Testing tips from earlier sessions

- Unity's Play mode only advances while the editor is focused (Run In Background is off). For scripted checks set `Application.runInBackground = true` inside Play mode and switch it back off; check afterwards that `ProjectSettings.asset` has no `runInBackground` change.
- Render the game camera to a `RenderTexture` (1170×2532 or 1080×1920) to check layouts; scripted walkthroughs can run as a coroutine on `GameFlow` with `WaitForSeconds` between steps.
- Scripted level starts change `Progress.LastPlayed`; note the value first and restore it.
