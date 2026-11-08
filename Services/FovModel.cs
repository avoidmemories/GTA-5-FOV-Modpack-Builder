using System.IO;
using System.Text.Json;

namespace FovApp.Services;

/// <summary>
/// One patchable FOV parameter: a (type, field) pair with every instance position
/// in cameras.ymt. The value model is explicit:
///
///   final = default + personal + global
///
/// where `personal` is what the user moved this row's slider by and `global` is
/// the Global Shift slider. Moving the global slider never rewrites personal
/// offsets, so the order of operations (global, then personal, then global
/// again) is unambiguous: the same final value results either way.
/// </summary>
public class FovParam
{
    public required string Type { get; init; }
    public required string Field { get; init; }
    public required string Label { get; init; }
    public string Description { get; init; } = "";
    public required string Group { get; init; }

    /// <summary>Absolute file offsets of every instance of this field.</summary>
    public required List<int> Offsets { get; init; }

    /// <summary>Original value per instance (kept for reference).</summary>
    public required List<float> Defaults { get; init; }

    /// <summary>Slider left edge = the default value of the first instance.</summary>
    public float BaseDefault => Defaults.Count > 0 ? Defaults[0] : 0;

    private readonly List<float> _personal = new();
    private float _global;

    /// <summary>Final values, one per instance: default + personal + global.</summary>
    public IEnumerable<float> Values => Offsets.Select((_, i) => Defaults[i] + PersonalAt(i) + _global);

    public float Personal => _personal.Count > 0 ? _personal[0] : 0;

    /// <summary>Current global shift applied to this parameter.</summary>
    public float PersonalGlobal() => _global;

    private float PersonalAt(int i) => _personal.Count > i ? _personal[i] : _personal.Count > 0 ? _personal[0] : 0;

    public void EnsureInit()
    {
        if (_personal.Count > Offsets.Count)
            _personal.RemoveRange(Offsets.Count, _personal.Count - Offsets.Count);
        else if (_personal.Count < Offsets.Count)
            _personal.AddRange(Enumerable.Repeat(0f, Offsets.Count - _personal.Count));
    }

    /// <summary>Set the personal offset (uniform across instances, preserving
    /// their relative deltas because each instance keeps its own default).</summary>
    public void SetPersonal(float personal)
    {
        EnsureInit();
        for (int i = 0; i < _personal.Count; i++)
            _personal[i] = personal;
    }

    public void SetGlobal(float global) => _global = global;

    public void Reset()
    {
        _personal.Clear();
        _personal.AddRange(Enumerable.Repeat(0f, Offsets.Count));
        _global = 0;
    }

    /// <summary>Updates the file-specific baseline while preserving the user's
    /// personal and global adjustments.</summary>
    public void Rebase(FovParam source)
    {
        Offsets.Clear();
        Offsets.AddRange(source.Offsets);
        Defaults.Clear();
        Defaults.AddRange(source.Defaults);
        EnsureInit();
    }
}

public class RegistryEntry
{
    public string Type { get; set; } = "";
    public int Stride { get; set; }
    public int Count { get; set; }
    public int Off { get; set; }
    public List<RegistryField> Fields { get; set; } = new();
}

public class RegistryField
{
    public string Name { get; set; } = "";
    public int Off { get; set; }
}

public static class FovModel
{
    public const double MaxSlider = 200.0;

    private static readonly string[] Banned =
    {
        "camScriptedCameraMetadata", "camReplayPresetCameraMetadata", "camReplayFreeCameraMetadata",
        "camScriptedFlyCameraMetadata", "camInconsistentBehaviourZoomHelperMetadata",
    };

