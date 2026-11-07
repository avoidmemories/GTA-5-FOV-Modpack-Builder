# FOV Modpack Builder

A desktop application for building GTA 5 OIV modpacks that modify camera parameters in `cameras.ymt`.

## Features

- Simple mode with three main controls: First person, In vehicle, and Third person.
- Advanced mode with individual camera parameters.
- Import `cameras.ymt`, `.oiv`, and `.zip` files.
- Yellow highlighting for values that differ from the imported baseline.
- Undo / Redo: `Ctrl+Z` and `Ctrl+Shift+Z`.
- Export as OIV, ZIP, or `cameras.ymt` only.
- After a build, the `Open folder` button opens the folder containing the created modpack.
- Hovering over a parameter name shows its exact metadata type and a detailed description; clicking copies only the type to the clipboard.

> **First-person FOV note:** For the modded first-person FOV to take effect, lower GTA V's in-game FOV slider below 100% (or set it to the minimum): `Esc → Settings → Camera → First Person On Foot Field of View`.

## Build

Requirements: Windows and .NET SDK 10.0 or newer with WPF support.

```powershell
dotnet build ".\FOV Modpack Builder.csproj" -c Release
```

The compiled application will be located at:

```
bin\Release\net10.0-windows\FOV Modpack Builder.exe
```

`cameras_original.ymt`, the JSON registries, and `Assets\Templates\oiv_template.zip` must remain alongside the project: they are required while building and running the application.

## Advanced parameters

### First person — on foot

| Name in the app | Type / field | Description |
|---|---|---|
| Aim — first person (on foot) | `camFirstPersonPedAimCameraMetadata / BaseFov` | Base first-person FOV while aiming on foot. The file contains several weapon and situation profiles. |
| Shooter base — first person | `camFirstPersonShooterCameraMetadata / BaseFov` | Main first-person shooter-camera FOV; this is not ADS or the phone camera. |
| Aim (ADS) — first person | `camFirstPersonShooterCameraMetadata / AimFov` | FOV while aiming down sights. A lower value gives a stronger zoom. |
| Phone camera — first person | `camFirstPersonShooterCameraMetadata / PhoneFov` | First-person phone-camera profile FOV. |
| Aim + head tracking — FP | `camFirstPersonHeadTrackingAimCameraMetadata / BaseFov` | First-person aiming-profile FOV with head-tracking behavior. |
| Cinematic idle — horizontal (FP) | `camCinematicFirstPersonIdleCameraMetadata / PlayerHorizontalFov` | Horizontal FOV of the first-person idle camera. |
| Cinematic idle — vertical (FP) | `camCinematicFirstPersonIdleCameraMetadata / PlayerVerticalFov` | Vertical FOV of the idle camera. Change carefully: it affects the horizontal-to-vertical viewing ratio. |
| Cinematic idle — zoom TA (FP) | `camCinematicFirstPersonIdleCameraMetadata / ZoomTAFov` | FOV for the Zoom TA state. A value of `-1` usually means the profile does not define its own FOV. |

### Third person — on foot

| Name in the app | Type / field | Description |
|---|---|---|
| On foot — third person (follow) | `camFollowPedCameraMetadata / BaseFov` | Base FOV of the normal third-person follow camera on foot. |
| Aim — third person (on foot) | `camThirdPersonPedAimCameraMetadata / BaseFov` | Base third-person FOV while aiming on foot. |
| Aim focal emulation | `camThirdPersonPedAimCameraMetadata / BaseFovToEmulateWithFocalLengthMultiplier` | Reference FOV for focal-length multiplier emulation. It is not always the direct on-screen FOV. |
| Aim in cover — third person | `camThirdPersonPedAimInCoverCameraMetadata / BaseFov` | Base FOV while aiming from cover. |
| Cover aim focal emulation | `camThirdPersonPedAimInCoverCameraMetadata / BaseFovToEmulateWithFocalLengthMultiplier` | Focal-length reference for aiming from cover. |
| Assisted aim | `camThirdPersonPedAssistedAimCameraMetadata / BaseFov` | FOV of the assisted-aim profile. Its use depends on the aiming mode and situation. |
| Assisted aim focal emulation | `camThirdPersonPedAssistedAimCameraMetadata / BaseFovToEmulateWithFocalLengthMultiplier` | Focal-length reference for assisted aim. |
| Melee aim — third person | `camThirdPersonPedMeleeAimCameraMetadata / BaseFov` | Third-person camera FOV in the melee-aim context. |
| Parachute / freefall | `camFollowParachuteCameraMetadata / BaseFov` | Base FOV of the follow camera while parachuting and free-falling. |

