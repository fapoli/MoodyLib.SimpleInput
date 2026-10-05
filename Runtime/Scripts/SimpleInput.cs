using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.InputSystem.Switch;
using UnityEngine.InputSystem.XInput;

public class SimpleInput : MonoBehaviour {
    /// <summary>
    /// The singleton instance, set on Awake and cleared on destroy.
    /// </summary>
    public static SimpleInput instance { get; private set; }

    /// <summary>
    /// The asset to read actions from.
    /// </summary>
    public InputActionAsset actions;

    /// <summary>
    /// The active control scheme name. Reflects the last device that drove an action, or a
    /// best-guess from connected devices before the first real input. Null if nothing is connected.
    /// </summary>
    public string activeScheme {
        get {
            var device = _hasRealInput ? _lastDevice : GuessDevice();
            return device != null ? GetSchemeForDevice(device) : null;
        }
    }

    private readonly Dictionary<string, InputAction> _cache = new();
    private readonly Dictionary<int, SlotState> _slots = new();
    private InputActionMap _activeProfile;
    private InputDevice _lastDevice;
    private int _nextSlot = 2;
    private bool _hasRealInput;

    public void Awake() {
        foreach (var other in FindObjectsByType<SimpleInput>()) {
            if (other == this) continue;
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Subscribes to input and device-change events, and assigns slots for already-connected devices.
    /// </summary>
    public void OnEnable() {
        InputSystem.onActionChange += OnActionChange;
        InputSystem.onDeviceChange += OnDeviceChange;

        foreach (var device in InputSystem.devices) AssignSlot(device);

        if (!actions || actions.actionMaps.Count == 0) return;
        _activeProfile = actions.actionMaps[0];
        _activeProfile.Enable();
    }

    /// <summary>
    /// Unsubscribes from input and device-change events.
    /// </summary>
    public void OnDisable() {
        InputSystem.onActionChange -= OnActionChange;
        InputSystem.onDeviceChange -= OnDeviceChange;
        _activeProfile?.Disable();
    }

    /// <summary>
    /// Clears the static instance reference if this was the surviving instance.
    /// </summary>
    public void OnDestroy() {
        if (instance == this) instance = null;
    }

    /// <summary>
    /// Disables the current action-map profile and enables the named one, e.g. to switch from
    /// gameplay to a menu. Action lookups and their cache are scoped to whichever is active, so
    /// two profiles can safely reuse the same action name.
    /// </summary>
    /// <param name="profileName">The name of the action map to switch to.</param>
    public void SwitchProfile(string profileName) {
        var profile = actions ? actions.FindActionMap(profileName) : null;
        if (profile == null || profile == _activeProfile) return;

        _activeProfile?.Disable();
        _activeProfile = profile;
        _activeProfile.Enable();
        _cache.Clear();
    }

    /// <summary>
    /// Lists currently-connected slots. Disconnected slots are omitted but kept internally.
    /// </summary>
    public List<SlotInfo> GetSlots() {
        var result = new List<SlotInfo>();
        foreach (var pair in _slots) {
            if (pair.Value.devices.Count == 0) continue;
            result.Add(new SlotInfo {
                slot = pair.Value.info.slot,
                deviceType = pair.Value.info.deviceType,
                gamepadBrand = pair.Value.info.gamepadBrand
            });
        }
        return result;
    }

    /// <summary>
    /// With no slot, reads the action as usual. With a slot, reads directly off whichever of
    /// that slot's devices has the binding.
    /// </summary>
    /// <typeparam name="TValue">The value type the action is read as.</typeparam>
    /// <param name="actionName">The name of the action to read.</param>
    /// <param name="slot">The slot to read from, or 0 for the default single-player behavior.</param>
    public TValue GetValue<TValue>(string actionName, int slot = 0) where TValue : struct {
        if (slot == 0) {
            var action = GetAction(actionName);
            return action != null ? action.ReadValue<TValue>() : default;
        }

        var control = ResolveControl<InputControl<TValue>>(actionName, slot);
        return control != null ? control.ReadValue() : default;
    }

    /// <summary>
    /// Shortcut for reading the action as a Vector2.
    /// </summary>
    /// <param name="actionName">The name of the action to read.</param>
    /// <param name="slot">The slot to read from, or 0 for the default single-player behavior.</param>
    public Vector2 GetAxis2D(string actionName, int slot = 0) {
        return GetValue<Vector2>(actionName, slot);
    }

    /// <summary>
    /// Shortcut for reading the action as a float.
    /// </summary>
    /// <param name="actionName">The name of the action to read.</param>
    /// <param name="slot">The slot to read from, or 0 for the default single-player behavior.</param>
    public float GetAxis(string actionName, int slot = 0) {
        return GetValue<float>(actionName, slot);
    }

    /// <summary>
    /// True while the button is held.
    /// </summary>
    /// <param name="actionName">The name of the action to read.</param>
    /// <param name="slot">The slot to read from, or 0 for the default single-player behavior.</param>
    public bool IsButtonPressed(string actionName, int slot = 0) {
        if (slot == 0) {
            var action = GetAction(actionName);
            return action != null && action.IsPressed();
        }

        var control = ResolveControl<ButtonControl>(actionName, slot);
        return control != null && control.isPressed;
    }

    /// <summary>
    /// True on the frame the button was pressed.
    /// </summary>
    /// <param name="actionName">The name of the action to read.</param>
    /// <param name="slot">The slot to read from, or 0 for the default single-player behavior.</param>
    public bool IsButtonDown(string actionName, int slot = 0) {
        if (slot == 0) {
            var action = GetAction(actionName);
            return action != null && action.WasPressedThisFrame();
        }

        var control = ResolveControl<ButtonControl>(actionName, slot);
        return control != null && control.wasPressedThisFrame;
    }

    /// <summary>
    /// True on the frame the button was released.
    /// </summary>
    /// <param name="actionName">The name of the action to read.</param>
    /// <param name="slot">The slot to read from, or 0 for the default single-player behavior.</param>
    public bool IsButtonUp(string actionName, int slot = 0) {
        if (slot == 0) {
            var action = GetAction(actionName);
            return action != null && action.WasReleasedThisFrame();
        }

        var control = ResolveControl<ButtonControl>(actionName, slot);
        return control != null && control.wasReleasedThisFrame;
    }

    /// <summary>
    /// With no slot, uses the same real-or-guessed device as activeScheme. With a slot, reports
    /// that slot's brand instead. Null if no gamepad is involved.
    /// </summary>
    /// <param name="slot">The slot to check, or 0 to use the same device as activeScheme.</param>
    public GamepadBrand? GetGamepadBrand(int slot = 0) {
        if (slot == 0) return GamepadBrandOf(_hasRealInput ? _lastDevice : GuessDevice());

        if (!_slots.TryGetValue(slot, out var state)) return null;
        foreach (var device in state.devices) {
            var brand = GamepadBrandOf(device);
            if (brand != null) return brand;
        }
        return null;
    }

    /// <summary>
    /// Finds the named action within the active profile. Looked-up actions are cached by name.
    /// </summary>
    /// <param name="actionName">The name of the action to find.</param>
    public InputAction GetAction(string actionName) {
        if (_cache.TryGetValue(actionName, out var cached)) return cached;

        var action = _activeProfile != null ? _activeProfile.FindAction(actionName) : null;
        _cache[actionName] = action;
        return action;
    }

    /// <summary>
    /// Returns the control bound to this action under the given scheme (activeScheme if none
    /// given), device prefix stripped - "buttonSouth", not "&lt;Gamepad&gt;/buttonSouth".
    /// </summary>
    /// <param name="actionName">The name of the action to look up.</param>
    /// <param name="schemeName">The scheme to look under, or activeScheme if not given.</param>
    public string GetBindingPath(string actionName, string schemeName = null) {
        var action = GetAction(actionName);
        if (action == null) return null;

        schemeName ??= activeScheme;
        if (string.IsNullOrEmpty(schemeName)) return null;

        foreach (var scheme in actions.controlSchemes) {
            if (scheme.name != schemeName) continue;

            foreach (var binding in action.bindings) {
                if (!MatchesGroup(binding, scheme.bindingGroup)) continue;
                return StripDevice(binding.effectivePath);
            }
        }
        return null;
    }

    // Tracks whichever device last actually drove one of our actions - from here on, activeScheme
    // and GetGamepadBrand(0) reflect this confirmed device instead of guessing.
    private void OnActionChange(object obj, InputActionChange change) {
        if (change != InputActionChange.ActionPerformed) return;
        if (obj is not InputAction action || action.actionMap?.asset != actions) return;

        var device = action.activeControl?.device;
        if (device == null) return;
        _hasRealInput = true;
        _lastDevice = device;
    }

    private void OnDeviceChange(InputDevice device, InputDeviceChange change) {
        if (change == InputDeviceChange.Added) AssignSlot(device);
        else if (change == InputDeviceChange.Removed) RemoveFromSlots(device);
    }

    // Best-effort device to report from before any real input: a connected gamepad, or whatever
    // occupies slot 1 (keyboard/mouse) otherwise. Null if nothing is connected at all.
    private InputDevice GuessDevice() {
        foreach (var pair in _slots) {
            if (pair.Key == 1 || pair.Value.devices.Count == 0) continue;
            return pair.Value.devices[0];
        }
        return _slots.TryGetValue(1, out var keyboardMouse) && keyboardMouse.devices.Count > 0
            ? keyboardMouse.devices[0]
            : null;
    }

    // Keyboard/mouse always share slot 1. Other devices reclaim a matching disconnected slot,
    // or get a new one.
    private void AssignSlot(InputDevice device) {
        if (device is Keyboard or Mouse) {
            AddToSlot(GetOrCreateSlot(1), device, 1);
            return;
        }

        var deviceType = device.description.deviceClass;
        var gamepadBrand = GamepadBrandOf(device);

        var reclaimSlot = (int?)null;
        foreach (var pair in _slots) {
            if (pair.Key == 1 || pair.Value.devices.Count > 0) continue;
            if (pair.Value.info?.deviceType != deviceType || pair.Value.info?.gamepadBrand != gamepadBrand) continue;
            if (reclaimSlot == null || pair.Key < reclaimSlot) reclaimSlot = pair.Key;
        }

        var slot = reclaimSlot ?? _nextSlot++;
        AddToSlot(GetOrCreateSlot(slot), device, slot);
    }

    private SlotState GetOrCreateSlot(int slot) {
        if (!_slots.TryGetValue(slot, out var state)) {
            state = new SlotState();
            _slots[slot] = state;
        }
        return state;
    }

    private void AddToSlot(SlotState state, InputDevice device, int slot) {
        state.devices.Add(device);
        state.info = new SlotInfo {
            slot = slot,
            deviceType = device.description.deviceClass,
            gamepadBrand = GamepadBrandOf(device)
        };
    }

    private void RemoveFromSlots(InputDevice device) {
        foreach (var state in _slots.Values) state.devices.Remove(device);
    }

    private static GamepadBrand? GamepadBrandOf(InputDevice device) {
        return device switch {
            DualShockGamepad => GamepadBrand.PlayStation,
            SwitchProControllerHID => GamepadBrand.Switch,
            XInputController => GamepadBrand.Xbox,
            Gamepad => GamepadBrand.Generic,
            _ => null
        };
    }

    // Tries each device in the slot, returns the first with this action bound under its scheme.
    private TControl ResolveControl<TControl>(string actionName, int slot) where TControl : InputControl {
        if (!_slots.TryGetValue(slot, out var state)) return null;

        foreach (var device in state.devices) {
            var schemeName = GetSchemeForDevice(device);
            if (schemeName == null) continue;

            var path = GetBindingPath(actionName, schemeName);
            if (string.IsNullOrEmpty(path)) continue;

            var control = device.TryGetChildControl(path) as TControl;
            if (control != null) return control;
        }
        return null;
    }

    private string GetSchemeForDevice(InputDevice device) {
        foreach (var scheme in actions.controlSchemes) {
            if (scheme.SupportsDevice(device)) return scheme.name;
        }
        return null;
    }

    private static bool MatchesGroup(InputBinding binding, string bindingGroup) {
        if (string.IsNullOrEmpty(binding.groups)) return false;

        foreach (var group in binding.groups.Split(InputBinding.Separator)) {
            if (group == bindingGroup) return true;
        }
        return false;
    }

    private static string StripDevice(string path) {
        var deviceEnd = path.IndexOf('>');
        return deviceEnd >= 0 ? path[(deviceEnd + 2)..] : path;
    }

    /// <summary>
    /// Snapshot of one connected slot, returned by <see cref="GetSlots"/>.
    /// </summary>
    public class SlotInfo {
        /// <summary>The slot number. Keyboard and mouse always share slot 1; each other device gets its own.</summary>
        public int slot;
        /// <summary>The device class reported by Unity (e.g. "Gamepad", "Keyboard").</summary>
        public string deviceType;
        /// <summary>The gamepad brand, or null if the slot holds no gamepad.</summary>
        public GamepadBrand? gamepadBrand;
    }

    /// <summary>
    /// Identifies the hardware brand of a connected gamepad.
    /// </summary>
    public enum GamepadBrand {
        /// <summary>Xbox or generic XInput controller.</summary>
        Xbox,
        /// <summary>PlayStation DualShock or DualSense controller.</summary>
        PlayStation,
        /// <summary>Nintendo Switch Pro Controller.</summary>
        Switch,
        /// <summary>Gamepad detected by Unity but not matched to a known brand.</summary>
        Generic
    }

    // Live devices in a slot, wraps the public SlotInfo with the internal device list.
    private class SlotState {
        public readonly List<InputDevice> devices = new();
        public SlotInfo info;
    }
}
