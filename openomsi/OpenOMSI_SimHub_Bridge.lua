-- OpenOMSI_SimHub_Bridge.lua
-- UDP telemetry sender for openOMSI 0.2.x and SimHub.
-- Copy this file to <openOMSI>/plugins/.

local UDP_PORT = 44444
local SEND_INTERVAL = 0.05 -- 20 Hz
local PROTOCOL_VERSION = 1

local sequence = 0
local send_error_reported = false
local turn_left_var = nil
local turn_right_var = nil
local turn_switch_var = nil
local hazards_var = nil
local transmission_type = "automatic"

local TURN_LEFT_CANDIDATES = {
  "elec_lights_blinker_l",
  "lights_blinker_l",
  "lights_blink_l",
  "blinker_l",
  "blinker_left",
  "indicator_left",
  "turnsignal_left",
  "turn_left"
}

local TURN_RIGHT_CANDIDATES = {
  "elec_lights_blinker_r",
  "lights_blinker_r",
  "lights_blink_r",
  "blinker_r",
  "blinker_right",
  "indicator_right",
  "turnsignal_right",
  "turn_right"
}

local TURN_SWITCH_CANDIDATES = {
  "lights_sw_blinker",
  "elec_lights_sw_blinker",
  "blinker_sw",
  "turnsignal_switch"
}

local HAZARD_CANDIDATES = {
  "elec_warnblinker",
  "lights_sw_warnblinker",
  "warnblinker",
  "hazard",
  "hazards"
}

local function finite_number(value, fallback)
  local number = tonumber(value)
  if number == nil or number ~= number or number == math.huge or number == -math.huge then
    return fallback or 0
  end
  return number
end

local function var(name, fallback)
  return finite_number(omsi.var(name), fallback)
end

local function var_any(names)
  for _, name in ipairs(names) do
    local value = omsi.var(name)
    if value ~= nil then return finite_number(value, 0) end
  end
  return 0
end

local function clamp(value, minimum, maximum)
  if value < minimum then return minimum end
  if value > maximum then return maximum end
  return value
end

local function json_number(value)
  return string.format("%.9g", finite_number(value, 0))
end

local function json_boolean(value)
  return value and "true" or "false"
end

local function json_string(value)
  local text = tostring(value or "")
  text = text:gsub("\\", "\\\\")
  text = text:gsub('"', '\\"')
  text = text:gsub("\b", "\\b")
  text = text:gsub("\f", "\\f")
  text = text:gsub("\n", "\\n")
  text = text:gsub("\r", "\\r")
  text = text:gsub("\t", "\\t")
  text = text:gsub("[%z\1-\31]", function(character)
    return string.format("\\u%04x", string.byte(character))
  end)
  return '"' .. text .. '"'
end

local function bool_var(name)
  return var(name, 0) > 0.5
end

local function bus_api(name)
  local ok, value = pcall(function() return omsi.bus[name]() end)
  if ok then return value end
  return nil
end

local function control_profile()
  if omsi.var("almex_menu") ~= nil or omsi.str("almex_input") ~= nil then return "almex" end
  if omsi.var("AFR4_BT_0") ~= nil or omsi.var("AFR4_Display_Panel") ~= nil then return "afr4" end
  if omsi.var("atron_t_0") ~= nil or omsi.var("atron_t_OK") ~= nil then return "atron" end
  if omsi.var("copilot_mode") ~= nil or omsi.str("copilot_display") ~= nil then return "mvg-copilot" end
  local has_ibox = omsi.var("ibox_taste_D1") ~= nil or omsi.var("ibox_modus") ~= nil
  local has_ibis = omsi.var("IBIS_mode") ~= nil or omsi.var("IBIS_Route") ~= nil or omsi.var("IBIS_LinieKurs") ~= nil
  local has_ticketprinter = omsi.str("ticketprinter_display") ~= nil
  if has_ibox and has_ibis then return "ibox-ibis" end
  if has_ibox then return "ibox" end
  if has_ibis and has_ticketprinter then return "ibis-ticketprinter" end
  if has_ibis then return "ibis" end
  if has_ticketprinter then return "ticketprinter" end
  return "none"
end

local function first_existing_var(candidates)
  for _, name in ipairs(candidates) do
    if omsi.var(name) ~= nil then return name end
  end
  return nil
end