### Vehicles and first person in vehicle

| Name in the app | Type / field | Description |
|---|---|---|
| In vehicle — third person (follow) | `camFollowVehicleCameraMetadata / BaseFov` | Base FOV of the normal third-person follow camera in vehicles. |
| Mounted-part camera | `camCinematicMountedPartCameraMetadata / BaseFov` | FOV of a camera attached to a vehicle or object part. Exact usage depends on the active profile. |
| Driveby aim — from vehicle TP | `camThirdPersonVehicleAimCameraMetadata / BaseFov` | Third-person FOV while performing a drive-by or aiming from a vehicle. |
| Driveby focal emulation | `camThirdPersonVehicleAimCameraMetadata / BaseFovToEmulateWithFocalLengthMultiplier` | Focal-length reference for the drive-by camera; not necessarily the direct FOV. |
| Cinematic vehicle tracking | `camCinematicVehicleTrackingCameraMetadata / Fov` | FOV of the cinematic vehicle-tracking profile, not the regular driving follow camera. |
| Mounted vehicle camera | `camCinematicMountedCameraMetadata / BaseFov` | FOV of the mounted vehicle-profile set. The file contains many entries, so this parameter can affect several vehicle situations. |

### Cinematic / gameplay moments

| Name in the app | Type / field | Description |
|---|---|---|
| Mounted driveby offset | `camCinematicMountedCameraMetadata / FoVOffsetForDriveby` | Additional FOV offset for the mounted profile during a drive-by. A positive value widens the view relative to the profile's `BaseFov`. |
| Ped close-up | `camCinematicPedCloseUpCameraMetadata / BaseFov` | FOV of the cinematic close-up camera on a character. It is not confirmed that this controls the normal WASTED screen. |
| Cinematic two-shot | `camCinematicTwoShotCameraMetadata / BaseFov` | FOV of a camera that frames two characters at once. A higher value makes the shot wider. |
| Two-shot FOV speed | `camCinematicTwoShotCameraMetadata / FovScalingSpeed` | Speed of automatic FOV adjustment while the two-shot keeps both characters in frame. |
| Position camera zoom | `camCinematicPositionCameraMetadata / MaxZoomFov` | FOV at the close end of the position-camera zoom range. A lower value appears more zoomed in. |
| Character switch camera | `camSwitchCameraMetadata / Fov` | FOV of the transition camera when switching characters in Story Mode. It does not affect switching camera views with the V key. |

### Cinematic zoom ranges

General rule: a `wide FOV` is a wider, less zoomed-in shot; a `close FOV` is a closer, more zoomed-in shot. `speed start/end` are the lower and upper speed thresholds for dynamic FOV. `FOV smoothing` controls the stiffness or speed of the automatic FOV transition.

