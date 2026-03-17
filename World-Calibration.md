# World Orientation Calibration

## Context

The stick-handler app needs to map Pi camera pixel coordinates to real-world 3D positions. The camera is fixed; the floor has 4 known orange stickers (on labelled 8.5×11 paper: 1, 2, 3, 4). The user places 4 virtual cylinders over the stickers in MR passthrough — the cylinders' Unity world positions become the ground truth for a camera-extrinsic solve.

4 markers are required (not 3) because a full perspective homography has 8 degrees of freedom and needs exactly 4 point pairs to be uniquely determined. This correctly handles any camera mounting angle, not just directly overhead.

The `WorldCalibrationData` ScriptableObject is the **single source of truth** for the entire calibration pipeline. It gates progression: HSV Filter must pass before World Orientation can begin; World Orientation must complete before the game can be entered. No calibration step can be skipped.

**Implementation order**: `Server-Game-Loop.md` should be implemented **before** this feature. With the tracking WebSocket live, a 5th "blob indicator" cylinder shows in real time where the Pi thinks the green blob is in world space — giving immediate visual feedback on calibration accuracy as the user adjusts the cylinders.

---

## Calibration Gate Chain

| Gate | Condition | Blocks |
|------|-----------|--------|
| HSV satisfied | `orangeValid && greenValid` | World Orient button (greyed out) |
| World calibrated | `isCalibrated` | Game entry (greyed out / scene locked) |

Any system that needs calibration state holds a `[SerializeField]` ref to `WorldCalibrationData` — no singletons, no service locators.

---

## New Files

### `Assets/Scripts/WorldCalibrationData.cs` — ScriptableObject

Runtime data bus and persistence layer.

```csharp
[CreateAssetMenu(fileName = "WorldCalibrationData", menuName = "StickHandle/World Calibration Data")]
public class WorldCalibrationData : ScriptableObject
{
    // HSV gate — written by HsvFilterController after each scan
    public bool orangeValid;   // true when orange bank detects exactly 4 blobs
    public bool greenValid;    // true when green bank detects exactly 1 blob
    public bool HsvSatisfied => orangeValid && greenValid;

    // World orientation — written by WorldOrientationController on Save
    public Vector3 cylinderA;
    public Vector3 cylinderB;
    public Vector3 cylinderC;
    public Vector3 cylinderD;
    public float[] transformMatrix; // 3x3 homography (pixel→floor-plane), row-major, 9 elements
    public bool isCalibrated;

    private static string FilePath =>
        System.IO.Path.Combine(Application.persistentDataPath, "world_calibration.json");

    public void SaveToDisk() =>
        System.IO.File.WriteAllText(FilePath, JsonUtility.ToJson(this, prettyPrint: true));

    public void LoadFromDisk()
    {
        if (!System.IO.File.Exists(FilePath)) return;
        JsonUtility.FromJsonOverwrite(System.IO.File.ReadAllText(FilePath), this);
    }
}
```

Create asset at: `Assets/Resources/WorldCalibrationData.asset`

`JsonUtility.FromJsonOverwrite` deserialises directly into the existing SO instance — all inspector references remain valid across load.

**Bootstrap**: `CalibrationMenuController.Awake()` calls `_calibrationData.LoadFromDisk()` so the SO is hydrated before any UI renders. If no file exists, all fields default to false/zero and calibration must be performed.

---

### `Assets/Scripts/WorldOrientationController.cs`

Follows the same pattern as `HsvFilterController.cs` / `CalibrationBackButton.cs`:

- `[SerializeField]` refs to 4 cylinder Transforms (`_cylinderA`, `_cylinderB`, `_cylinderC`, `_cylinderD`)
- `[SerializeField] private WorldCalibrationData _calibrationData`
- `[SerializeField] private CalibrationMenuController _menuController`
- `[SerializeField] private string _serverUrl`
- `OnEnable`:
  - Queries `back-btn`, `save-btn`, `error-label` from UIDocument; wires named methods
  - Calls `_calibrationData.LoadFromDisk()` — restores cylinder positions and snaps GOs
  - Calls `POST /calibration/start` — Pi enters calibration tracking mode
  - Subscribes `selectExited` on each `FloorConstrainedGrabInteractable` → `OnCylinderReleased()`
- `OnDisable`:
  - Unregisters buttons and cylinder events
  - Calls `POST /calibration/end` — Pi returns to setup mode
- `Update()`: validates quadrilateral each frame — if any pairwise distance among the 4 cylinders < `_minSideLength` (default `0.3f` m), show `error-label` in red ("Spread cylinders further apart") and disable Save. Otherwise hide error and enable Save.
- `OnCylinderReleased()`: if quadrilateral is valid, `StartCoroutine(Recalc())` → `POST /calibration/recalc` with current positions → updates `_calibrationData.transformMatrix` → magenta indicator moves
- `SavePositions()`:
  1. Writes final positions + matrix into SO
  2. `_calibrationData.SaveToDisk()`
  3. Sets `_calibrationData.isCalibrated = true`

