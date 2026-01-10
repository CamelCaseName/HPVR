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

using MelonLoader;
using UnityEngine;

namespace FidelityFX.FSR3
{
    public static class Fsr3ShaderIDs
    {
        private static int ShaderGetIDHook(string s)
        {
            int i = Shader.PropertyToID(s);
            MelonLogger.Msg($"Shader returned ID {i} for {s}");
            return i;
        }

        // Shader resource views, i.e. read-only bindings
        public static readonly int SrvInputColor = ShaderGetIDHook("r_input_color_jittered");
        public static readonly int SrvOpaqueOnly = ShaderGetIDHook("r_input_opaque_only");
        public static readonly int SrvInputMotionVectors = ShaderGetIDHook("r_input_motion_vectors");
        public static readonly int SrvInputDepth = ShaderGetIDHook("r_input_depth");
        public static readonly int SrvInputExposure = ShaderGetIDHook("r_input_exposure");
        public static readonly int SrvFrameInfo = ShaderGetIDHook("r_frame_info");
        public static readonly int SrvReactiveMask = ShaderGetIDHook("r_reactive_mask");
        public static readonly int SrvTransparencyAndCompositionMask = ShaderGetIDHook("r_transparency_and_composition_mask");
        public static readonly int SrvReconstructedPrevNearestDepth = ShaderGetIDHook("r_reconstructed_previous_nearest_depth");
        public static readonly int SrvDilatedMotionVectors = ShaderGetIDHook("r_dilated_motion_vectors");
        public static readonly int SrvDilatedDepth = ShaderGetIDHook("r_dilated_depth");
        public static readonly int SrvInternalUpscaled = ShaderGetIDHook("r_internal_upscaled_color");
        public static readonly int SrvAccumulation = ShaderGetIDHook("r_accumulation");
        public static readonly int SrvLumaHistory = ShaderGetIDHook("r_luma_history");
        public static readonly int SrvRcasInput = ShaderGetIDHook("r_rcas_input");
        public static readonly int SrvLanczosLut = ShaderGetIDHook("r_lanczos_lut");
        public static readonly int SrvSpdMips = ShaderGetIDHook("r_spd_mips");
        public static readonly int SrvDilatedReactiveMasks = ShaderGetIDHook("r_dilated_reactive_masks");
        public static readonly int SrvNewLocks = ShaderGetIDHook("r_new_locks");
        public static readonly int SrvFarthestDepth = ShaderGetIDHook("r_farthest_depth");
        public static readonly int SrvFarthestDepthMip1 = ShaderGetIDHook("r_farthest_depth_mip1");
        public static readonly int SrvShadingChange = ShaderGetIDHook("r_shading_change");
        public static readonly int SrvCurrentLuma = ShaderGetIDHook("r_current_luma");
        public static readonly int SrvPreviousLuma = ShaderGetIDHook("r_previous_luma");
        public static readonly int SrvLumaInstability = ShaderGetIDHook("r_luma_instability");
        public static readonly int SrvPrevColorPreAlpha = ShaderGetIDHook("r_input_prev_color_pre_alpha");
        public static readonly int SrvPrevColorPostAlpha = ShaderGetIDHook("r_input_prev_color_post_alpha");

        // Unordered access views, i.e. random read/write bindings
        public static readonly int UavReconstructedPrevNearestDepth = ShaderGetIDHook("rw_reconstructed_previous_nearest_depth");
        public static readonly int UavDilatedMotionVectors = ShaderGetIDHook("rw_dilated_motion_vectors");
        public static readonly int UavDilatedDepth = ShaderGetIDHook("rw_dilated_depth");
        public static readonly int UavInternalUpscaled = ShaderGetIDHook("rw_internal_upscaled_color");
        public static readonly int UavAccumulation = ShaderGetIDHook("rw_accumulation");
        public static readonly int UavLumaHistory = ShaderGetIDHook("rw_luma_history");
        public static readonly int UavUpscaledOutput = ShaderGetIDHook("rw_upscaled_output");
        public static readonly int UavDilatedReactiveMasks = ShaderGetIDHook("rw_dilated_reactive_masks");
        public static readonly int UavFrameInfo = ShaderGetIDHook("rw_frame_info");
        public static readonly int UavSpdAtomicCount = ShaderGetIDHook("rw_spd_global_atomic");
        public static readonly int UavNewLocks = ShaderGetIDHook("rw_new_locks");
        public static readonly int UavAutoReactive = ShaderGetIDHook("rw_output_autoreactive");
        public static readonly int UavShadingChange = ShaderGetIDHook("rw_shading_change");
        public static readonly int UavFarthestDepth = ShaderGetIDHook("rw_farthest_depth");
        public static readonly int UavFarthestDepthMip1 = ShaderGetIDHook("rw_farthest_depth_mip1");
        public static readonly int UavCurrentLuma = ShaderGetIDHook("rw_current_luma");
        public static readonly int UavLumaInstability = ShaderGetIDHook("rw_luma_instability");
        public static readonly int UavIntermediate = ShaderGetIDHook("rw_intermediate_fp16x1");
        public static readonly int UavSpdMip0 = ShaderGetIDHook("rw_spd_mip0");
        public static readonly int UavSpdMip1 = ShaderGetIDHook("rw_spd_mip1");
        public static readonly int UavSpdMip2 = ShaderGetIDHook("rw_spd_mip2");
        public static readonly int UavSpdMip3 = ShaderGetIDHook("rw_spd_mip3");
        public static readonly int UavSpdMip4 = ShaderGetIDHook("rw_spd_mip4");
        public static readonly int UavSpdMip5 = ShaderGetIDHook("rw_spd_mip5");
        public static readonly int UavAutoComposition = ShaderGetIDHook("rw_output_autocomposition");
        public static readonly int UavPrevColorPreAlpha = ShaderGetIDHook("rw_output_prev_color_pre_alpha");
        public static readonly int UavPrevColorPostAlpha = ShaderGetIDHook("rw_output_prev_color_post_alpha");

        // Constant buffer bindings
        public static readonly int CbFsr3Upscaler = ShaderGetIDHook("cbFSR3Upscaler");
        public static readonly int CbSpd = ShaderGetIDHook("cbSPD");
        public static readonly int CbRcas = ShaderGetIDHook("cbRCAS");
        public static readonly int CbGenReactive = ShaderGetIDHook("cbGenerateReactive");
    }
}
