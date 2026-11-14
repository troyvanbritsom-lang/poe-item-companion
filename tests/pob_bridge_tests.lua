return function(root, pob1, pob2, scratch)
	local realOpen = io.open
	local realSocket = require("socket")
	local json = require("dkjson")
	local sha = require("sha2")
	local count = 0
	local key, nonce = string.rep("a", 64), string.rep("b", 64)
	local path = scratch .. "\\current.xml"
	local function hex(text)
		return (text:gsub(".", function(char) return string.format("%02x", char:byte()) end))
	end
	local function requireThat(value, message)
		assert(value, message)
		count = count + 1
	end
	local function fixture(name)
		return { name = name, class = "Witch", league = "Standard", level = 40,
			equipment = {}, skills = {}, jewels = {}, passives = {
				hashes = {}, hashes_ex = {}, jewel_data = {}, mastery_effects = {},
				specialisations = {}, skill_overrides = {}, quest_stats = {}, alternate_ascendancy = 0,
			} }
	end
	requireThat(sha.hmac(sha.sha256, "key", "The quick brown fox jumps over the lazy dog") ==
		"f7bc83f430538424b13298e6aa6fb143ef4d59a14946175997479dbc2d1a3cd8", "Lua HMAC-SHA256 interoperability vector failed")
	local function setup(game, realBuild)
		local env = { now = 0, wire = "", callbacks = {}, calls = {}, chunks = {} }
		local character = fixture("BridgeFixture")
		local client = {}
		function client:settimeout(value) requireThat(value == 0, "Socket must not block UI") end
		function client:receive()
			if env.disconnected then return nil, "closed", "" end
			return nil, "timeout", table.remove(env.chunks, 1) or ""
		end
		function client:send(text)
			local size = env.partialSend and math.min(3, #text) or #text
			env.wire = env.wire .. text:sub(1, size)
			if size < #text then return nil, "timeout", size end
			return size
		end
		function client:close() env.closed = true end
		local listener = {}
		function listener:settimeout(value) requireThat(value == 0, "Listener must not block UI") end
		function listener:bind(host, port)
			requireThat(host == "127.0.0.1", "Listener exposed to non-loopback interface")
			requireThat(port == (game == "poe1" and 49171 or 49172), "Wrong game port")
			return true
		end
		function listener:listen() return true end
		function listener:accept()
			if env.accepted then return nil, "timeout" end
			env.accepted = true
			return client
		end
		function listener:close() end
		package.loaded.socket = { tcp4 = function() return listener end, gettime = function() return env.now end }
		io.open = function(file, mode)
			if file:match("CompanionBridge.key$") then
				return { read = function() return key end, close = function() return true end }
			end
			if realBuild and env.failDisk and file == path and mode == "w+" then
				return { write = function() return nil, "disk full" end, close = function() return nil, "disk full" end }
			end
			if realBuild then return realOpen(file, mode) end
			return { close = function() return true end }
		end
		GetScriptPath = function() return game == "poe1" and pob1 or pob2 end
		ConPrintf = function() end
		local tab
		local build
		if realBuild then
			build, tab = realBuild, realBuild.importTab
			tab.usingOauth, tab.charImportMode = true, "SELECTCHAR"
			tab.isAuthorized = function() return true end
			for _, option in ipairs({ "charImportTreeClearJewels", "charImportItemsClearItems",
				"charImportItemsClearSkills", "charImportItemsIgnoreWeaponSwap" }) do
				tab.controls[option].state = false
			end
			tab.controls.charImportItemsIgnoreWeaponSwap.state = true
			tab.controls.charSelect.GetSelValue = function() return { label = character.name, char = character } end
			local importTree, importItems, save = tab.ImportPassiveTreeAndJewels, tab.ImportItemsAndSkills, build.SaveDBFile
			env.nativeTree, env.nativeItems, env.nativeSave = importTree, importItems, save
			tab.ImportPassiveTreeAndJewels = function(self, data, clear)
				requireThat(env.inTick, "Native tree import outside update thread")
				table.insert(env.calls, "TREE")
				return importTree(self, data, clear)
			end
			tab.ImportItemsAndSkills = function(self, data, clearItems, clearSkills, swap)
				requireThat(env.inTick, "Native items import outside update thread")
				table.insert(env.calls, "ITEMS")
				return importItems(self, data, clearItems, clearSkills, swap)
			end
			build.SaveDBFile = function(self)
				requireThat(env.inTick, "Native save outside update thread")
				table.insert(env.calls, "SAVE")
				return save(self)
			end
		else
			tab = { controls = {}, usingOauth = true, isAuthorized = function() return true end,
				charImportMode = "SELECTCHAR" }
			for _, option in ipairs({ "charImportTreeClearJewels", "charImportItemsClearItems",
				"charImportItemsClearSkills", "charImportItemsIgnoreWeaponSwap" }) do
				tab.controls[option] = { state = false }
			end
			tab.controls.charImportItemsIgnoreWeaponSwap.state = true
			tab.controls.charSelect = { GetSelValue = function() return { label = character.name, char = character } end }
			tab.controls.accountRealm = { GetSelValue = function()
				return { id = game == "poe1" and "PC" or "PoE2", realmCode = game == "poe1" and "pc" or "poe2" }
			end }
			tab.controls.charSelectLeague = { GetSelValue = function() return "Standard" end }
			function tab:ImportPassiveTreeAndJewels(data, clear)
				requireThat(env.inTick, "Tree import ran outside UI tick")
				requireThat(clear == false, "Clear-jewels option lost")
				if env.throwTree then error("private exception details") end
				table.insert(env.calls, "TREE")
			end
			function tab:ImportItemsAndSkills(data, items, skills, swap)
				requireThat(env.inTick, "Items import ran outside UI tick")
				requireThat(items == false and skills == false and swap == true, "Item/skill/weapon-swap options lost")
				if env.throwItems then error("private exception details") end
				table.insert(env.calls, "ITEMS")
			end
			function tab:SaveApiSettings() env.savedSettings = true end
			build = { dbFileName = path, importTab = tab, spec = {}, itemsTab = {}, skillsTab = {} }
			function build:SaveDBFile()
				requireThat(env.inTick, "Save ran outside UI tick")
				table.insert(env.calls, "SAVE")
				if env.saveError then return true end
			end
		end
		local main = realBuild and _G.main or { mode = "BUILD", modes = { BUILD = build }, popups = {} }
		main.api = { authToken = "never-sent", DownloadCharacter = function(self, realm, name, callback)
			table.insert(env.callbacks, callback)
		end }
		common = common or {}
		common.sha1 = common.sha1 or function() return "hash" end
		env.main, env.tab, env.build, env.character = main, tab, build, character
		env.bridge = assert(loadfile(root .. "\\scripts\\pob_import_bridge.lua"))(game)
		function env:request(secret, requestedPath, command, requestedGame)
			self.secret, self.requestedPath = secret or key, requestedPath or path
			self.command, self.requestedGame = command or "IMPORT", requestedGame or game
			self.chunks = { "PIC1 HELLO " .. self.requestedGame .. " " .. nonce .. "\n" }
		end
		function env:tick()
			self.inTick = true
			self.bridge:Tick(main)
			self.inTick = false
			local challenge = self.wire:match("^PIC1 CHALLENGE " .. nonce .. " ([0-9a-f]+) [0-9a-f]+\n")
			if challenge and not self.sentImport then
				self.sentImport = true
				local encoded = hex(self.requestedPath)
				local signature = sha.hmac(sha.sha256, self.secret,
					"IMPORT|" .. self.requestedGame .. "|" .. nonce .. "|" .. challenge .. "|" .. encoded)
				self.importLine = "PIC1 " .. nonce .. " " .. self.command .. " " .. self.requestedGame
					.. " " .. challenge .. " " .. encoded .. " " .. signature .. "\n"
				table.insert(self.chunks, self.replayLine or self.importLine)
			end
		end
		function env:begin()
			for i = 1, 300 do
				self:tick()
				if #self.callbacks > 0 or self.closed then return end
			end
			error("Handshake did not finish")
		end
		function env:response(err, data)
			local callback = table.remove(self.callbacks, 1)
			requireThat(callback ~= nil, "No pending download")
			data = data or { character = self.character }
			callback(game == "poe1" and data or json.encode(data), err, false)
		end
		function env:complete()
			self:begin()
			self:response()
			requireThat(#self.calls == 0, "Callback mutated build off the UI thread")
			self:tick()
			self:response()
			self:tick()
			self:tick()
		end
		return env
	end

	for _, game in ipairs({ "poe1", "poe2" }) do
		local env = setup(game)
		env:request()
		env:complete()
		requireThat(table.concat(env.calls, ",") == "TREE,ITEMS,SAVE", "Imports not sequential")
		requireThat(env.wire:find("STATUS DONE", 1, true), "Missing completion acknowledgement")
		requireThat(not env.wire:find("BridgeFixture", 1, true) and not env.wire:find("never-sent", 1, true)
			and not env.wire:find(key, 1, true), "Private data in protocol")
		requireThat(not env.bridge:IsBusy() and env.closed, "Busy lock/client leaked")
		local replay = env.importLine
		env = setup(game)
		env.replayLine = replay
		env:request(); env:begin()
		requireThat(env.closed and #env.callbacks == 0, "Previous connection's signed request replayed")

		for _, spec in ipairs({
			{ code = "BUILD", path = scratch .. "\\other.xml" },
			{ code = "BUILD", path = scratch .. "\\..\\current.xml" },
			{ code = "BUILD", path = scratch .. "\\current.xml:stream" },
			{ code = "BUILD", unsaved = true },
			{ code = "AUTH", auth = true },
			{ code = "BUSY", popup = true },
		}) do
			env = setup(game)
			if spec.unsaved then env.build.dbFileName = nil end
			if spec.auth then
				env.main.api.authToken = nil
				env.tab.isAuthorized = function() return false end
			end
			if spec.popup then env.main.popups[1] = {} end
			env:request(nil, spec.path)
			env:begin()
			requireThat(env.wire:find("ERROR " .. spec.code, 1, true), "Precondition not rejected: " .. spec.code)
			requireThat(#env.calls == 0 and #env.callbacks == 0, "Rejected request touched build/API")
		end
		for _, bad in ipairs({ "token", "command", "game" }) do
			env = setup(game)
			env:request(bad == "token" and string.rep("c", 64) or nil, nil, bad == "command" and "EVAL" or nil,
				bad == "game" and (game == "poe1" and "poe2" or "poe1") or nil)
			env:begin()
			requireThat(env.closed and #env.callbacks == 0 and not env.wire:find("STATUS", 1, true),
				"Unauthenticated/arbitrary command accepted")
		end
		for _, bad in ipairs({ "DOWNLOAD", "DATA", "IMPORT", "SAVE", "CHANGED", "TIMEOUT", "DISCONNECT" }) do
			env = setup(game)
			env:request()
			env:begin()
			if bad == "TIMEOUT" then
				env.now = 91
			elseif bad == "DISCONNECT" then
				env.disconnected = true
			else
				if bad == "IMPORT" then env.throwTree = true end
				env:response(bad == "DOWNLOAD" and "secret API error" or nil,
					bad == "DATA" and { character = { name = "Wrong" } } or nil)
				if bad == "CHANGED" then env.build.dbFileName = scratch .. "\\other.xml" end
				if bad == "SAVE" then
					env.saveError = true
					env:tick(); env:response(); env:tick()
				end
			end
			env:tick(); env:tick()
			if bad ~= "DISCONNECT" then
				requireThat(env.wire:find("ERROR " .. bad, 1, true), "Failure not acknowledged: " .. bad)
			end
			requireThat(not env.wire:find("STATUS DONE", 1, true), "Failure reported success")
			requireThat(not env.wire:find("secret", 1, true), "API error leaked")
			requireThat(not env.bridge:IsBusy(), "Failure left PoB input locked")
			if env.callbacks[1] then
				env:response()
				env:tick()
				requireThat(#env.calls == 0, "Late callback imported after cancellation")
			end
		end
		env = setup(game)
		env:request()
		local request = table.remove(env.chunks, 1)
		env.chunks = { request:sub(1, 35), request:sub(36) }
		env:tick()
		requireThat(#env.callbacks == 0, "Fragmented request parsed early")
		env:begin(); env:response(); env:tick(); env:response(); env:tick(); env:tick()
		requireThat(env.wire:find("STATUS DONE", 1, true), "Fragmented request failed")
		for _, changed in ipairs({ "option", "character" }) do
			env = setup(game)
			env:request(); env:begin()
			if changed == "option" then env.tab.controls.charImportTreeClearJewels.state = true
			else env.character.name = "Different" end
			env:response(); env:tick()
			requireThat(env.wire:find("ERROR CHANGED", 1, true) and #env.calls == 0, "Changed selection/options imported")
		end
		env = setup(game)
		env.throwItems = true
		env:request(); env:begin(); env:response(); env:tick(); env:response(); env:tick(); env:tick()
		requireThat(env.wire:find("ERROR IMPORT", 1, true) and table.concat(env.calls, ",") == "TREE",
			"Items failure saved a partial import")
		env = setup(game)
		env.chunks = { "PIC1" }
		env:tick(); env.now = 6; env:tick()
		requireThat(env.closed and #env.callbacks == 0, "Incomplete handshake exceeded 5-second deadline")
		env = setup(game)
		env.chunks = { string.rep("x", 8193) }
		env:tick()
		requireThat(env.closed and #env.callbacks == 0, "Oversized input accepted")
		env = setup(game)
		env.partialSend = true
		env:request(); env:complete()
		for i = 1, 300 do env:tick() end
		requireThat(env.wire:find("STATUS DONE", 1, true) and env.closed, "Partial socket send lost acknowledgement")
	end
	io.open = realOpen
	package.loaded.socket = realSocket
	-- Exercise real LuaSocket rather than only mocked receive/accept behavior.
	do
		local listener
		package.loaded.socket = { gettime = realSocket.gettime, tcp4 = function()
			listener = assert(realSocket.tcp4())
			local proxy = {}
			function proxy:settimeout(value) return listener:settimeout(value) end
			function proxy:bind(host) return listener:bind(host, 0) end
			function proxy:listen(backlog) return listener:listen(backlog) end
			function proxy:accept() return listener:accept() end
			function proxy:close() return listener:close() end
			return proxy
		end }
		io.open = function(file, mode)
			if file:match("CompanionBridge.key$") then
				return { read = function() return key end, close = function() return true end }
			end
			return realOpen(file, mode)
		end
		GetScriptPath = function() return pob1 end
		local bridge = assert(loadfile(root .. "\\scripts\\pob_import_bridge.lua"))("poe1")
		local main = { mode = "LIST", modes = {}, popups = {} }
		bridge:Tick(main)
		local _, port = listener:getsockname()
		for attempt = 1, 3 do
			local peer = assert(realSocket.tcp4())
			peer:settimeout(1)
			assert(peer:connect("127.0.0.1", port))
			assert(peer:send("PIC1 HELLO poe1 " .. nonce .. "\n"))
			for frame = 1, 3 do bridge:Tick(main) end
			local line, err = peer:receive("*l")
			requireThat(line and line:match("^PIC1 CHALLENGE "), "Real LuaSocket HELLO failed: " .. tostring(err))
			local challenge, signature = line:match("^PIC1 CHALLENGE " .. nonce .. " ([0-9a-f]+) ([0-9a-f]+)$")
			requireThat(signature == sha.hmac(sha.sha256, key, "HELLO|poe1|" .. nonce .. "|" .. challenge),
				"Real socket challenge signature failed")
			peer:close()
			for frame = 1, 3 do bridge:Tick(main) end
		end
		listener:close()
		io.open = realOpen
		package.loaded.socket = realSocket
	end
	local ffi = require("ffi")
	ffi.cdef[[int _chdir(const char *path);]]
	local crt = ffi.load("msvcrt")
	for _, game in ipairs({ "poe1", "poe2" }) do
		assert(crt._chdir(game == "poe1" and pob1 or pob2) == 0)
		local wrapper = assert(loadfile(root .. "\\scripts\\pob_wrapper.lua"))()
		wrapper.init(game == "poe1" and pob1 or pob2, game)
		local xmlRoot = game == "poe1" and "PathOfBuilding" or "PathOfBuilding2"
		local treeVersion = game == "poe1" and "3_29" or "0_5"
		local class = game == "poe1" and 'classId="3"' or 'classInternalId="1"'
		local xml = '<' .. xmlRoot .. '><Build level="20" className="Witch" ascendClassName="None" targetVersion="' ..
			(game == "poe1" and "3_0" or "0_1") .. '"/><Tree activeSpec="1"><Spec title="Default" treeVersion="' ..
			treeVersion .. '" ' .. class .. ' ascendClassId="0" nodes=""/></Tree><Items activeItemSet="1">' ..
			'<Item id="1">Rarity: Rare\nBridge Boots\nDragonscale Boots\nItem Level: 82\n+100 to maximum Life</Item>' ..
			'<ItemSet id="1"><Slot name="Boots" itemId="1"/></ItemSet></Items>' ..
			'<Skills activeSkillSet="1"><SkillSet id="1"><Skill enabled="true" slot="Boots" mainActiveSkill="1">' ..
			'<Gem nameSpec="Fireball" level="5" quality="0" enabled="true"/></Skill></SkillSet></Skills></' .. xmlRoot .. '>'
		local loaded = json.decode(wrapper.loadBuild(xml, "Bridge fixture"))
		requireThat(loaded.success, "Native fixture failed: " .. tostring(loaded.error))
		local build = main.modes.BUILD
		build.dbFileName = path
		local file = assert(realOpen(path, "w"))
		assert(file:write(xml))
		file:close()
		local originalItems = #build.itemsTab.itemOrderList
		local originalSkills = #build.skillsTab.socketGroupList
		local env = setup(game, build)
		env:request()
		env:complete()
		requireThat(env.wire:find("STATUS DONE", 1, true), "Native " .. game .. " import/save failed: " .. env.wire)
		requireThat(table.concat(env.calls, ",") == "TREE,ITEMS,SAVE", "Native sequence wrong")
		requireThat(build.characterLevel == 40, "Native character level not imported")
		requireThat(#build.itemsTab.itemOrderList == originalItems and #build.skillsTab.socketGroupList == originalSkills,
			"Native clear=false options lost equipment/skills")
		io.open = realOpen
		package.loaded.socket = realSocket
		file = assert(realOpen(path, "r"))
		local saved = file:read("*a")
		file:close()
		requireThat(saved:find(xmlRoot, 1, true) and saved:find('level="40"', 1, true), "Native save did not persist imported build")
		-- Verify the installed SaveDBFile write/close guard without touching the fixture file.
		build.importTab.ImportPassiveTreeAndJewels = env.nativeTree
		build.importTab.ImportItemsAndSkills = env.nativeItems
		build.SaveDBFile = env.nativeSave
		env = setup(game, build)
		env.failDisk = true
		env:request(); env:complete()
		requireThat(env.wire:find("ERROR SAVE", 1, true) and not env.wire:find("STATUS DONE", 1, true),
			"Installed native disk failure reported success")
		io.open = realOpen
		package.loaded.socket = realSocket
	end
	return count
end