    // (type, field) -> (label, group)
    public static readonly Dictionary<(string type, string field), (string label, string group)> Labels = new()
    {
        { ("camFirstPersonPedAimCameraMetadata", "BaseFov"), ("Aim — first person (on foot)", "fp") },
        { ("camFirstPersonShooterCameraMetadata", "BaseFov"), ("Shooter base — first person", "fp") },
        { ("camFirstPersonShooterCameraMetadata", "AimFov"), ("Aim (ADS) — first person", "fp") },
        { ("camFirstPersonShooterCameraMetadata", "PhoneFov"), ("Phone camera — first person", "fp") },
        { ("camFirstPersonHeadTrackingAimCameraMetadata", "BaseFov"), ("Aim + head tracking — FP", "fp") },
        { ("camCinematicFirstPersonIdleCameraMetadata", "PlayerHorizontalFov"), ("Cinematic idle — horizontal (FP)", "fp") },
        { ("camCinematicFirstPersonIdleCameraMetadata", "PlayerVerticalFov"), ("Cinematic idle — vertical (FP)", "fp") },
        { ("camCinematicFirstPersonIdleCameraMetadata", "ZoomTAFov"), ("Cinematic idle — zoom TA (FP)", "fp") },

        { ("camFollowPedCameraMetadata", "BaseFov"), ("On foot — third person (follow)", "tp") },
        { ("camThirdPersonPedAimCameraMetadata", "BaseFov"), ("Aim — third person (on foot)", "tp") },
        { ("camThirdPersonPedAimCameraMetadata", "BaseFovToEmulateWithFocalLengthMultiplier"), ("Aim focal emulation", "tp") },
        { ("camThirdPersonPedAimInCoverCameraMetadata", "BaseFov"), ("Aim in cover — third person", "tp") },
        { ("camThirdPersonPedAimInCoverCameraMetadata", "BaseFovToEmulateWithFocalLengthMultiplier"), ("Cover aim focal emulation", "tp") },
        { ("camThirdPersonPedAssistedAimCameraMetadata", "BaseFov"), ("Assisted aim", "tp") },
        { ("camThirdPersonPedAssistedAimCameraMetadata", "BaseFovToEmulateWithFocalLengthMultiplier"), ("Assisted aim focal emulation", "tp") },
        { ("camThirdPersonPedMeleeAimCameraMetadata", "BaseFov"), ("Melee aim — third person", "tp") },
        { ("camFollowParachuteCameraMetadata", "BaseFov"), ("Parachute / freefall — third person", "tp") },

        { ("camFollowVehicleCameraMetadata", "BaseFov"), ("In vehicle — third person (follow)", "veh") },
        { ("camFollowVehicleCameraMetadata", "MinSafeOrbitDistanceScalingForExtensions"), ("Safe distance scale", "distance") },
        { ("camFollowVehicleCameraMetadata", "CustomOrbitDistanceLimitsToForce_Min"), ("Forced distance — min", "distance") },
        { ("camFollowVehicleCameraMetadata", "CustomOrbitDistanceLimitsToForce_Max"), ("Forced distance — max", "distance") },
        { ("camFollowVehicleCameraMetadata", "OrbitDistanceLimitSpringConstant"), ("Distance limit smoothing", "distance") },
        { ("camFollowVehicleCameraMetadata", "OrbitDistanceLimitsForBasePosition_Min"), ("Base distance — min", "distance") },
        { ("camFollowVehicleCameraMetadata", "OrbitDistanceLimitsForBasePosition_Max"), ("Base distance — max", "distance") },
        { ("camCinematicMountedPartCameraMetadata", "BaseFov"), ("Mounted-part camera", "veh") },
        { ("camThirdPersonVehicleAimCameraMetadata", "BaseFov"), ("Driveby aim — from vehicle TP", "veh") },
        { ("camThirdPersonVehicleAimCameraMetadata", "BaseFovToEmulateWithFocalLengthMultiplier"), ("Driveby focal emulation", "veh") },
        { ("camCinematicVehicleTrackingCameraMetadata", "Fov"), ("Cinematic vehicle tracking", "veh") },

        { ("camCinematicMountedCameraMetadata", "BaseFov"), ("Mounted vehicle camera", "mfp") },
        { ("camCinematicMountedCameraMetadata", "FoVOffsetForDriveby"), ("Mounted driveby offset", "cine") },

        { ("camFollowPedCameraMetadata", "MinSafeOrbitDistanceScalingForExtensions"), ("Safe distance scale", "ped_distance") },
        { ("camFollowPedCameraMetadata", "CustomOrbitDistanceLimitsToForce_Min"), ("Forced distance — min", "ped_distance") },
        { ("camFollowPedCameraMetadata", "CustomOrbitDistanceLimitsToForce_Max"), ("Forced distance — max", "ped_distance") },
        { ("camFollowPedCameraMetadata", "OrbitDistanceLimitSpringConstant"), ("Distance limit smoothing", "ped_distance") },
        { ("camFollowPedCameraMetadata", "OrbitDistanceLimitsForBasePosition_Min"), ("Base distance — min", "ped_distance") },
        { ("camFollowPedCameraMetadata", "OrbitDistanceLimitsForBasePosition_Max"), ("Base distance — max", "ped_distance") },

        { ("camCinematicPedCloseUpCameraMetadata", "BaseFov"), ("Ped close-up", "cine") },
        { ("camCinematicTwoShotCameraMetadata", "BaseFov"), ("Cinematic two-shot", "cine") },
        { ("camCinematicTwoShotCameraMetadata", "FovScalingSpeed"), ("Two-shot FOV speed", "cine") },
        { ("camCinematicPositionCameraMetadata", "MaxZoomFov"), ("Position camera zoom", "cine") },
        { ("camSwitchCameraMetadata", "Fov"), ("Character switch camera", "cine") },

        { ("camCinematicHeliChaseCameraMetadata", "MinZoomFov"), ("Helicopter: wide FOV", "zoom") },
        { ("camCinematicHeliChaseCameraMetadata", "MaxZoomFov"), ("Helicopter: close FOV", "zoom") },
        { ("camCinematicHeliChaseCameraMetadata", "HighSpeedMaxZoomFov"), ("Helicopter: close FOV (fast)", "zoom") },
        { ("camCinematicHeliChaseCameraMetadata", "ExtraZoomFovDelta"), ("Helicopter: extra FOV change", "zoom") },
        { ("camCinematicHeliChaseCameraMetadata", "FovSpringConstant"), ("Helicopter: FOV smoothing", "zoom") },
        { ("camCinematicHeliChaseCameraMetadata", "MinSpeedForHighFovScalar"), ("Helicopter: FOV speed start", "zoom") },
        { ("camCinematicHeliChaseCameraMetadata", "MaxSpeedForHighFovScalar"), ("Helicopter: FOV speed end", "zoom") },
        { ("camCinematicCameraManCameraMetadata", "MinZoomFov"), ("Tracking cam: wide FOV", "zoom") },
        { ("camCinematicCameraManCameraMetadata", "MaxZoomFov"), ("Tracking cam: close FOV", "zoom") },
        { ("camCinematicCameraManCameraMetadata", "HighSpeedMaxZoomFov"), ("Tracking cam: close FOV (fast)", "zoom") },
        { ("camCinematicCameraManCameraMetadata", "FovSpringConstant"), ("Tracking cam: FOV smoothing", "zoom") },
        { ("camCinematicCameraManCameraMetadata", "MinSpeedForHighFovScalar"), ("Tracking cam: FOV speed start", "zoom") },
        { ("camCinematicCameraManCameraMetadata", "MaxSpeedForHighFovScalar"), ("Tracking cam: FOV speed end", "zoom") },
        { ("camCinematicCameraManCameraMetadata", "BoundsRadiusScaleForMinFov"), ("Tracking cam: wide bounds scale", "zoom") },
        { ("camCinematicCameraManCameraMetadata", "BoundsRadiusScaleForMaxFov"), ("Tracking cam: close bounds scale", "zoom") },
        { ("camCinematicTrainTrackingCameraMetadata", "MinZoomFov"), ("Train: wide FOV", "zoom") },
        { ("camCinematicTrainTrackingCameraMetadata", "MaxZoomFov"), ("Train: close FOV", "zoom") },
        { ("camCinematicTrainTrackingCameraMetadata", "HighSpeedMaxZoomFov"), ("Train: close FOV (fast)", "zoom") },
        { ("camCinematicTrainTrackingCameraMetadata", "FovSpringConstant"), ("Train: FOV smoothing", "zoom") },
        { ("camCinematicTrainTrackingCameraMetadata", "MinSpeedForHighFovScalar"), ("Train: FOV speed start", "zoom") },
        { ("camCinematicTrainTrackingCameraMetadata", "MaxSpeedForHighFovScalar"), ("Train: FOV speed end", "zoom") },
        { ("camCinematicTrainTrackingCameraMetadata", "BoundsRadiusScaleForMinFov"), ("Train: wide bounds scale", "zoom") },
        { ("camCinematicTrainTrackingCameraMetadata", "BoundsRadiusScaleForMaxFov"), ("Train: close bounds scale", "zoom") },
        { ("camCinematicGroupCameraMetadata", "MinFovToZoom"), ("Group cam: close-FOV limit", "zoom") },
        { ("camCinematicGroupCameraMetadata", "FovSpringConstant"), ("Group cam: FOV smoothing", "zoom") },
        { ("camCinematicStuntCameraMetadata", "MinFovToZoom"), ("Stunt cam: close-FOV limit", "zoom") },

        { ("camFollowPedCameraMetadata", "MaxBaseFovScaling"), ("On foot: FOV scaling limit", "dyn") },
        { ("camFollowVehicleCameraMetadata", "MaxBaseFovScaling"), ("Vehicle: FOV scaling limit", "dyn") },
        { ("camFollowParachuteCameraMetadata", "MaxBaseFovScaling"), ("Parachute: FOV scaling limit", "dyn") },
        { ("camHintHelperMetadata", "FovScalar"), ("Lock-on camera: FOV multiplier", "dyn") },
        { ("camFreeCameraMetadata", "FovAcceleration"), ("Free camera: FOV acceleration", "dyn") },
        { ("camFreeCameraMetadata", "MaxFovSpeed"), ("Free camera: max FOV speed", "dyn") },

        { ("camTimedSplineCameraMetadata", "DefaultFov"), ("Spline cinematic — default FOV", "misc") },
        { ("camCustomTimedSplineCameraMetadata", "DefaultFov"), ("Custom spline — default FOV", "misc") },
        { ("camRoundedSplineCameraMetadata", "DefaultFov"), ("Rounded spline — default FOV", "misc") },
        { ("camSmoothedSplineCameraMetadata", "DefaultFov"), ("Smoothed spline — default FOV", "misc") },

        { ("camFirstPersonPedAimCameraMetadata", "ZoomMotionBlurMinFovDelta"), ("FP aim: blur threshold (min)", "blur") },
        { ("camFirstPersonPedAimCameraMetadata", "ZoomMotionBlurMaxFovDelta"), ("FP aim: blur threshold (max)", "blur") },
        { ("camFirstPersonPedAimCameraMetadata", "ZoomMotionBlurMaxStrengthForFov"), ("FP aim: max blur strength", "blur") },
        { ("camFirstPersonShooterCameraMetadata", "ZoomMotionBlurMinFovDelta"), ("FP shooter: blur threshold (min)", "blur") },
        { ("camFirstPersonShooterCameraMetadata", "ZoomMotionBlurMaxFovDelta"), ("FP shooter: blur threshold (max)", "blur") },
        { ("camFirstPersonShooterCameraMetadata", "ZoomMotionBlurMaxStrengthForFov"), ("FP shooter: max blur strength", "blur") },
        { ("camFirstPersonHeadTrackingAimCameraMetadata", "ZoomMotionBlurMinFovDelta"), ("FP head track: blur threshold (min)", "blur") },
        { ("camFirstPersonHeadTrackingAimCameraMetadata", "ZoomMotionBlurMaxFovDelta"), ("FP head track: blur threshold (max)", "blur") },
        { ("camFirstPersonHeadTrackingAimCameraMetadata", "ZoomMotionBlurMaxStrengthForFov"), ("FP head track: max blur strength", "blur") },
    };

