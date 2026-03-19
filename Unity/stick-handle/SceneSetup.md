# World Calibration — Scene Setup Guide

Complete wiring instructions for the World Calibration feature. Assumes the Calibration root
hierarchy already exists (CalibrationMenuGO, VideoToolsGO, HsvFilterGO). All new work is under
WorldOrientationGO.

---

## 1. Create Assets First

Do these before touching the scene — scripts and Inspector slots need the assets to exist.

### 1a. WorldCalibrationData ScriptableObject

1. In the Project window, navigate to `Assets/Resources/` (create the folder if it doesn't exist).
2. Right-click → **Create → StickHandle → World Calibration Data**.
3. Name it exactly `WorldCalibrationData`.

> All calibration state flows through this asset. Every script that needs calibration data gets a
> reference to this single asset.

### 1b. Cylinder Material (Cyan, Transparent)

1. Right-click in the Project window where you keep materials → **Create → Material**.
   Name it `Cylinder_Calibration`.
2. In the Inspector:
   - **Shader**: `Universal Render Pipeline/Lit`
   - **Surface Type**: `Transparent`
   - **Base Map** colour: `#00BFFF` (R: 0, G: 191, B: 255), **Alpha: 178** (≈ 0.7)
   - Everything else: defaults.

### 1c. BlobIndicator Material (Magenta, Transparent)

1. Right-click → **Create → Material**. Name it `BlobIndicator`.
2. In the Inspector:
   - **Shader**: `Universal Render Pipeline/Lit`
   - **Surface Type**: `Transparent`
   - **Base Map** colour: `#FF00FF` (R: 255, G: 0, B: 255), **Alpha: 178** (≈ 0.7)
   - Everything else: defaults.

### 1d. WorldSpacePanelSettings Asset

Already exists at `Assets/UI/WorldSpacePanelSettings.asset` — use it as-is. No changes needed.

---

## 2. Create WorldOrientationGO

### 2a. Add the GO

1. In the **Hierarchy**, find the `Calibration` root GO.
2. Right-click it → **Create Empty**. Name it `WorldOrientationGO`.
3. **Transform**: Position `(0, 0, 0)`, Rotation `(0, 0, 0)`, Scale `(1, 1, 1)`.
4. **Uncheck the active checkbox** — it starts inactive and is activated by `CalibrationMenuController`.

### 2b. Add UIDocument

1. With `WorldOrientationGO` selected, **Add Component → UI Document**.
2. **Source Asset**: `Assets/UI/WorldOrientation.uxml`.
3. **Panel Settings**: `Assets/UI/WorldSpacePanelSettings.asset`.

### 2c. Add WorldOrientationController

1. **Add Component → WorldOrientationController**.
2. Wire **Ui Document** immediately: drag the `UIDocument` component (on this same GO) into the slot.
3. Leave the remaining fields for §5 after the child GOs exist.

---

## 3. Create the Four Calibration Cylinders

Repeat steps 3a–3f four times, substituting the values from this table:

| | GO name | Label text | Spawn position |
|---|---|---|---|
| A | `Cylinder_A` | `1` | `(−0.15, 0, 0.15)` |
| B | `Cylinder_B` | `2` | `(0.15, 0, 0.15)` |
| C | `Cylinder_C` | `3` | `(0.15, 0, −0.15)` |
| D | `Cylinder_D` | `4` | `(−0.15, 0, −0.15)` |

### 3a. Create cylinder primitive

1. Right-click `WorldOrientationGO` → **3D Object → Cylinder**.
2. Rename to the GO name in the table.
3. **Transform**:
   - **Position**: see table above.
   - **Scale**: `(0.0508, 0.0508, 0.0508)` — 2-inch (~5 cm) diameter.

### 3b. Assign material

1. In the **Mesh Renderer** component, expand **Materials**.
2. Set **Element 0** to `Cylinder_Calibration`.

### 3c. Add Rigidbody

1. **Add Component → Rigidbody**.
2. **Is Kinematic**: ✅ checked.
3. **Use Gravity**: ☐ unchecked.
4. All other fields: defaults.

### 3d. Add FloorConstrainedGrabInteractable

1. **Add Component → Floor Constrained Grab Interactable**
   (search by name in the Add Component dialog).
2. Set these fields:
   - **Floor Y**: `0`
   - **Track Position**: ✅ checked
   - **Track Rotation**: ☐ unchecked
   - **Movement Type**: `Kinematic`
   - **Use Dynamic Attach**: ☐ unchecked
   - Everything else: defaults.

> `FloorConstrainedGrabInteractable` extends `XRGrabInteractable`. Do **not** also add a plain
> `XRGrabInteractable` — one interactable per GO.

### 3e. Collider

The cylinder primitive already has a **Capsule Collider**. Leave it as-is.

### 3f. Create Label child GO

1. Right-click the cylinder GO → **Create Empty**. Name it `Label_A` (or B/C/D).
2. **Transform**:
   - **Position**: `(0, 0.15, 0)` — 15 cm above the cylinder.
   - **Rotation**: `(0, 0, 0)`, **Scale**: `(1, 1, 1)`.
3. **Add Component → UI Document**.
   - **Source Asset**: `Assets/UI/CylinderLabel.uxml`.
   - **Panel Settings**: `Assets/UI/WorldSpacePanelSettings.asset`.
4. **Add Component → Cylinder Label Controller**.
   - **Ui Document**: drag the `UIDocument` component from this same GO.
   - **Text**: `1` / `2` / `3` / `4` per the table.
5. **Add Component → Lazy Follow**
   (`UnityEngine.XR.Interaction.Toolkit.UI.LazyFollow`).
   - **Rotation Mode**: `Look At With World Up`.
   - Leave all other fields at defaults.

---

## 4. Create BlobIndicator

1. Right-click `WorldOrientationGO` → **3D Object → Cylinder**.
2. Rename it `BlobIndicator`.
3. **Transform**:
   - **Position**: `(0, 0, 0)` — moved at runtime.
   - **Scale**: `(0.0762, 0.0762, 0.0762)` — 3-inch (~7.6 cm) diameter.
4. **Mesh Renderer → Materials → Element 0**: `BlobIndicator` (magenta).
5. **Remove the Capsule Collider**: right-click the component header → **Remove Component**.
6. Do **not** add Rigidbody or any interactable.
7. **Add Component → Blob Indicator Controller**.
   - **Calibration Data**: `Assets/Resources/WorldCalibrationData.asset`.
   - **Server Url**: `test-pi` (match the hostname used elsewhere in the project).

> `BlobIndicator` requires the `/tracking` WebSocket from Server-Game-Loop.md.
> It silently no-ops if the server isn't in tracking mode.

---

## 5. Wire WorldOrientationController

Select `WorldOrientationGO`. Fill in the remaining **WorldOrientationController** fields:

| Field | Value |
|---|---|
| **Calibration Data** | `Assets/Resources/WorldCalibrationData.asset` |
| **Menu Controller** | `CalibrationMenuController` component on the `Calibration` root GO |
| **Server Url** | `test-pi` |
| **Min Side Length** | `0.3` |
| **Cylinder A** | `Cylinder_A` GO (Unity resolves the `FloorConstrainedGrabInteractable` component) |
| **Cylinder B** | `Cylinder_B` GO |
| **Cylinder C** | `Cylinder_C` GO |
| **Cylinder D** | `Cylinder_D` GO |

*(UIDocument was wired in §2c.)*

---

## 6. Wire CalibrationMenuController

Select the GO that has `CalibrationMenuController`.

New fields added by this feature:

| Field | Value |
|---|---|
| **World Orient GO** | `WorldOrientationGO` |
| **Calibration Data** | `Assets/Resources/WorldCalibrationData.asset` |

Confirm the existing fields are still set correctly — they will have been cleared if the script
was recompiled:

| Field | Value |
|---|---|
| **Calibration Menu GO** | the GO that has `CalibrationMenuController` on it |
| **Video Tools GO** | `VideoToolsGO` |
| **Hsv Filter GO** | `HsvFilterGO` |

---

## 7. Wire HsvFilterController

Select the GO that has `HsvFilterController`.

New field:

| Field | Value |
|---|---|
| **Calibration Data** | `Assets/Resources/WorldCalibrationData.asset` |

---

## 8. Verify Default Active States

| GO | Active at scene open |
|---|---|
| `Calibration` root | ✅ active |
| `CalibrationMenuGO` | ✅ active |
| `VideoToolsGO` | ☐ inactive |
| `HsvFilterGO` | ☐ inactive |
| `WorldOrientationGO` | ☐ inactive |

`CalibrationMenuController.OnEnable()` calls `ShowMenu()` which enforces these states at runtime,
but set them correctly at edit time to avoid one-frame flashes.

---

## 9. XRI Interactor Setup (if not already done)

The cylinders need a grab interactor on the controller rig. If the scene already supports grabbing
objects, nothing needs to change.

If not:

1. On the XR controller GO, confirm `XRRayInteractor` or `XRDirectInteractor` is present with
   **Select** action wired.
2. Confirm **Interaction Layer Mask** on the interactor includes the layer the cylinders are on
   (default: `Everything`).

---

## 10. Final Checklist

- [ ] `WorldCalibrationData.asset` exists at `Assets/Resources/WorldCalibrationData.asset`
- [ ] `Cylinder_Calibration` material: URP Lit, Transparent, cyan `#00BFFF`, alpha ≈ 0.7
- [ ] `BlobIndicator` material: URP Lit, Transparent, magenta `#FF00FF`, alpha ≈ 0.7
- [ ] `WorldOrientationGO` starts **inactive**
- [ ] `WorldOrientationGO`: UIDocument → `WorldOrientation.uxml` + `WorldSpacePanelSettings`
- [ ] Each `Cylinder_X`: FloorConstrainedGrabInteractable, Rigidbody (Kinematic, no gravity), Capsule Collider, `Cylinder_Calibration` material
- [ ] Each `Label_X`: UIDocument → `CylinderLabel.uxml` + `WorldSpacePanelSettings`, CylinderLabelController (correct text), LazyFollow (LookAtWithWorldUp)
- [ ] `BlobIndicator`: no collider, no rigidbody, `BlobIndicator` material, BlobIndicatorController wired
- [ ] `WorldOrientationController`: all fields wired (UIDocument, CalibrationData, MenuController, ServerUrl, 4× Cylinder)
- [ ] `CalibrationMenuController`: `worldOrientGO` and `calibrationData` wired
- [ ] `HsvFilterController`: `calibrationData` wired
- [ ] Play mode: Calibration menu shows 3 buttons (Video Tools, HSV Filter, World Orient)
- [ ] World Orient button greyed out until HSV Filter validates orange (4 blobs) and green (1 blob)