local function is_noise_blinker_name(lower)
  return lower:find("ai_", 1, true)
      or lower:find("armed", 1, true)
      or lower:find("runtime", 1, true)
      or lower:find("faktor", 1, true)
      or lower:find("timegap", 1, true)
      or lower:find("timer", 1, true)
      or lower:find("bulb", 1, true)
      or lower:find("count", 1, true)
      or lower:find("ibis", 1, true)
      or lower:find("kneeling", 1, true)
      or lower:find("tuer", 1, true)
      or lower:find("fahrschul", 1, true)
end

local function find_direction_var(direction)
  local names = omsi.vars() or {}
  for _, name in ipairs(names) do
    local lower = string.lower(tostring(name))
    local is_blinker = lower:find("blinker", 1, true)
        or lower:find("indicator", 1, true)
        or lower:find("turnsignal", 1, true)
    if is_blinker and not is_noise_blinker_name(lower) then
      if direction == "left" and
          (lower:match("_l$") or lower:match("_left$") or lower:find("links", 1, true)) then
        return name
      end
      if direction == "right" and
          (lower:match("_r$") or lower:match("_right$") or lower:find("rechts", 1, true)) then
        return name
      end
    end
  end
  return nil
end

local function detect_turn_variables()
  turn_left_var = first_existing_var(TURN_LEFT_CANDIDATES) or find_direction_var("left")
  turn_right_var = first_existing_var(TURN_RIGHT_CANDIDATES) or find_direction_var("right")
  turn_switch_var = first_existing_var(TURN_SWITCH_CANDIDATES)
  hazards_var = first_existing_var(HAZARD_CANDIDATES)

  omsi.log(
    "SimHub bridge indicators: left=" .. tostring(turn_left_var or "switch fallback") ..
    ", right=" .. tostring(turn_right_var or "switch fallback") ..
    ", switch=" .. tostring(turn_switch_var or "none") ..
    ", hazards=" .. tostring(hazards_var or "none"))
end

local function contains_any(text, needles)
  for _, needle in ipairs(needles) do
    if text:find(needle, 1, true) then return true end
  end
  return false
end

local function detect_transmission_type()
  local names = omsi.vars() or {}
  local available = {}
  for _, name in ipairs(names) do
    available[string.lower(tostring(name))] = true
  end

  local vehicle_text = string.lower(
    tostring(omsi.vehicle() or "") .. " " ..
    tostring(omsi.vehicle_manufacturer() or "") .. " " ..
    tostring(omsi.vehicle_model() or ""))

  local electric_name = contains_any(vehicle_text, {
    "elektro", "electric", "e-bus", "ebus", "e bus", "e_solaris", "e-solaris"
  })
  local electric_variables = available["electric_main"]
      or available["electric_battery"]
      or available["electric_power_level"]
      or available["electric_sound_summen"]

  local manual_variables = available["antrieb_man_gearchange_unfinished"]
      or available["antrieb_man_hebel_x_target"]
      or available["antrieb_man_hebel_y_target"]
      or available["cti_kupplung"]

  if electric_name or electric_variables then
    transmission_type = "electric"
  elseif manual_variables then
    transmission_type = "manual"
  else
    transmission_type = "automatic"
  end

  omsi.log("SimHub bridge transmission: " .. transmission_type)
end

local function turn_states()
  local hazards = hazards_var ~= nil and bool_var(hazards_var)
  local switch = turn_switch_var ~= nil and var(turn_switch_var, 0) or 0
  local left
  local right

  if turn_left_var ~= nil then
    left = bool_var(turn_left_var)
  else
    left = math.abs(switch - 1) < 0.25
  end

  if turn_right_var ~= nil then
    right = bool_var(turn_right_var)
  else
    right = math.abs(switch - 2) < 0.25
  end

  if hazards then
    left = true
    right = true
  end
  return left, right, hazards
end

local function selector_position(gear, gear_selection)
  if transmission_type == "manual" then
    if gear < -0.5 then return "R" end
    if math.abs(gear) < 0.5 then return "N" end
    return tostring(math.floor(gear + 0.5))
  end

  if bool_var("voev_gearR_light") then return "R" end
  if bool_var("voev_gearN_light") then return "N" end
  if bool_var("voev_gearD_light") then return "D" end
  if bool_var("cockpit_gangR_licht") then return "R" end
  if bool_var("cockpit_gangN_licht") then return "N" end
  if bool_var("cockpit_gangD_licht") then return "D" end

  -- Diesel and electric automatic buses commonly use 0=R, 1=N and 2..5=D.
  if gear_selection <= 0.5 then return "R" end
  if gear_selection <= 1.5 then return "N" end
  return "D"
