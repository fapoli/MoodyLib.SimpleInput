# MoodyLib.SimpleInput

[![Unity](https://img.shields.io/badge/Unity-000000?logo=unity&logoColor=white)](https://unity.com)
[![License: MIT](https://img.shields.io/github/license/fapoli/MoodyLib.SimpleInput)](LICENSE)

A minimal, name-based wrapper over Unity's Input System. Reads actions by name off an `InputActionAsset`, with no generated wrapper class to regenerate or keep in sync.

## Contents
- **SimpleInput MonoBehaviour**: assign an `InputActionAsset` and read its actions by name with `GetAxis2D`/`GetAxis`/`GetValue<T>`/`IsButtonPressed`/`IsButtonDown`/`IsButtonUp`/`GetAction`. Looked-up actions are cached by name. Only one instance is meant to exist for the whole game session - it survives scene loads and destroys any later duplicate automatically, so don't drop it into more than one scene. Get it with `FindAnyObjectByType<SimpleInput>()`, or the static `SimpleInput.instance` if you'd rather use that - both work, `instance` is purely an optional convenience.
- **Profiles**: if your asset has multiple action maps (e.g. "Player" and "UI"), the first one is active by default. Call `SwitchProfile("UI")` to disable the current one and enable another - action lookups and their cache are scoped to whichever profile is active, so two profiles can safely reuse the same action name.
- **Slots**: every connected device is assigned a slot - keyboard and mouse always share slot 1, and every other device (each gamepad, etc.) gets its own slot. `GetSlots()` lists the currently-connected ones as `SimpleInput.SlotInfo` (`slot`, `deviceType`, `gamepadBrand`) so you can build a player-assignment screen off it - including a player choosing keyboard over a connected gamepad. If a device disconnects, its slot is kept (just left out of `GetSlots()`) in case the same type/brand reconnects, in which case it reclaims that slot instead of getting a new one; a genuinely different device (or one Unity can't tell apart from the original, since matching is by type/brand rather than hardware identity) gets a fresh slot number instead. Assigning a slot to a player, and handling a device changing mid-game, is the game's responsibility, not SimpleInput's.
- **Per-slot reads**: `GetAxis2D`/`GetAxis`/`GetValue<T>`/`IsButtonPressed`/`IsButtonDown`/`IsButtonUp` all take an optional `slot` (from `GetSlots`), defaulting to `0` for today's single-player behavior (reads whatever is driving the action, auto-switching schemes included). Pass a specific slot and the read is resolved directly on whichever of that slot's devices actually has the binding - so slot 1 (keyboard+mouse) correctly reads "Move" off the keyboard and "Look" off the mouse from the same slot number, and two gamepad slots in local multiplayer don't see each other's input.
- **activeScheme**: the name of the control scheme (e.g. "Keyboard&Mouse", "Gamepad") whose device last actually drove one of this asset's actions. Before that's ever happened, it's a live best guess instead - a connected gamepad, or whatever occupies slot 1, or null if nothing's connected - recomputed on every read rather than cached, so it stays correct through any connecting/disconnecting before the first real input. Once real input happens, it reflects that confirmed device permanently.
- **GetGamepadBrand**: `SimpleInput.GamepadBrand.Xbox`, `.PlayStation`, `.Switch` or `.Generic`, detected from a device's concrete type; null if it isn't a gamepad. With no `slot`, uses the same real-or-guessed device as `activeScheme` - pass a specific `slot` for a per-player brand instead.
- **GetBindingPath**: given an action name (and optionally a scheme name, defaulting to `activeScheme`), returns the control it's bound to under that scheme with the device prefix stripped - `"leftButton"`, not `"<Mouse>/leftButton"`.

## Why would you want to use this?
Unity's Input System gives you three ways to read actions:
- **Generated C# class** per `.inputactions` asset: compile-time safety, at the cost of tying your code to one project's specific type - a shared component can't move to another project without being forked or rewired to match.
- **`InputActionAsset.FindAction()`** by name: that coupling disappears, since it already reads by name - but you're on your own for everything else, rebuilt from scratch in every component: caching, enabling and disabling action maps, tracking the active scheme, detecting gamepad brand.
- **The built-in `PlayerInput` component**: action-map switching and scheme detection come built in, at the cost of a callback-driven interface (`SendMessage`, per-action `UnityEvent`s, C# event subscriptions) instead of plain polling.

SimpleInput takes the name-based approach and does that missing work once instead of N times, with a plain polling API and no callbacks to wire: assign your asset to a single shared instance, and every component just finds it with `FindAnyObjectByType<SimpleInput>()` and gets caching, action-map profiles, `activeScheme` tracking, and a ready-made `GetGamepadBrand` API - for free. It also covers local multiplayer out of the box: every connected device gets its own slot, and reads scoped to a slot stay isolated from every other slot - no per-project code needed to stop two gamepads, or a keyboard and a gamepad, from reading each other's input.

## Install via local path

Add to your project's `Packages/manifest.json`:
```json
"com.moodylib.simpleinput": "file:/Users/federicopoli/Code/moodylib/SimpleInput/"
```

## Install via Git URL

1. In Unity, open **Window > Package Manager**.
2. Click the **+** button in the top-left corner.
3. Select **"Add package from Git URL…"**.
4. Paste this URL and click **Add**:
   ```text
   https://github.com/fapoli/MoodyLib.SimpleInput.git
   ```

## How to use
1. Add a `SimpleInput` component to a GameObject in your first scene (e.g. an input manager) and assign your `InputActionAsset` - it persists across scene loads on its own, so it only needs to exist once.
2. From any other component, find it with `FindAnyObjectByType<SimpleInput>()` and read actions by name: `simpleInput.GetAxis2D("Move")`, `simpleInput.IsButtonDown("Jump")`, etc.
3. If you have more than one action map, call `simpleInput.SwitchProfile("UI")` when you want to move from gameplay to a menu (or back), instead of toggling actions one by one.
4. For local multiplayer, call `simpleInput.GetSlots()` to list the connected devices, assign a `slot` to each player (keyboard+mouse is always slot 1), and pass that `slot` into every read for that player: `simpleInput.GetAxis2D("Move", player.slot)`, `simpleInput.IsButtonDown("Jump", player.slot)`.
