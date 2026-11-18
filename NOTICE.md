# Third-Party Notices and Attribution

This project uses the user's installed **Path of Building Community (PoE1 and PoE2)** engines. Comparisons are read-only; separately confirmed exports/source writes use the selected engine.

### Path of Building Community (PoE1)
* Upstream Project: [Path of Building Community](https://github.com/PathOfBuildingCommunity/PathOfBuilding)
* License: MIT License (see the installed/upstream project's LICENSE for its notices).

The MIT permission and warranty disclaimer reproduced below also describe the license terms under which this engine is distributed. The companion does not redistribute the user's installation.

### Path of Building Community (PoE2)
* Upstream Project: [Path of Building Community](https://github.com/PathOfBuildingCommunity/PathOfBuilding-PoE2)
* License: MIT License

Copyright (c) 2016-2024 Path of Building Community Authors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

### LuaJIT
* Copyright (C) 2005-2023 Mike Pall.
* Released under the MIT license.

### PoENavi gem acquisition data
* Source: https://github.com/buri34/poenavi/blob/e5fba514418413fd0ac4d919971d931098470085/data/gems.json
* Copyright (c) 2026 Buri_Isono; MIT License.
* The complete license is included in `data\poenavi-LICENSE.txt` and copied alongside the bundled dataset.
* The companion adapts quest-giver metadata to actual town purchase vendors, filters by Current class, and includes Lilly's ordinary-gem access after Fallen from Grace. This is an offline snapshot, not live quest-state or vendor inventory data.

### Campaign quest facts
* `data\campaign_quests.json` contains independently worded factual summaries checked 2026-11-15 against https://www.poewiki.net/wiki/Quest and https://www.poe2wiki.net/wiki/Quest, their individual quest/reward-item pages, and PoE Wiki's `quest_rewards` data.
* PoEDB sources resolve missing/conflicting entries: https://poedb.tw/us/The_Cloven_One and https://poedb.tw/us/Unearthing_the_Past.
* Each record links to its game-specific wiki page; the two PoEDB exceptions are identified in the record's requirement text and in the README. No wiki walkthrough prose, images or dialogue is bundled. Unknown reward details are labelled, and all facts remain patch-dependent.

### Official trade site search links and item art
* Trade stat IDs are read at runtime from `Data\TradeSiteStats.lua` in the user's installed Path of Building Community (PoE1/PoE2) - PoB's MIT-licensed copy of the official trade stat list. The companion bundles no copy of that list.
* "Search on Trade" only opens a public search link on https://www.pathofexile.com/trade or https://www.pathofexile.com/trade2 in the user's browser after the user confirms the filters. The optional public league list is fetched only when the user presses **Load leagues**. No account data, build data or credentials are sent, and no listings are fetched, bought or messaged. Path of Exile and its trade site are owned by Grinding Gear Games; this project is not affiliated with or endorsed by them.
* Item pictures in the Recommended equipment overlay are read from game-specific, user-supplied local folders (`poe 1 active items` / `poe 1 pic` for PoE1; equipment categories within `poe_2_images` for PoE2) and are neither copied nor bundled. Users remain responsible for their asset permissions.