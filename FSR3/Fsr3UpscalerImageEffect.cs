// Copyright (c) 2024 Nico de Poel
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using FidelityFX;
using FidelityFX.FSR3;
using Il2CppInterop.Runtime.Attributes;
using MelonLoader;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace HPVR.FSR3
{
    /// <summary>
    /// This class is responsible for hooking into various Unity events and translating them to the FSR3 Upscaler subsystem.
    /// This includes creation and destruction of the FSR3 Upscaler context, as well as dispatching commands at the right time.
    /// This component also exposes various FSR3 Upscaler parameters to the Unity inspector.
    /// </summary>
    //[RequireComponent(typeof(Camera))]
    [RegisterTypeInIl2Cpp(true)]
    public class Fsr3UpscalerImageEffect : MonoBehaviour
    {
        [HideFromIl2Cpp]
        public IFsr3UpscalerCallbacks Callbacks { get; set; } = new Fsr3UpscalerCallbacksBase();

        //[Tooltip("Standard scaling ratio presets.")]
        public Fsr3Upscaler.QualityMode qualityMode = Fsr3Upscaler.QualityMode.Balanced;

        //[Tooltip("Apply RCAS sharpening to the image after upscaling.")]
        public bool performSharpenPass = true;
        //[Tooltip("Strength of the sharpening effect.")]
        //[Range(0, 1)]
        public float sharpness = 0.8f;

        //[Tooltip("Adjust the influence of motion vectors on temporal accumulation.")]
        //[Range(0, 1)]
        public float velocityFactor = 1.0f;

        //[Header("Exposure")]
        //[Tooltip("Allow an exposure value to be computed internally. When set to false, either the provided exposure texture or a default exposure value will be used.")]
        public bool enableAutoExposure = false;
        //[Tooltip("Value by which the input signal will be divided, to get back to the original signal produced by the game.")]
        public float preExposure = 0.5f;
        //[Tooltip("Optional 1x1 texture containing the exposure value for the current frame.")]
        public Texture exposure = null!;

        //[Header("Debug")]
        //[Tooltip("Enable a debug view to analyze the upscaling process.")]
        public bool enableDebugView = false;

        //[Header("Reactivity, Transparency & Composition")]
        //[Tooltip("Optional texture to control the influence of the current frame on the reconstructed output. If unset, either an auto-generated or a default cleared reactive mask will be used.")]
        public Texture reactiveMask = null!;
        //[Tooltip("Optional texture for marking areas of specialist rendering which should be accounted for during the upscaling process. If unset, a default cleared mask will be used.")]
        public Texture transparencyAndCompositionMask = null!;
        //[Tooltip("Automatically generate a reactive mask based on the difference between opaque-only render output and the final render output including alpha transparencies.")]
        public bool autoGenerateReactiveMask = true;
        //[Tooltip("Parameters to control the process of auto-generating a reactive mask.")]
        private readonly GenerateReactiveParameters generateReactiveParameters = new();
        [HideFromIl2Cpp]
        public GenerateReactiveParameters GenerateReactiveParams => generateReactiveParameters;

        [Serializable]
        public class GenerateReactiveParameters
        {
            //[Tooltip("A value to scale the output")]
            //[Range(0, 2)]
            public float scale = 0.5f;
            //[Tooltip("A threshold value to generate a binary reactive mask")]
            //[Range(0, 1)]
            public float cutoffThreshold = 0.2f;
            //[Tooltip("A value to set for the binary reactive mask")]
            //[Range(0, 1)]
            public float binaryValue = 0.9f;
            //[Tooltip("Flags to determine how to generate the reactive mask")]
            public Fsr3Upscaler.GenerateReactiveFlags flags = Fsr3Upscaler.GenerateReactiveFlags.ApplyTonemap | Fsr3Upscaler.GenerateReactiveFlags.ApplyThreshold | Fsr3Upscaler.GenerateReactiveFlags.UseComponentsMax;
        }

        //[Tooltip("(Experimental) Automatically generate and use Reactive mask and Transparency & composition mask internally.")]
        public bool autoGenerateTransparencyAndComposition = false;
        //[Tooltip("Parameters to control the process of auto-generating transparency and composition masks.")]
        private readonly GenerateTcrParameters generateTransparencyAndCompositionParameters = new();
        [HideFromIl2Cpp]
        public GenerateTcrParameters GenerateTcrParams => generateTransparencyAndCompositionParameters;

        [Serializable]
        public class GenerateTcrParameters
        {
            //[Tooltip("Setting this value too small will cause visual instability. Larger values can cause ghosting.")]
            //[Range(0, 1)]
            public float autoTcThreshold = 0.05f;
            //[Tooltip("Smaller values will increase stability at hard edges of translucent objects.")]
            //[Range(0, 2)]
            public float autoTcScale = 1.0f;
            //[Tooltip("Larger values result in more reactive pixels.")]
            //[Range(0, 10)]
            public float autoReactiveScale = 5.0f;
            //[Tooltip("Maximum value reactivity can reach.")]
            //[Range(0, 1)]
            public float autoReactiveMax = 0.9f;
        }

        internal static Fsr3UpscalerAssets? assets;

        private Fsr3UpscalerContext? _context;
        private Vector2Int _maxRenderSize;
        private Vector2Int _displaySize;
        private bool _resetHistory;

        private readonly Fsr3Upscaler.DispatchDescription _dispatchDescription = new();
        private readonly Fsr3Upscaler.GenerateReactiveDescription _genReactiveDescription = new();

        internal Fsr3UpscalerImageEffectHelper? _helper;

        private Camera? _renderCamera;
        //private HDCamera? HDCamera;
        private HDAdditionalCameraData? HDdata;
        private RenderTexture? _originalRenderTarget;
        private DepthTextureMode _originalDepthTextureMode;
        private Rect _originalRect;

        private Fsr3Upscaler.QualityMode _prevQualityMode;
        private Vector2Int _prevDisplaySize;
        private bool _prevAutoExposure;

        private RenderTexture? _colorOpaqueOnly;
        private RenderTexture? _colorOnly;
        private RenderTexture? _colorOnly2;
        private RenderTexture? _depth;
        private RTHandle? _depthFullHandle;
        private RenderTexture? _motion;

        private float scaleRatio = 1.0f;

        private Material? _copyDepth;
        private int _copyDepthPass;

        //private RTHandle blitBuffer;

        int i = 0;
        internal string scene;

        public bool Initialized { get; private set; } = false;

        protected void OnEnable()
        {
            Initialized = false;
        }

        internal void Init(Camera camera)
        {
            if (!Initialized)
            {
                MelonLogger.Msg("FSR init running");

                // Set up the original camera to output all of the required FSR3 input resources at the desired resolution
                //_renderCamera = GetComponent<Camera>();
                //we dont care about other cameras :D
                _renderCamera = camera;
                //MelonLogger.Error($"{_renderCamera.pixelWidth}x{_renderCamera.pixelHeight} | {_renderCamera.scaledPixelWidth}x{_renderCamera.scaledPixelHeight}");
                //MelonLogger.Error($"{_renderCamera.rect.width}x{_renderCamera.rect.height}");
                _originalRenderTarget = _renderCamera.targetTexture;
                //MelonLogger.Msg("orig render target is null? " + (_originalRenderTarget is null));
                _originalDepthTextureMode = _renderCamera.depthTextureMode;
                //_renderCamera.targetTexture = null;     // Clear the camera's target texture so we can fully control how the output gets written
                _renderCamera.depthTextureMode = _originalDepthTextureMode | DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

                // Determine the desired rendering and display resolutions
                _displaySize = GetDisplaySize();
                Fsr3Upscaler.GetRenderResolutionFromQualityMode(out var maxRenderWidth, out var maxRenderHeight, _displaySize.x, _displaySize.y, qualityMode);
                _maxRenderSize = new Vector2Int(maxRenderWidth, maxRenderHeight);
                //MelonLogger.Error($"{_maxRenderSize.x}x{_maxRenderSize.y} | {_renderCamera.pixelWidth}x{_renderCamera.pixelHeight}"); //hmm this is 0 here?

                assets = Fsr3UpscalerAssets.Create();
                scaleRatio = 1 / Fsr3Upscaler.GetUpscaleRatioFromQualityMode(qualityMode);

                if (!SystemInfo.supportsComputeShaders)
                {
                    MelonLogger.Error("FSR3 Upscaler requires compute shader support!");
                    enabled = false;
                    return;
                }

                if (assets == null || assets?.shaders is null)
                {
                    MelonLogger.Error($"FSR3 Upscaler assets are not assigned! Please ensure an {nameof(Fsr3UpscalerAssets)} asset is assigned to the Assets property of this component.");
                    enabled = false;
                    return;
                }

                if (_maxRenderSize.x == 0 || _maxRenderSize.y == 0)
                {
                    MelonLogger.Error($"FSR3 Upscaler render size is invalid: {_maxRenderSize.x}x{_maxRenderSize.y}. Please check your screen resolution and camera viewport parameters.");
                    enabled = false;
                    return;
                }

                _helper = GetComponent<Fsr3UpscalerImageEffectHelper>();
                _copyDepth = new Material(Fsr3UpscalerAssets.FindShader("DepthStealer"));
                _copyDepthPass = _copyDepth.FindPass("Depth");
                //_depthFullHandle = RTHandles.Alloc(_displaySize.x, _displaySize.y, colorFormat: GraphicsFormat.R32_SFloat, name: "FSR_DEPTH_FULLSIZE", enableRandomWrite: true);

                _copyDepth.SetFloat("_DepthScale", 25);
                _copyDepth.SetFloat("_TextureScale", scaleRatio);

                var scaledRenderSize = GetScaledRenderSize();
                //MelonLogger.Msg($"{scaledRenderSize.x} {scaledRenderSize.y}");
                _depthFullHandle = RTHandles.Alloc(scaledRenderSize.x, scaledRenderSize.y, colorFormat: GraphicsFormat.R32_SFloat, name: "FSR_DEPTH_TEMP", enableRandomWrite: true);
                //_depth = RenderTexture.GetTemporary(scaledRenderSize.x, scaledRenderSize.y, 0, RenderTextureFormat.RFloat); //GraphicsFormat.R32_FLOAT
                //_depth.enableRandomWrite = true;
                //_depth.name = "FSR_DEPTH_TEMP";
                _depth = _depthFullHandle.rt;

                //blitBuffer = RTHandles.Alloc(Vector2.one, TextureXR.slices, dimension: TextureXR.dimension, colorFormat: GraphicsFormat.R8G8B8A8_UInt, name: "Outline Buffer");

                //seems to work?
                //HDCamera ??= _renderCamera.GetComponent<HDCamera>();
                HDdata ??= _renderCamera.GetComponent<HDAdditionalCameraData>();
                HDdata.customRenderingSettings = true;
                var mask = HDdata.renderingPathCustomFrameSettingsOverrideMask;
                mask.mask[(uint)FrameSettingsField.MotionVectors] = true;
                //mask.mask[(uint)FrameSettingsField.DepthPrepassWithDeferredRendering] = true; //the docs say this only enables object motion vectors and has nothing to do with depth. without we only get camera motion vectors, which is enough for us
                HDdata.renderingPathCustomFrameSettingsOverrideMask = mask;

                scaleRatio = 1 / Fsr3Upscaler.GetUpscaleRatioFromQualityMode(qualityMode);

                CreateFsrContext();
                MelonLogger.Msg("FSR enabled");
                //MelonLogger.Msg("after context test " + _context?._generateReactivePass?.ComputeShader?.name);
                //MelonLogger.Msg("check assets " + assets.shaders.autoGenReactivePass!.name);
                Initialized = true;
            }
        }

        protected void OnDisable()
        {
            MelonLogger.Msg("FSR shutdown");
            DestroyFsrContext();

            if (_copyDepth != null)
            {
                Destroy(_copyDepth);
                _copyDepth = null!;
            }

            // Restore the camera's original state
            _renderCamera!.depthTextureMode = _originalDepthTextureMode;
            _renderCamera!.targetTexture = _originalRenderTarget;

            RTHandles.Release(_depthFullHandle);

            Camera.main.rect = new(0, 0, 1, 1);

            Initialized = false;
        }

        private void CreateFsrContext()
        {
            // Initialize FSR3 Upscaler context
            Fsr3Upscaler.InitializationFlags flags = 0;
            if (_renderCamera?.allowHDR ?? false)
            {
                flags |= Fsr3Upscaler.InitializationFlags.EnableHighDynamicRange;
            }

            if (enableAutoExposure)
            {
                flags |= Fsr3Upscaler.InitializationFlags.EnableAutoExposure;
            }

            if (UsingDynamicResolution())
            {
                flags |= Fsr3Upscaler.InitializationFlags.EnableDynamicResolution;
            }

            _context = Fsr3Upscaler.CreateContext(_displaySize, _maxRenderSize, assets!.shaders!, flags);

            _prevDisplaySize = _displaySize;
            _prevQualityMode = qualityMode;
            _prevAutoExposure = enableAutoExposure;

            ApplyMipmapBias();
            //MelonLogger.Msg("created context");
            //MelonLogger.Msg("context test? " + _context?._generateReactivePass?.ComputeShader?.name);
        }

        private void DestroyFsrContext()
        {
            UndoMipmapBias();

            if (_context != null)
            {
                _context.Destroy();
                _context = null;
            }
        }

        private void ApplyMipmapBias()
        {
            // Apply a mipmap bias so that textures retain their sharpness
            float biasOffset = Fsr3Upscaler.GetMipmapBiasOffset(_maxRenderSize.x, _displaySize.x);
            if (!float.IsNaN(biasOffset) && !float.IsInfinity(biasOffset))
            {
                Callbacks.ApplyMipmapBias(biasOffset);
            }
        }

        private void UndoMipmapBias()
        {
            // Undo the current mipmap bias offset
            Callbacks.UndoMipmapBias();
        }

        protected void Update()
        {
            // Monitor for any changes in parameters that require a reset of the FSR3 Upscaler context
            var displaySize = GetDisplaySize();
            //MelonLogger.Msg("sizes:");
            //MelonLogger.Msg($"{displaySize.x}|{displaySize.y}");
            //MelonLogger.Msg($"{_prevDisplaySize.x}|{_prevDisplaySize.y}");
            if (displaySize.x != _prevDisplaySize.x || displaySize.y != _prevDisplaySize.y || qualityMode != _prevQualityMode || enableAutoExposure != _prevAutoExposure)
            {
                _prevDisplaySize = displaySize;
                // Force all resources to be destroyed and recreated with the new settings
                OnDisable();
                OnEnable();
                //todo use the cinemachine cutscene cams here.
                Init(Camera.main);
            }
        }

        public void ResetHistory()
        {
            // Reset the temporal accumulation, for when the camera cuts to a different location or angle
            _resetHistory = true;
        }

        protected void LateUpdate()
        {
            if (Initialized && _renderCamera is not null)
            {
                // Remember the original camera viewport before we modify it in OnPreCull
                _originalRect = _renderCamera.rect;

                //is called
                //MelonLogger.Msg("FSR late update");
            }
        }

        internal void OnPreCull()
        {
            if (!Initialized || _renderCamera is null)
            {
                //MelonLogger.Msg($"init: {Initialized}  cam: {_renderCamera is null}");
                return;
            }

            if (_helper == null || !_helper.enabled)
            {
                MelonLogger.Msg("helper is null??  enabled: " + _helper?.enabled);
                // Render to a smaller portion of the screen by manipulating the camera's viewport rect
                _renderCamera.aspect = (float)_displaySize.x / _displaySize.y;
                _renderCamera.rect = new Rect(0, 0, _originalRect.width * _maxRenderSize.x / _renderCamera.pixelWidth, _originalRect.height * _maxRenderSize.y / _renderCamera.pixelHeight);
            }

            //MelonLogger.Msg(scene + " OnPreCull");

            // Set up the opaque-only command buffer to make a copy of the camera color buffer right before transparent drawing starts 
            if (autoGenerateReactiveMask || autoGenerateTransparencyAndComposition)
            {
                MakeColorOpaqueTex();
            }

            ApplyJitter();
        }

        private void MakeColorOpaqueTex()
        {
            var scaledRenderSize = GetScaledRenderSize();
            //MelonLogger.Msg($"{scaledRenderSize.x}:{scaledRenderSize.y}  --   {Camera.main.pixelWidth}:{Camera.main.pixelHeight}");
            _colorOpaqueOnly = RenderTexture.GetTemporary(scaledRenderSize.x, scaledRenderSize.y, 0, FsrPreRefraction.Context!.cameraColorBuffer.rt.graphicsFormat); //GraphicsFormat.B10G11R11_UFloatPack32
            _colorOpaqueOnly.enableRandomWrite = true;
            _colorOpaqueOnly.name = "FSR_COLOROPAQUEONLY_TEMP";

            CoreUtils.SetRenderTarget(FsrPreRefraction.Context!.cmd, _colorOpaqueOnly);
            HDUtils.BlitTexture(FsrPreRefraction.Context!.cmd, FsrPreRefraction.Context!.cameraColorBuffer, new(scaleRatio, scaleRatio, 0, 0), 0, true);
            //var displaySize = GetDisplaySize();
            //Graphics.CopyTexture_Region(FsrPreRefraction.Context!.cameraColorBuffer.rt, 0, 0, 0, displaySize.y - scaledRenderSize.y, scaledRenderSize.x, scaledRenderSize.y, _colorOpaqueOnly, 0, 0, 0, 0);
        }

        private void SetupDispatchDescription()
        {
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                return;
            }

            var scaledRenderSize = GetScaledRenderSize();

            _dispatchDescription.Color = new ResourceView(_colorOnly2);
            _dispatchDescription.Depth = new ResourceView(_depth);
            _dispatchDescription.MotionVectors = new ResourceView(_motion);
            _dispatchDescription.Exposure = ResourceView.Unassigned;
            _dispatchDescription.Reactive = ResourceView.Unassigned;
            _dispatchDescription.TransparencyAndComposition = ResourceView.Unassigned;

            if (!enableAutoExposure && exposure != null)
            {
                _dispatchDescription.Exposure = new ResourceView(exposure);
            }

            if (reactiveMask != null)
            {
                _dispatchDescription.Reactive = new ResourceView(reactiveMask);
            }

            if (transparencyAndCompositionMask != null)
            {
                _dispatchDescription.TransparencyAndComposition = new ResourceView(transparencyAndCompositionMask);
            }

            _dispatchDescription.Output = new ResourceView(Fsr3ShaderIDs.UavUpscaledOutput);
            _dispatchDescription.PreExposure = preExposure;
            _dispatchDescription.EnableSharpening = performSharpenPass;
            _dispatchDescription.Sharpness = sharpness;
            _dispatchDescription.MotionVectorScale.x = -scaledRenderSize.x;
            _dispatchDescription.MotionVectorScale.y = -scaledRenderSize.y;
            _dispatchDescription.RenderSize = scaledRenderSize;
            _dispatchDescription.UpscaleSize = _displaySize;
            _dispatchDescription.FrameTimeDelta = Time.unscaledDeltaTime;
            _dispatchDescription.CameraNear = _renderCamera.nearClipPlane; //0.01 in game main
            _dispatchDescription.CameraFar = _renderCamera.farClipPlane; //5k in game main
            _dispatchDescription.CameraFovAngleVertical = _renderCamera.fieldOfView * Mathf.Deg2Rad;
            _dispatchDescription.ViewSpaceToMetersFactor = 1.0f; // 1 unit is 1 meter in Unity
            _dispatchDescription.VelocityFactor = velocityFactor;
            _dispatchDescription.Reset = _resetHistory;
            _dispatchDescription.Flags = enableDebugView ? Fsr3Upscaler.DispatchFlags.DrawDebugView : 0;
            _resetHistory = false;

            // Set up the parameters for the optional experimental auto-TCR feature
            _dispatchDescription.EnableAutoReactive = autoGenerateTransparencyAndComposition;
            if (autoGenerateTransparencyAndComposition)
            {
                _dispatchDescription.ColorOpaqueOnly = new ResourceView(_colorOpaqueOnly);
                _dispatchDescription.AutoTcThreshold = generateTransparencyAndCompositionParameters.autoTcThreshold;
                _dispatchDescription.AutoTcScale = generateTransparencyAndCompositionParameters.autoTcScale;
                _dispatchDescription.AutoReactiveScale = generateTransparencyAndCompositionParameters.autoReactiveScale;
                _dispatchDescription.AutoReactiveMax = generateTransparencyAndCompositionParameters.autoReactiveMax;
            }

            if (SystemInfo.usesReversedZBuffer)
            {
                //MelonLogger.Msg("inverted depth 1");
                // Swap the near and far clip plane distances as FSR3 expects this when using inverted depth
                _dispatchDescription.CameraFar = _renderCamera.nearClipPlane; //0.01 in game main
                _dispatchDescription.CameraNear = _renderCamera.farClipPlane; //5k in game main
            }
        }

        private void CopyTextures()
        {
            //###############    motion    ############################
            CoreUtils.SetRenderTarget(FsrPrePostProcess.Context!.cmd, _motion);
            HDUtils.BlitTexture(FsrPrePostProcess.Context!.cmd, FsrPrePostProcess.Context.cameraMotionVectorsBuffer, new(scaleRatio, scaleRatio, 0, 0), 0, true);

            //###############    depth    ############################
            CoreUtils.SetRenderTarget(FsrPrePostProcess.Context!.cmd, _depth);
            HDUtils.DrawFullScreen(FsrPrePostProcess.Context!.cmd, _copyDepth, _depthFullHandle, shaderPassId: _copyDepthPass);

            //###############    color    ############################
            CoreUtils.SetRenderTarget(FsrPrePostProcess.Context!.cmd, _colorOnly2);
            HDUtils.BlitTexture(FsrPrePostProcess.Context!.cmd, FsrPrePostProcess.Context.cameraColorBuffer, new(scaleRatio, scaleRatio, 0, 0), 0, true);
        }

        private void SetupAutoReactiveDescription()
        {
            // Set up the parameters to auto-generate a reactive mask
            _genReactiveDescription.ColorOpaqueOnly = new ResourceView(_colorOpaqueOnly);
            _genReactiveDescription.OutReactive = new ResourceView(Fsr3ShaderIDs.UavAutoReactive);
            _genReactiveDescription.RenderSize = GetScaledRenderSize();
            _genReactiveDescription.Scale = generateReactiveParameters.scale;
            _genReactiveDescription.CutoffThreshold = generateReactiveParameters.cutoffThreshold;
            _genReactiveDescription.BinaryValue = generateReactiveParameters.binaryValue;
            _genReactiveDescription.Flags = generateReactiveParameters.flags;

            CoreUtils.SetRenderTarget(FsrPrePostProcess.Context!.cmd, _colorOnly);
            HDUtils.BlitTexture(FsrPrePostProcess.Context!.cmd, FsrPrePostProcess.Context.cameraColorBuffer, new(scaleRatio, scaleRatio, 0, 0), 0, true);

            _genReactiveDescription.ColorPreUpscale = new ResourceView(_colorOnly);
        }

        private void ApplyJitter()
        {
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                return;
            }
            var scaledRenderSize = GetScaledRenderSize();

            // Perform custom jittering of the camera's projection matrix according to FSR3's recipe
            int jitterPhaseCount = Fsr3Upscaler.GetJitterPhaseCount(scaledRenderSize.x, _displaySize.x);
            Fsr3Upscaler.GetJitterOffset(out float jitterX, out float jitterY, Time.frameCount, jitterPhaseCount);

            _dispatchDescription.JitterOffset = new Vector2(jitterX, jitterY);

            jitterX = 2.0f * jitterX / scaledRenderSize.x;
            jitterY = 2.0f * jitterY / scaledRenderSize.y;

            var jitterTranslationMatrix = Matrix4x4.Translate(new Vector3(jitterX, jitterY, 0));
            _renderCamera.nonJitteredProjectionMatrix = _renderCamera.projectionMatrix;
            _renderCamera.projectionMatrix = jitterTranslationMatrix * _renderCamera.nonJitteredProjectionMatrix;
            _renderCamera.useJitteredProjectionMatrixForTransparentRendering = true;
        }

        public void OnRenderImage()
        {
            //this (vias the execute method on the custompass) is actually called way before the actual commandbuffer is executed -.-
            if (!Initialized)
            {
                //MelonLogger.Msg($"init render: {Initialized}");
                return;
            }
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                return;
            }
            //MelonLogger.Msg(scene + " OnRenderImage");
            var scaledRenderSize = GetScaledRenderSize();

            //this seems to work fine
            CreateTemporaryRTs(scaledRenderSize);

            var _dispatchCommandBuffer = FsrPrePostProcess.Context!.cmd;
            //MelonLogger.Msg("pre async callback");

            //MelonLogger.Msg("async callback");
            //do it here because we then have the current context
            if (autoGenerateReactiveMask)
            {
                //MelonLogger.Msg("FSR on pre cull5");
                SetupAutoReactiveDescription();
            }

            // Set up the main FSR3 Upscaler dispatch parameters
            CopyTextures();

            // Restore the camera's viewport rect so we can output at full resolution
            //MelonLogger.Msg($"restoring camera from {_renderCamera.rect.width}:{_renderCamera.rect.height} rect to {_originalRect.width}:{_originalRect.height}");
            _renderCamera.rect = _originalRect;
            _renderCamera.ResetProjectionMatrix();
            //MelonLogger.Msg("post async callback");

            SetupDispatchDescription();

            //_dispatchCommandBuffer.Clear();

            if (autoGenerateReactiveMask)
            {
                // The auto-reactive mask pass is executed separately from the main FSR3 Upscaler passes
                _dispatchCommandBuffer.GetTemporaryRT(Fsr3ShaderIDs.UavAutoReactive, scaledRenderSize.x, scaledRenderSize.y, 0, default, GraphicsFormat.R8_UNorm, 1, true); //works

                _context?.GenerateReactiveMask(_genReactiveDescription, _dispatchCommandBuffer);
                _dispatchDescription.Reactive = new ResourceView(Fsr3ShaderIDs.UavAutoReactive);
            }

            // The backbuffer is not set up to allow random-write access, so we need a temporary render texture for FSR3 to output to
            _dispatchCommandBuffer.GetTemporaryRT(Fsr3ShaderIDs.UavUpscaledOutput, _displaySize.x, _displaySize.y, 0, FilterMode.Point, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 1, true);
            //outputHandle.SetRenderTexture(new(_displaySize.x, _displaySize.y, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default));

            //_context!._contextDescription.Flags |= Fsr3Upscaler.InitializationFlags.EnableDebugChecking;
            _context?.Dispatch(_dispatchDescription, _dispatchCommandBuffer);

            // Output the upscaled image
            //this is fine, we should jsut copy what we have here into the full screen buffer
            _dispatchCommandBuffer.SetRenderTarget(FsrPrePostProcess.Context.cameraColorBuffer);
            _dispatchCommandBuffer.ClearRenderTarget(true, true, Color.clear);
            _dispatchCommandBuffer.Blit(Fsr3ShaderIDs.UavUpscaledOutput, Display.main.colorBuffer); //works, but is overwritten somehow by other shit

            //Graphics.ExecuteCommandBuffer(_dispatchCommandBuffer);

            _dispatchCommandBuffer.ReleaseTemporaryRT(Fsr3ShaderIDs.UavUpscaledOutput);
            _dispatchCommandBuffer.ReleaseTemporaryRT(Fsr3ShaderIDs.UavAutoReactive);

            if (_colorOpaqueOnly != null)
            {
                RenderTexture.ReleaseTemporary(_colorOpaqueOnly);
                _colorOpaqueOnly = null!;
            }
            if (_colorOnly != null)
            {
                RenderTexture.ReleaseTemporary(_colorOnly);
                _colorOnly = null!;
            }
            if (_colorOnly2 != null)
            {
                RenderTexture.ReleaseTemporary(_colorOnly2);
                _colorOnly2 = null!;
            }
            //if (_depth != null)
            //{
            //    RenderTexture.ReleaseTemporary(_depth);
            //    _depth = null!;
            //}
            if (_motion != null)
            {
                RenderTexture.ReleaseTemporary(_motion);
                _motion = null!;
            }
            //MelonLogger.Msg("post execute");

            //MelonLogger.Msg($"{FsrPrePostProcess.Context!.cameraColorBuffer.rt.width}x{FsrPrePostProcess.Context!.cameraColorBuffer.rt.height}");
            //todo this only works for some qualitysettings for whatever reason, gotta debug more
            if (FsrPrePostProcess.Context!.cameraColorBuffer.rt.width != _displaySize.x && FsrPrePostProcess.Context!.cameraColorBuffer.rt.height != _displaySize.y)
            {
                _renderCamera.pixelRect = new(0, 0, FsrPrePostProcess.Context!.cameraColorBuffer.rt.width, FsrPrePostProcess.Context!.cameraColorBuffer.rt.height);
            }
        }

        private void CreateTemporaryRTs(Vector2Int scaledRenderSize)
        {
            _colorOnly2 = RenderTexture.GetTemporary(scaledRenderSize.x, scaledRenderSize.y, 0, FsrPrePostProcess.Context!.cameraColorBuffer.rt.graphicsFormat); //GraphicsFormat.B10G11R11_UFloatPack32
            _colorOnly2.enableRandomWrite = true;
            _colorOnly2.name = "FSR_COLORONLY_TEMP2";

            //this seems very fine
            _motion = RenderTexture.GetTemporary(scaledRenderSize.x, scaledRenderSize.y, 0, FsrPrePostProcess.Context!.cameraMotionVectorsBuffer.rt.graphicsFormat); //GraphicsFormat.R16G16_FLOAT
            _motion.enableRandomWrite = true;
            _motion.name = "FSR_MOTION_TEMP";

            _colorOnly = RenderTexture.GetTemporary(scaledRenderSize.x, scaledRenderSize.y, 0, FsrPrePostProcess.Context!.cameraColorBuffer.rt.graphicsFormat); //GraphicsFormat.B10G11R11_UFloatPack32
            _colorOnly.enableRandomWrite = true;
            _colorOnly.name = "FSR_COLORONLY_TEMP";
        }

        private Vector2Int GetDisplaySize()
        {
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                throw new InvalidOperationException("_rendercamera was null");
            }
            return new Vector2Int(Display.main.renderingWidth, Display.main.renderingHeight);
            //return new Vector2Int(Camera.main.pixelWidth, Camera.main.pixelHeight);
        }

        private bool UsingDynamicResolution()
        {
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                return false;
            }
            return _renderCamera.allowDynamicResolution || _originalRenderTarget != null && _originalRenderTarget.useDynamicScale;
        }

        private Vector2Int GetScaledRenderSize()
        {
            if (UsingDynamicResolution())
            {
                return new Vector2Int(Mathf.CeilToInt(_maxRenderSize.x * ScalableBufferManager.widthScaleFactor), Mathf.CeilToInt(_maxRenderSize.y * ScalableBufferManager.heightScaleFactor));
            }

            return _maxRenderSize;
        }
    }
}
