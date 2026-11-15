-- Project-owned runtime rule; never match the shared Steam class alone.
-- Apply after the match, before restarting HDT. No fixed position/size rule:
-- HDT must follow the game's own coordinates.
if hdtProtonOverlayRule then hdtProtonOverlayRule:set_enabled(false) end
if hdtProtonMainRule then hdtProtonMainRule:set_enabled(false) end
if hdtProtonControlRule then hdtProtonControlRule:set_enabled(false) end
if hdtProtonControlTimer then hdtProtonControlTimer:set_enabled(false) end
hdtProtonMainRule = hl.window_rule({
    name = "hdt-proton-main-startup",
    match = { class = "^__STEAM_WINDOW_CLASS__$", title = "^Hearthstone Deck Tracker$" },
    no_initial_focus = true,
    focus_on_activate = false,
})
-- Register the base rule first so the narrower control override has precedence.
hdtProtonOverlayRule = hl.window_rule({
    name = "hdt-proton-overlay",
    match = { class = "^__STEAM_WINDOW_CLASS__$", title = "^HearthstoneOverlay$" },
    float = true, no_initial_focus = true, no_focus = true,
    no_follow_mouse = true, focus_on_activate = false,
    decorate = false, no_anim = true, no_blur = true, no_shadow = true,
    rounding = 0, border_size = 0, force_rgbx = false,
})
-- Input regions alone do not constrain this compositor's top-level hit testing.
-- Override the blanket no-focus rule only while the pointer is over an HDT control.
hdtProtonControlRule = hl.window_rule({
    name = "hdt-proton-control-input",
    match = { class = "^__STEAM_WINDOW_CLASS__$", title = "^HearthstoneOverlay$" },
    no_focus = false,
})
hdtProtonControlRule:set_enabled(false)
hdtProtonControls = nil
hdtProtonControlTicks = 0
hdtProtonControlActive = false
hdtProtonControlTimer = hl.timer(function()
    local overlay = hl.get_window("title:^HearthstoneOverlay$")
    local active = hl.get_active_window()
    local hit = false
    if hdtProtonControlTicks > 0 then hdtProtonControlTicks = hdtProtonControlTicks - 1 end
    if overlay and overlay.class == "__STEAM_WINDOW_CLASS__" and active and
       active.class == "__STEAM_WINDOW_CLASS__" and
       (active.title == "Hearthstone" or active.title == "HearthstoneOverlay") and
       hdtProtonControls and hdtProtonControlTicks > 0 then
        local cursor = hl.get_cursor_pos()
        local x, y = cursor.x - overlay.at.x, cursor.y - overlay.at.y
        for _, rect in ipairs(hdtProtonControls) do
            if x >= rect[1] and y >= rect[2] and x < rect[1]+rect[3] and y < rect[2]+rect[4] then hit = true; break end
        end
    end
    if hit ~= hdtProtonControlActive then
        hdtProtonControlActive = hit
        hdtProtonControlRule:set_enabled(hit)
    end
end, { timeout = 25, type = "repeat" })
-- Wine's topmost state does not reliably preserve compositor stacking after clicks.
-- Raise only the overlay, without activation, while this game's window is active.
if hdtProtonStackTimer then hdtProtonStackTimer:set_enabled(false) end
if hdtProtonStackSubscription then hdtProtonStackSubscription:remove() end
local function stackOverlay(game)
    local overlay = hl.get_window("title:^HearthstoneOverlay$")
    if overlay ~= nil and overlay.class == "__STEAM_WINDOW_CLASS__" then
        local inGame = game ~= nil and (game.title == "Hearthstone" or game.title == "HearthstoneOverlay") and game.class == "__STEAM_WINDOW_CLASS__"
        hl.dispatch(hl.dsp.window.alter_zorder({ window = "address:" .. overlay.address, mode = inGame and "top" or "bottom" }))
    end
end
hdtProtonStackSubscription = hl.on("window.active", function(game) stackOverlay(game) end)
hdtProtonStackTimer = hl.timer(function()
    stackOverlay(hl.get_active_window())
end, { timeout = 100, type = "repeat" })
