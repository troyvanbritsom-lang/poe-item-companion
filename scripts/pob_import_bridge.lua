-- Fixed-command, same-user loopback bridge. All mutations run from main:OnFrame.
local game = ...
local socket = require("socket")
local json = require("dkjson")
local sha = require("sha2")
local ffi = require("ffi")
ffi.cdef[[long BCryptGenRandom(void *algorithm, unsigned char *buffer, unsigned long length, unsigned long flags);]]
local crypto = ffi.load("bcrypt")
local bridge = {}
local server, activePeer
local peers = {}
local lastListenAttempt = -10
local maxPeers = 8
local token
local port = game == "poe1" and 49171 or 49172

local function mac(message)
	return sha.hmac(sha.sha256, token, message)
end

local function equalMac(actual, expected)
	if not actual or #actual ~= #expected then return false end
	local difference = 0
	for i = 1, #actual do
		difference = bit.bor(difference, bit.bxor(actual:byte(i), expected:byte(i)))
	end
	return difference == 0
end

local function randomNonce()
	local bytes = ffi.new("unsigned char[32]")
	assert(crypto.BCryptGenRandom(nil, bytes, 32, 2) == 0, "Bridge secure random failed")
	return sha.bin_to_hex(ffi.string(bytes, 32))
end

local function canonical(path)
	if type(path) ~= "string" or #path > 2048 or path:find("[%z\1-\31]") then return nil end
	path = path:gsub("\\", "/")
	if not path:match("^%a:/") or path:find("//", 1, true) then return nil end
	for segment in path:gmatch("[^/]+") do
		if segment == "." or segment == ".." or segment:match("[%. ]$") then return nil end
	end
	if path:sub(3):find(":", 1, true) then return nil end
	return path:lower()
end

local function send(peer, kind, code)
	local job = peer.job
	local text = "PIC1 " .. job.nonce .. " " .. kind .. " " .. code
	peer.output = peer.output .. text .. " " .. mac("STATUS|" .. game .. "|" .. peer.session.challenge .. "|" .. text) .. "\n"
end

local function release(peer)
	if activePeer ~= peer then return end
	activePeer = nil
	local job = peer.job
	if not job or not job.tab then return end
	local tab = job.tab
	if game == "poe1" then
		tab.oauthLoading = false
	elseif tab.charImportMode == "IMPORTING" then
		tab.charImportMode = "SELECTCHAR"
	end
end

local function closeClient(peer)
	release(peer)
	if peer.client then peer.client:close() end
	peer.client, peer.job, peer.session = nil, nil, nil
	peer.input, peer.output = "", ""
end

local function fail(peer, code)
	local job = peer.job
	release(peer)
	job.finished = true
	job.finishedAt = socket.gettime()
	send(peer, "ERROR", code)
	if job.tab then
		if game == "poe1" then job.tab.oauthErrCode = "Companion import failed: " .. code
		else job.tab.charImportStatus = "Companion import failed: " .. code end
	end
end

local function selected(tab)
	local char = tab.controls.charSelect:GetSelValue()
	local realm = tab.controls.accountRealm:GetSelValue()
	if not char or not realm then return nil end
	return game == "poe1" and char.label or (char.char and char.char.name), realm
end

local optionNames = { "charImportTreeClearJewels", "charImportItemsClearItems",
	"charImportItemsClearSkills", "charImportItemsIgnoreWeaponSwap" }

local function unchanged(peer, main)
	local job = peer.job
	local build = main.modes and main.modes[main.mode]
	if main.mode ~= "BUILD" or build ~= job.build or build.importTab ~= job.tab
		or canonical(build.dbFileName) ~= job.path or main.newMode or main.popups[1]
		or build.actionOnSave or build.spec ~= job.spec or build.activeLoadout ~= job.loadout
		or build.itemsTab.activeItemSetId ~= job.itemSet or build.skillsTab.activeSkillSetId ~= job.skillSet then
		return false
	end
	local name, realm = selected(job.tab)
	if name ~= job.name or not realm or realm.realmCode ~= job.realm then return false end
	for i, key in ipairs(optionNames) do
		if job.tab.controls[key].state ~= job.options[i] then return false end
	end
	return true
end

local function download(peer, main, phase)
	local job = peer.job
	job.phase = phase
	job.pending = nil
	send(peer, "STATUS", phase)
	if game == "poe1" then job.tab.oauthLoading = true
	else
		job.tab.charImportMode = "IMPORTING"
		job.tab.charImportStatus = "Companion: retrieving character data..."
	end
	local current = job
	-- Download callbacks only enqueue; late responses never touch a switched build.
	main.api:DownloadCharacter(job.realm, job.name, function(data, err, updateSettings)
		if peer.client and peer.job == current and not current.finished and not current.pending then
			current.pending = { data = data, error = err, updateSettings = updateSettings }
		end
	end)
