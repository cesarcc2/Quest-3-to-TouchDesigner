using System.IO;
using Klak.Ndi;
using Meta.XR.EnvironmentDepth;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

// One-shot project configuration. Run from CLI:
//   Unity -batchmode -quit -projectPath . -buildTarget Android -executeMethod ProjectSetup.Run
public static class ProjectSetup
{
    const string AppId = "com.cesarcc2.quest3touchdesignerbridge";
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string RTPath = "Assets/DepthRenderTexture.renderTexture";
    const string NdiResourcesPath = "Packages/jp.keijiro.klak.ndi/Runtime/Resource/NdiResources.asset";
    const string CameraRigPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";

    [MenuItem("Quest3Bridge/Run Project Setup")]
    public static void Run()
    {
        ConfigurePlayer();
        ConfigureUrp();
        ConfigureXr();
        ConfigureOvrProject();
        var rt = CreateDepthRenderTexture();
        CreateScene(rt);
        AssetDatabase.SaveAssets();
        Debug.Log("ProjectSetup: done");
    }

    static void ConfigurePlayer()
    {
        var android = NamedBuildTarget.Android;
        PlayerSettings.companyName = "cesarcc2";
        PlayerSettings.productName = "Quest3 TouchDesigner Bridge";
        PlayerSettings.SetApplicationIdentifier(android, AppId);
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)32;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.GameActivity;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.Android.forceInternetPermission = true;
    }

    static void ConfigureUrp()
    {
        Directory.CreateDirectory("Assets/Settings");
        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, "Assets/Settings/URP_Renderer.asset");
        var urp = UniversalRenderPipelineAsset.Create(rendererData);
        urp.supportsHDR = false;          // passthrough needs alpha-clear, no HDR
        urp.msaaSampleCount = 4;
        AssetDatabase.CreateAsset(urp, "Assets/Settings/URP_Quest.asset");
        GraphicsSettings.defaultRenderPipeline = urp;
        for (var i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = urp;
        }
    }

    static void ConfigureXr()
    {
        const BuildTargetGroup group = BuildTargetGroup.Android;
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perTarget))
        {
            Directory.CreateDirectory("Assets/XR");
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
        }
        if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
        if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);

        var manager = perTarget.ManagerSettingsForBuildTarget(group);
        XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, group);

        FeatureHelpers.RefreshFeatures(group);
        var openxr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
        openxr.GetFeature<Meta.XR.MetaXRFeature>().enabled = true;
        openxr.GetFeature<OculusTouchControllerProfile>().enabled = true;
        // Depth provider on OpenXR needs the Meta Quest session + occlusion subsystems
        openxr.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.ARSessionFeature>().enabled = true;
        openxr.GetFeature<UnityEngine.XR.OpenXR.Features.Meta.AROcclusionFeature>().enabled = true;
        EditorUtility.SetDirty(openxr);
    }

    static void ConfigureOvrProject()
    {
        var config = OVRProjectConfig.CachedProjectConfig;
        config.experimentalFeaturesEnabled = true;
        config.isPassthroughCameraAccessEnabled = true; // color camera for AI depth
        config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
        config.sceneSupport = OVRProjectConfig.FeatureSupport.Required; // USE_SCENE for Depth API
        config.targetDeviceTypes.Clear();
        config.targetDeviceTypes.Add(OVRProjectConfig.DeviceType.Quest3);
        OVRProjectConfig.CommitProjectConfig(config);
    }

    static RenderTexture CreateDepthRenderTexture()
    {
        var rt = AssetDatabase.LoadAssetAtPath<RenderTexture>(RTPath);
        // RGBA8: NDI is 8-bit per channel; gray uses RGB, packed16 uses R (hi) + G (lo)
        if (rt != null && rt.graphicsFormat == GraphicsFormat.R8G8B8A8_UNorm) return rt;
        if (rt != null) AssetDatabase.DeleteAsset(RTPath);
        rt = new RenderTexture(1280, 1280, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None) { name = "DepthRenderTexture" };
        AssetDatabase.CreateAsset(rt, RTPath);
        return rt;
    }

    static void CreateScene(RenderTexture rt)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Camera rig + passthrough
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPath));
        var ovrManager = rig.GetComponent<OVRManager>();
        if (ovrManager == null) ovrManager = rig.AddComponent<OVRManager>();
        ovrManager.isInsightPassthroughEnabled = true;
        rig.AddComponent<OVRPassthroughLayer>();
        foreach (var cam in rig.GetComponentsInChildren<Camera>())
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
        }

        // Depth
        var depthGo = new GameObject("EnvironmentDepth");
        var depthManager = depthGo.AddComponent<EnvironmentDepthManager>();

        // NDI sender + bridge
        var ndiGo = new GameObject("NDI Sender");
        var sender = ndiGo.AddComponent<NdiSender>();
        sender.SetResources(AssetDatabase.LoadAssetAtPath<NdiResources>(NdiResourcesPath));
        sender.ndiName = "Quest3_Depth";
        sender.captureMethod = CaptureMethod.Texture;
        sender.sourceTexture = rt;
        sender.keepAlpha = false;
        sender.enabled = false; // bridge enables it once depth is available

        var bridge = ndiGo.AddComponent<DepthToNdiBridge>();
        var so = new SerializedObject(bridge);
        so.FindProperty("_depthManager").objectReferenceValue = depthManager;
        so.FindProperty("_ndiSender").objectReferenceValue = sender;
        so.FindProperty("_depthRenderTexture").objectReferenceValue = rt;
        so.FindProperty("_blitShader").objectReferenceValue = Shader.Find("Hidden/Quest3Bridge/DepthArrayToRT");
        so.ApplyModifiedPropertiesWithoutUndo();

        // Color camera -> second NDI source for AI depth on the PC
        var colorGo = new GameObject("NDI Color Sender");
        var camAccess = colorGo.AddComponent<Meta.XR.PassthroughCameraAccess>();
        camAccess.CameraPosition = Meta.XR.PassthroughCameraAccess.CameraPositionType.Left;
        camAccess.RequestedResolution = new Vector2Int(1280, 960);
        var colorSender = colorGo.AddComponent<NdiSender>();
        colorSender.SetResources(AssetDatabase.LoadAssetAtPath<NdiResources>(NdiResourcesPath));
        colorSender.ndiName = "Quest3_Color";
        colorSender.captureMethod = CaptureMethod.Texture;
        colorSender.keepAlpha = false;
        colorSender.enabled = false; // ColorToNdi enables it once frames arrive
        var color = new SerializedObject(colorGo.AddComponent<ColorToNdi>());
        color.FindProperty("_bridge").objectReferenceValue = bridge;
        color.FindProperty("_camera").objectReferenceValue = camAccess;
        color.FindProperty("_ndiSender").objectReferenceValue = colorSender;
        color.ApplyModifiedPropertiesWithoutUndo();
        so.Update();
        so.FindProperty("_colorCamera").objectReferenceValue = camAccess;
        so.ApplyModifiedPropertiesWithoutUndo();

        var osc = new SerializedObject(ndiGo.AddComponent<OscControl>());
        osc.FindProperty("_bridge").objectReferenceValue = bridge;
        osc.ApplyModifiedPropertiesWithoutUndo();

        var controls = new SerializedObject(ndiGo.AddComponent<HeadsetControls>());
        controls.FindProperty("_bridge").objectReferenceValue = bridge;
        controls.FindProperty("_head").objectReferenceValue = rig.transform.Find("TrackingSpace/CenterEyeAnchor");
        controls.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}