---

### `Assets/Scripts/BlobIndicatorController.cs`

Subscribes to the tracking WebSocket (`ws://<serverUrl>/tracking`). On each `{"x": px, "y": py}` message:

1. Reads `WorldCalibrationData.transformMatrix` (float[9], row-major 3×3 homography)
2. Applies: `worldPos = M * [px, py, 1]` → normalise → `new Vector3(x, 0f, z)`
3. Sets `blobIndicator.transform.position`

**⚠ Requires Server-Game-Loop.md** — tracking WS must exist before this is functional.

---

### `Assets/Scripts/FloorConstrainedGrabInteractable.cs`

Extends `XRGrabInteractable`. Overrides `ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase)` to clamp `transform.position.y = _floorY` (serialized float, default `0f`) after the base position update runs. Bakes the Y constraint into the interactable — no separate script fighting the grab system each frame.

Used by all 4 calibration cylinders in place of plain `XRGrabInteractable`.

---

### Billboard: use existing `LazyFollow` (XRI built-in)

`UnityEngine.XR.Interaction.Toolkit.UI.LazyFollow` (XRI 3.3.1, already in project). Add to each Label GO with `Rotation Mode = LookAtWithWorldUp`. No custom script needed.

---

### `Assets/UI/CylinderLabel.uxml`

Minimal — single label element. Uses `WorldSpacePanelSettings.asset` (`Assets/UI/`, `m_RenderMode: 1`). World space size ~80×80px. Label text ("1", "2", "3", "4") set in Inspector.

### `Assets/UI/CylinderLabel.uss`

Large centered bold white text, small dark semi-transparent background.

---

### `Assets/UI/WorldOrientation.uxml`

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" editor-extension-mode="False">
  <Style src="WorldOrientation.uss"/>
  <ui:VisualElement name="root" class="world-panel">
    <ui:VisualElement class="header">
      <ui:Button name="back-btn" text="← Back" class="bs back-btn" />
      <ui:Label text="World Orientation" class="title" />
    </ui:VisualElement>
    <ui:Label name="status-label" text="Place cylinders on orange stickers" class="status" />
    <ui:Label name="error-label" text="" class="error" />
    <ui:Button name="save-btn" text="Save Positions" class="bs save-btn" />
  </ui:VisualElement>