    public static readonly (string id, string title)[] Groups =
    {
        ("fp", "FIRST PERSON — ON FOOT"),
        ("tp", "THIRD PERSON — ON FOOT"),
        ("veh", "VEHICLES"),
        ("mfp", "FIRST PERSON — IN VEHICLE"),
        ("ped_distance", "ON-FOOT CAMERA DISTANCE"),
        ("distance", "VEHICLE CAMERA DISTANCE"),
        ("cine", "CINEMATIC / GAMEPLAY MOMENTS"),
        ("zoom", "CINEMATIC ZOOM RANGES"),
        ("dyn", "FOV DYNAMICS (scaling, speeds)"),
        ("blur", "ZOOM MOTION BLUR"),
        ("misc", "SPLINE / MISC"),
    };

    /// <summary>Long-form help shown on hover. Labels stay compact so rows do
    /// not overlap sliders; the metadata type remains the tooltip heading.</summary>
    private static string GetDescription(string type, string field) => (type, field) switch
    {
        ("camCinematicPedCloseUpCameraMetadata", "BaseFov") =>
            "Base field of view for the cinematic close-up camera that frames a pedestrian. It is not confirmed to control the normal WASTED death screen.",
        ("camCinematicMountedPartCameraMetadata", "BaseFov") =>
            "Base field of view for a camera attached to a vehicle or another object part. The exact use depends on the active camera profile.",
        ("camCinematicMountedCameraMetadata", "BaseFov") =>
            "Base field of view for Rockstar's mounted vehicle camera profiles. The file contains many profiles, so this can affect more than one in-vehicle context.",
        ("camCinematicMountedCameraMetadata", "FoVOffsetForDriveby") =>
            "Extra FOV offset applied by mounted camera profiles during drive-by aiming. A positive value widens the view relative to that profile's base FOV.",
        ("camCinematicTwoShotCameraMetadata", "BaseFov") =>
            "Base field of view for a cinematic camera that frames two characters together. Larger values make the framing wider.",
        ("camCinematicTwoShotCameraMetadata", "FovScalingSpeed") =>
            "Speed of automatic FOV adjustment while the two-shot camera keeps both subjects in frame. Higher values make its FOV react faster.",
        ("camSwitchCameraMetadata", "Fov") =>
            "Field of view used by the character-switch transition camera in Story Mode. It does not change the normal V-key first/third-person switch.",
        ("camCinematicVehicleTrackingCameraMetadata", "Fov") =>
            "Field of view for the cinematic vehicle-tracking camera profile, rather than the standard driving follow camera.",
        ("camCinematicPositionCameraMetadata", "MaxZoomFov") =>
            "FOV used at the close end of cinematic position-camera zoom. Smaller FOV values look more zoomed in.",
        ("camFollowPedCameraMetadata", "BaseFov") =>
            "Base field of view for the ordinary third-person on-foot follow camera.",
        ("camFollowVehicleCameraMetadata", "BaseFov") =>
            "Base field of view for the ordinary third-person vehicle follow camera.",
        ("camFollowPedCameraMetadata", "MinSafeOrbitDistanceScalingForExtensions") =>
            "Scale used when calculating the minimum safe camera distance around a pedestrian and nearby geometry. Increasing it can keep the third-person camera slightly farther from the character in constrained spaces.",
        ("camFollowPedCameraMetadata", "CustomOrbitDistanceLimitsToForce_Min") =>
            "Lower endpoint of the forced on-foot camera-distance range. The original value is -1, which means no custom range is forced. Set this together with Forced distance — max.",
        ("camFollowPedCameraMetadata", "CustomOrbitDistanceLimitsToForce_Max") =>
            "Upper endpoint of the forced on-foot camera-distance range. The original value is -1, which means no custom range is forced. A larger value can keep the third-person camera farther from the character.",
        ("camFollowPedCameraMetadata", "OrbitDistanceLimitSpringConstant") =>
            "How firmly and quickly the on-foot camera moves toward its permitted distance. This changes smoothing, not the maximum distance by itself.",
        ("camFollowPedCameraMetadata", "OrbitDistanceLimitsForBasePosition_Min") =>
            "Minimum normal orbit distance from the character for the third-person follow camera.",
        ("camFollowPedCameraMetadata", "OrbitDistanceLimitsForBasePosition_Max") =>
            "Maximum normal orbit distance from the character for the third-person follow camera. Increase this to allow the camera to move farther back.",
        ("camFollowVehicleCameraMetadata", "MinSafeOrbitDistanceScalingForExtensions") =>
            "Scale used when calculating the minimum safe camera distance around vehicle extensions, trailers, and unusual bodywork. It can make the camera keep a larger clearance from the vehicle.",
        ("camFollowVehicleCameraMetadata", "CustomOrbitDistanceLimitsToForce_Min") =>
            "Lower endpoint of the forced vehicle-camera distance range. The original value is -1, which means no custom range is forced. Set this together with Forced distance — max.",
        ("camFollowVehicleCameraMetadata", "CustomOrbitDistanceLimitsToForce_Max") =>
            "Upper endpoint of the forced vehicle-camera distance range. The original value is -1, which means no custom range is forced. A larger value can keep the camera farther from the vehicle.",
        ("camFollowVehicleCameraMetadata", "OrbitDistanceLimitSpringConstant") =>
            "How firmly and quickly the camera moves toward its permitted distance. This changes smoothing, not the maximum distance by itself.",
        ("camFollowVehicleCameraMetadata", "OrbitDistanceLimitsForBasePosition_Min") =>
            "Minimum normal orbit distance from the vehicle. This is one endpoint of the standard vehicle-camera distance range.",
        ("camFollowVehicleCameraMetadata", "OrbitDistanceLimitsForBasePosition_Max") =>
            "Maximum normal orbit distance from the vehicle. Increase this to allow the third-person camera to move farther back.",
        ("camFollowParachuteCameraMetadata", "BaseFov") =>
            "Base field of view for the parachute and freefall follow-camera profiles.",
        ("camThirdPersonPedAimCameraMetadata", "BaseFov") =>
            "Base third-person FOV while aiming on foot.",
        ("camThirdPersonPedAimInCoverCameraMetadata", "BaseFov") =>
            "Base third-person FOV while aiming from cover.",
        ("camThirdPersonPedAssistedAimCameraMetadata", "BaseFov") =>
            "Base third-person FOV for the game's assisted-aim camera profile. Whether it is active depends on aim settings and gameplay context.",
        ("camThirdPersonVehicleAimCameraMetadata", "BaseFov") =>
            "Base third-person FOV while aiming or using a drive-by camera from a vehicle.",
        ("camFirstPersonPedAimCameraMetadata", "BaseFov") =>
            "Base first-person FOV while aiming on foot. Multiple weapon and situation profiles are stored together.",
        ("camFirstPersonShooterCameraMetadata", "BaseFov") =>
            "Base first-person shooter camera FOV. This is distinct from the dedicated ADS and phone fields below.",
        ("camFirstPersonShooterCameraMetadata", "AimFov") =>
            "First-person FOV when the shooter camera enters ADS (aim down sights). Smaller values produce a stronger zoom.",
        ("camFirstPersonShooterCameraMetadata", "PhoneFov") =>
            "First-person FOV while using the in-game phone camera profile.",
        ("camFirstPersonHeadTrackingAimCameraMetadata", "BaseFov") =>
            "Base FOV for the first-person aim profile with head tracking behavior.",
        ("camCinematicFirstPersonIdleCameraMetadata", "PlayerHorizontalFov") =>
            "Horizontal FOV used by the first-person cinematic idle camera.",
        ("camCinematicFirstPersonIdleCameraMetadata", "PlayerVerticalFov") =>
            "Vertical FOV used by the first-person cinematic idle camera. Change it cautiously because it alters the horizontal/vertical relationship.",
        ("camCinematicFirstPersonIdleCameraMetadata", "ZoomTAFov") =>
            "FOV for the cinematic idle camera's Zoom TA state. A value of -1 generally means that this profile does not override it.",
        ("camHintHelperMetadata", "FovScalar") =>
            "Multiplier used by hint and lock-on camera helpers. It scales a camera FOV instead of defining a direct angle.",
        ("camFreeCameraMetadata", "FovAcceleration") =>
            "Acceleration rate for FOV changes in the internal free-camera profile; it is not a direct FOV angle.",
        ("camFreeCameraMetadata", "MaxFovSpeed") =>
            "Maximum rate of FOV change in the internal free-camera profile; it is not a maximum FOV angle.",
        (_, "BaseFovToEmulateWithFocalLengthMultiplier") =>
            "Reference FOV used when the engine emulates a focal-length multiplier. This is not always the direct on-screen FOV.",
        (_, "MaxBaseFovScaling") =>
            "Limit for the automatic scaling applied to this camera's base FOV. It controls dynamic behavior, not the base angle itself.",
        (_, "MinZoomFov") =>
            "FOV at the wide, less-zoomed end of this camera's zoom range. Larger values show more of the scene.",
        (_, "MaxZoomFov") =>
            "FOV at the close, more-zoomed end of this camera's zoom range. Smaller values look more zoomed in.",
        (_, "HighSpeedMaxZoomFov") =>
            "Close-end FOV used by this camera's high-speed behavior.",
        (_, "ExtraZoomFovDelta") =>
            "Additional amount by which this camera can change its FOV during its zoom behavior.",
        (_, "FovSpringConstant") =>
            "Strength of smoothing for automatic FOV movement. Higher values generally make the response firmer and faster.",
        (_, "MinSpeedForHighFovScalar") =>
            "Lower speed threshold at which this camera begins its high-speed dynamic-FOV behavior.",
        (_, "MaxSpeedForHighFovScalar") =>
            "Upper speed threshold at which this camera reaches the full high-speed dynamic-FOV behavior.",
        (_, "BoundsRadiusScaleForMinFov") =>
            "Bounds-radius multiplier used when selecting the wide end of this camera's FOV range.",
        (_, "BoundsRadiusScaleForMaxFov") =>
            "Bounds-radius multiplier used when selecting the close end of this camera's FOV range.",
        (_, "MinFovToZoom") =>
            "FOV limit used when this cinematic camera zooms in. Smaller values allow a tighter close-up.",
        (_, "DefaultFov") =>
            "Default FOV for this spline or scripted camera profile.",
        (_, "ZoomMotionBlurMinFovDelta") =>
            "Minimum amount of FOV change required before this camera's zoom motion blur begins to apply.",
        (_, "ZoomMotionBlurMaxFovDelta") =>
            "FOV-change amount at which this camera's zoom motion blur reaches its maximum range.",
        (_, "ZoomMotionBlurMaxStrengthForFov") =>
            "Maximum strength of zoom motion blur for this camera profile.",
        _ => $"Controls the {field} field of this camera metadata profile."
    };