end

local function start(peer, main, line)
	local session = peer.session
	if not session then
		local requestedGame, nonce = line:match("^PIC1 HELLO (poe[12]) ([0-9a-f]+)$")
		if requestedGame ~= game or not nonce or #nonce ~= 64 then closeClient(peer); return end
		session = { nonce = nonce, challenge = randomNonce() }
		peer.session = session
		peer.output = "PIC1 CHALLENGE " .. nonce .. " " .. session.challenge .. " "
			.. mac("HELLO|" .. game .. "|" .. nonce .. "|" .. session.challenge) .. "\n"
		return
	end
	local nonce, requestedGame, challenge, hex, signature =
		line:match("^PIC1 ([0-9a-f]+) IMPORT (poe[12]) ([0-9a-f]+) ([0-9a-f]+) ([0-9a-f]+)$")
	if not nonce or nonce ~= session.nonce or requestedGame ~= game or challenge ~= session.challenge
		or #hex % 2 ~= 0 or #hex > 4096
		or not equalMac(signature, mac("IMPORT|" .. game .. "|" .. nonce .. "|" .. challenge .. "|" .. hex)) then
		closeClient(peer); return
	end
	local path = hex:gsub("%x%x", function(pair) return string.char(tonumber(pair, 16)) end)
	local job = { nonce = nonce, path = canonical(path), started = socket.gettime() }
	peer.job = job
	if activePeer then fail(peer, "BUSY"); return end
	local build = main.modes and main.modes[main.mode]
	if not job.path or job.path:sub(-4) ~= ".xml" or main.mode ~= "BUILD" or not build
		or canonical(build.dbFileName) ~= job.path then fail(peer, "BUILD"); return end
	if main.newMode or main.popups[1] or build.actionOnSave then fail(peer, "BUSY"); return end
	local tab = build.importTab
	if not tab or not main.api then fail(peer, "AUTH"); return end
	local name, realm = selected(tab)
	if not name or not realm or (game == "poe1" and (not tab.usingOauth or not tab.isAuthorized()))
		or (game == "poe2" and (not main.api.authToken or tab.charImportMode ~= "SELECTCHAR")) then
		fail(peer, "AUTH"); return
	end
	if tab.oauthLoading then fail(peer, "BUSY"); return end
	local file = io.open(build.dbFileName, "r")
	if not file then fail(peer, "BUILD"); return end
	file:close()
	job.build, job.tab, job.name, job.realm = build, tab, name, realm.realmCode
	job.spec, job.loadout = build.spec, build.activeLoadout
	job.itemSet, job.skillSet = build.itemsTab.activeItemSetId, build.skillsTab.activeSkillSetId
	job.options = {}
	for i, key in ipairs(optionNames) do job.options[i] = tab.controls[key].state end
	activePeer = peer
	if game == "poe1" then
		local league = tab.controls.charSelectLeague:GetSelValue()
		main.lastRealm, tab.lastRealm = realm.id, realm.id
		main.lastLeague, tab.lastLeague = league, league
		main.lastCharacterHash, tab.lastCharacterHash = common.sha1(name), common.sha1(name)
	end
	download(peer, main, "TREE")
end

local function apply(peer, main)
	local job = peer.job
	local pending = job.pending
	if not pending then return end
	job.pending = nil
	if pending.error then fail(peer, "DOWNLOAD"); return end
	local data = pending.data
	if game == "poe2" then
		if type(data) ~= "string" then fail(peer, "DATA"); return end
		local err
		data, _, err = json.decode(data)
		if err then fail(peer, "DATA"); return end
	end
	if type(data) ~= "table" or type(data.character) ~= "table"
		or data.character.name ~= job.name then fail(peer, "DATA"); return end
	local character = data.character
	if type(character.equipment) ~= "table" or type(character.passives) ~= "table"
		or type(character.passives.hashes) ~= "table" or type(character.class) ~= "string"
		or type(character.league) ~= "string" then fail(peer, "DATA"); return end
	if game == "poe2" and (type(character.skills) ~= "table" or type(character.jewels) ~= "table"
		or type(character.passives.specialisations) ~= "table" or type(character.passives.skill_overrides) ~= "table"
		or type(character.passives.quest_stats) ~= "table") then
		fail(peer, "DATA"); return
	end
	if type(character.level) ~= "number" or character.level < 1 or character.level > 100 then fail(peer, "DATA"); return end
	if game == "poe2" and pending.updateSettings then job.tab:SaveApiSettings() end
	if job.phase == "TREE" then
		job.tab:ImportPassiveTreeAndJewels(character, job.options[1])
		download(peer, main, "ITEMS")
	else
		job.tab:ImportItemsAndSkills(character, job.options[2], job.options[3], job.options[4])
		job.phase = "SAVE"
		send(peer, "STATUS", "SAVE")
	end