| Name in the app | Type / field | Description |
|---|---|---|
| Helicopter: wide FOV | `camCinematicHeliChaseCameraMetadata / MinZoomFov` | Wide end of the cinematic helicopter-camera zoom range. |
| Helicopter: close FOV | `camCinematicHeliChaseCameraMetadata / MaxZoomFov` | Close end of the helicopter-camera zoom range. |
| Helicopter: close FOV (fast) | `camCinematicHeliChaseCameraMetadata / HighSpeedMaxZoomFov` | Close helicopter-camera FOV at high speed. |
| Helicopter: extra FOV change | `camCinematicHeliChaseCameraMetadata / ExtraZoomFovDelta` | Additional amount of FOV change during the camera's zoom behavior. |
| Helicopter: FOV smoothing | `camCinematicHeliChaseCameraMetadata / FovSpringConstant` | Smoothing strength of automatic helicopter-camera FOV movement. |
| Helicopter: FOV speed start | `camCinematicHeliChaseCameraMetadata / MinSpeedForHighFovScalar` | Speed at which high-speed dynamic FOV begins. |
| Helicopter: FOV speed end | `camCinematicHeliChaseCameraMetadata / MaxSpeedForHighFovScalar` | Speed at which high-speed dynamic FOV reaches its full effect. |
| Tracking cam: wide FOV | `camCinematicCameraManCameraMetadata / MinZoomFov` | Wide end of the cinematic tracking-camera zoom range. |
| Tracking cam: close FOV | `camCinematicCameraManCameraMetadata / MaxZoomFov` | Close end of the tracking-camera zoom range. |
| Tracking cam: close FOV (fast) | `camCinematicCameraManCameraMetadata / HighSpeedMaxZoomFov` | Close tracking-camera FOV at high speed. |
| Tracking cam: FOV smoothing | `camCinematicCameraManCameraMetadata / FovSpringConstant` | FOV smoothing strength of the tracking camera. |
| Tracking cam: FOV speed start | `camCinematicCameraManCameraMetadata / MinSpeedForHighFovScalar` | Lower speed threshold for dynamic FOV. |
| Tracking cam: FOV speed end | `camCinematicCameraManCameraMetadata / MaxSpeedForHighFovScalar` | Upper speed threshold for dynamic FOV. |
| Tracking cam: wide bounds scale | `camCinematicCameraManCameraMetadata / BoundsRadiusScaleForMinFov` | Radius/bounds multiplier used when selecting the wide FOV. |
| Tracking cam: close bounds scale | `camCinematicCameraManCameraMetadata / BoundsRadiusScaleForMaxFov` | Radius/bounds multiplier used when selecting the close FOV. |
| Train: wide FOV | `camCinematicTrainTrackingCameraMetadata / MinZoomFov` | Wide end of the train tracking-camera zoom range. |
| Train: close FOV | `camCinematicTrainTrackingCameraMetadata / MaxZoomFov` | Close end of the train tracking-camera zoom range. |
| Train: close FOV (fast) | `camCinematicTrainTrackingCameraMetadata / HighSpeedMaxZoomFov` | Close train tracking-camera FOV at high speed. |
| Train: FOV smoothing | `camCinematicTrainTrackingCameraMetadata / FovSpringConstant` | FOV smoothing strength of the train camera. |
| Train: FOV speed start | `camCinematicTrainTrackingCameraMetadata / MinSpeedForHighFovScalar` | Lower speed threshold for dynamic train-camera FOV. |
| Train: FOV speed end | `camCinematicTrainTrackingCameraMetadata / MaxSpeedForHighFovScalar` | Upper speed threshold for dynamic train-camera FOV. |
| Train: wide bounds scale | `camCinematicTrainTrackingCameraMetadata / BoundsRadiusScaleForMinFov` | Bounds multiplier for the train camera's wide FOV. |
| Train: close bounds scale | `camCinematicTrainTrackingCameraMetadata / BoundsRadiusScaleForMaxFov` | Bounds multiplier for the train camera's close FOV. |
| Group cam: close-FOV limit | `camCinematicGroupCameraMetadata / MinFovToZoom` | Minimum FOV to which the group camera can zoom in. |
| Group cam: FOV smoothing | `camCinematicGroupCameraMetadata / FovSpringConstant` | FOV smoothing of the group camera. |
| Stunt cam: close-FOV limit | `camCinematicStuntCameraMetadata / MinFovToZoom` | Minimum FOV to which the stunt camera can zoom in. |

### FOV dynamics

| Name in the app | Type / field | Description |
|---|---|---|
| On foot: FOV scaling limit | `camFollowPedCameraMetadata / MaxBaseFovScaling` | Limit for automatic base-FOV scaling of the on-foot follow camera; not the base angle itself. |
| Vehicle: FOV scaling limit | `camFollowVehicleCameraMetadata / MaxBaseFovScaling` | Limit for automatic base-FOV scaling of the vehicle follow camera. |
| Parachute: FOV scaling limit | `camFollowParachuteCameraMetadata / MaxBaseFovScaling` | Limit for automatic base-FOV scaling of the parachute camera. |
| Lock-on camera: FOV multiplier | `camHintHelperMetadata / FovScalar` | FOV multiplier for the hint/lock-on camera helper, not a direct viewing angle. |
| Free camera: FOV acceleration | `camFreeCameraMetadata / FovAcceleration` | Acceleration of FOV changes in the internal free-camera profile; not a direct FOV. |
| Free camera: max FOV speed | `camFreeCameraMetadata / MaxFovSpeed` | Maximum FOV change speed in the internal free-camera profile; not the maximum angle. |

