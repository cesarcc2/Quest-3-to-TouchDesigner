using System;
using System.Collections.Generic;
using Klak.Ndi;
using Meta.XR.EnvironmentDepth;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// Copies Meta's environment depth (one eye) into DepthRenderTexture every frame
// and drives a KlakNDI sender that streams it to TouchDesigner.
public sealed class DepthToNdiBridge : MonoBehaviour
{
    const string ScenePermission = "com.oculus.permission.USE_SCENE";
    static readonly int DepthTextureID = Shader.PropertyToID("_EnvironmentDepthTexture");
    static readonly int ZBufferParamsID = Shader.PropertyToID("_EnvironmentDepthZBufferParams");
    static readonly int SliceID = Shader.PropertyToID("_Slice");
    static readonly int MinDepthID = Shader.PropertyToID("_MinDepth");
    static readonly int MaxDepthID = Shader.PropertyToID("_MaxDepth");
    static readonly int InvertID = Shader.PropertyToID("_Invert");
    static readonly int MirrorID = Shader.PropertyToID("_Mirror");
    static readonly int TooCloseID = Shader.PropertyToID("_TooClose");
    static readonly int TooFarID = Shader.PropertyToID("_TooFar");
    static readonly int UnknownID = Shader.PropertyToID("_Unknown");
    static readonly int EncodingID = Shader.PropertyToID("_Encoding");
    static readonly int KinectScaleID = Shader.PropertyToID("_KinectScale");
    static readonly int AlignID = Shader.PropertyToID("_Align");
    static readonly int AlignOriginID = Shader.PropertyToID("_AlignOrigin");
    static readonly int[] AlignCornerIDs = {
        Shader.PropertyToID("_AlignC00"), Shader.PropertyToID("_AlignC10"),
        Shader.PropertyToID("_AlignC01"), Shader.PropertyToID("_AlignC11") };
    static readonly Vector2[] Corners = { new(0, 0), new(1, 0), new(0, 1), new(1, 1) };

    [SerializeField] EnvironmentDepthManager _depthManager;
    [SerializeField] NdiSender _ndiSender;
    [SerializeField] RenderTexture _depthRenderTexture; // initial target; replaced on resolution change
    [SerializeField] Shader _blitShader;
    [SerializeField] Meta.XR.PassthroughCameraAccess _colorCamera; // for align-to-color
    [SerializeField] DepthStreamSettings _settings = new();
    [SerializeField] bool _debugLog = true;

    public DepthStreamSettings Settings => _settings;
    public event Action SettingsApplied;

    Material _material;
    CommandBuffer _cmd;
    RenderTexture _runtimeRT;
    float _maxDisplayHz = 72f;
#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject _nsdManager; // keeps Android's mDNS daemon alive for NDI discovery
#endif

    void Awake()
    {
        _material = new Material(_blitShader);
        _ndiSender.captureMethod = CaptureMethod.Texture;
        _ndiSender.keepAlpha = false;
        _ndiSender.enabled = false;
        _settings.Load();

#if UNITY_ANDROID && !UNITY_EDITOR
        var activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer")
            .GetStatic<AndroidJavaObject>("currentActivity");
        _nsdManager = activity.Call<AndroidJavaObject>("getSystemService", "servicediscovery");

        if (!Permission.HasUserAuthorizedPermission(ScenePermission))
        {
            _depthManager.enabled = false;
            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => _depthManager.enabled = true;
            callbacks.PermissionDenied += _ => Debug.LogError("DepthToNdi: USE_SCENE denied, depth unavailable");
            Permission.RequestUserPermission(ScenePermission, callbacks);
        }
#endif
    }

    void Start()
    {
        if (!EnvironmentDepthManager.IsSupported)
            Debug.LogError("DepthToNdi: Environment depth not supported on this device");

        var rates = OVRManager.display?.displayFrequenciesAvailable;
        if (rates != null && rates.Length > 0) _maxDisplayHz = Mathf.Max(rates);
        ApplySettings();
    }

    // Call after changing Settings (from OSC, headset controls or the inspector).
    public void ApplySettings()
    {
        _settings.Clamp();

        // Display: lowest refresh that still covers the stream fps (saves battery)
        var hz = _settings.lowPower ? 72f : Mathf.Max(72f, Mathf.Min(_maxDisplayHz, NearestDisplayRate(_settings.fps)));
        if (OVRManager.display != null) OVRManager.display.displayFrequency = hz;
        _ndiSender.frameRate = _settings.EffectiveFps;

        // Resolution: (re)create the target when size differs
        var res = _settings.EffectiveResolution;
        var current = _runtimeRT != null ? _runtimeRT : _depthRenderTexture;
        if (current == null || current.width != res || current.graphicsFormat != GraphicsFormat.R8G8B8A8_UNorm)
        {
            var old = _runtimeRT;
            _runtimeRT = new RenderTexture(res, res, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None)
                { name = "DepthRenderTexture" };
            _runtimeRT.Create();
            _ndiSender.sourceTexture = _runtimeRT;
            if (old != null) old.Release();
        }
        else _ndiSender.sourceTexture = current;

        _settings.Save();
        SettingsApplied?.Invoke();
        Debug.Log($"DepthToNdi: settings {JsonUtility.ToJson(_settings)} display={hz}Hz");
    }

