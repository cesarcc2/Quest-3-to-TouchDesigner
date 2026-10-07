using System;
using UnityEngine;

// Runtime-tunable stream settings (Kinect-panel style). Shared by headset controls and OSC.
[Serializable]
public sealed class DepthStreamSettings
{
    public static readonly int[] FpsSteps = { 15, 30, 60, 72, 90, 120 };
    public static readonly int[] ResolutionSteps = { 320, 640, 960, 1280 };

    public enum Encoding { Gray8 = 0, Packed16 = 1, Detail = 2 }

    public int fps = 120;
    public int resolution = 1280;
    public int eye = 0;                 // 0 = left, 1 = right
    public bool mirror = false;
    public bool kinectScale = true;     // Kinect TOP Depth convention: value = meters / 8.191 (near = dark)
    public bool invert = true;          // normalized mode only: true = near is 1 (white)
    public float minDepth = 0.1f;       // meters
    public float maxDepth = 5f;         // meters
    public float tooCloseValue = 0f;    // output for pixels nearer than minDepth
    public float tooFarValue = 0f;      // output for pixels beyond maxDepth
    public float unknownValue = 0f;     // output for pixels with no depth
    public Encoding encoding = Encoding.Gray8;
    public bool colorStream = true;     // Quest3_Color NDI stream (passthrough camera) for AI depth
    public int colorFps = 30;
    public bool alignToColor = true;    // depth resampled from the color camera's view (needs colorStream)
    public bool colorFlip = false;      // extra horizontal flip of the camera image (Mirror Image flips both streams)
    public bool lowPower = false;       // 30 fps, 320 px, 72 Hz display

    const string PrefsKey = "DepthStreamSettings";

    public void Clamp()
    {
        fps = Mathf.Clamp(fps, 1, 120);
        resolution = Mathf.Clamp(resolution, 64, 2048);
        eye = Mathf.Clamp(eye, 0, 1);
        minDepth = Mathf.Clamp(minDepth, 0f, 10f);
        maxDepth = Mathf.Clamp(maxDepth, minDepth + 0.05f, 20f);
        tooCloseValue = Mathf.Clamp01(tooCloseValue);
        tooFarValue = Mathf.Clamp01(tooFarValue);
        unknownValue = Mathf.Clamp01(unknownValue);
    }

    // Kinect "near mode" equivalent
    public void SetNearMode(bool near)
    {
        if (near) { minDepth = 0.1f; maxDepth = 1.5f; }
        else      { minDepth = 0.3f; maxDepth = 5f; }
    }

    public int EffectiveFps => lowPower ? Mathf.Min(fps, 30) : fps;
    public int EffectiveResolution => lowPower ? Mathf.Min(resolution, 320) : resolution;

    public static int Step(int[] steps, int current, int dir)
    {
        var i = Array.FindIndex(steps, s => s >= current);
        if (i < 0) i = steps.Length - 1;
        else if (steps[i] != current && dir > 0) i--; // between steps: land on next one up
        return steps[Mathf.Clamp(i + dir, 0, steps.Length - 1)];
    }

    public void Save() { try { PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(this)); PlayerPrefs.Save(); } catch { } }
    public void Load() { try { if (PlayerPrefs.HasKey(PrefsKey)) JsonUtility.FromJsonOverwrite(PlayerPrefs.GetString(PrefsKey), this); } catch { } Clamp(); }

    public override string ToString() =>
        $"{EffectiveFps} fps  {EffectiveResolution}px  {minDepth:0.00}-{maxDepth:0.00} m\n" +
        $"{encoding}  mirror {(mirror ? "on" : "off")}  {(lowPower ? "LOW POWER" : "")}";
}
