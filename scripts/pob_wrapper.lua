-- PoB2 Wrapper for PoE2 Item Comparison Companion
-- Executes within LuaJIT (lua51.dll) hosted by the companion app.
-- Interfaces with Path of Building Community (PoE2) modules read-only.

local pobCompanion = {}
local json = nil
local currentBuild = nil
local currentBuildXml = nil
local currentBuildPath = nil
local game = "poe2"

local function sanitizePath(path)
    if not path:match("[\\/]$") then
        path = path .. "/"
    end
    return path:gsub("\\", "/")
end

function pobCompanion.init(pobPath, selectedGame)
    game = selectedGame or "poe2"
    pobPath = sanitizePath(pobPath)
    
    package.path = pobPath .. "?.lua;" .. pobPath .. "?/init.lua;" .. pobPath .. "lua/?.lua;" .. pobPath .. "lua/?/init.lua;" .. package.path
    package.cpath = pobPath .. "?.dll;" .. package.cpath

    APP_NAME = game == "poe1" and "Path of Building" or "Path of Building (PoE2)"
    arg = {}

    -- Stubs for GUI and graphics APIs required by PoB
    function ConPrintf(fmt, ...) end
    function ConPrintTable() end
    function ConClear() end
    function GetTime() return os.clock() * 1000 end
    function SetWindowTitle() end
    function ConExecute() end
    function RenderInit() end
    function SetMainObject() end
    function IsKeyDown() return false end
    function Copy() end
    function Paste() return "" end
    function Deflate() return "" end
    local ffi = require("ffi")
    ffi.cdef[[
        int uncompress(unsigned char *dest, unsigned long *destLen,
            const unsigned char *source, unsigned long sourceLen);
    ]]
    local zlib = ffi.load(pobPath .. "zlib1.dll")
    function Inflate(compressed)
        local size = math.max(1024 * 1024, #compressed * 2)
        while size <= 512 * 1024 * 1024 do
            local buffer = ffi.new("unsigned char[?]", size)
            local length = ffi.new("unsigned long[1]", size)
            local status = zlib.uncompress(buffer, length, compressed, #compressed)
            if status == 0 then return ffi.string(buffer, tonumber(length[0])) end
            if status ~= -5 then error("PoB compressed data could not be inflated (zlib status " .. status .. ")") end
            size = size * 2
        end
        error("PoB compressed data exceeds the 512 MiB headless decompression limit")
    end
    function GetScriptPath() return pobPath end
    function GetRuntimePath() return pobPath end
    function GetUserPath() return pobPath end
    function GetVirtualScreenSize() return 1920, 1080 end
    function GetScreenSize() return 1920, 1080 end
    function GetScreenScale() return 1 end
    function GetCursorPos() return 0, 0 end
    function SetCursorPos() end
    function ShowCursor() end
    function DrawString() end
    function DrawStringWidth() return 50 end
    function DrawStringCursorIndex() return 0 end
    function SetDrawColor() end
    function GetDrawColor() return 1, 1, 1, 1 end
    function DrawImage() end
    function DrawImageQuad() end
    function SetViewport() end
    function SetDrawLayer() end
    function GetDrawLayer() return 0 end
    function SetBlendMode() end
    function SetClearColor() end
    function SetDPIScaleOverridePercent() end
    function GetDPIScaleOverridePercent() return 100 end
    function MakeDir(dir) end
    function RemoveDir(dir) end
    function SetWorkDir(dir) end
    function GetWorkDir() return pobPath end
    function OpenURL() end
    function SpawnProcess() end
    function SetProfiling() end
    function TakeScreenshot() end
    function Restart() end
    function Exit() end
    function SetForeground() end

    function StripEscapes(s)
        if not s then return "" end
        return (s:gsub("%^%d", ""):gsub("%^x%x%x%x%x%x%x", ""):gsub("%^x%x%x%x%x%x", ""))
    end

    function NewImageHandle()
        return {
            Load = function() end,
            ImageSize = function() return 100, 100 end,
            Unload = function() end,
            IsValid = function() return true end,
            SetLoadingPriority = function() end,
        }
    end

    -- Headless stand-in for PoB's Launch.lua. PoB reports problems (e.g. XML parse errors in
    -- Build:LoadDB) via launch:ShowErrMsg / ShowPrompt, which normally pop up a dialog. Here we
    -- record the message so the companion can show the real reason instead of a nil-method crash.
    launch = {
        devMode = false,
        installedMode = true,
        subScripts = {},
        lastErrMsg = nil,
    }
    function launch:ShowErrMsg(fmt, ...)
        local ok, msg = pcall(string.format, fmt, ...)
        msg = tostring(ok and msg or fmt):gsub("%^x%x%x%x%x%x%x", ""):gsub("%^%d", "")
        self.lastErrMsg = msg
        if ConPrintf then pcall(ConPrintf, "PoB error: %s", msg) end
    end
    function launch:ShowPrompt(r, g, b, str, func)
        self:ShowErrMsg("%s", tostring(str))
    end
    function launch:ApplyUpdate() end

    local function resolvePath(name)
        local path = pobPath .. name
        if not path:match("%.lua$") then
            path = path .. ".lua"
        end
        return path
    end

    function LoadModule(name, ...)
        if name == "Modules/DataJewelFileLoader" then
            -- Use the installed lookup data without PoB's GUI file-search API or disk cache writes.
            return function(jewelTypeName)
                if not jewelTypeName:match("^[%w]+$") then error("Invalid jewel lookup data name") end
                local path = pobPath .. "Data/TimelessJewelData/" .. jewelTypeName
                local chunks = {}
                local file = io.open(path .. ".zip", "rb")
                if file then
                    chunks[1] = file:read("*a")
                    file:close()
                else
                    local part = 0
                    while true do
                        file = io.open(path .. ".zip.part" .. part, "rb")
                        if not file then break end
                        chunks[#chunks + 1] = file:read("*a")
                        file:close()
                        part = part + 1
                    end
                end
                if #chunks == 0 then error("Missing installed jewel lookup data: " .. jewelTypeName) end
                return Inflate(table.concat(chunks))
            end
        end
        local path = resolvePath(name)
        local fn, err = loadfile(path)
        if not fn then
            error("LoadModule failed for " .. name .. ": " .. tostring(err))
        end
        return fn(...)
    end

    function PLoadModule(name, ...)
        local path = resolvePath(name)
        local fn, err = loadfile(path)
        if not fn then
            return tostring(err)
        end
        local res = { pcall(fn, ...) }
        if not res[1] then
            return tostring(res[2])
        end
        table.remove(res, 1)
        return nil, unpack(res)
    end

    function PCall(fn, ...)
        local res = { pcall(fn, ...) }
        if not res[1] then
            return tostring(res[2])
        end
        table.remove(res, 1)
        return nil, unpack(res)
    end

    -- Load PoB Main module and initialize core environment
    LoadModule("Modules/Main")
    main:Init()

    json = require("dkjson")
    return true
end

local function arr(t)
    return setmetatable(t or {}, { __jsontype = "array" })
end

local function clean(s)
    if s == nil then return nil end
    s = tostring(s)
    return (s:gsub("%^x%x%x%x%x%x%x", ""):gsub("%^%d", ""))
end

local function getMainSkillInfo()
    local skillName, partName, groupLabel = "Unknown", nil, nil
    local env = currentBuild.calcsTab and currentBuild.calcsTab.mainEnv
    local mainSkill = env and env.player and env.player.mainSkill
    if mainSkill and mainSkill.activeEffect and mainSkill.activeEffect.grantedEffect then
        local ge = mainSkill.activeEffect.grantedEffect
        skillName = ge.name or skillName
        if ge.parts and mainSkill.skillPart and ge.parts[mainSkill.skillPart] then
            partName = ge.parts[mainSkill.skillPart].name
        end
    end
    local skillsTab = currentBuild.skillsTab
    if skillsTab and skillsTab.socketGroupList and currentBuild.mainSocketGroup then
        local group = skillsTab.socketGroupList[currentBuild.mainSocketGroup]
        if group then groupLabel = group.displayLabel or group.label end
    end
    return skillName, partName, groupLabel
end

local function fitStats(output)
    local stats = {}
    for _, key in ipairs({ "Life", "EnergyShield", "Armour", "Evasion", "FireResist", "ColdResist",
        "LightningResist", "ChaosResist", "Str", "Dex", "Int", "EffectiveMovementSpeedMod" }) do
        if type(output[key]) == "number" then stats[key] = output[key] end
    end
    return stats
end

local function buildSummary(cached)
    local skillName = getMainSkillInfo()
    local mainOutput = (currentBuild.calcsTab and currentBuild.calcsTab.mainOutput) or {}
    return {
        success = true,
        cached = cached,
        characterName = currentBuild.buildName,
        level = currentBuild.characterLevel,
        className = currentBuild.spec and currentBuild.spec.curClassName or currentBuild.className,
        ascendClassName = currentBuild.spec and currentBuild.spec.curAscendClassName or currentBuild.ascendClassName,
        mainSkill = skillName,
        guideTargets = fitStats(mainOutput),
        baseline = {
            TotalDPS = mainOutput.TotalDPS,
            CombinedDPS = mainOutput.CombinedDPS,
            FullDPS = mainOutput.FullDPS,
            Life = mainOutput.Life,
            EnergyShield = mainOutput.EnergyShield,
            TotalEHP = mainOutput.TotalEHP,
            FireResist = mainOutput.FireResist,
            ColdResist = mainOutput.ColdResist,
            LightningResist = mainOutput.LightningResist,
            ChaosResist = mainOutput.ChaosResist
        }
    }
end

function pobCompanion.loadBuild(xmlText, buildName)
    if not xmlText or xmlText == "" then
        return json.encode({ success = false, error = "Build XML content is empty" })
    end
    
    if currentBuildXml == xmlText and currentBuild then
        if buildName then currentBuild.buildName = buildName end
        return json.encode(buildSummary(true))
    end

    buildName = buildName or "ActiveBuild"
    launch.lastErrMsg = nil
    local success, err = xpcall(function()
        if main.mode then main:CallMode("Shutdown") end
        -- A previous failed Init may leave partially populated tabs in the reusable build mode.
        main.modes["BUILD"] = LoadModule("Modules/Build")
        main:SetMode("BUILD", false, buildName, xmlText)
        main.mode = main.newMode
        main.newMode = nil
        main:CallMode("Init", unpack(main.newModeArgs))
        currentBuild = main.modes["BUILD"]
        currentBuildXml = xmlText
    end, debug.traceback)

    if launch.lastErrMsg then
        -- PoB rejected the build (LoadDB returned an error and closed the build).
        local msg = launch.lastErrMsg
        launch.lastErrMsg = nil
        currentBuild = nil
        currentBuildXml = nil
        return json.encode({ success = false, error = "Path of Building could not load this build: " .. msg })
    end

    if not success or not currentBuild then
        currentBuild = nil
        currentBuildXml = nil
        return json.encode({ success = false, error = "Failed to load build: " .. tostring(err) })
    end

    -- Builds converted from a GGG .build carry no gem levels (level="0"): let PoB2 choose the level
    -- its "characterLevel" default would give at this character level, then recalculate.
    local fixOk, fixErr = pcall(function()
        local fixed = false
        for _, group in ipairs(currentBuild.skillsTab.socketGroupList or {}) do
            for _, gem in ipairs(group.gemList or {}) do
                if gem.level == 0 then
                    gem.level = currentBuild.skillsTab:ProcessGemLevel(gem.gemData)
                    fixed = true
                end
            end
        end
        if fixed then
            if wipeGlobalCache then wipeGlobalCache() end
            currentBuild.outputRevision = (currentBuild.outputRevision or 0) + 1
            currentBuild.buildFlag = false
            currentBuild.calcsTab:BuildOutput()
        end
    end)
    if not fixOk then
        currentBuild = nil
        currentBuildXml = nil
        return json.encode({ success = false, error = "Failed to set gem levels: " .. tostring(fixErr) })
    end

    return json.encode(buildSummary(false))
end

local function itemAffixes(item)
    local lines = {}
    if not item then return arr(lines) end
    local groups = { { item.runeModLines or {}, "rune" }, { item.enchantModLines or {}, "enchant" },
        { item.implicitModLines or {}, "implicit" }, { item.explicitModLines or {}, "explicit" } }
    for _, entry in ipairs(groups) do
        local group, kind = entry[1], entry[2]
        for _, mod in ipairs(group) do
            if item:CheckModLineVariant(mod) then
                local text = itemLib.applyRange(mod.line, mod.range or main.defaultItemAffixQuality, 1, mod.corruptedRange)
                if not text:match("^%s*Dex:") and not text:match("^%s*Str:") and not text:match("^%s*Int:")
                    and not text:match("^%s*Level:") and not text:match("^%s*Requirements:") then
                    table.insert(lines, {
                        text = clean(text),
                        understood = not mod.extra and mod.modList ~= nil and #mod.modList > 0,
                        scalable = not item.catalyst,
                        kind = kind,
                        crafted = mod.crafted and kind == "explicit" or false,
                        fractured = mod.fractured and true or false,
                        exarch = mod.exarch and true or false,
                        eater = mod.eater and true or false,
                        synthesis = mod.synthesis and true or false
                    })
                end
            end
        end
    end
    return arr(lines)
end

local function socketNotice(item, slotName)
    if game ~= "poe1" then return nil end
    local requirements = {}
    local colours = { "R", "G", "B" }
    for _, group in ipairs(currentBuild.skillsTab.socketGroupList or {}) do
        if group.slot == slotName and group.enabled ~= false and not group.source then
            local need = { R = 0, G = 0, B = 0, W = 0 }
            for _, gem in ipairs(group.gemList or {}) do
                local effect = gem.grantedEffect or (gem.gemData and gem.gemData.grantedEffect)
                if effect and gem.enabled ~= false then
                    local colour = colours[effect.color] or "W"
                    need[colour] = need[colour] + 1
                end
            end
            if need.R + need.G + need.B + need.W > 0 then table.insert(requirements, need) end
        end
    end
    if #requirements == 0 then
        return "PoE1 calculation uses saved skill groups and item-granted effects. Socket/link compatibility is not certified."
    end
    local pools = {}
    for _, socket in ipairs(item.sockets or {}) do
        if socket.color ~= "A" then
            local key = socket.group or 0
            pools[key] = pools[key] or { R = 0, G = 0, B = 0, W = 0 }
            pools[key][socket.color] = (pools[key][socket.color] or 0) + 1
        end
    end
    local function assign(index)
        if index > #requirements then return true end
        local need = requirements[index]
        for key, pool in pairs(pools) do
            local deficit, count = 0, 0
            for _, colour in ipairs(colours) do
                deficit = deficit + math.max(0, need[colour] - pool[colour])
                count = count + need[colour]
            end
            if deficit <= pool.W and count + need.W <= pool.R + pool.G + pool.B + pool.W then
                pools[key] = nil
                if assign(index + 1) then pools[key] = pool; return true end
                pools[key] = pool
            end
        end
        return false
    end
    if not assign(1) then
        return "SOCKET/LINK WARNING: candidate does not show separate linked colour groups for the saved skills in this slot. PoB numerical deltas assume those configured skills; they do not certify this item can host them. Check in game before equipping."
    end
    return "Candidate has matching linked colour groups for saved skills. Item-granted supports, requirements and skill usability still need verification."
end

function pobCompanion.compareItem(itemRawText)
    if not currentBuild then
        return json.encode({ success = false, error = "No build currently loaded" })
    end
    if not itemRawText or itemRawText:match("^%s*$") then
        return json.encode({ success = false, error = "Item text is empty" })
    end

    local newItem = nil
    local parseSuccess, parseErr = pcall(function()
        newItem = new("Item", itemRawText)
    end)

    if not parseSuccess or not newItem or not newItem.name then
        local errDetail = (parseErr and tostring(parseErr)) or "Item name or structure invalid"
        return json.encode({
            success = false,
            error = "Failed to parse item. Ensure clipboard contains valid Path of Exile item text (" .. errDetail .. ")."
        })
    end

    if not newItem.base then
        return json.encode({
            success = false,
            error = "Item base '" .. tostring(newItem.baseName or newItem.name) .. "' is not recognized in Path of Building 2 (PoE2)."
        })
    end

    local itemsTab = currentBuild.itemsTab
    if not itemsTab then
        return json.encode({ success = false, error = "Build has no items tab" })
    end

    local calcFunc, calcBase = currentBuild.calcsTab:GetMiscCalculator()
    if not calcFunc or not calcBase then
        return json.encode({ success = false, error = "Failed to obtain calculator from build" })
    end

    local comparisons = {}

    -- Find matching slots
    local slotOrder = {
        "Weapon 1", "Weapon 2", "Weapon 1 Swap", "Weapon 2 Swap",
        "Helmet", "Body Armour", "Gloves", "Boots",
        "Amulet", "Ring 1", "Ring 2", "Belt"
    }

    local activeWeaponSet = (itemsTab.activeItemSet and itemsTab.activeItemSet.useSecondWeaponSet) and 2 or 1

    local function evaluateSlot(slotName, slot)
        if not slot or slot.inactive then return end
        if slot.weaponSet and slot.weaponSet ~= activeWeaponSet then return end
        if slot.shown and not slot.shown() then return end
        
        local valid = false
        local ok, res = pcall(function() return itemsTab:IsItemValidForSlot(newItem, slotName) end)
        if ok and res then valid = true end
        if not valid then return end

        local selItem = itemsTab.items[slot.selItemId]
        local calcOk, output = pcall(function()
            return calcFunc({ repSlotName = slotName, repItem = newItem })
        end)
        if not calcOk or not output then return end

        local tooltip = new("Tooltip")
        local header = "Equipping this in " .. (slot.label or slotName) .. (selItem and ("\n(replacing " .. selItem.name .. ")") or "")
        local addOk = pcall(function()
            currentBuild:AddStatComparesToTooltip(tooltip, calcBase, output, header)
        end)
        if not addOk then return end

        local lines = {}
        for _, line in ipairs(tooltip.lines) do
            local text = line[1] or line.text or ""
            table.insert(lines, text)
        end

        table.insert(comparisons, {
            compatibilityNotice = socketNotice(newItem, slotName),
            slotName = slotName,
            slotLabel = slot.label or slotName,
            replacedItemName = selItem and selItem.name or "(Empty)",
            replacedItemRarity = selItem and selItem.rarity or "NORMAL",
            before = fitStats(calcBase),
            after = fitStats(output),
            lines = lines
        })
    end

    -- Process in standard slot order first
    local seenSlots = {}
    for _, name in ipairs(slotOrder) do
        local slot = itemsTab.slots[name]
        if slot then
            seenSlots[name] = true
            evaluateSlot(name, slot)
        end
    end

    -- Process any additional slots (e.g. Jewels, Flasks, etc. if applicable)
    for name, slot in pairs(itemsTab.slots) do
        if not seenSlots[name] then
            evaluateSlot(name, slot)
        end
    end

    if #comparisons == 0 then
        return json.encode({
            success = false,
            error = "Item '" .. newItem.name .. "' (" .. tostring(newItem.base and newItem.base.type or "Unknown") .. ") does not fit any active equipment slots in this build."
        })
    end

    return json.encode({
        success = true,
        itemName = newItem.name,
        itemRarity = newItem.rarity or "RARE",
        itemBase = newItem.base and newItem.base.name or nil,
        itemType = newItem.base and newItem.base.type or nil,
        affixes = itemAffixes(newItem),
        comparisons = comparisons
    })
end

local function forceReload(xmlText, buildName)
    currentBuildXml = nil
    return json.decode(pobCompanion.loadBuild(xmlText, buildName))
end

-- Produces NEW build XML: the loaded build with the candidate item equipped in slotName, using
-- PoB2's own Item parser, ItemsTab equip logic and Build:SaveDB serializer. Nothing is written to
-- disk here, and the original build is reloaded afterwards so the companion keeps comparing against
-- the unmodified source build. The generated XML is re-loaded once to validate it.
function pobCompanion.exportWithCandidate(itemRawText, slotName)
    if not currentBuild or not currentBuildXml then
        return json.encode({ success = false, error = "No build currently loaded" })
    end
    local origXml = currentBuildXml
    local origName = currentBuild.buildName or "ActiveBuild"
    local result = { success = false }

    local ok, err = pcall(function()
        local itemsTab = currentBuild.itemsTab
        local slot = itemsTab.slots[slotName]
        if not slot then error("Unknown slot '" .. tostring(slotName) .. "'") end
        local item = new("Item", itemRawText)
        if not item.base then error("Item base is not recognized by Path of Building 2") end
        if not itemsTab:IsItemValidForSlot(item, slotName) then error("Item does not fit slot " .. slotName) end
        local replaced = itemsTab.items[slot.selItemId]
        itemsTab:AddItem(item, true)
        slot:SetSelItemId(item.id)
        itemsTab:PopulateSlots()
        currentBuild.buildFlag = true
        local xml = currentBuild:SaveDB("companion-export")
        if not xml then error(launch.lastErrMsg or "PoB2 SaveDB failed") end
        result.xml = xml
        result.itemName = item.name
        result.slotName = slotName
        result.slotLabel = slot.label or slotName
        result.replacedItemName = replaced and replaced.name or "(Empty)"
    end)

    -- Always restore the untouched source build.
    local restored = forceReload(origXml, origName)
    if not ok then
        return json.encode({ success = false, error = "Export failed: " .. tostring(err) })
    end
    if not restored or not restored.success then
        return json.encode({ success = false, error = "Export created but restoring the source build failed" })
    end

    -- Validate: PoB2 can load the generated XML and the candidate is equipped in the chosen slot.
    local check = forceReload(result.xml, origName .. " (export check)")
    local equippedName = nil
    if check and check.success and currentBuild then
        local s = currentBuild.itemsTab.slots[slotName]
        local it = s and currentBuild.itemsTab.items[s.selItemId]
        equippedName = it and it.name
        result.validatedLevel = currentBuild.characterLevel
        local out = currentBuild.calcsTab and currentBuild.calcsTab.mainOutput or {}
        result.validatedLife = out.Life or 0
        result.validatedTotalDPS = out.TotalDPS or 0
    end
    local finalRestore = forceReload(origXml, origName)
    if not finalRestore or not finalRestore.success then
        return json.encode({ success = false, error = "Restoring the Current character source build after validation failed" })
    end
    if not check or not check.success then
        return json.encode({ success = false, error = "Generated build failed to reload in PoB2: " .. tostring(check and check.error) })
    end
    if equippedName ~= result.itemName then
        return json.encode({ success = false, error = "Validation failed: slot " .. slotName .. " contains '" .. tostring(equippedName) .. "' after reload" })
    end
    result.validated = true
    result.success = true
    return json.encode(result)
end

-- Where the loaded build came from, as recorded by PoB2 itself (Import tab attributes).
function pobCompanion.getImportInfo()
    if not currentBuild or not currentBuild.importTab then
        return json.encode({ success = false })
    end
    local it = currentBuild.importTab
    return json.encode({
        success = true,
        importedCharacter = it.lastCharacterHash ~= nil,
        realm = it.lastRealm,
        league = it.lastLeague,
        importLink = it.importLink or currentBuild.importLink
    })
end

-- PoB2 loadouts (the "Loadouts" dropdown, e.g. "Act 1 {1}" ... "Min-Max {8}"), discovered with
-- PoB2's own SyncLoadouts so the companion lists exactly what Path of Building shows.
local function loadoutNames()
    local names = arr()
    if not currentBuild then return names end
    currentBuild:SyncLoadouts()
    local control = currentBuild.controls and currentBuild.controls.buildLoadouts
    for _, v in ipairs(control and control.list or {}) do
        if type(v) == "string" and v ~= "No Loadouts" and not v:find("^%^7%^7") then
            table.insert(names, v)
        end
    end
    return names
end

local function resolveLoadout(name)
    if game == "poe2" then return currentBuild:GetLoadoutByName(name) end
    local link = name:match("%{(%w+)%}")
    local function find(order, sets, links)
        if #order == 1 then return order[1] end
        if link and links[link] then return links[link].setId end
        for _, id in ipairs(order) do
            if (sets[id].title or "Default") == name then return id end
        end
    end
    local specId
    if link and currentBuild.treeListSpecialLinks[link] then
        specId = currentBuild.treeListSpecialLinks[link].setId
    else
        for id, title in ipairs(currentBuild.treeTab:GetSpecList()) do
            if title == name then specId = id end
        end
    end
    return {
        specId = specId,
        itemSetId = find(currentBuild.itemsTab.itemSetOrderList, currentBuild.itemsTab.itemSets, currentBuild.itemListSpecialLinks),
        skillSetId = find(currentBuild.skillsTab.skillSetOrderList, currentBuild.skillsTab.skillSets, currentBuild.skillListSpecialLinks),
        configSetId = find(currentBuild.configTab.configSetOrderList, currentBuild.configTab.configSets, currentBuild.configListSpecialLinks)
    }
end

local function activeLoadoutName(names)
    for _, n in ipairs(names) do
        local l = resolveLoadout(n)
        if l and l.specId == currentBuild.treeTab.activeSpec
            and l.itemSetId == currentBuild.itemsTab.activeItemSetId
            and l.skillSetId == currentBuild.skillsTab.activeSkillSetId
            and l.configSetId == currentBuild.configTab.activeConfigSetId then
            return n
        end
    end
    return nil
end

function pobCompanion.listLoadouts()
    if not currentBuild then
        return json.encode({ success = false, error = "No build currently loaded" })
    end
    local names = loadoutNames()
    return json.encode({ success = true, loadouts = names, activeLoadout = activeLoadoutName(names) })
end

-- Switches the loaded build to a PoB2 loadout (tree, item, skill and config set together) in memory
-- and recalculates, exactly like choosing it in PoB2's Loadouts dropdown. Nothing is saved.
function pobCompanion.setLoadout(name)
    if not currentBuild then
        return json.encode({ success = false, error = "No build currently loaded" })
    end
    local names = loadoutNames()
    local found = false
    for _, n in ipairs(names) do if n == name then found = true end end
    if not found then
        return json.encode({ success = false, error = "Loadout '" .. tostring(name) .. "' does not exist in this build" })
    end
    local ok, err = pcall(function()
        local loadout = resolveLoadout(name)
        if not loadout or not loadout.specId then error("PoB could not resolve the loadout sets") end
        if game == "poe1" then
            local control = currentBuild.controls.buildLoadouts
            for index, value in ipairs(control.list) do
                if value == name then control.selFunc(index, value); break end
            end
        else
            currentBuild:SetActiveLoadout(loadout)
        end
        if wipeGlobalCache then wipeGlobalCache() end
        currentBuild.outputRevision = (currentBuild.outputRevision or 0) + 1
        currentBuild.buildFlag = false
        currentBuild.calcsTab:BuildOutput()
        if activeLoadoutName(names) ~= name then error("PoB did not activate all sets for the selected loadout") end
    end)
    -- The in-memory build no longer matches the source XML's saved state; force a real reload next time.
    currentBuildXml = nil
    if not ok then
        return json.encode({ success = false, error = "Switching loadout failed: " .. tostring(err) })
    end
    local summary = buildSummary(false)
    summary.activeLoadout = activeLoadoutName(names)
    if summary.activeLoadout ~= name then
        return json.encode({ success = false, error = "PoB2 did not activate loadout '" .. name .. "'" })
    end
    return json.encode(summary)
end

local configVarList = nil

-- Read-only export of the loaded build for the Build tab: gear, allocated passive tree and the
-- calculation assumptions (main skill, active sets, non-default configuration).
function pobCompanion.exportBuildDetails()
    if not currentBuild then
        return json.encode({ success = false, error = "No build currently loaded" })
    end

    local result = { success = true, game = game }
    local skillName, partName, groupLabel = getMainSkillInfo()
    local spec = currentBuild.spec
    local itemsTab = currentBuild.itemsTab
    local configTab = currentBuild.configTab
    local treeTab = currentBuild.treeTab
    result.treeVersion = spec and spec.treeVersion

    result.buildName = currentBuild.buildName
    result.level = currentBuild.characterLevel
    result.className = spec and spec.curClassName or currentBuild.className
    result.ascendClassName = spec and spec.curAscendClassName or currentBuild.ascendClassName
    result.mainSkill = skillName
    result.loadouts = loadoutNames()
    result.activeLoadout = activeLoadoutName(result.loadouts)
    result.skills = arr()
    for index, group in ipairs(currentBuild.skillsTab and currentBuild.skillsTab.socketGroupList or {}) do
        local gems = arr()
        for _, gem in ipairs(group.gemList or {}) do
            local gd = gem.gemData
            local kind = nil
            if gd then
                local tags = gd.tags or {}
                if (gd.grantedEffect and gd.grantedEffect.support) or tags.support then
                    kind = "Support"
                elseif tags.persistent and gd.Tier and gd.Tier > 0 and gd.Tier % 2 == 0 then
                    kind = "Spirit"
                else
                    kind = "Skill"
                end
            end
            table.insert(gems, {
                color = gd and ((game == "poe1" and gd.grantedEffect and gd.grantedEffect.color)
                    or ((gd.reqStr or 0) > (gd.reqDex or 0) and (gd.reqStr or 0) > (gd.reqInt or 0) and 1)
                    or ((gd.reqDex or 0) > (gd.reqStr or 0) and (gd.reqDex or 0) > (gd.reqInt or 0) and 2)
                    or ((gd.reqInt or 0) > (gd.reqStr or 0) and (gd.reqInt or 0) > (gd.reqDex or 0) and 3)) or 0,
                requiredLevel = game == "poe1" and gd and gd.grantedEffect and gd.grantedEffect.levels
                    and gd.grantedEffect.levels[1] and gd.grantedEffect.levels[1].levelRequirement or nil,
                targetRequiredLevel = game == "poe1" and gd and gd.grantedEffect and gd.grantedEffect.levels
                    and gd.grantedEffect.levels[gem.level or 1] and gd.grantedEffect.levels[gem.level or 1].levelRequirement or nil,
                id = gem.gemId or (gd and gd.id),
                variant = gem.variantId or (gd and gd.variantId),
                name = clean(gem.nameSpec or (gd and gd.name) or gem.skillId or "Unknown gem"),
                level = gem.level,
                quality = gem.quality,
                enabled = gem.enabled ~= false,
                tier = gd and gd.Tier or nil,
                kind = kind,
                gemType = gd and clean(gd.gemType) or nil,
                weaponRequirements = gd and clean(gd.weaponRequirements) or nil,
                tags = gd and clean(gd.tagString) or nil
            })
        end
        table.insert(result.skills, {
            label = clean(group.displayLabel or group.label or ""),
            slot = clean(group.slot),
            enabled = group.enabled ~= false,
            main = index == currentBuild.mainSocketGroup,
            gems = gems
        })
    end

    -- Calculation assumptions
    local assumptions = arr()
    local function addA(label, value)
        if value ~= nil and value ~= "" then
            table.insert(assumptions, { label = label, value = clean(value) })
        end
    end
    addA("Loadout", result.activeLoadout or (#result.loadouts > 0 and "None matching (saved active sets)" or nil))
    addA("Main skill group", groupLabel)
    addA("Main skill", skillName)
    addA("Skill part", partName)
    if itemsTab and itemsTab.activeItemSet then
        addA("Item set", itemsTab.activeItemSet.title or "Default")
        addA("Weapon set", itemsTab.activeItemSet.useSecondWeaponSet and "Weapon set 2" or "Weapon set 1")
    end
    if treeTab and treeTab.specList and treeTab.activeSpec and treeTab.specList[treeTab.activeSpec] then
        addA("Passive tree", treeTab.specList[treeTab.activeSpec].title or "Default")
    end
    if spec and spec.treeVersion then addA("Tree version", spec.treeVersion) end
    if configTab and configTab.configSets and configTab.activeConfigSetId and configTab.configSets[configTab.activeConfigSetId] then
        addA("Config set", configTab.configSets[configTab.activeConfigSetId].title or "Default")
    end
    if configTab and configTab.enemyLevel then addA("Enemy level", configTab.enemyLevel) end
    if currentBuild.targetVersion then addA("Game version", currentBuild.targetVersion) end
    result.assumptions = assumptions

    -- Non-default configuration inputs (exactly what PoB uses for every calculation)
    local configList = arr()
    if configTab and configTab.input then
        if not configVarList then
            local ok, vl = pcall(LoadModule, "Modules/ConfigOptions")
            if ok and type(vl) == "table" then configVarList = vl end
        end
        local defaults = configTab.defaultState or {}
        local section = ""
        local seen = {}
        for _, varData in ipairs(configVarList or {}) do
            if varData.section then
                section = varData.section
            elseif varData.var and not seen[varData.var] then
                local value = configTab.input[varData.var]
                if value ~= nil and value ~= defaults[varData.var] and value ~= false then
                    seen[varData.var] = true
                    local display = value
                    if varData.type == "list" and varData.list then
                        for _, entry in ipairs(varData.list) do
                            if entry.val == value then display = entry.label break end
                        end
                    elseif value == true then
                        display = "Yes"
                    end
                    table.insert(configList, {
                        section = clean(section),
                        label = clean(varData.label or varData.var),
                        value = clean(display)
                    })
                end
            end
        end
    end
    result.config = configList

    -- Key stats as calculated by PoB for the build as-is
    local statKeys = {
        { "Life", "Life" }, { "EnergyShield", "Energy Shield" }, { "Mana", "Mana" }, { "Spirit", "Spirit" },
        { "TotalEHP", "Effective Hit Pool" }, { "Armour", "Armour" }, { "Evasion", "Evasion" },
        { "FireResist", "Fire Resistance" }, { "ColdResist", "Cold Resistance" },
        { "LightningResist", "Lightning Resistance" }, { "ChaosResist", "Chaos Resistance" },
        { "BlockChance", "Block Chance" }, { "TotalDPS", "Hit DPS" }, { "CombinedDPS", "Combined DPS" },
        { "AverageHit", "Average Hit" }, { "Speed", "Attack/Cast Rate" }, { "CritChance", "Crit Chance" },
        { "FullDPS", "Full DPS (configured groups)" }, { "SpellSuppressionChance", "Spell Suppression Chance" },
        { "ManaUnreserved", "Unreserved Mana" }, { "Ward", "Ward" },
        { "Str", "Strength" }, { "Dex", "Dexterity" }, { "Int", "Intelligence" },
    }
    local mainOutput = (currentBuild.calcsTab and currentBuild.calcsTab.mainOutput) or {}
    local stats = arr()
    for _, sk in ipairs(statKeys) do
        local v = mainOutput[sk[1]]
        if type(v) == "number" then
            table.insert(stats, { key = sk[1], label = sk[2], value = v })
        end
    end
    result.stats = stats

    -- Equipped gear (active item set)
    local gear = arr()
    if itemsTab and itemsTab.slots then
        local order = {
            "Weapon 1", "Weapon 2", "Weapon 1 Swap", "Weapon 2 Swap", "Helmet", "Body Armour", "Gloves",
            "Boots", "Amulet", "Ring 1", "Ring 2", "Ring 3", "Belt",
        }
        local seen = {}
        local function addSlot(name, slot)
            if seen[name] or not slot then return end
            seen[name] = true
            local item = slot.selItemId and slot.selItemId ~= 0 and itemsTab.items[slot.selItemId] or nil
            local mainSlot = false
            for _, n in ipairs(order) do if n == name then mainSlot = true end end
            if not item and not mainSlot then return end
            if not item and slot.shown and not slot.shown() then return end
            local raw = nil
            local socketCount, maxLinks, influences = 0, 0, arr()
            if item then
                local ok, r = pcall(function() return item:BuildRaw() end)
                raw = (ok and r) or item.raw
                local groupSizes = {}
                for _, socket in ipairs(item.sockets or {}) do
                    socketCount = socketCount + 1
                    local key = socket.group or 0
                    groupSizes[key] = (groupSizes[key] or 0) + 1
                    if groupSizes[key] > maxLinks then maxLinks = groupSizes[key] end
                end
                if type(influenceInfo) == "table" then
                    for _, info in ipairs(influenceInfo) do
                        if info.key and item[info.key] then table.insert(influences, info.display or info.key) end
                    end
                end
            end
            table.insert(gear, {
                slotName = name,
                slotLabel = slot.label or name,
                itemName = item and item.name or nil,
                rarity = item and item.rarity or nil,
                baseName = item and item.baseName or nil,
                raw = clean(raw),
                affixes = itemAffixes(item),
                itemLevel = item and tonumber(item.itemLevel) or nil,
                levelRequirement = item and item.requirements and tonumber(item.requirements.level) or nil,
                quality = item and tonumber(item.quality) or nil,
                socketCount = socketCount,
                maxLinks = maxLinks,
                corrupted = item and item.corrupted and true or false,
                mirrored = item and item.mirrored and true or false,
                influences = influences,
                itemType = item and (item.type or (item.base and item.base.type)) or nil,
                inactiveWeaponSet = (slot.weaponSet and itemsTab.activeItemSet and ((itemsTab.activeItemSet.useSecondWeaponSet and 2 or 1) ~= slot.weaponSet)) and true or false,
                isJewel = item and item.type == "Jewel" or false
            })
        end
        for _, n in ipairs(order) do addSlot(n, itemsTab.slots[n]) end
        local rest = {}
        for n in pairs(itemsTab.slots) do if not seen[n] then table.insert(rest, n) end end
        table.sort(rest)
        for _, n in ipairs(rest) do addSlot(n, itemsTab.slots[n]) end
    end
    result.gear = gear

    -- Passive tree: every drawable node + edges, with the existing allocation flags
    local nodes, edges = arr(), arr()
    if spec and spec.nodes then
        local ascend = spec.curAscendClassName
        local included = {}
        for id, node in pairs(spec.nodes) do
            local skip = (not node.x) or (not node.y) or node.type == "OnlyImage" or node.isProxy
                or (node.group and node.group.isProxy)
                or (node.ascendancyName and node.ascendancyName ~= ascend)
            if not skip then
                included[id] = true
                local alloc = spec.allocNodes and spec.allocNodes[id] ~= nil
                local shown = (alloc and spec.hashOverrides and spec.hashOverrides[id]) or node
                local entry = {
                    id = id,
                    x = node.x,
                    y = node.y,
                    type = node.type or "Normal",
                    name = clean(shown.dn or shown.name),
                    alloc = alloc,
                    ascendancy = node.ascendancyName and true or false,
                    weaponSet = alloc and node.allocMode or 0,
                    masteryEffect = spec.masterySelections and spec.masterySelections[id] or nil,
                    graphContext = node.group and node.group.id and tostring(node.group.id) or nil,
                }
                if node.type == "Mastery" and alloc and spec.masterySelections then
                    local effect = spec.tree.masteryEffects and spec.tree.masteryEffects[spec.masterySelections[id]]
                    if effect then shown = effect end
                end
                if node.id and node.id >= 65536 then
                    local context = {}
                    for socketId, itemId in pairs(spec.jewels or {}) do
                        local item = currentBuild.itemsTab.items[itemId]
                        if item and item.jewelData and item.jewelData.clusterJewelValid then
                            table.insert(context, tostring(socketId) .. ":" .. item:BuildRaw())
                        end
                    end
                    table.sort(context)
                    entry.graphContext = table.concat(context, "|")
                end
                if alloc and shown.sd then
                    local sd = arr()
                    for _, line in ipairs(shown.sd) do table.insert(sd, clean(line)) end
                    entry.stats = sd
                end
                table.insert(nodes, entry)
            end
        end
        for id, node in pairs(spec.nodes) do
            if included[id] then
                local links = node.linked or {}
                for _, other in ipairs(links) do
                    if other.id and included[other.id] and id < other.id then
                        table.insert(edges, arr({ id, other.id }))
                    end
                end
            end
        end
    end
    result.tree = { nodes = nodes, edges = edges }

    return json.encode(result)
end
_G.pobCompanion = pobCompanion
return pobCompanion
