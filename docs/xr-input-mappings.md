# XR Input Mappings

Source: https://docs.unity3d.com/6000.3/Documentation/Manual/xr_input.html#XRInputMappings

| **InputFeatureUsage** | **Feature type** | **Legacy input index** (left/right) | **WMR** | **Oculus** | **GearVR** | **Daydream** | **OpenVR (Full)** | **Vive** | **Oculus via OpenVR** | **WMR via OpenVR** |
|---|---|---|---|---|---|---|---|---|---|---|
| `primary2DAxis` | 2D axis | (1,2)/(4,5) | Touchpad | Joystick | Touchpad | Touchpad | Trackpad/Joystick | Trackpad | Joystick | Joystick |
| `trigger` | Axis | 9/10 | Trigger | Trigger | Trigger | Trigger | Trigger | Trigger | Trigger | Trigger |
| `grip` | Axis | 11/12 | Grip | Grip | Bumper | Grip | Grip | Grip | Grip | |
| `secondary2DAxis` | 2D axis | (17,18)/(19,20) | Joystick | Touchpad | | | | | | |
| `secondary2DAxisClick` | Button | 18/19 | Joystick - Click | | | | | | | |
| `primaryButton` | Button | 2/0 | [X/A] - Press | App | Primary | Primary (sandwich button) | Primary (Y/B) | Menu | | |
| `primaryTouch` | Button | 12/10 | [X/A] - Touch | | | | | | | |
| `secondaryButton` | Button | 3/1 | [Y/B] - Press | Alternate | Alternate (X/A) | | | | | |
| `secondaryTouch` | Button | 13/11 | [Y/B] - Touch | | | | | | | |
| `gripButton` | Button | 4/5 | Grip - Press | Grip - Press | Grip - Press | Grip - Press | Grip - Press | Grip - Press | | |
| `triggerButton` | Button | 14/15 | Trigger - Press | Trigger - Press | Trigger - Press | Trigger - Press | Trigger - Press | Trigger - Press | Trigger - Touch | Trigger - Press |
| `menuButton` | Button | 6/7 | Menu | Start (left only) | | | | | | |
| `primary2DAxisClick` | Button | 8/9 | Touchpad - Click | Thumbstick - Press | Touchpad - Press | Touchpad - Press | Trackpad/Joystick - Press | Trackpad - Press | Joystick - Press | Touchpad - Press |
| `primary2DAxisTouch` | Button | 16/17 | Touchpad - Touch | Thumbstick - Touch | Touchpad - Touch | Touchpad - Touch | Trackpad/Joystick - Touch | Trackpad - Touch | Joystick - Touch | Touchpad - Touch |
| `batteryLevel` | Axis | | Battery level | | | | | | | |
| `userPresence` | Button | | User presence | User presence | | | | | | |

## Oculus / Meta Quest Quick Reference

| InputFeatureUsage | Quest Physical Input |
|---|---|
| `primary2DAxis` | Thumbstick (analog) |
| `primary2DAxisClick` | Thumbstick press |
| `primary2DAxisTouch` | Thumbstick touch |
| `trigger` | Trigger (analog) |
| `triggerButton` | Trigger press |
| `grip` | Grip (analog) |
| `gripButton` | Grip press |
| `primaryButton` | App button |
| `secondaryButton` | Alternate button (X on left, A on right) |
| `menuButton` | Start (left controller only) |
| `userPresence` | User presence |

> **Note**: On Quest, `primaryButton` maps to the **App** button, not A/X.
> A (right) and X (left) are `secondaryButton` (`Alternate`).
> This differs from WMR where `primaryButton` = X/A - Press.