    private static string AppDir => AppDomain.CurrentDomain.BaseDirectory;

    public static List<FovParam> Load(string ymtFileName = "cameras_original.ymt")
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var registry = JsonSerializer.Deserialize<List<RegistryEntry>>(
            File.ReadAllText(Path.Combine(AppDir, "fov_registry.json")), opts) ?? new();
        var defaultsDoc = JsonSerializer.Deserialize<Dictionary<string, List<float>>>(
            File.ReadAllText(Path.Combine(AppDir, "fov_defaults.json")), opts) ?? new();

        var ymt = File.ReadAllBytes(Path.Combine(AppDir, ymtFileName));
        var result = new List<FovParam>();

        foreach (var entry in registry)
        {
            if (Banned.Contains(entry.Type))
                continue;

            if (entry.Type == "camCinematicMountedCameraMetadata")
            {
                // split by FirstPersonCamera flag at +0x164; label covers FP instances
                var fpOffsets = new List<int>();
                var fpDefaults = new List<float>();
                for (int i = 0; i < entry.Count; i++)
                {
                    var baseOff = entry.Off + i * entry.Stride;
                    if (ymt[baseOff + 0x164] != 0)
                    {
                        fpOffsets.Add(baseOff + 0x54);
                        fpDefaults.Add(ReadBeF32(ymt, baseOff + 0x54));
                    }
                }
                result.Add(new FovParam
                {
                    Type = entry.Type, Field = "BaseFov",
                    Label = Labels[(entry.Type, "BaseFov")].label, Group = "mfp",
                    Description = GetDescription(entry.Type, "BaseFov"),
                    Offsets = fpOffsets, Defaults = fpDefaults
                });
                // driveby offset param (all instances)
                var dbOffsets = new List<int>();
                var dbDefaults = new List<float>();
                for (int i = 0; i < entry.Count; i++)
                {
                    var baseOff = entry.Off + i * entry.Stride;
                    dbOffsets.Add(baseOff + 0x160);
                    dbDefaults.Add(ReadBeF32(ymt, baseOff + 0x160));
                }
                if (Labels.TryGetValue((entry.Type, "FoVOffsetForDriveby"), out var dbl))
                    result.Add(new FovParam
                    {
                        Type = entry.Type, Field = "FoVOffsetForDriveby",
                        Label = dbl.label, Group = dbl.group,
                        Description = GetDescription(entry.Type, "FoVOffsetForDriveby"),
                        Offsets = dbOffsets, Defaults = dbDefaults
                    });
                continue;
            }

            foreach (var f in entry.Fields)
            {
                if (!Labels.TryGetValue((entry.Type, f.Name), out var lab))
                    continue;
                var dkey = entry.Type + "|" + f.Name;
                if (!defaultsDoc.TryGetValue(dkey, out var defs) || defs is null || defs.Count != entry.Count)
                {
                    // Some non-FOV fields are intentionally not listed in the
                    // hand-maintained defaults file. Their pristine values are
                    // read directly from cameras_original.ymt instead.
                    defs = Enumerable.Range(0, entry.Count)
                        .Select(i => ReadBeF32(ymt, entry.Off + i * entry.Stride + f.Off))
                        .ToList();
                }
                result.Add(new FovParam
                {
                    Type = entry.Type, Field = f.Name,
                    Label = lab.label, Group = lab.group,
                    Description = GetDescription(entry.Type, f.Name),
                    Offsets = Enumerable.Range(0, entry.Count)
                        .Select(i => entry.Off + i * entry.Stride + f.Off).ToList(),
                    Defaults = defs
                });
            }
        }