end

local function tickPeer(peer, main, now)
	local job = peer.job
	if (not job and now - peer.connectedAt > 5) or (job and job.finished and now - job.finishedAt > 5) then
		closeClient(peer); return
	end
	-- Read at most 4096 bytes per frame, with a bounded fragmented-line buffer.
	local chunk, err, partial = peer.client:receive(4096)
	peer.input = peer.input .. (chunk or partial or "")
	if err and err ~= "timeout" then closeClient(peer); return end
	if #peer.input > 8192 then closeClient(peer); return end
	if not job then
		local ending = peer.input:find("\n", 1, true)
		if ending then
			local line = peer.input:sub(1, ending - 1)
			peer.input = peer.input:sub(ending + 1)
			start(peer, main, line)
		elseif now - peer.connectedAt > 5 then closeClient(peer); return end
	end
	if not peer.client then return end
	job = peer.job
	if job and not job.finished then
		if now - job.started > 90 then fail(peer, "TIMEOUT")
		elseif not unchanged(peer, main) then fail(peer, "CHANGED")
		elseif job.phase == "SAVE" then
			-- SaveDBFile's patched write/close checks distinguish failure from nil success.
			if job.build:SaveDBFile() then fail(peer, "SAVE")
			else
				release(peer)
				job.finished = true
				job.finishedAt = now
				send(peer, "STATUS", "DONE")
				if game == "poe1" then job.tab.oauthErrCode = nil
				else job.tab.charImportStatus = "Companion: import saved successfully." end
			end
		else
			apply(peer, main)
		end
	end
	if #peer.output > 0 then
		local sent, sendError, partialSent = peer.client:send(peer.output)
		peer.output = peer.output:sub((sent or partialSent or 0) + 1)
		if sendError and sendError ~= "timeout" then closeClient(peer); return end
	end
	if job and job.finished and #peer.output == 0 then closeClient(peer) end
end

function bridge:IsBusy()
	return activePeer ~= nil
end

local function tick(main)
	local now = socket.gettime()
	if not server then
		if now - lastListenAttempt < 10 then return end
		lastListenAttempt = now
		local file = io.open(GetScriptPath() .. "/Modules/CompanionBridge.key", "r")
		if not file then error("Missing bridge credential; reinstall companion bridge") end
		token = file:read("*l")
		file:close()
		if not token or #token ~= 64 or not token:match("^[0-9a-f]+$") then error("Invalid bridge credential") end
		local listener = assert(socket.tcp4())
		listener:settimeout(0)
		local ok, err = listener:bind("127.0.0.1", port)
		if ok then ok, err = listener:listen(maxPeers) end
		if not ok then listener:close(); error("Bridge loopback listener unavailable") end
		server = listener
	end
	-- Idle/fragmented peers must not hold the only slot needed by a valid HELLO.
	while #peers < maxPeers do
		local client, err = server:accept()
		if err and err ~= "timeout" then error("Bridge accept failed") end
		if not client then break end
		local peer = { client = client, connectedAt = now, input = "", output = "" }
		peers[#peers + 1] = peer
		client:settimeout(0)
	end
	local index = 1
	while index <= #peers do
		local peer = peers[index]
		if not pcall(tickPeer, peer, main, now) then
			-- API exceptions can contain account data; log only a fixed message.
			ConPrintf("Companion bridge failed; no further import/save will be attempted.")
			if peer.job and peer.client and not peer.job.finished then
				if not pcall(fail, peer, peer.job.phase == "SAVE" and "SAVE" or "IMPORT") then closeClient(peer) end
			else
				closeClient(peer)
			end
		end
		if not peer.client then table.remove(peers, index)
		else index = index + 1 end
	end
end

function bridge:Tick(main)
	if not pcall(tick, main) then
		ConPrintf("Companion bridge listener failed; no further import/save will be attempted.")
		for _, peer in ipairs(peers) do closeClient(peer) end
		peers = {}
	end
end

return bridge
