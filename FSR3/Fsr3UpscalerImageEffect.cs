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
using HPVR.UI;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

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
        public Fsr3Upscaler.QualityMode qualityMode = Fsr3Upscaler.QualityMode.UltraPerformance;

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
        public bool enableAutoExposure = true;
        //[Tooltip("Value by which the input signal will be divided, to get back to the original signal produced by the game.")]
        public float preExposure = 1.0f;
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
        private RenderTexture? _originalRenderTarget;
        private DepthTextureMode _originalDepthTextureMode;
        private Rect _originalRect;

        private Fsr3Upscaler.QualityMode _prevQualityMode;
        private Vector2Int _prevDisplaySize;
        private bool _prevAutoExposure;

        private CommandBuffer? _dispatchCommandBuffer;
        private CommandBuffer? _opaqueInputCommandBuffer;
        private RenderTexture? _colorOpaqueOnly;

        private Material? _copyWithDepthMaterial;

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
                _originalRenderTarget = _renderCamera.targetTexture;
                _originalDepthTextureMode = _renderCamera.depthTextureMode;
                _renderCamera.targetTexture = null;     // Clear the camera's target texture so we can fully control how the output gets written
                _renderCamera.depthTextureMode = _originalDepthTextureMode | DepthTextureMode.Depth | DepthTextureMode.MotionVectors;

                // Determine the desired rendering and display resolutions
                _displaySize = GetDisplaySize();
                Fsr3Upscaler.GetRenderResolutionFromQualityMode(out var maxRenderWidth, out var maxRenderHeight, _displaySize.x, _displaySize.y, qualityMode);
                _maxRenderSize = new Vector2Int(maxRenderWidth, maxRenderHeight);

                assets = Fsr3UpscalerAssets.Create();

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
                _copyWithDepthMaterial = new Material(Shader.Find("Hidden/BlitCopyWithDepth"));

                CreateFsrContext();
                CreateCommandBuffers();
                MelonLogger.Msg("FSR enabled");
                //MelonLogger.Msg("after context test " + _context?._generateReactivePass?.ComputeShader?.name);
                //MelonLogger.Msg("check assets " + assets.shaders.autoGenReactivePass!.name);
                Initialized = true;
            }
        }

        private void OnDisable()
        {
            //DestroyCommandBuffers();
            //DestroyFsrContext();

            //if (_copyWithDepthMaterial != null)
            //{
            //    Destroy(_copyWithDepthMaterial);
            //    _copyWithDepthMaterial = null!;
            //}

            //// Restore the camera's original state
            //_renderCamera.depthTextureMode = _originalDepthTextureMode;
            //_renderCamera.targetTexture = _originalRenderTarget;
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

            _prevDisplaySize = _context._contextDescription.MaxRenderSize;
            _prevQualityMode = qualityMode;
            _prevAutoExposure = enableAutoExposure;

            ApplyMipmapBias();
            MelonLogger.Msg("created context");
            MelonLogger.Msg("context test? " + _context?._generateReactivePass?.ComputeShader?.name);
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

        private void CreateCommandBuffers()
        {
            //todo this command buffer has no temporary render texture on SetComputeTextureParam
            _dispatchCommandBuffer = new CommandBuffer { name = "FSR3 Upscaler Dispatch" };
            _opaqueInputCommandBuffer = new CommandBuffer { name = "FSR3 Upscaler Opaque Input" };
            _renderCamera?.AddCommandBuffer(CameraEvent.BeforeForwardAlpha, _opaqueInputCommandBuffer);
            MelonLogger.Msg("Added command buffers");
        }

        private void DestroyCommandBuffers()
        {
            if (_opaqueInputCommandBuffer != null)
            {
                _renderCamera?.RemoveCommandBuffer(CameraEvent.BeforeForwardAlpha, _opaqueInputCommandBuffer);
                _opaqueInputCommandBuffer.Release();
                _opaqueInputCommandBuffer = null!;
            }

            if (_dispatchCommandBuffer != null)
            {
                _dispatchCommandBuffer.Release();
                _dispatchCommandBuffer = null!;
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
            //todo re-enable
            // Monitor for any changes in parameters that require a reset of the FSR3 Upscaler context
            //var displaySize = GetDisplaySize();
            //MelonLogger.Msg("sizes:");
            //MelonLogger.Msg($"{displaySize.x}|{displaySize.y}");
            //MelonLogger.Msg($"{_prevDisplaySize.x}|{_prevDisplaySize.y}");
            //if (displaySize.x != _prevDisplaySize.x || displaySize.y != _prevDisplaySize.y || qualityMode != _prevQualityMode || enableAutoExposure != _prevAutoExposure)
            //{
            //    // Force all resources to be destroyed and recreated with the new settings
            //    OnDisable();
            //    OnEnable();
            //}

            //is called
            //MelonLogger.Msg("fsr update");
            //if (assets?.shaders?.autoGenReactivePass is not null && _context?._generateReactivePass?.ComputeShader is not null)
            //{
            //    try
            //    {
            //        //MelonLogger.Msg("check assets " + assets.shaders.autoGenReactivePass?.name);
            //        // MelonLogger.Msg("update loop pass test" + _context?._generateReactivePass?.ComputeShader?.name);
            //    }
            //    catch (Exception e)
            //    {
            //        MelonLogger.Error(e);
            //        MelonLogger.Msg("creating new");
            //        var shader = Fsr3UpscalerAssets.FindComputeShader("ffx_fsr3upscaler_autogen_reactive_pass");
            //        MelonLogger.Msg("check new " + shader.name);
            //        assets.shaders.autoGenReactivePass = shader;
            //        _context._contextDescription.Shaders.autoGenReactivePass = shader;
            //        MelonLogger.Msg("check assets " + assets.shaders.autoGenReactivePass?.name);
            //        _context._generateReactivePass = new Fsr3UpscalerGenerateReactivePass(_context._contextDescription, _context._resources, _context._generateReactiveConstantsBuffer);
            //        MelonLogger.Msg("second test" + _context?._generateReactivePass?.ComputeShader?.name);
            //    }
            //}
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
            if (!Initialized || _opaqueInputCommandBuffer is null || _renderCamera is null)
            {
                return;
            }

            if (_helper == null || !_helper.enabled)
            {
                MelonLogger.Msg("helper is null??");
                // Render to a smaller portion of the screen by manipulating the camera's viewport rect
                _renderCamera.aspect = (float)_displaySize.x / _displaySize.y;
                _renderCamera.rect = new Rect(0, 0, _originalRect.width * _maxRenderSize.x / _renderCamera.pixelWidth, _originalRect.height * _maxRenderSize.y / _renderCamera.pixelHeight);
            }

            //MelonLogger.Msg("FSR on pre cull1");
            // Set up the opaque-only command buffer to make a copy of the camera color buffer right before transparent drawing starts 
            _opaqueInputCommandBuffer.Clear();
            //MelonLogger.Msg("FSR on pre cull2");
            if (autoGenerateReactiveMask || autoGenerateTransparencyAndComposition)
            {
                var scaledRenderSize = GetScaledRenderSize();
                _colorOpaqueOnly = RenderTexture.GetTemporary(scaledRenderSize.x, scaledRenderSize.y, 0, GetDefaultFormat());
                //MelonLogger.Msg("FSR on pre cull3");
                _opaqueInputCommandBuffer.Blit(BuiltinRenderTextureType.CameraTarget, _colorOpaqueOnly);
                //MelonLogger.Msg("FSR on pre cull4");
            }

            if (autoGenerateReactiveMask)
            {
                //MelonLogger.Msg("FSR on pre cull5");
                SetupAutoReactiveDescription();
            }

            //MelonLogger.Msg("FSR on pre cull6");
            SetupDispatchDescription();

            //MelonLogger.Msg("FSR on pre cull7");
            ApplyJitter();

            //MelonLogger.Msg("FSR ran on pre cull");
        }

        private void SetupDispatchDescription()
        {
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                return;
            }

            // Set up the main FSR3 Upscaler dispatch parameters
            _dispatchDescription.Color = new ResourceView(BuiltinRenderTextureType.CameraTarget, RenderTextureSubElement.Color);
            _dispatchDescription.Depth = new ResourceView(GetDepthTexture(), RenderTextureSubElement.Depth);
            _dispatchDescription.MotionVectors = new ResourceView(BuiltinRenderTextureType.MotionVectors);
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

            var scaledRenderSize = GetScaledRenderSize();

            _dispatchDescription.Output = new ResourceView(Fsr3ShaderIDs.UavUpscaledOutput);
            _dispatchDescription.PreExposure = preExposure;
            _dispatchDescription.EnableSharpening = performSharpenPass;
            _dispatchDescription.Sharpness = sharpness;
            _dispatchDescription.MotionVectorScale.x = -scaledRenderSize.x;
            _dispatchDescription.MotionVectorScale.y = -scaledRenderSize.y;
            _dispatchDescription.RenderSize = scaledRenderSize;
            _dispatchDescription.UpscaleSize = _displaySize;
            _dispatchDescription.FrameTimeDelta = Time.unscaledDeltaTime;
            _dispatchDescription.CameraNear = _renderCamera.nearClipPlane;
            _dispatchDescription.CameraFar = _renderCamera.farClipPlane;
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
                // Swap the near and far clip plane distances as FSR3 expects this when using inverted depth
                (_dispatchDescription.CameraNear, _dispatchDescription.CameraFar) = (_dispatchDescription.CameraFar, _dispatchDescription.CameraNear);
            }
        }

        private void SetupAutoReactiveDescription()
        {
            // Set up the parameters to auto-generate a reactive mask
            _genReactiveDescription.ColorOpaqueOnly = new ResourceView(_colorOpaqueOnly);
            _genReactiveDescription.ColorPreUpscale = new ResourceView(BuiltinRenderTextureType.CameraTarget, RenderTextureSubElement.Color);
            _genReactiveDescription.OutReactive = new ResourceView(Fsr3ShaderIDs.UavAutoReactive);
            _genReactiveDescription.RenderSize = GetScaledRenderSize();
            _genReactiveDescription.Scale = generateReactiveParameters.scale;
            _genReactiveDescription.CutoffThreshold = generateReactiveParameters.cutoffThreshold;
            _genReactiveDescription.BinaryValue = generateReactiveParameters.binaryValue;
            _genReactiveDescription.Flags = generateReactiveParameters.flags;
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

        public void OnRenderImage(RenderTexture scr, RenderTexture dest)
        {
            if (!Initialized)
            {
                return;
            }
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                return;
            }
            if (_dispatchCommandBuffer is null)
            {
                MelonLogger.Error("dispatchCommandBuffer was null!");
                return;
            }

            //MelonLogger.Msg("on render image");

            // Restore the camera's viewport rect so we can output at full resolution
            _renderCamera.rect = _originalRect;
            _renderCamera.ResetProjectionMatrix();

            _dispatchCommandBuffer.Clear();

            if (autoGenerateReactiveMask)
            {
                // The auto-reactive mask pass is executed separately from the main FSR3 Upscaler passes
                var scaledRenderSize = GetScaledRenderSize();
                _dispatchCommandBuffer.GetTemporaryRT(Fsr3ShaderIDs.UavAutoReactive, scaledRenderSize.x, scaledRenderSize.y, 0, default, GraphicsFormat.R8_UNorm, 1, true);
                _context?.GenerateReactiveMask(_genReactiveDescription, _dispatchCommandBuffer);
                _dispatchDescription.Reactive = new ResourceView(Fsr3ShaderIDs.UavAutoReactive);
            }

            // The backbuffer is not set up to allow random-write access, so we need a temporary render texture for FSR3 to output to
            _dispatchCommandBuffer.GetTemporaryRT(Fsr3ShaderIDs.UavUpscaledOutput, _displaySize.x, _displaySize.y, 0, default, GetDefaultFormat(), default, 1, true);

            _context?.Dispatch(_dispatchDescription, _dispatchCommandBuffer);

            // Output the upscaled image
            if (_originalRenderTarget != null)
            {
                //MelonLogger.Msg("render to camera");
                // Output to the camera target texture, passing through depth as well
                _dispatchCommandBuffer.SetGlobalTexture("_DepthTex", GetDepthTexture(), RenderTextureSubElement.Depth);
                _dispatchCommandBuffer.Blit(Fsr3ShaderIDs.UavUpscaledOutput, _originalRenderTarget, _copyWithDepthMaterial);
            }
            else
            {
                //todo find out why we render here and not to the camera
                //MelonLogger.Msg("render global buffer");
                // Output directly to the backbuffer
                _dispatchCommandBuffer.Blit(Fsr3ShaderIDs.UavUpscaledOutput, dest);
            }

            _dispatchCommandBuffer.ReleaseTemporaryRT(Fsr3ShaderIDs.UavUpscaledOutput);
            _dispatchCommandBuffer.ReleaseTemporaryRT(Fsr3ShaderIDs.UavAutoReactive);

            Graphics.ExecuteCommandBuffer(_dispatchCommandBuffer);

            if (_colorOpaqueOnly != null)
            {
                RenderTexture.ReleaseTemporary(_colorOpaqueOnly);
                _colorOpaqueOnly = null!;
            }

            // Shut up the Unity warning about not writing to the destination texture 
            //RenderTexture.active = dest;
        }

        private RenderTextureFormat GetDefaultFormat()
        {
            if (_originalRenderTarget != null)
            {
                return _originalRenderTarget.format;
            }
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                return RenderTextureFormat.Default;
            }

            return _renderCamera.allowHDR ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
        }

        private BuiltinRenderTextureType GetDepthTexture()
        {
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                return BuiltinRenderTextureType.Depth;
            }
            RenderingPath renderingPath = _renderCamera.renderingPath;
            return renderingPath == RenderingPath.Forward || renderingPath == RenderingPath.VertexLit ? BuiltinRenderTextureType.Depth : BuiltinRenderTextureType.CameraTarget;
        }

        private Vector2Int GetDisplaySize()
        {
            if (_renderCamera is null)
            {
                MelonLogger.Error("rendercamera was null!");
                throw new InvalidOperationException("_rendercamera was null");
            }
            if (_originalRenderTarget != null)
            {
                return new Vector2Int(_originalRenderTarget.width, _originalRenderTarget.height);
            }

            return new Vector2Int(_renderCamera.pixelWidth, _renderCamera.pixelHeight);
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
