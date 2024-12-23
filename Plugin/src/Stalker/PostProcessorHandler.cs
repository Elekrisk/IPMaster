using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GraphicsAPI.CustomPostProcessing;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering.HighDefinition;

namespace IPMaster.Stalker;

class PostProcessorHandler : MonoBehaviour
{
    public float MinVolume = 40;

    Material fullscreenMaterial = null!;
    FullScreenCustomPass customPass = null!;
    RenderTexture? renderTexture = null;
    GameObject? stalkerCamera = null;

    Dictionary<int, float> activeStalkers = [];
    int closestStalker = -1;
    float smallestDist = float.PositiveInfinity;

    float currentStrength = 0.0f;

    float _strengthTarget = 0.0f;
    float StrengthTarget
    {
        get { return _strengthTarget; }
        set
        {
            _strengthTarget = value;
            if (value > currentStrength)
            {
                UpdateStrengthValue(value);
            }
        }
    }
    float strengthDecay = 0.5f;

    bool passEnabled = false;

    public AudioMixer AudioMixer = null!;

    public static PostProcessorHandler Instance { get; private set; } = null!;

    void UpdateStrengthValue(float value)
    {
        currentStrength = value;
        fullscreenMaterial.SetFloat("_Strength", value);
        if (!AudioMixer.SetFloat("AllExceptStalkerVolume", -value * MinVolume)) {
            Plugin.Logger.LogWarning("Cannot set AllExceptStalkerVolume property");
        }
    }

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Instance = this;
    }

    void Update()
    {
        if (currentStrength > StrengthTarget)
        {
            currentStrength -= strengthDecay * Time.deltaTime;
            if (currentStrength < StrengthTarget)
            {
                currentStrength = StrengthTarget;
            }
            UpdateStrengthValue(currentStrength);
        }

        RecreateRenderTextureIfNeeded();

        if (!passEnabled && currentStrength == 0.0f && customPass.enabled)
        {
            passEnabled = false;
            customPass.enabled = false;
            DestroyStalkerCamera();
            RestoreAudioMixer();
        }
    }


    public void Initialize(Material material, FullScreenCustomPass customPass)
    {
        fullscreenMaterial = material;
        this.customPass = customPass;
        customPass.enabled = false;
    }

    void CreateStalkerCamera()
    {
        stalkerCamera = new GameObject("StalkerCamera");
        Camera camera = stalkerCamera.AddComponent<Camera>();
        Camera gameCamera = StartOfRound.Instance.localPlayerController.gameplayCamera;
        camera.fieldOfView = gameCamera.fieldOfView;
        camera.backgroundColor = Color.black;
        camera.cullingMask = 1 << 31;
        stalkerCamera.AddComponent<HDAdditionalCameraData>();
        stalkerCamera.transform.SetParent(gameCamera.transform, false);
        RecreateRenderTextureIfNeeded(force: true);

        gameCamera.cullingMask &= ~(1 << 31);
    }

    void DestroyStalkerCamera()
    {
        if (stalkerCamera != null)
        {
            Destroy(stalkerCamera);
            stalkerCamera = null;
        }
    }

    public void RecreateRenderTextureIfNeeded(bool force = false)
    {
        Camera camera = StartOfRound.Instance.localPlayerController.gameplayCamera;

        if (force || renderTexture == null || !renderTexture.IsCreated() || renderTexture.width != camera.pixelWidth || renderTexture.height != camera.pixelHeight)
        {
            renderTexture?.Release();
            renderTexture = new RenderTexture(camera.pixelWidth, camera.pixelHeight, 0);
            if (stalkerCamera != null)
            {
                stalkerCamera.GetComponent<Camera>().targetTexture = renderTexture;
            }
            fullscreenMaterial.SetTexture("_StalkerRenderTexture", renderTexture);
        }
    }

    public void RegisterStalker(StalkerAI stalker)
    {
        Plugin.Logger.LogInfo("Registering stalker " + stalker.StalkerId);
        if (activeStalkers.Count == 0)
        {
            Enable();
        }
        activeStalkers.Add(stalker.StalkerId, float.PositiveInfinity);
    }

    public void DeregisterStalker(StalkerAI stalker)
    {
        activeStalkers.Remove(stalker.StalkerId);

        if (closestStalker == stalker.StalkerId)
        {
            RecalculateClosestStalker();
        }

        if (activeStalkers.Count == 0)
        {
            Disable();
        }
    }

    public void UpdateDistance(StalkerAI stalker, float distance)
    {
        activeStalkers[stalker.StalkerId] = distance;

        if (stalker.StalkerId == closestStalker)
        {
            if (distance > smallestDist)
            {
                RecalculateClosestStalker();
            }
            else
            {
                UpdateSmallestDistance(distance);
            }
        }
        else
        {
            if (distance < smallestDist)
            {
                closestStalker = stalker.StalkerId;
                UpdateSmallestDistance(distance);
            }
        }
    }

    void RecalculateClosestStalker()
    {
        KeyValuePair<int, float> closest = new(-1, float.PositiveInfinity);
        if (activeStalkers.Count > 0)
        {
            closest = activeStalkers.Aggregate((l, r) => l.Value < r.Value ? l : r);
        }
        closestStalker = closest.Key;
        UpdateSmallestDistance(closest.Value);
    }

    void UpdateSmallestDistance(float distance)
    {
        smallestDist = distance;
        StrengthTarget = Mathf.Pow(Mathf.Clamp01(1.0f - (smallestDist / 10.0f)), 2.0f);
    }

    public void Enable()
    {
        passEnabled = true;
        customPass.enabled = true;
        fullscreenMaterial.SetFloat("_Strength", 0.0f);
        currentStrength = 0;
        _strengthTarget = 0;
        CreateStalkerCamera();
        RecreateRenderTextureIfNeeded();
        InjectAudioMixer();
    }

    public void InjectAudioMixer() {
        SoundManager.Instance.diageticMixer.outputAudioMixerGroup = AudioMixer.FindMatchingGroups("AllExceptStalker")[0];
    }

    public void RestoreAudioMixer() {
        SoundManager.Instance.diageticMixer.outputAudioMixerGroup = null;
    }

    public void Disable()
    {
        StrengthTarget = 0.0f;
        passEnabled = false;
    }
}