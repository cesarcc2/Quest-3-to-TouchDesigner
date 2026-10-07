using Klak.Ndi;
using Meta.XR;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// Streams the passthrough color camera as a second NDI source (Quest3_Color) for AI depth on the PC.
// Toggled by DepthStreamSettings.colorStream (OSC /color); rate capped by colorFps (OSC /colorfps).
public sealed class ColorToNdi : MonoBehaviour
{
    const string CameraPermission = "horizonos.permission.HEADSET_CAMERA";

    [SerializeField] DepthToNdiBridge _bridge;
    [SerializeField] PassthroughCameraAccess _camera;
    [SerializeField] NdiSender _ndiSender;

    int _blits;
    float _nextLog;
    RenderTexture _flipped; // camera copy so Mirror Image (and optional colorFlip) apply like on depth

    void Awake()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!Permission.HasUserAuthorizedPermission(CameraPermission))
            Permission.RequestUserPermission(CameraPermission); // PassthroughCameraAccess waits for it
#endif
    }

    void Start()
    {
        _bridge.SettingsApplied += Apply;
        Apply();
    }

    void Apply()
    {
        var s = _bridge.Settings;
        _camera.enabled = s.colorStream;           // stops the camera entirely when off (battery)
        _ndiSender.frameRate = Mathf.Min(s.colorFps, s.EffectiveFps);
        if (!s.colorStream) _ndiSender.enabled = false;
    }

    void Update()
    {
        var live = _bridge.Settings.colorStream && _camera.enabled && _camera.IsPlaying;
        if (live && _camera.IsUpdatedThisFrame)
        {
            var src = _camera.GetTexture();
            if (_flipped == null || _flipped.width != src.width || _flipped.height != src.height)
            {
                if (_flipped != null) _flipped.Release();
                _flipped = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32) { name = "ColorFlipped" };
                _flipped.Create();
                _ndiSender.sourceTexture = _flipped;
                Debug.Log($"DepthToNdi: color camera {src.width}x{src.height} -> NDI '{_ndiSender.ndiName}'");
            }
            // Raw camera image matches depth orientation; Mirror Image flips both, colorFlip adds an extra flip
            var flip = _bridge.Settings.colorFlip != _bridge.Settings.mirror;
            Graphics.Blit(src, _flipped, new Vector2(flip ? -1 : 1, 1), new Vector2(flip ? 1 : 0, 0));
            _blits++;
        }
        if (Time.time > _nextLog)
        {
            Debug.Log($"DepthToNdi: color {_blits / 5f:0.#} fps copied, flip={_bridge.Settings.colorFlip != _bridge.Settings.mirror}, src={( _camera.enabled ? _camera.GetTexture()?.GetType().Name : "off")}");
            _blits = 0; _nextLog = Time.time + 5f;
        }
        var send = live && _flipped != null;
        if (_ndiSender.enabled != send) _ndiSender.enabled = send;
    }

    void OnDestroy()
    {
        if (_bridge != null) _bridge.SettingsApplied -= Apply;
        if (_flipped != null) _flipped.Release();
    }
}