        var order = new Dictionary<string, int>();
        for (int i = 0; i < Groups.Length; i++)
            order[Groups[i].id] = i;
        result.Sort((a, b) =>
        {
            var g = order.GetValueOrDefault(a.Group, 99).CompareTo(order.GetValueOrDefault(b.Group, 99));
            return g != 0 ? g : string.Compare(a.Label, b.Label, StringComparison.Ordinal);
        });
        return result;
    }


    /// <summary>Reads the current FOV values from a ymt for every known
    /// (type, field) — used by the import function to load an already-modded
    /// file into the editor.</summary>
    public static Dictionary<(string type, string field), float> LoadValuesOnly(
        string ymtFileName, List<FovParam> known)
    {
        var ymt = File.ReadAllBytes(Path.Combine(AppDir, ymtFileName));
        return LoadValuesOnly(ymt, known);
    }

    /// <summary>Reads known FOV values from an already-loaded cameras.ymt.
    /// Import uses this overload so the pristine base file is never replaced.</summary>
    public static Dictionary<(string type, string field), float> LoadValuesOnly(
        byte[] ymt, List<FovParam> known)
    {
        var result = new Dictionary<(string, string), float>();
        foreach (var p in known)
        {
            // read the first instance of this field
            if (p.Offsets.Count == 0) continue;
            result[(p.Type, p.Field)] = ReadBeF32(ymt, p.Offsets[0]);
        }
        return result;
    }

    public static float ReadBeF32(byte[] data, int pos)
    {
        uint u = (uint)(data[pos] << 24 | data[pos + 1] << 16 | data[pos + 2] << 8 | data[pos + 3]);
        return BitConverter.ToSingle(BitConverter.GetBytes(u), 0);
    }

    public static void WriteBeF32(byte[] data, int pos, float val)
    {
        var u = BitConverter.ToUInt32(BitConverter.GetBytes(val), 0);
        data[pos] = (byte)(u >> 24);
        data[pos + 1] = (byte)(u >> 16);
        data[pos + 2] = (byte)(u >> 8);
        data[pos + 3] = (byte)u;
    }
}
