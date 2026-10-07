using UnityEngine;

// Controller shortcuts + a floating label (auto-hides to save battery).
//   A / B            : fps up / down          X / Y        : resolution up / down
//   Right stick Y    : max depth +/-          Left stick Y : min depth +/-
//   Right stick click: low-power toggle       Left stick click: near mode toggle
//   Hold both grips  : show the label without changing anything
public sealed class HeadsetControls : MonoBehaviour
{
    [SerializeField] DepthToNdiBridge _bridge;
    [SerializeField] Transform _head;             // CenterEyeAnchor
    [SerializeField] float _labelSeconds = 3f;
    [SerializeField] float _depthSpeed = 1f;      // meters per second at full stick

    TextMesh _label;
    float _hideAt;
    bool _depthDirty;
    float _applyAt;

    void Start()
    {
        var go = new GameObject("SettingsLabel");
        _label = go.AddComponent<TextMesh>();
        _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        go.GetComponent<MeshRenderer>().sharedMaterial = _label.font.material;
        _label.fontSize = 48;
        _label.characterSize = 0.006f;
        _label.anchor = TextAnchor.MiddleCenter;
        _label.alignment = TextAlignment.Center;
        _label.color = Color.white;
        go.SetActive(false);
        _bridge.SettingsApplied += ShowLabel;
    }

    void Update()
    {
        var s = _bridge.Settings;
        var changed = false;

        if (OVRInput.GetDown(OVRInput.Button.One))   { s.fps = DepthStreamSettings.Step(DepthStreamSettings.FpsSteps, s.fps, +1); changed = true; }
        if (OVRInput.GetDown(OVRInput.Button.Two))   { s.fps = DepthStreamSettings.Step(DepthStreamSettings.FpsSteps, s.fps, -1); changed = true; }
        if (OVRInput.GetDown(OVRInput.Button.Three)) { s.resolution = DepthStreamSettings.Step(DepthStreamSettings.ResolutionSteps, s.resolution, +1); changed = true; }
        if (OVRInput.GetDown(OVRInput.Button.Four))  { s.resolution = DepthStreamSettings.Step(DepthStreamSettings.ResolutionSteps, s.resolution, -1); changed = true; }
        if (OVRInput.GetDown(OVRInput.Button.SecondaryThumbstick)) { s.lowPower = !s.lowPower; changed = true; }
        if (OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick))   { s.SetNearMode(s.maxDepth > 1.6f); changed = true; }

        // Depth range: continuous while stick held, applied (and saved) once released
        var maxAxis = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).y;
        var minAxis = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick).y;
        if (Mathf.Abs(maxAxis) > 0.3f || Mathf.Abs(minAxis) > 0.3f)
        {
            if (Mathf.Abs(maxAxis) > 0.3f) s.maxDepth += maxAxis * _depthSpeed * Time.deltaTime;
            if (Mathf.Abs(minAxis) > 0.3f) s.minDepth += minAxis * _depthSpeed * Time.deltaTime;
            s.Clamp();
            _depthDirty = true;
            _applyAt = Time.time + 0.3f;
            ShowLabel();
        }
        else if (_depthDirty && Time.time > _applyAt) { _depthDirty = false; changed = true; }

        if (changed) _bridge.ApplySettings(); // triggers ShowLabel via SettingsApplied
        if (OVRInput.Get(OVRInput.Button.PrimaryHandTrigger) && OVRInput.Get(OVRInput.Button.SecondaryHandTrigger)) ShowLabel();

        if (_label.gameObject.activeSelf)
        {
            if (Time.time > _hideAt) _label.gameObject.SetActive(false);
            else if (_head != null)
            {
                var t = _label.transform;
                var fwd = Vector3.ProjectOnPlane(_head.forward, Vector3.up).normalized;
                if (fwd.sqrMagnitude < 0.01f) fwd = _head.forward;
                t.position = Vector3.Lerp(t.position, _head.position + fwd * 0.8f - Vector3.up * 0.15f, 10f * Time.deltaTime);
                t.rotation = Quaternion.LookRotation(t.position - _head.position);
            }
        }
    }

    void ShowLabel()
    {
        if (_label == null) return;
        _label.text = _bridge.Settings.ToString();
        if (!_label.gameObject.activeSelf && _head != null)
            _label.transform.position = _head.position + _head.forward * 0.8f;
        _label.gameObject.SetActive(true);
        _hideAt = Time.time + _labelSeconds;
    }

    void OnDestroy()
    {
        if (_bridge != null) _bridge.SettingsApplied -= ShowLabel;
    }
}
