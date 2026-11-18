# Path of Exile 1 / 2 Companion

A lightweight Windows companion for **Path of Exile 1 and Path of Exile 2**, using the matching installed **Path of Building Community** engine.

Choose **Companion game** at the top of the dashboard or in the tray menu. Hover over an item in that game and press **Ctrl+D**: the companion copies it, calculates changes against your **Current character snapshot**, and displays them near your cursor. Your separate **Guide build** defines goals, never the equipped baseline.

## Install on Windows

New to the companion? Read the [User Guide](USER-GUIDE.txt) for official PoB downloads and your first comparison. Setup installs a copy in the companion folder and adds it to the Start menu.

Download `PoE-Item-Companion-Setup-v<version>-x64.exe` from the [official GitHub releases](https://github.com/troyvanbritsom-lang/poe-item-companion/releases). Setup installs for the current Windows user, lets you choose the destination on first install, and adds a Start menu shortcut with an optional desktop shortcut. The installer is unsigned. It does not bundle Path of Building.

Save and close any PoB you want to connect before running Setup. On **Path of Building bridges (optional)**, browse to the installation root for each game you use, or leave either field blank to skip it. Setup checks the game and supported patch points, then shows a separate result for each bridge. If PoB is in a protected folder that Setup cannot write to, use a writable PoB installation under the same Windows account or leave the bridge disabled; the companion can still calculate with a compatible PoB configured in Settings. Restart PoB yourself after a successful bridge setup.

The app checks for stable releases in the background on startup. Right-click its tray icon and choose **Check for updates** to check again or see your version. If a newer release is available, the app offers to open GitHub in your browser. It never downloads or runs the update. Exit the companion before running a newer installer. Leave the PoB fields blank on upgrades to keep the current profile paths and avoid reapplying bridges.

Installed settings, logs, exports and captures are kept in `%LOCALAPPDATA%\PoeItemCompanion\UserData`. Portable builds still use their own folder. The installer does not migrate or delete portable data; you can point the new copy at the same build files in Settings. Windows **Installed apps** removes the installed companion and shortcuts but leaves user data and PoB builds. It also leaves installed PoB hooks intact; see the bridge removal instructions below.

## Game support and profiles

| Feature | PoE1 | PoE2 |
|---|---|---|
| Current/Guide XML and compressed PoB codes | `PathOfBuilding` root | `PathOfBuilding2` root |
| Calculations, equipment/jewels, saved loadouts, source-safe export | Installed PoB1 engine | Installed PoB2 engine |
| Skills and gem overlay | Per-instance level/quality/variant checklist, equipment socket/link warnings | Existing skill/spirit/support gem-cutting guidance |
| In-app tree and passive checklist | Mastery effects are separate choices; cluster-context/tree-version mismatches block advice | Existing tree/checklist, with version/context guards |
| Campaign log stages | Ten acts and maps | Acts 1-4, Interlude and maps |
| Automatic gem-cutter detection | Not applicable; manual checklist remains available | Available |
| GGG/Maxroll `.build` guide files and bundled passive-ID mapping | Not supported | Available; explicit notes/data only |
| Crafting and in-game passive-tree highlight rings | Removed | Removed |

Default installations are `D:\Path of Building Community` and `D:\Path of Building Community (PoE2)`; configure each in its own **Settings**. Build folders are discovered from redirected Documents/PoB `Settings.xml`, and can be overridden. The installed engines must have compatible native DLLs to coexist in one process; incompatible installations are rejected with a restart/setup message, not cross-loaded.

Each game remembers its own engine/build directory, Current and Guide sources, resolved codes, guide stage, log settings, overlay positions and detection spots. Switching saves the outgoing profile, closes stale views/popups, recreates the engine and rebinds hotkeys. It is blocked during an active operation. Wrong-game XML/codes/installations are rejected; no automatic conversion is attempted. A fresh PoE1 profile requires importing its own Current snapshot.

Existing flat `bin\config.json` is migrated to a versioned `ActiveGame`/`Profiles` envelope with the original preserved in a unique `.legacy-backup`. Existing PoE2 data remains in its PoE2 profile; unused crafting/ring settings are ignored. Broken configuration is reported and never replaced with defaults. Detection captures, user builds and installed PoB files are not deleted. Keep editing through Settings; manual settings changes belong inside the selected `Profiles.Poe1` or `Profiles.Poe2` object.

Both games can use the same executable name (including `PathOfExileSteam.exe`). Foreground capture/detection therefore identifies the installation path, not just the name. For unusually named installation directories, set that game's correct `GameLogPath` so the client can be identified. An ambiguous/wrong foreground game is not captured. Clipboard paste and manual overlays remain available offline.

PoB calculations use the saved skill/config assumptions, not live game state. PoE1 socket/link notices do not certify item-granted supports or skill usability. Full DPS, suppression, unreserved mana and ward are shown only when reported by that engine. Passive suggestions require matching tree and cluster contexts; they are not an invented levelling order. Website links work only when they expose a supported raw PoB build (pobb.in, Pastebin, game-specific poe.ninja/PoEDB/Maxroll routes); interactive planners are not reconstructed.

PoE1 timeless-jewel effects use the installed compressed lookup data through the installed zlib runtime. The companion decompresses it in memory and does not write PoB's uncompressed `.bin` cache. Missing/corrupt lookup data is reported as a load error; it is never treated as an empty lookup. Failed loads are isolated so retrying another build cannot reuse a partially initialized passive tree.

### PoE1 gem buying guidance

**Overlay selector (Ctrl+G) > Gems** shows one compact line per gem: its name and starting character level. Hover a gem to see its act, vendor/location, quest unlock, class restrictions and character-level requirements without expanding the overlay. Quest completion is not detected; check your quest log. `L12*` means the gem starts at level 12, but its vendor quest unlock is still required; `?` means purchase availability is unverified, and special gems show `not sold`. **Build > Gem checklist** retains **Character level requirements** and **Buy / acquire** columns with vendor/location, quest and class details; hover a row for its full text or double-click it to open that exact gem's wiki page.

The minimum character level for a level-1 gem is separate from the character level needed for the guide's target gem level. A higher-quality/high-level guide gem is not promised at a vendor: buy the ordinary gem and level/improve it, or trade for the target. Early vendor suggestions use your **Current class**, not your Guide class; without Current, restricted options are labelled unknown. Siosa and Lilly options retain their quest unlocks. Quest completion is not detected, so no entry claims "available now".

Vaal, awakened and transfigured versions show acquisition instructions instead of ordinary vendors. Gems absent from the offline dataset are explicitly unverified and link to the wiki; missing/corrupt data is reported rather than assuming Lilly sells everything. Acquisition data is bundled from the MIT-licensed [PoENavi snapshot](https://github.com/buri34/poenavi/blob/e5fba514418413fd0ac4d919971d931098470085/data/gems.json), retrieved 2026-11-11; patches may change availability. No character/build data is sent to a service, and the checklist works offline. PoE2 gem-cutting guidance is unchanged.

Focused verification: `bin\Poe2ItemCompanion.exe --test-gem-acquisition`.

### Gems needed and campaign quests

The gem overlay and **Build > Gem checklist / Gem cutting** now default to **missing gem instances only**. A gem already present in an enabled Current skill group is hidden even if its level/quality is below the Guide target. Use **all** in the overlay or **Show equipped / disabled gems** in the app to inspect those upgrade details. Matching uses game identity, gem ID (or case-insensitive name if an ID is absent), variant and kind; one equipped copy covers only one enabled Guide requirement. Without a Current snapshot the list still shows the Guide's unmet requirements and explicitly says Current is not loaded. Refresh/import Current after changing gems in game; this is not live inventory detection. Icons and purchase/cutting guidance are unchanged.

Open **Build > Quests** for the campaign overview, including rewards, alternative choices and requirements. Press the overlay selector key (default **Ctrl+G**) and choose **3 Quests** to toggle its draggable, non-activating always-on-top overlay, or use **Overlays > Quests overlay** in the system tray menu or **Open quest overlay** in the overview. The selector works with the dashboard hidden. Filter by act, uncheck an optional quest to omit it from your route, and use **Show deselected quests** in the overlay to reveal and restore skipped quests. Main-story quests cannot be unchecked. Choices are saved separately in each game's profile, across restarts; they are route preferences, not character-specific completion records. The main overview shows deselected quests by default and updates immediately when the overlay changes a choice. Drag the overlay's title to move it; **x** hides it. Double-click a quest/reward row for its source.

The offline catalogue covers **PoE1 Acts 1-10** (including campaign mechanic introductions and one cross-act Labyrinth entry) and **PoE2 Acts 1-4 plus Interludes**. It is an overview, not a step-by-step or dependency-solving route: selecting a dependent optional quest does not silently reselect its prerequisites. Requirements are displayed with each quest. Bandit branches describe mutually exclusive choices, not three mandatory kills. PoE2 Act 4 weapon-piece locations can rotate; potentially required island branches remain locked conservatively, and their notes say to follow the character's quest log rather than claim all islands are mandatory. Boss/map objectives that are not quests, endgame/Epilogue quests, retired Cruel repetition and unreleased acts are not included. No quest is automatically marked complete from a level, area or PoB setting.

The quest overlay defaults to **Follow story act**: the selected game's read-only `logs\Client.txt` automatically selects the current act (or PoE2 Interludes), including when the dashboard is hidden. Hideouts and unrecognized zones retain the last recognized act; maps show no campaign quests. This follows area/act changes, not exact quest steps or completion. A missing/unreadable log is reported in the overlay instead of guessing progress. It uses the same game-specific log discovery and `GameLogPath` setting as guide-stage following, without enabling or changing guide-stage following. Pick an act manually to pause following; tick **Follow story act** to resume. Drag an edge/corner or the bottom-right resize grip to change the overlay size. Its dimensions and follow preference are saved separately per game; restored dimensions stay within the screen.

Facts were checked **2026-11-15** against [PoE Wiki's quest index](https://www.poewiki.net/wiki/Quest), its [quest reward data](https://www.poewiki.net/wiki/List_of_quest_rewards) and individual quest/Book of Skill pages; and [PoE2 Wiki's quest index](https://www.poe2wiki.net/wiki/Quest), individual quest and reward-item pages. Numeric alternatives use individual quest pages where aggregate lists disagree (notably Ancient Vows). [PoEDB's Cloven One](https://poedb.tw/us/The_Cloven_One) supplies the optional classification where the wiki infobox disagrees, and [Unearthing the Past](https://poedb.tw/us/Unearthing_the_Past) supplies the Expedition introduction absent from its wiki page. These are concise independently worded factual summaries, not copied walkthroughs or a promise of current-patch completeness. Incomplete item-reward details for PoE2 **Legacy of the Vaal** and **The Search** are explicitly labelled unverified, not guessed; class-dependent gem/equipment choices are summarized by type and linked to the full source. Quest rewards may change with patches. Boss drops are labelled separately from NPC quest rewards; choices never mean every alternative is granted.

### Overlay selector

One global key opens all in-game overlays: **Ctrl+G** by default (**Settings > Keyboard shortcuts > Overlay selector**; set the key to (disabled) to turn it off). A compact chooser appears near the cursor with **1 Gems**, **2 Passives**, **3 Quests**, **4 Recommended equipment** and **5 Update character**; press the number or click. An open overlay is marked "open - hide" and choosing it again hides it. **Update character** opens the dashboard and runs the same **Import via PoB** action: it imports the character selected in the matching PoB into **Current**, shows progress/errors there, and leaves **Guide** unchanged. The existing PoB bridge setup and import safeguards still apply. **Esc** or the selector key closes the chooser and returns focus to the game. The same choices are under **Overlays** in the tray menu. Overlays themselves stay draggable and non-activating, and gem/passive auto-open with marked detection spots still works.

The **Guide stage** dropdown at the top uses the active game's loaded Guide stages (PoB loadouts or PoE2 `.build` level ranges). Choosing a stage keeps the selector open, saves the same selection used by **Build**, and refreshes the stage-dependent gem, passive and equipment overlays on their normal refresh cycle. Changing the stage in **Build** also updates an open selector. **Current** is unchanged. With no readable Guide or no stages the dropdown is disabled; load/reload a Guide in **Build**. Manually changing a stage in either place pauses **Follow game**, so the detected act cannot immediately replace your choice. Tick **Build > Follow game** to resume automatic stages. The quest overlay's separate **Follow story act** setting is unchanged; Guide loadout names are not guessed to be quest acts. Open the stage dropdown to navigate/type stage names; number shortcuts 1-5 resume when the dropdown closes.

The separate gem, passive and quest toggle keys were replaced. On first start the selector takes your previous gem key (or the passive/quest key if that one was disabled) unless it collides with the item-compare or detection-spot keys, and a tray notice shows the result. The old values stay in `config.json`, so nothing is erased. The compare key (**Ctrl+D**) and the detection-spot keys (**Ctrl+Shift+G/P**) are unchanged and remain separate settings.

### Recommended equipment overlay

Selector choice **4** shows the selected **Guide stage's** gear (never the Current character) in an inventory layout: weapons and gloves on the left, helmet/body/belt in the middle, amulet/rings and boots on the right, flasks (PoE1) or charms (PoE2) underneath. **main/swap** switches weapon sets; tree jewels are counted, not drawn. It refreshes when the guide source, stage or game changes. Borders use the item rarity; hover an item for a dark, PoE-style card with a rarity-coloured name/base header, item level, level requirement, quality, sockets/links and separated modifier sections. Normal modifiers are blue, crafted modifiers light blue, and fractured modifiers gold; the source flags and missing-art warnings remain visible.

Art: for PoE1 the overlay reads matching PNGs from local `poe 1 active items` (by base type or unique name) and `poe 1 pic` (by unique name) folders. PoE2 uses its separate `poe_2_images` folder, recursively within equipment categories such as `Gloves`, `Body Armour`, `Warstaff`, `LifeFlask`, `UtilityFlask` (charms) and `Unique`; gem, currency and other non-equipment categories are ignored. Put these folders beside the executable or in its parent (normally the checkout above `bin`); executable-side folders take precedence. Games never borrow each other's art. Names match case-insensitively with underscores/spaces and apostrophes normalised. Bases retain their actual identity; a trailing PoB variant label in parentheses is removed, but tier names are not guessed. Uniques match their own name after removing PoB's base suffix and numeric stage prefix, never their base type's art. Missing or ambiguous matches stay clearly labelled **text only**, with the reason in the hover card. Images are read-only, never copied or bundled. Items that only exist as notes in a `.build` guide are drawn dashed and marked **note**: they are not rolled items and cannot be trade-searched.

**Right-click an item > Search on Trade...** opens a confirmation editor before anything is sent:
- League: type it or press **Load leagues** (public list from the official site, fetched only on that click and cached for the session). The choice is saved per game; nothing is preselected.
- Base type and rarity (rares) or unique name are used; rare names are random and never searched.
- Each guide mod mapped to an official trade stat is a row with its kind (implicit, explicit, crafted, fractured, enchant, rune) and an editable min/max. Rare explicit/implicit rows start enabled at 80% of the guide roll (changeable); crafted, enchant and rune rows start off. Nothing requires an exact all-stat match.
- Item level (affix pool), sockets and links are separate opt-in filters; level requirement is never used as item level.
- Mods that cannot be mapped (PoB metadata such as `Prefix:` lines, ambiguous or unknown wording) are listed as **NOT SEARCHED**. If no stat list is available, or no constraints remain, you must confirm a base-only search.

**Open in browser** opens `https://www.pathofexile.com/trade/search/<league>?q=...` (PoE1) or `https://www.pathofexile.com/trade2/search/poe2/<league>?q=...` (PoE2). Stat IDs come from the `Data\TradeSiteStats.lua` file in your selected Path of Building installation (PoB's copy of the official stat list); no IDs are guessed. This is a link only: the companion does not log in, buy, whisper or scrape results, and the URL format is the site's public search link, not a guaranteed API.

Focused verification: `bin\Poe2ItemCompanion.exe --test-equipment-trade` checks selector migration/routing, guide-only slots and trade queries for both games using throwaway settings.
Focused verification: `bin\Poe2ItemCompanion.exe --test-quest-planner` checks both games' gem instance filtering and quest route UI/persistence using throwaway settings; `--test-ui` also includes both quest overlays.

### One-button PoB character import (local bridge)

**Import via PoB** in the companion main window asks the selected game's PoB to import its already-selected, authenticated character: passive tree/jewels first, then equipment/skills. It preserves PoB's delete-jewels, delete-equipment, delete-skills and ignore-weapon-swap checkboxes. After both imports succeed, PoB saves the open build **only if its existing filename matches CURRENT**, then the companion reloads Current. Guide is unchanged. The button and source/profile-changing controls are disabled while busy.

Use the optional PoB step in the Windows installer, or install the opt-in hook manually with PoB closed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-pob-bridge.ps1 -Poe1Path 'C:\Games\Path of Building Community'
```

Pass `-Poe1Path`, `-Poe2Path`, or both. Add `-ValidateOnly` to check compatibility without applying the bridge. If you use the script directly, set the same paths in the companion's game-specific Settings. Restart each PoB after installing. In its Import/Export tab, authenticate and select the intended character once. Open your saved character build in PoB and choose **that same XML file** as the companion's Current source. Afterwards, **Import via PoB** performs the sequence without coordinate automation or tree-overwrite dialogs. The explicit button authorizes replacement using the existing import checkboxes; it does not authenticate, choose a character, open another build, or accept Save As dialogs for you.

For a targeted repair after a game update, add `-Game poe2` (or `-Game poe1`) to leave the other installation untouched. Updates can remove the Main/Build hooks while leaving the bridge module/key behind; the module alone does not establish an active bridge.

The companion refuses import if CURRENT and the active file-backed GUIDE name the same file, including case/relative-path variants and hardlink aliases. Select a separate CURRENT XML first. This guard runs before networking and again before sending IMPORT; share-code guides do not use an inactive file path.

Run `bin\Poe2ItemCompanion.exe` from the **bridge checkout**, not an older companion in another checkout. The bridge window title ends in **PoB import bridge**, and its tray tooltip ends in **PoB bridge**, in both games and after switching. Switching games recreates the dashboard within the same executable; it does not launch a different companion. If the import button is missing, check the window title and executable path in Task Manager: another companion instance can have the same game selected but no bridge. Renaming a Git branch does not rename its worktree directory.

The installer checks known source anchors and backs up every preexisting source before writing. In each selected installation it changes `Modules\Main.lua` (update-thread hook) and `Modules\Build.lua` (check write/close failures), adds a copy of the bundled payload as `Modules\CompanionBridge.lua`, and creates `Modules\CompanionBridge.key`. The random credential file has an ACL restricted to the installing Windows user, SYSTEM and Administrators; run companion and PoB as that user. Never share or commit the key. Backups stay beside their originals as `.companion-backup-<timestamp>-<id>`; repeat installs also back up an existing bridge module. Setup saves the selected paths in the companion, not in PoB. Neither Setup nor the manual bridge script edits builds or character files.

The dedicated IPv4 listeners bind **127.0.0.1 only**, on **49171 for PoE1** and **49172 for PoE2**. PoB's OAuth receiver/ports are untouched. A fresh Windows-cryptographic challenge and HMAC-SHA256 authenticate both sides and bind commands/statuses to that connection; the bridge key is never sent, and captured requests cannot be replayed on a new connection. The bounded protocol permits only an authenticated `IMPORT` request with a request nonce and the expected Current filename; filenames cannot select a different save destination, arbitrary commands/Lua are rejected, and responses contain only fixed statuses/error codes. API authentication tokens and character payloads stay inside PoB. This is same-user local IPC, not a security boundary against software already running as that user.

PoB consumes requests on its frame thread; asynchronous API responses only queue data for that thread. PoB input is temporarily suspended during the operation. Build/character/options changes, dialogs, invalid data, API errors, disconnects and a 90-second timeout abort without a bridge save; late callbacks are ignored. Imports can leave **unsaved in-memory changes** when a later step fails. A disk write failure can leave a partially written Current file, as with PoB's normal save; the bridge reports failure, not completion. Inspect PoB before retrying after a failed/uncertain acknowledgement. No automatic retry occurs.

Up to eight bounded connections are serviced independently on each frame, so an idle or fragmented unrelated connection does not monopolize the handshake slot. Only one import can run at a time; another authenticated import receives `BUSY` without changing the active import. The original single-client implementation could delay a valid handshake until an idle peer's five-second expiry, racing the companion's five-second authentication deadline.

Both installed SimpleGraphic runtimes (`2.5-3b1a346`) can skip the **entire Lua OnFrame**, not just rendering, when PoB lacks focus, the cursor is elsewhere and no coroutine/subscript is active. A loopback socket does not wake that gate. The bridge retains one yielded coroutine registered through PoB's existing `Modules\Common.lua` tracking, so the engine continues calling the normal UI-thread hook in the background. It does not run imports in a worker or change focus/cursor. When neither foreground nor cursor belongs to the PoB process, polling is paced with a 100 ms sleep (about 10 frames/second); foreground/cursor-over use keeps normal pacing. This trades fully idle frame suspension for bounded background servicing. The installer checks the coroutine tracking and adds a `main:Shutdown()` hook that closes listener/peers, cancels pending callbacks and finishes the coroutine before normal PoB shutdown. Wake/pacing failures log a fixed error and stop the bridge instead of spinning or importing without servicing guarantees.

Reapply the installer and restart PoB yourself to load these corrections; an already-running PoB keeps its old runtime code. Connection saturation or a blocked UI can still cause a bounded timeout. The wake/pacing implementation has isolated coroutine-gate/transport/lifecycle tests, not a live background character-import confirmation.

Connection failures identify the phase and socket error code without logging credentials or payloads. A failure before sending `IMPORT` explicitly says **No import command was sent by this attempt**. A failure while sending it or awaiting completion instead reports that import/save completion is unconfirmed; inspect PoB before retrying. A listening port alone does not establish a responsive handshake. If the local connection is refused or times out despite a running listener, keep PoB responsive and investigate local reachability rather than assuming authentication or Current selection is the cause. Do not automatically retry, restart PoB or disable firewall/security settings.

For a safe reachability check, run `bin\Poe2ItemCompanion.exe --probe-pob-bridge --game poe1` (or `poe2`; optionally append the installation path). This command only sends `HELLO` and verifies PoB's authenticated challenge, then disconnects. It never sends `IMPORT`, loads a user build, saves configuration, or starts/restarts PoB. A successful probe establishes handshake reachability only, not character import/save completion.

**Updates/removal:** PoB updates can remove or invalidate the hook. Nothing silently repatches PoB. Save and close PoB, rerun Setup (or the bridge script), select the affected game, and restart PoB. If an anchor check fails, stop rather than hand-patching unknown code. To remove on the same PoB version, restore the original `Main.lua` and `Build.lua` backups (not backups from a repeat install) and delete only `Modules\CompanionBridge.lua` and `Modules\CompanionBridge.key`. If PoB has updated, restore official files for that version instead of downgrading from old backups. Never remove the module while `Main.lua` still references it.

Run `bin\Poe2ItemCompanion.exe --test-pob-bridge-fixtures` to check the installer script and mock TCP protocol against synthetic fixtures without an installed PoB.

Focused checks: `bin\Poe2ItemCompanion.exe --test-pob-bridge` exercises both installed native import/save routines against throwaway builds, mock TCP client acknowledgements, authentication/build guards, option preservation, errors and fragmented I/O. It also checks real LuaSocket connection lifecycles, idle/fragmented competing peers, concurrent-import refusal/callback isolation, and actual C#-to-LuaSocket authenticated exchanges against build-less fixtures behind a model of the native background-gate predicate using each installed Common coroutine registry. Background fixtures measure 10 frames against a 900-4000 ms elapsed/under-400 ms CPU budget; they do not launch the real renderer or measure its rendering cost. Installer fixtures check shutdown insertion/reapply, source backups and unchanged credentials. Network test listeners use ephemeral ports, not the live PoB ports. Optional arguments are existing PoE1 and PoE2 XML files to verify read-only Current load/reload. `--test-ui` checks the button/layout/busy state, and `--test-dual-game` covers shared-engine behavior. These are **offline integration tests, not a live authenticated API end-to-end confirmation**; a restarted PoB with the user's selected character and matching Current is required for that final check.

### Desktop appearance

The desktop uses an obsidian-and-brass workbench theme: a custom vector sigil and masthead, serif section titles, alternating character-stat rows, gold primary actions, and layered dark surfaces. The shared palette also styles Current/Guide views, Settings, code import, comparison popups, diagnostic logs and checklist overlays. Stat, rarity and checklist colors retain their meanings. All artwork is drawn locally, with no downloaded assets or additional dependencies. The dashboard opens at 1240 × 900 and supports resizing down to 960 × 740.

The Build Skills tab and gem overlay can display your local gem PNGs. In **Settings > Gem image directory**, select a folder for the selected game (for example `C:\Games\PoE\poe1_gems` for PoE1), then save. With that field empty, the app looks for `poe 1 gem` / `poe1_gems` for PoE1 and `poe 2 gem` / `poe2_gems` for PoE2, beside the executable first, then in its parent directory (the repository root when running from `bin`). Within each location, the space-named folder takes precedence if both spellings exist. Each game only discovers its own folders; an explicitly configured path always takes precedence. Relative configured paths resolve from the executable directory. No artwork is downloaded or bundled.

PNGs in the selected folder and all its subfolders are indexed, including category folders such as `skill_gems`, `support_gems` and `transfigured_skill_gems`. Names match case-insensitively, with underscores treated as spaces and an optional ` inventory icon` filename suffix removed. For example `Fireball.png` and `Fireball_inventory_icon.png` match Fireball individually; if both exist anywhere in the folder tree, the ambiguous match falls back. For PoE1 gems identified as supports by PoB metadata, ` Support` is appended to the lookup name unless already present: Faster Attacks then matches `Faster Attacks Support inventory icon.png`. No suffix is stripped or variant guessed: Barrage artwork is not used for Barrage Support, nor Ice Nova for Ice Nova of Frostbolts. PoE2 name matching and its separately configured folder are unchanged. PoE1 assets are not automatically reused in PoE2. Local folders may contain outdated or incorrect artwork; filename matches are not a guarantee of game identity or complete coverage.

Only matching local pictures are drawn; missing, ambiguous or unreadable images leave the icon area empty, with load errors logged. No placeholder gem symbols are shown. Border colors follow PoB's gem color or dominant attribute where available; gray means unknown. Images preserve aspect ratio and are cached for each view without keeping source files locked; save Settings again or restart the app after changing image files. Names and status remain text, and the Skills list supports keyboard selection and Ctrl+C. This works offline and sends no build data or image requests to a service. Images remain user-local and are neither copied nor bundled; users remain responsible for their asset permissions.

Source controls and Settings use grouped, resizing layouts. Current and Guide remain separate, long source paths can be inspected by hovering, and Settings scrolls on smaller windows while its action buttons stay visible. Tabs retain native keyboard navigation; overlays retain their non-activating, draggable behavior. Windows system-DPI awareness is enabled at startup; this is not per-monitor DPI support, and title bars and window borders use dark Windows 11 caption colors. Scrollbars and dropdown arrows are skinned in the shared palette while retaining native scrolling and keyboard behavior. Tray submenus and text-edit context menus use the same dark styling. If Windows caption coloring is unavailable, a dark frame is drawn over the native caption; window buttons retain their native actions.

Run `bin\Poe2ItemCompanion.exe --test-ui` for isolated theme/layout checks for both games, including text contrast (at least 4.5:1 for the shared normal/secondary/accent text palette), minimum/default/large window layouts and a simulated 150% layout scale. This harness uses synthetic content and throwaway settings, not user builds or configuration. Add a screenshot directory as the next argument to save UI snapshots for visual inspection.

---

## Key Features (PoE2 examples)

The walkthrough below retains the existing PoE2 examples. Shared features use the selected engine in PoE1; gem-cutter tiers and `.build` instructions apply only to PoE2, as described above.

- **Visual Companion Dashboard Window**: An interactive desktop window showing what build is currently loaded, full character stats, quick-switch build dropdown, reload button, and live item comparison tester.
- **Actual PoB2 Calculation Engine**: Current-gear stat differences use PoB2's `calcFunc` routines, not approximations. The separately labeled guide fit is a heuristic.
- **Source Protection**: Comparisons use a temporary calc environment (`repSlotName` / `repItem`). No automatic equip or save; explicit export writes a separate new build file, and the opt-in PoB import button saves only the matching Current file.
- **Two Independent Sources**: The top **GUIDE code / link** box or **Guide file...** sets the desired build. **Current character file**, **Use my PoE2 character**, or **Import Current character code...** sets the comparison baseline. Changing either retains the other. Resolved guide codes are cached in `config.json`.
- **Current Character Tab**: Current-character summary, calculation assumptions, equipped gear with raw item text, allocated passives and a zoomable passive tree. No leveling order is invented.
- **Build Tab (Guide)**: Dedicated target-build view with guide summary/calculated stats/config, equipment and jewels with raw item text, saved skill groups/gem levels/quality/enabled state, allocated passives and a tree overview. The guide loads in a separate engine and never replaces the Current character baseline. Use **Reload guide** to refresh a saved file. A **Stage (PoB2 loadout)** dropdown lists exactly the loadouts Path of Building 2 shows (e.g. `Act 1 {1}` ... `Min-Max {8}`, built from the `{n}` tags on tree/item/skill/config sets) and activates them with PoB2's own loadout logic. The guide opens on its saved sets, as in PoB2. The chosen stage drives the Build tab and the guide item used for the stat asterisks; it is remembered per guide and never changes the guide source or the Current character. Other weapon-set items are labeled; missing gems, jewels or tree data are explicitly reported. Website-only interactive planner information and leveling order are not inferred.
- **Stage from the game log**: The Build tab reads PoE2's `logs\Client.txt` read-only and uses only zone-change and level-up lines; chat is ignored. The log is found via the PoE2 install location in the registry, or the `GameLogPath` setting in `config.json`. Zones `G1`–`G4` map to Act 1–4, `P1`–`P3` to the Interlude, and maps or the endgame town to Endgame. In maps, your character level picks the Early, Mid or Endgame stage: Mid starts at level `StageMidLevel` (default 80) and Endgame at `StageLateLevel` (default 90). A Min-Max stage is never suggested. Click **Use suggested stage**, or tick **Follow game** to switch automatically. A manual stage change in Build or the overlay selector pauses following until you enable it again. Hideouts and league areas keep the previous stage. Until you level up once in the current log, the character level is unknown.
- **Gem cutting** (Build tab sub-tab): Lists every gem used by the selected guide stage, with columns for:
  - Gem type: Skill, Spirit or Support.
  - The PoB2 tier, shown as the Roman numeral from the in-game gem cutting window.
  - The weapon tab, taken from the gem's weapon requirement.
  - The uncut gem to use: an uncut gem of level N lists every gem up to tier N, and a cut skill gem gets the uncut gem's level.
  - Whether your Current character already has the gem. Missing gems are shown in gold.

  Spells with no weapon requirement have no tab in PoB2's data, so use the in-game search box. Tier 0 gems can't be cut from an uncut gem; they are usually granted by items.
- **In-game gem overlay**: Press the overlay selector key and choose **1 Gems**, or use the tray menu item **Overlays > Gems overlay**, to show a small always-on-top panel over the game. Like **Build > Skills**, it is grouped per skill: a bold heading with the skill (`[main]` for the guide's main skill), then the skill gem and the support gems that go with it. Each row shows:
  - `+` in gold: missing from your Current character
  - `^` in orange: you have it at a lower level than the guide
  - grey `(have)`: already owned
  - the gem tier and the gem-cutter tab to open

  By default only skill groups that still need a gem are shown; click **all** to show every group. The list uses the guide's selected stage, so it follows **Follow game** stage changes and saves of your Current character file. Drag the panel to move it; the position is remembered. The overlay never takes keyboard focus from the game; PoE2 must run in windowed or borderless fullscreen mode.
- **Auto-open with the gem cutting window**: PoE2 writes nothing to its log when you open gem cutting, so the app recognises the window on screen instead. Teach it once: open the gem cutting window, hover the mouse over a part that never changes (for example the "Type keywords here..." search box or the window title) and press **Ctrl+Shift+G**. That small area (140x36 pixels) is saved to `bin\gemdetect`; up to 3 spots are kept, and marking more replaces the oldest. From then on, while PoE2 is the active window, the app compares those spots twice a second and opens the overlay when they match, then hides it again when the gem cutting window closes (an overlay you opened yourself through the selector stays open). Toggle this or clear the spots from the tray menu. This only takes screenshots of those small areas, with no text recognition and no reading of game memory. All marked spots must match, so the overlay no longer opens with other screens such as the passive tree, and it stays hidden while the passive tree is detected. Plain dark areas (no text or frame) are refused because they also match other screens; spots saved before this rule are ignored, and the tray menu shows how many. If you move or resize the game window or change the UI scale, clear the spots and mark them again.
- **In-game passive overlay**: Press the overlay selector key and choose **2 Passives**, or use the tray menu item **Overlays > Passives overlay**, to show what to allocate next. The app compares the guide's selected stage tree with your Current character's tree (both use the same PoB2 node IDs) and lists, step by step:
  - **Next: <notable/keystone/jewel socket>**: the nearest one you don't have yet, with the small passives on the way, in the order you click them (the gold row is the target; each row shows the node's stats)
  - the ascendancy points still to take
  - guide nodes that do not connect to your current tree, and nodes you have that this stage doesn't use (shown in red; refund them later if you need the points)

  The guide has no levelling order, so the order is a suggestion: the shortest path from your tree to the nearest guide notable, repeated. By default the first 4 steps are shown; click **all** for every step. The overlay is only as current as your Current character file, so re-import the character in PoB2 after levelling and the overlay refreshes on save.
- **Auto-open with the passive tree**: Same as for gem cutting. Open the passive tree, hover over a part that never changes (for example the points counter or the title area at the top) and press **Ctrl+Shift+P**. The overlay then opens with the tree and hides when you close it. Toggle this or clear its spots from the tray menu. It also only takes screenshots of those small areas.
- Crafting/item planning and in-game passive-tree highlight rings are not included for either game. The in-app passive tree, passive checklist overlay, and normal gem/passive screen detection remain available.
- **Global Hotkey Activation**: Configurable hotkey (default: `Ctrl+D`) with automatic Ctrl+C item capture and clipboard restoration.
- **Smart Slot Detection**: Automatically identifies matching equipment slots (Boots, Helmet, Body Armour, Gloves, Rings, Amulet, Weapons, Belts) and compares against currently equipped items. Dual slots (such as Ring 1 and Ring 2) show comparisons for both options.
- **Compact In-Game Overlay**: Dark-themed, borderless, topmost tooltip positioned near the mouse cursor displaying:
  - Candidate item name and rarity
  - Target equipment slot
  - Replaced item name and rarity
  - PoB color-coded stat changes (^x33FF77 green for gains, ^xDD0022 red for losses)
- **Automatic Build Reload**: Detects when your saved build XML has been updated on disk in PoB and reloads it automatically (file source only; share codes are static snapshots — load a new code after changing the build).
- **Diagnostic Logging**: Dedicated log file (`logs\companion.log`) records every initialization, item copy, Lua calculation, and error so you can easily see what went wrong.
- **Windows System Tray Integration**: Sits unobtrusively in the system tray with easy access to Settings, Sample Previews, Build Reload, View Log File, and Exit.

---

## Requirements

- **Windows 10 / 11 (64-bit)**
- **Path of Building Community** installed for the game you select (both installations for switching between games)
- A separate saved/imported Current character snapshot and optional Guide for each game
- .NET Framework 4.8 (standard on all modern Windows installations)

---

## Quick Start

This repository contains source, scripts, offline datasets and synthetic test fixtures.
Compiled executables, local configuration, character/build XMLs, exports, backups,
logs and detection screenshots are intentionally excluded. Build locally and import
your own Current character and Guide sources; installed Path of Building engines
are not bundled.

### 1. Build
Run `build.bat` in this folder:
```cmd
build.bat
```
This compiles the C# source files using Windows `.NET csc.exe` into `bin\Poe2ItemCompanion.exe`.

### Package a Windows release

`VERSION` is the single source for the app version, Setup version and release tag (`v<VERSION>`). Install [Inno Setup 6.7.3 or later](https://jrsoftware.org/isdl.php) and run these commands from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\test.ps1
```

The smoke test requires a Windows account without an existing installed copy. It uses synthetic PoB folders and an isolated user-data directory. `installer\Companion.iss` explicitly lists distributed files. Only publish the tested `bin\installer\PoE-Item-Companion-Setup-v<version>-x64.exe` from the audited source revision on `main`, as a stable GitHub Release with the matching tag. Do not upload portable build folders, user data or external PoB files.

### 2. Run Verification Tests
For isolated dual-game fixtures against both installed engines, including profile migration and repeated live UI switches:
```cmd
bin\Poe2ItemCompanion.exe --test-dual-game
```

To run the automated test suite verifying PoB2 engine loading, build XML parsing, and live item stat comparison:
```cmd
run_tests.bat
```
or:
```cmd
bin\Poe2ItemCompanion.exe --test
```

For focused local PoB2 Current/Guide source/tab, guide-stage and `.build` note regression coverage:
```cmd
bin\Poe2ItemCompanion.exe --test-sources
```

### 3. Launch Companion
Run `run.bat` or double-click `bin\Poe2ItemCompanion.exe`.
An icon will appear in your Windows System Tray (notification area).

---

## Configuration & Usage

### First Time Setup
1. Choose **Path of Exile 1** or **Path of Exile 2** in the dashboard or tray menu, then open **Settings...**
2. Verify that game's **Path of Building Folder** and **Build directory**
3. Set your desired build with the dashboard's **GUIDE code / link** box or **Guide file...**, and inspect it in **Build**. Separately import your **Current character file/code** in the dashboard; Settings' file selector is guide-only.
4. Set your preferred **Global Hotkey** (default: `Ctrl + D`).
5. Click **Test Compare** to verify your build and preview a sample calculation.
6. Click **Save & Apply**. Switching games later restores that game's settings and sources.

The executable name remains `Poe2ItemCompanion.exe` for launcher compatibility. To select a profile from the command line, use `run.bat --game poe1` or `bin\Poe2ItemCompanion.exe --game poe2 --compare <itemFile>`. `--game` can accompany GUI launch, comparison or export; it explicitly selects and remembers that profile.

### Read-only source regression tests

`bin\Poe2ItemCompanion.exe --test-sources` verifies independent Current/Guide loading, Build-tab content, guide stages and `.build` notes, including unchanged comparison baselines and source/config bytes. The configured guide link must already be resolved for offline stage tests.

### In-Game Usage
1. Open the selected **Path of Exile** game (windowed or borderless fullscreen).
2. Hover your mouse over any item (in inventory, vendor, ground, or stash).
3. Press **Ctrl + D** (or your configured hotkey).
4. The comparison popup will appear near your cursor.
5. Press **Escape**, click anywhere outside the popup, or wait for the auto-dismiss timer to close it.

### Comparing against your current PoE2 character
The comparison baseline must be explicitly supplied separately from the guide. An account-import marker is shown in green; manually supplied Current character snapshots without that marker are labeled unverified in orange. A marker indicates import history, not proof that the saved gear is still current.

Prerequisite: the companion does **not** log in to your Path of Exile account itself. Grinding Gear Games' official API requires OAuth with a client registered to the application. The only registered client available is Path of Building's own, and reusing it would impersonate PoB. The companion never asks for your password or session cookie. Instead:
1. In Path of Building 2, open **Import/Export Build > Character import** and click **Authorize with Path of Exile** (official login in your browser).
2. Import your character's items and passive tree, then **save** the build in PoB2.
3. In the companion dashboard, click **Use my PoE2 character** and confirm the suggested file. It suggests the newest character-imported build (`lastCharacterHash` marker); check that it is the correct character. Alternatively, browse to the correct **Current character file**, or use **Import Current character code...** for a generated PoB2 code.

The baseline is your gear as of the last import/save in PoB2. Re-import after changing gear. Never paste your character code into the GUIDE box: that box is only for desired gear/build goals. Existing guide settings are retained on upgrade; a separate Current character source is required rather than guessing that a guide represents your gear.

### Guide-relative fit (not universal BIS)
Each slot shows the candidate item's modifier lines with a colored **asterisk (`*`) beside each stat**, compared to the guide item in the same slot:

| Asterisk | Meaning |
|---|---|
| Red | Stat is not present on that guide item (not necessarily useless to the whole build) |
| Orange | Matching stat is below 90% of the guide item's value |
| Green | Matching stat reaches 90-100% of the guide item's value |
| Gold | Matching stat exceeds the guide item's value |
| Gray `?` | No guide item, unsupported modifier, or an ambiguous comparison |

Hover over the stat or marker for its rating and guide value. Implicit, explicit, rune and enchant modifier lines come from PoB2's parser with active variants/ranges resolved. Comparisons match modifier wording, preserving conditions and units. Unconditional additive elemental resistance and attribute lines also match their combined forms (e.g. fire resistance versus all-elemental resistance). A combined candidate line with only some component stats present in the guide is marked unrated with a partial-match explanation. Familiar beneficial numeric modifiers are rated by magnitude; an exact supported modifier match is green. Conditional/inverse/mixed effects with different values, repeated stat lines and catalyst-scaled modifiers are left unrated rather than guessed. Base item headers such as quality, requirements and item level are not affixes and are not rated. For multi-number damage rolls, every number must meet the threshold; gold requires no value below the guide.

The dense calculated-total gap summary is no longer displayed in the dashboard/popup; it remains in CLI JSON's `GuideFit` for diagnostics, alongside structured `ItemStats`. Below the item rows, real PoB stat deltas still use the **Current character** skills, tree and configuration. The colors describe same-slot guide-item fit, not universal BIS or overall build usefulness. A missing/invalid guide leaves current-gear deltas available and item stats unrated; a missing Current character baseline blocks comparison/export.

### Using a PoE2 `.build` file (Maxroll / in-game Build Planner) as the Guide
Load the file via **Build tab > Load .build guide...**, or select it in the **Guide file...** dialog, which accepts `*.xml;*.build`. The file is opened read-only, and the Current character baseline is unchanged.

To get a `.build` file:
- **Maxroll:** in the PoE2 planner, use its export/download option for the GGG Build Planner `.build` format.
- **In-game planner:** copy the file from `%USERPROFILE%\Documents\My Games\Path of Exile 2\BuildPlanner`. Builds subscribed at pathofexile2.com/my-account/builds sync there.

How the companion uses the file:
- Each `level_interval` boundary becomes a level-range stage, such as `Level 1-40` or `Level 41+`. The game-log stage suggestion picks the stage for your character level.
- The selected stage's passives (including weapon sets) and gems are converted in memory and calculated by PoB2.
- Unknown JSON fields are preserved and listed.

Limitations:
- The format carries **unique item names and rare-item base/stat-priority notes, not full rare rolls**. Guide-fit against these notes is a keyword **heuristic**, labelled as such, and slots without a note are unrated. Real stat deltas still come from PoB2 against the Current character.
- Gems use the highest level allowed at the stage's level, with no quality.
- Passives the companion cannot map are listed as unmapped. Passives that PoB2 drops because they are not connected to the class start are also listed.
- Stages are level ranges, not named PoB loadouts. Guide stats include tree and gems only, without rare gear.

### Exporting a build with the candidate item
After a comparison, click **Export** (hotkey popup) or **Export as new PoB2 build...** (dashboard) for a slot. PoB2 then equips the item in the **Current character snapshot**, serialises the result, and reloads it to validate. The result is written as a **new** file, by default in `bin\exports\`. The companion never overwrites existing files, the source build, the imported character or the PoB2 installation. To open the file in PoB2, either save or copy it into PoB2's build folder as a new file, or use the offered import code (**Import/Export Build > Import from code**).

### Equipping into your Current character build
**Equip** (hotkey popup) or **Equip into Current build...** (dashboard) is the one action that changes a source file, and only after you confirm. It works only when the Current character is a build **file** (not an import code) and never touches the Guide.
1. The original file is copied to `bin\backups\<name> <timestamp>.xml`.
2. The new build is produced by PoB2's own serializer and replaces the file.
3. The file is reloaded in PoB2 to verify the item is equipped. If that fails, the original is restored.

Comparisons then use the updated gear. **Path of Building 2 does not reload open builds when their file changes**, so reopen the build in PoB2 to see the item. If the build is open in PoB2, don't save it there first, or PoB2 overwrites the change. To equip live in an open PoB2 without the companion, paste the copied item (Ctrl+V) in PoB2's Items tab.

---

## Command Line Interface (CLI)

The companion executable also supports direct CLI commands for scripting and testing:

| Command | Description |
|---|---|
| `Poe2ItemCompanion.exe` | Launches GUI system tray mode |
| `Poe2ItemCompanion.exe --test` | Runs automated end-to-end verification suite |
| `Poe2ItemCompanion.exe --compare [item_file_or_text]` | Compares against the configured Current character snapshot; JSON includes PoB deltas and guide-relative fit |
| `Poe2ItemCompanion.exe --export <item_file> <slot> [out_file]` | Writes a NEW copy of the Current character build with the item equipped in `<slot>` (e.g. `Boots`, `Ring 1`) |
| `Poe2ItemCompanion.exe --log` | Opens the diagnostic log file (`logs\companion.log`) in Notepad |
| `Poe2ItemCompanion.exe --help` | Displays help and usage information |

---

## Troubleshooting & Logs

If something goes wrong (e.g. an item does not compare, hotkey doesn't trigger, or build fails to load):
1. **Open the Log File**:
   - Right-click the system tray icon and click **View Log File...**
   - Or click **View Log...** in the Settings window.
   - Or run `bin\Poe2ItemCompanion.exe --log`
   - Or directly open `bin\logs\companion.log`.
2. **What the Log Records**:
   - Engine initialization steps and Lua module loading.
   - Build file path, character level, main skill, and load status.
   - Hotkey registration and Win32 errors if a conflict exists.
   - Ctrl+C clipboard capture results and item text.
   - Detailed Lua/PoB parser warnings if an item structure or base is unrecognized.
   - Any unexpected exceptions with full stack traces.

---

## Architecture

```
poe-item-companion\
├── bin\
│   ├── Poe2ItemCompanion.exe    # Compiled standalone Windows executable
│   ├── config.json              # Saved configuration
│   ├── logs\
│   │   └── companion.log        # Diagnostic log file
│   └── scripts\
│       └── pob_wrapper.lua      # Lua bridge running inside PoB2's LuaJIT environment
├── scripts\
│   └── pob_wrapper.lua          # Headless PoB2 initialization & calc wrapper
├── src\
│   ├── AppConfig.cs             # JSON configuration persistence & build auto-detection
│   ├── ComparisonPopup.cs       # WinForms borderless overlay with PoB color parsing
│   ├── HotkeyManager.cs         # Win32 RegisterHotKey message window
│   ├── InputSimulator.cs        # Win32 SendInput Ctrl+C item text extractor
│   ├── Logger.cs                # Diagnostic file logger with auto-rotation
│   ├── LogViewerForm.cs         # Built-in dark-themed log viewer (no Notepad dependency)
│   ├── MainWindow.cs            # Interactive visual dashboard showing active build & live preview
│   ├── PobLuaEngine.cs          # Thread-safe C# P/Invoke bridge into PoB's lua51.dll
│   ├── PobStatParser.cs         # Parser for PoB ^xRRGGBB and ^0-^9 color format
│   ├── SettingsForm.cs          # GUI settings dialog with live preview & log viewer
│   ├── TrayApplicationContext.cs# Windows System Tray coordinator
│   └── Program.cs               # Entry point supporting GUI tray, --test, and --compare
├── samples\
│   └── ruby_ring.txt            # Sample PoE2 item for testing
├── build.bat                    # One-click build script
├── run.bat                      # Launch companion in system tray
├── run_tests.bat                # Automated verification script
├── NOTICE.md                    # Upstream Path of Building MIT notice & attribution
└── README.md                    # Documentation
```

### Technical Notes
- **In-Process LuaJIT**: Communicates with `lua51.dll` from the installed Path of Building 2 installation using native P/Invoke. No separate heavy OpenGL GUI instance of PoB is spawned.
- **Headless Stubs**: Provides lightweight stubs for SimpleGraphic C functions so PoB modules run completely headless.
- **Safe Multi-Threading**: All Lua calls are protected by synchronization locks and execute inside PoB's directory context to preserve asset and data loading integrity.