</ui:UXML>
```

### `Assets/UI/WorldOrientation.uss`

Reuses `.bs` button conventions from `CalibrationMenu.uss`. Panel anchored bottom-centre. `.error` class: red text, hidden when empty.

---

## Modified Files

### `Assets/UI/CalibrationMenu.uxml`

Add third button:
```xml
<ui:Button name="world-btn" text="World Orient" class="bs menu-btn" />
```

### `Assets/Scripts/CalibrationMenuController.cs`

- Add `[SerializeField] private GameObject worldOrientGO`
- Add `[SerializeField] private WorldCalibrationData _calibrationData`
- Add `private Button _worldBtn`
- `Awake()`: call `_calibrationData.LoadFromDisk()`
- `OnEnable`: query `"world-btn"`, register `_worldBtn.clicked += ShowWorldOrientation`
- `OnDisable`: unregister `_worldBtn.clicked -= ShowWorldOrientation`
- `Update()`: `_worldBtn?.SetEnabled(_calibrationData.HsvSatisfied)` — live gate
- `ShowMenu()`: also calls `worldOrientGO.SetActive(false)`
- New: `public void ShowWorldOrientation()` — deactivates menu/video/hsv, activates worldOrientGO

### `Assets/Scripts/HsvFilterController.cs`

After each capture/mask request, server returns blob count alongside the image. Display a validation badge per bank:

| Bank | Required | Valid | Invalid |
|------|----------|-------|---------|
| Orange | exactly 4 | "4 blobs ✓" (green) | "N blobs ✗ — must be 4" (red) |
| Green | exactly 1 | "1 blob ✓" (green) | "N blobs ✗ — must be 1" (red) |

- Writes `_calibrationData.orangeValid` and `_calibrationData.greenValid` after each scan
- Add `blob-count-label` element to each bank section in `HsvFilter.uxml`
- **Server side**: `/still/overlay` and `/still/mask` include `X-Blob-Count` response header (or JSON wrapper). Count uses the same contour/centroid detection as tracking.

---

## Rust Server Changes (`backend/server/src/main.rs`)

### New mode: Calibration Tracking Mode

- `POST /calibration/start` — Pi switches to World Calibration mode: same MJPEG + green blob centroid streaming pipeline as Tracking mode (see `Server-Game-Loop.md`). Streams `{"x":…,"y":…}` JSON over WebSocket at `/tracking`.
- `POST /calibration/end` — returns to setup/WebRTC mode.
- Orange blob detection available on-demand via `POST /calibration/recalc` (single frame from the live MJPEG stream).

### `POST /calibration/recalc`

Called by Quest on each cylinder release (if quadrilateral is valid).

**Request**:
```json
{ "cylinders": [{"label":1,"x":…,"z":…}, {"label":2,…}, {"label":3,…}, {"label":4,…}] }
```

**Server logic**:
1. Grab the most recent MJPEG frame from the live detection stream
2. Run orange HSV detection → 4 orange blob centroids (using saved orange preset from `hsv_presets.json`)
3. Sort blobs by angle from their centroid (clockwise from north) → stable spatial ordering independent of detection order
4. Match blobs to labels — **forward or reverse only**:
   - Compute signed area of triangle formed by blobs [0],[1],[2]: `cross_px = (b1−b0) × (b2−b0)`
   - Compute signed area of triangle formed by cylinders 1,2,3: `cross_world = (c1−c0) × (c2−c0)`
   - Same sign → forward: blob[i] → label[i+1] for i = 0..3
   - Different sign → reverse: blob[3−i] → label[i+1] for i = 0..3
5. `opencv::calib3d::find_homography` with the 4 matched pairs (4 pairs → system is exactly determined, full perspective homography)
6. Return 3×3 matrix row-major

**Response**:
```json
{ "matrix": [m00,m01,m02, m10,m11,m12, m20,m21,m22] }
```

**Error cases** (HTTP 422):
- Detected blob count ≠ 4 → Quest shows status message, user adjusts HSV
- No MJPEG frame available yet → HTTP 503, Quest retries

---

## Scene Hierarchy (create in Editor)

```
Calibration (existing — CalibrationMenuController)
├── CalibrationMenuGO     (existing)
├── VideoToolsGO          (existing)
├── HsvFilterGO           (existing)
└── WorldOrientationGO    [NEW] — WorldOrientationController + UIDocument(WorldOrientation.uxml)
    ├── Cylinder_A        [NEW] — Unity Cylinder primitive
    │   │                         2" dia = scale (0.0508, 0.0508, 0.0508)
    │   │                         Cyan #00BFFF, alpha 0.7, URP Lit Transparent
    │   │                         FloorConstrainedGrabInteractable + Rigidbody (Kinematic)
    │   │                         Track Position: true, Track Rotation: false
    │   └── Label_A       [NEW] — UIDocument (CylinderLabel.uxml + WorldSpacePanelSettings.asset)
    │                             ~0.15m above cylinder, LazyFollow (LookAtWithWorldUp)
    ├── Cylinder_B        [NEW] — same setup, label "2"
    ├── Cylinder_C        [NEW] — same setup, label "3"
    ├── Cylinder_D        [NEW] — same setup, label "4"
    └── BlobIndicator     [NEW] — Unity Cylinder primitive
                                  3" dia = scale (0.0762, 0.0762, 0.0762)
                                  Magenta #FF00FF, alpha 0.7, URP Lit Transparent
                                  NO XRGrabInteractable, NO Rigidbody, NO Collider
                                  BlobIndicatorController.cs (driven by tracking WS)
                                  ⚠ Requires Server-Game-Loop.md
```

**Cylinder spawn positions** (square cluster, ~0.3m spacing — user drags to actual stickers):
- A: (−0.15, 0, 0.15)
- B: (0.15, 0, 0.15)
- C: (0.15, 0, −0.15)
- D: (−0.15, 0, −0.15)

Number the physical stickers to match this clockwise layout when viewed from above so the winding check reliably resolves forward vs reverse in one step.

---

## Verification

1. Complete HSV Filter calibration (orange = 4 blobs, green = 1 blob) → World Orient button enables
2. Press World Orient → Pi enters calibration tracking mode, 4 cyan cylinders appear at floor level, magenta indicator visible
3. Grab cylinder → slides on floor only (Y stays 0), label billboards face headset
4. Spread all 4 cylinders until red error clears (all pairwise distances ≥ 0.3m)
5. Release a cylinder → Quest POSTs to `/calibration/recalc` → magenta indicator repositions
6. Align all 4 cylinders with stickers → Save enables → press Save
   - `world_calibration.json` written to `persistentDataPath`
   - `isCalibrated = true` → game entry unlocks
7. Press ← Back → returns to calibration menu, Pi exits calibration mode
8. Relaunch app → `LoadFromDisk()` in `Awake` restores all calibration state, no re-calibration needed