end

local function add_number(fields, name, value)
  fields[#fields + 1] = json_string(name) .. ":" .. json_number(value)
end

local function add_boolean(fields, name, value)
  fields[#fields + 1] = json_string(name) .. ":" .. json_boolean(value)
end

local function add_string(fields, name, value)
  fields[#fields + 1] = json_string(name) .. ":" .. json_string(value)
end

local function road_speed_limit()
  -- openOMSI 0.2.22+ exposes the current lane limit through this API.
  -- Keep the call protected so the bridge still runs on older 0.2.x builds.
  local ok, value = pcall(function() return omsi.map.speed_limit() end)
  if ok and value ~= nil then return value end
  return nil
end

local function build_packet()
  sequence = sequence + 1
  local fields = {}

  add_string(fields, "protocol", "openomsi-simhub")
  add_number(fields, "protocolVersion", PROTOCOL_VERSION)
  add_number(fields, "sequence", sequence)
  add_number(fields, "gameTimeSeconds", omsi.time())

  if not omsi.has_vehicle() then
    add_boolean(fields, "connected", false)
    return "{" .. table.concat(fields, ",") .. "}"
  end

  local info = omsi.info() or {}
  local bus_doors = bus_api("doors")
  local headlights_level = finite_number(bus_api("headlights"), -1)
  local api_handbrake = bus_api("handbrake")
  local api_stop_brake = bus_api("stop_brake")
  local api_indicator = bus_api("indicator")
  local x, y, z, heading = omsi.position()
  local steering_rad = var("Axle_Steering_0_L")
  -- OMSI bus scripts commonly expose door positions as door_0, etc.;
  -- retain the legacy names as fallbacks for buses that use them.
  local function door_position(index)
    if type(bus_doors) == "table" and bus_doors[index + 1] ~= nil then
      return finite_number(bus_doors[index + 1], 0)
    end
    return var_any({"door_" .. index, "door" .. index})
  end
  local door0, door1, door2, door3 = door_position(0), door_position(1), door_position(2), door_position(3)
  local door4, door5, door6, door7 = door_position(4), door_position(5), door_position(6), door_position(7)
  local turn_left, turn_right, hazards = turn_states()
  if api_indicator ~= nil then
    turn_left = api_indicator == "left"
    turn_right = api_indicator == "right"
    hazards = api_indicator == "hazard"
  end
  -- Merge openOMSI's normalized light level with common OMSI script variable names.
  -- Some buses expose their own variables while the normalized API still reports off.
  local raw_low_beam = var_any({
    "lights_abbl", "lights_main", "elec_lights_lowbeam", "elec_lights_abbl",
    "lowbeam", "low_beam", "abblendlicht"
  }) > 0.5
  local raw_high_beam = var_any({
    "lights_fern", "elec_lights_highbeam", "elec_lights_fern",
    "highbeam", "high_beam", "fernlicht"
  }) > 0.5
  if omsi.var("Spot_Select") ~= nil and var("Spot_Select") >= 0 then raw_low_beam = true end
  local headlights_on = raw_low_beam or raw_high_beam or headlights_level >= 2
  local high_beam_on = raw_high_beam or headlights_level >= 3
  local parking_brake_on = bool_var("bremse_feststell_sw")
  if type(api_handbrake) == "boolean" then parking_brake_on = api_handbrake end
  local stop_brake_on = var_any({"haltebremse", "Haltestellenbremse", "stop_brake", "StopBrake"}) > 0.5
  if type(api_stop_brake) == "boolean" then stop_brake_on = api_stop_brake end
  local gear = var("antrieb_getr_aktugang")
  local gear_selection = var("antrieb_getr_gangwahl", 1)

  add_boolean(fields, "connected", true)
  add_boolean(fields, "paused", info.paused == true)
  add_string(fields, "vehicle", omsi.vehicle())
  add_string(fields, "manufacturer", omsi.vehicle_manufacturer())
  add_string(fields, "model", omsi.vehicle_model())
  add_string(fields, "controlProfile", control_profile())
  add_string(fields, "map", info.map)

  add_number(fields, "speedKmh", info.speed or omsi.speed())
  add_number(fields, "speedLimitKmh", road_speed_limit())
  add_number(fields, "throttle", var("Throttle"))
  add_number(fields, "brake", var("Brake"))
  add_number(fields, "steeringRad", steering_rad)
  add_number(fields, "steeringNormalized", clamp(steering_rad / 0.65, -1, 1))

  add_number(fields, "engineRpm", var("engine_n"))
  add_boolean(fields, "engineRunning", bool_var("engine_on"))
  add_number(fields, "engineTemperatureC", var("engine_temperature"))
  add_number(fields, "fuelLiters", var("engine_tank_content"))
  add_number(fields, "fuelPercent", clamp(var("tank_percent") * 100, 0, 100))

  add_number(fields, "gear", gear)
  add_number(fields, "gearSelection", gear_selection)
  add_string(fields, "transmissionType", transmission_type)
  add_string(fields, "gearSelector", selector_position(gear, gear_selection))
  add_number(fields, "retarder", clamp(var("retarder_brake_vol"), 0, 1))
  add_boolean(fields, "parkingBrake", parking_brake_on)
  add_boolean(fields, "stopBrake", stop_brake_on)
  add_boolean(fields, "absActive", bool_var("bremse_ABS_eingriff"))

  add_number(fields, "airPressure1Bar", var("bremse_p_Tank01") / 100000)
  add_number(fields, "airPressure2Bar", var("bremse_p_Tank02") / 100000)

  add_number(fields, "door0", door0)
  add_number(fields, "door1", door1)
  add_number(fields, "door2", door2)
  add_number(fields, "door3", door3)
  add_number(fields, "door4", door4)
  add_number(fields, "door5", door5)
  add_boolean(fields, "frontDoorOpen", math.max(door0, door1) > 0.05)
  add_boolean(fields, "middleDoorOpen", math.max(door2, door3) > 0.05)
  add_boolean(fields, "rearDoorOpen", math.max(math.max(door4, door5), math.max(door6, door7)) > 0.05)
  add_boolean(fields, "doorRelease", bool_var("door_freigabe"))
  add_boolean(fields, "stopRequested", bool_var("Haltewunsch"))

  add_boolean(fields, "turnLeft", turn_left)
  add_boolean(fields, "turnRight", turn_right)
  add_boolean(fields, "hazards", hazards)
  add_boolean(fields, "brakeLight", bool_var("elec_lights_brems"))
  add_boolean(fields, "positionLights", bool_var("elec_lights_stand"))
  add_boolean(fields, "headlightsOn", headlights_on)
  add_number(fields, "headlightsLevel", headlights_level >= 0 and headlights_level or nil)
  add_boolean(fields, "lowBeam", headlights_on)
  add_boolean(fields, "highBeam", high_beam_on)
  add_boolean(fields, "reverseLight", bool_var("elec_lights_rueckfahr"))
  add_boolean(fields, "wipers", bool_var("elec_wiper_isrunning"))
  add_number(fields, "wiperPosition", var("elec_wiperpos"))

  add_string(fields, "line", info.line)
  add_string(fields, "tour", info.tour)
  add_number(fields, "trip", info.trip)
  add_number(fields, "trips", info.trips)
  add_string(fields, "tripName", info.trip_name)
  add_string(fields, "destination", info.destination or info.terminus)
  add_string(fields, "nextStop", info.next_stop)
  add_number(fields, "nextStopNumber", info.next_stop_number)
  add_number(fields, "nextStopArrival", info.next_stop_arrival)
  add_number(fields, "nextStopDeparture", info.next_stop_departure)
  add_number(fields, "delaySeconds", info.delay)
  add_number(fields, "passengers", info.passengers)

  add_number(fields, "positionX", x)
  add_number(fields, "positionY", y)
  add_number(fields, "positionZ", z)
  add_number(fields, "headingDeg", heading or info.heading)
  add_number(fields, "articulationRad", var("articulation_0_alpha"))

  return "{" .. table.concat(fields, ",") .. "}"
end

local function send_packet()
  local ok, reason = omsi.send(UDP_PORT, build_packet())
  if not ok and not send_error_reported then
    omsi.warn("SimHub bridge UDP send failed on port", UDP_PORT, reason or "unknown error")
    send_error_reported = true
  elseif ok then
    send_error_reported = false
  end
end

local function trigger_if_present(name)
  if name and omsi.has_vehicle() then
    omsi.log("tablet control trigger: " .. name)
    omsi.trigger(name)
  end
end

local function press_if_present(name)
  if name and omsi.has_vehicle() then
    omsi.log("tablet control press: " .. name)
    omsi.press(name)
    omsi.after(0.12, function()
      if omsi.has_vehicle() then omsi.release(name) end
    end)
  end
end

local function start_mvg_copilot_route_entry(menu_item)
  local mode = tonumber(omsi.var("copilot_mode")) or 0
  if mode == 13 then return end
  if mode == 24 then
    trigger_if_present("copilot_clickspot_bottom_02")
    omsi.after(0.2, function()
      if omsi.has_vehicle() and control_profile() == "mvg-copilot" then
        trigger_if_present("CoPilot_Mainmenu_clickspot_" .. tostring(menu_item))
      end
    end)
  elseif mode == 4 then
    trigger_if_present("CoPilot_Mainmenu_clickspot_" .. tostring(menu_item))
  else
    omsi.log("MVG CoPilot route input unavailable in mode " .. tostring(mode))
  end
end

local function start_atron_route_entry()
  if tonumber(omsi.var("atron_modus")) == 1 then
    trigger_if_present("atron_t_C")
    omsi.after(0.15, function()
      if omsi.has_vehicle() then trigger_if_present("atron_t_L2") end
    end)
  else
    trigger_if_present("atron_t_L2")
  end
end

local ibox_ticket_keys = {"A3", "A8", "A6", "A4", "A5", "A7", "A9"}

local function select_ibox_ticket(index)
  local key = ibox_ticket_keys[index]
  if not key then return end
  local mode = tonumber(omsi.var("ibox_modus"))
  if mode == 5 then
    trigger_if_present("ibox_taste_B1")
    omsi.after(0.15, function()
      if omsi.has_vehicle() then trigger_if_present("ibox_taste_" .. key) end
    end)
  elseif mode == 8 then
    trigger_if_present("ibox_taste_" .. key)
  end
end

local function run_tablet_command(frame)
  if not omsi.has_vehicle() then return end
  local profile = control_profile()
  omsi.log("tablet control frame: " .. tostring(frame) .. " profile=" .. tostring(profile))
  local has_ticketprinter = omsi.str("ticketprinter_display") ~= nil
  local has_atron = profile == "atron"
  local has_afr4 = profile == "afr4"
  local has_ibox = profile == "ibox" or profile == "ibox-ibis"
  local has_ibis = profile == "ibis" or profile == "ibis-ticketprinter" or profile == "ibox-ibis"
  local code = frame:sub(1, 1)
  local value = frame:sub(2, 2)
  if (code == "4" or code == "5") and (#frame ~= 2 or value < "0" or value > "9") then return end
  if code ~= "4" and code ~= "5" and #frame ~= 1 then return end

  if code == "0" then
    if profile == "almex" then trigger_if_present("almex_click_linie")
    elseif profile == "mvg-copilot" then start_mvg_copilot_route_entry(2)
    elseif has_afr4 then trigger_if_present("AFR4_BT_L_K")
    elseif has_ibox then trigger_if_present("ibox_taste_C3")
    elseif has_ibis then trigger_if_present("IBIS_setmode_linie_kurs")
    elseif has_atron then start_atron_route_entry() end
  elseif code == "1" then
    if profile == "almex" then trigger_if_present("almex_click_umlauf")
    elseif profile == "mvg-copilot" then start_mvg_copilot_route_entry(3)
    elseif has_afr4 then trigger_if_present("AFR4_BT_Fahrtauswahl")
    elseif has_ibox then trigger_if_present("ibox_taste_D12")
    elseif has_ibis then trigger_if_present("IBIS_setmode_route")
    elseif has_atron then start_atron_route_entry() end
  elseif code == "2" then
    if profile == "almex" then
      press_if_present("almex_clickNB")
    elseif has_afr4 then
      trigger_if_present("AFR4_BT_Bestaetigen")
    elseif has_ibox then
      trigger_if_present("ibox_taste_D12")
    elseif has_ibis then
      trigger_if_present("IBIS_eingabe")
    elseif has_atron then
      trigger_if_present("atron_t_R5")
    elseif profile == "mvg-copilot" then
      trigger_if_present("copilot_clickspot_bottom_04")
    elseif has_ticketprinter then
      trigger_if_present("ticketprinter_button_enter")
    end
  elseif code == "3" then
    if profile == "almex" then
      trigger_if_present("almex_clickNC")
    elseif has_afr4 then
      trigger_if_present("AFR4_BT_Loeschen")
    elseif has_ibox then
      trigger_if_present("ibox_taste_D10")
    elseif has_ibis then
      trigger_if_present("IBIS_loeschen")
    elseif has_atron then
      trigger_if_present("atron_t_C")
    elseif profile == "mvg-copilot" then
      trigger_if_present("copilot_clickspot_bottom_03")
    elseif has_ticketprinter then
      trigger_if_present("ticketprinter_button_cancel")
    end
  elseif code == "4" then
    if profile == "almex" then
      trigger_if_present("almex_clickN" .. value)
    elseif has_afr4 then
      trigger_if_present("AFR4_BT_" .. value)
    elseif has_ibox then
      local ibox_digit = value == "0" and "11" or value
      trigger_if_present("ibox_taste_D" .. ibox_digit)
    elseif has_ibis then
      trigger_if_present("IBIS_" .. value)
    elseif has_atron then
      trigger_if_present("atron_t_" .. value)
    elseif profile == "mvg-copilot" then
      local digit_index = value == "0" and "9" or tostring(tonumber(value) - 1)
      trigger_if_present("copilot_clickspot_numbers_" .. digit_index)
    end
  elseif code == "5" then
    local index = tonumber(value)
    if profile == "almex" then
      if index and index > 0 then trigger_if_present("almex_click_bubble" .. tostring(index)) end
    elseif has_afr4 then
      if index and index >= 1 and index <= 5 then trigger_if_present("AFR4_BT_Ticket_" .. tostring(index - 1)) end
    elseif has_ibox then
      if index then select_ibox_ticket(index) end
    elseif has_atron then
      local atron_keys = {"L1", "L2", "L3", "L4", "L5", "R1", "R2", "R3", "R4", "R5"}
      if index and atron_keys[index] then trigger_if_present("atron_t_" .. atron_keys[index]) end
    elseif has_ticketprinter then
      if index and index > 0 then trigger_if_present("ticketprinter_button_ticket_" .. tostring(index - 1)) end
    end
  elseif code == "6" then
    if profile == "almex" then
      trigger_if_present("almex_ticket_toggle")
    elseif profile == "mvg-copilot" then
      trigger_if_present("ticketprinter_getticket")
    elseif has_afr4 then
      trigger_if_present("AFR4_BT_OK_klein")
    elseif has_ibox then
      local ibox_mode = tonumber(omsi.var("ibox_modus"))
      if ibox_mode == 5 then
        trigger_if_present("ibox_taste_B1")
      elseif ibox_mode == 8 then
        trigger_if_present("ibox_taste_A10")
      end
    elseif has_atron then
      trigger_if_present("atron_t_OK")
    elseif has_ticketprinter then
      trigger_if_present("ticketprinter_getticket")
    end
  end
end

local command_frame = nil
omsi.on("key", function(key, down)
  if not down then return end
  if key == "NumpadAdd" then
    if command_frame == nil then
      command_frame = ""
      omsi.log("tablet input frame started")
    else
      omsi.log("tablet input frame received: " .. command_frame)
      run_tablet_command(command_frame)
      command_frame = nil
    end
  elseif command_frame ~= nil then
    local digit = tostring(key):match("^Numpad([0-9])$")
    if digit and #command_frame < 2 then
      command_frame = command_frame .. digit
    else
      command_frame = nil
    end
  end
end)

omsi.on("start", function()
  omsi.log("SimHub bridge sender started: UDP 127.0.0.1:" .. UDP_PORT .. " at 20 Hz")
  if omsi.has_vehicle() then
    detect_turn_variables()
    detect_transmission_type()
  end
  omsi.message("SimHub-Telemetriesender aktiv", 4)
end)

omsi.on("vehicle", function(name)
  omsi.log("SimHub bridge vehicle: " .. tostring(name))
  detect_turn_variables()
  detect_transmission_type()
end)

omsi.every(SEND_INTERVAL, send_packet)