    float NearestDisplayRate(int fps)
    {
        var rates = OVRManager.display?.displayFrequenciesAvailable;
        if (rates == null || rates.Length == 0) return 72f;
        var best = float.MaxValue;
        foreach (var r in rates) if (r >= fps && r < best) best = r;
        return best == float.MaxValue ? Mathf.Max(rates) : best;
    }

    void LateUpdate()
    {
        var available = _depthManager.enabled && _depthManager.IsDepthAvailable
                        && Shader.GetGlobalTexture(DepthTextureID) != null;

        if (_ndiSender.enabled != available)
        {
            _ndiSender.enabled = available;
            Debug.Log($"DepthToNdi: NDI sender '{_ndiSender.ndiName}' {(available ? "started" : "paused")}");
        }
        if (available && _debugLog && Time.frameCount % 360 == 0) LogDiagnostics();
    }

    // Meta refreshes the depth texture + ZBuffer params in Application.onBeforeRender,
    // which runs after LateUpdate; blitting at context start guarantees fresh data.
    void OnEnable() => RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
    void OnDisable() => RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;

    void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        if (!_ndiSender.enabled || _ndiSender.sourceTexture is not RenderTexture target) return;

        _material.SetFloat(SliceID, _settings.eye);
        _material.SetFloat(MinDepthID, _settings.minDepth);
        _material.SetFloat(MaxDepthID, _settings.maxDepth);
        _material.SetFloat(InvertID, _settings.invert ? 1 : 0);
        _material.SetFloat(MirrorID, _settings.mirror ? 1 : 0);
        _material.SetFloat(TooCloseID, _settings.tooCloseValue);
        _material.SetFloat(TooFarID, _settings.tooFarValue);
        _material.SetFloat(UnknownID, _settings.unknownValue);
        _material.SetFloat(EncodingID, (float)_settings.encoding);
        _material.SetFloat(KinectScaleID, _settings.kinectScale ? 1 : 0);
        SetAlignment();
        _cmd ??= new CommandBuffer { name = "DepthToNdi" };
        _cmd.Clear();
        _cmd.Blit(Texture2D.blackTexture, target, _material);
        context.ExecuteCommandBuffer(_cmd);
    }

    // Corner rays of the color camera, scaled to 1 m along its forward axis so they interpolate exactly
    void SetAlignment()
    {
        var on = _settings.alignToColor && _settings.colorStream && _colorCamera != null
                 && _colorCamera.enabled && _colorCamera.IsPlaying;
        _material.SetFloat(AlignID, on ? 1 : 0);
        if (!on) return;
        var pose = _colorCamera.GetCameraPose();
        var forward = pose.rotation * Vector3.forward;
        _material.SetVector(AlignOriginID, pose.position);
        for (var i = 0; i < 4; i++)
        {
            var dir = _colorCamera.ViewportPointToRay(Corners[i], pose).direction;
            _material.SetVector(AlignCornerIDs[i], dir / Vector3.Dot(dir, forward));
        }
    }

    void LogDiagnostics()
    {
        var tex = Shader.GetGlobalTexture(DepthTextureID);
        var src = _ndiSender.sourceTexture;
        Debug.Log($"DepthToNdi: src {tex.dimension} {tex.width}x{tex.height} " +
                  $"zparams={Shader.GetGlobalVector(ZBufferParamsID)} frame={Time.frameCount}");
        AsyncGPUReadback.Request(src, 0, TextureFormat.RGBA32, req =>
        {
            if (req.hasError) return;
            var w = src.width;
            var data = req.GetData<Color32>();
            Debug.Log($"DepthToNdi: out center={data[w * w / 2 + w / 2]} corner={data[0]}");
        });
    }

    void OnApplicationPause(bool paused)
    {
        if (paused) _ndiSender.enabled = false; // re-enabled by LateUpdate on resume
    }

    void OnDestroy()
    {
        if (_ndiSender != null) _ndiSender.enabled = false;
        if (_material != null) Destroy(_material);
        if (_runtimeRT != null) _runtimeRT.Release();
        _cmd?.Release();
#if UNITY_ANDROID && !UNITY_EDITOR
        _nsdManager?.Dispose();
#endif
    }
}