### Spline / misc

| Name in the app | Type / field | Description |
|---|---|---|
| Spline cinematic — default FOV | `camTimedSplineCameraMetadata / DefaultFov` | Default FOV of the timed-spline cinematic profile. |
| Custom spline — default FOV | `camCustomTimedSplineCameraMetadata / DefaultFov` | Default FOV of the custom timed-spline profile. |
| Rounded spline — default FOV | `camRoundedSplineCameraMetadata / DefaultFov` | Default FOV of the rounded-spline profile. |
| Smoothed spline — default FOV | `camSmoothedSplineCameraMetadata / DefaultFov` | Default FOV of the smoothed-spline profile. |

### Zoom motion blur

| Name in the app | Type / field | Description |
|---|---|---|
| FP aim: blur threshold (min) | `camFirstPersonPedAimCameraMetadata / ZoomMotionBlurMinFovDelta` | Minimum FOV difference at which zoom motion blur starts to apply. |
| FP aim: blur threshold (max) | `camFirstPersonPedAimCameraMetadata / ZoomMotionBlurMaxFovDelta` | FOV difference at which zoom motion blur reaches its maximum range. |
| FP aim: max blur strength | `camFirstPersonPedAimCameraMetadata / ZoomMotionBlurMaxStrengthForFov` | Maximum zoom motion-blur strength. |
| FP shooter: blur threshold (min) | `camFirstPersonShooterCameraMetadata / ZoomMotionBlurMinFovDelta` | Lower blur threshold while changing the shooter-camera FOV. |
| FP shooter: blur threshold (max) | `camFirstPersonShooterCameraMetadata / ZoomMotionBlurMaxFovDelta` | Upper blur threshold while changing the shooter-camera FOV. |
| FP shooter: max blur strength | `camFirstPersonShooterCameraMetadata / ZoomMotionBlurMaxStrengthForFov` | Maximum blur strength for the shooter camera. |
| FP head track: blur threshold (min) | `camFirstPersonHeadTrackingAimCameraMetadata / ZoomMotionBlurMinFovDelta` | Lower blur threshold while changing the head-tracking aim-camera FOV. |
| FP head track: blur threshold (max) | `camFirstPersonHeadTrackingAimCameraMetadata / ZoomMotionBlurMaxFovDelta` | Upper blur threshold while changing the head-tracking aim-camera FOV. |
| FP head track: max blur strength | `camFirstPersonHeadTrackingAimCameraMetadata / ZoomMotionBlurMaxStrengthForFov` | Maximum blur strength for the head-tracking aim camera. |

### Vehicle camera distance

`Forced distance` and `Base distance` are ranges, so each has separate minimum and maximum controls. Keep each minimum less than or equal to its corresponding maximum. The original `Forced distance` values are `-1`; this means that no custom range is forced.

| Name in the app | Type / field | Description |
|---|---|---|
| Safe distance scale | `camFollowVehicleCameraMetadata / MinSafeOrbitDistanceScalingForExtensions` | Scales the minimum safe camera distance around vehicle extensions, trailers, and unusual bodywork. |
| Forced distance — min | `camFollowVehicleCameraMetadata / CustomOrbitDistanceLimitsToForce` (minimum) | Lower endpoint of a custom forced distance range. Set together with its maximum. |
| Forced distance — max | `camFollowVehicleCameraMetadata / CustomOrbitDistanceLimitsToForce` (maximum) | Upper endpoint of a custom forced distance range. A larger value can keep the camera farther from the vehicle. |
| Distance limit smoothing | `camFollowVehicleCameraMetadata / OrbitDistanceLimitSpringConstant` | Controls how firmly and quickly the camera moves toward its permitted distance; it does not increase the distance by itself. |
| Base distance — min | `camFollowVehicleCameraMetadata / OrbitDistanceLimitsForBasePosition` (minimum) | Minimum normal orbit distance from the vehicle. |
| Base distance — max | `camFollowVehicleCameraMetadata / OrbitDistanceLimitsForBasePosition` (maximum) | Maximum normal orbit distance from the vehicle. Increase this to allow the third-person camera to move farther back. |
