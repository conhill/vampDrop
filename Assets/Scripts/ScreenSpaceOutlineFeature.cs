using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

namespace Vampire.DropPuzzle
{
    /// <summary>
    /// URP 17 (RenderGraph) Renderer Feature that draws a screen-space edge-detection
    /// outline over the scene — a clean, uniform border around walls/geometry, unlike a
    /// per-mesh hull outline which fights off-centre pivots. Add it to the active URP
    /// Renderer (PC_Renderer) and assign the ScreenSpaceOutline material.
    /// </summary>
    public class ScreenSpaceOutlineFeature : ScriptableRendererFeature
    {
        [Tooltip("Material using the Vampire/ScreenSpaceOutline shader.")]
        public Material OutlineMaterial;

        [Tooltip("When in the frame the outline is composited.")]
        public RenderPassEvent InjectionPoint = RenderPassEvent.BeforeRenderingPostProcessing;

        private OutlinePass _pass;

        public override void Create()
        {
            _pass = new OutlinePass(OutlineMaterial) { renderPassEvent = InjectionPoint };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (OutlineMaterial == null) return;
            // Ask URP to generate the depth + normals textures our shader samples.
            _pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            renderer.EnqueuePass(_pass);
        }

        // ── The pass ──────────────────────────────────────────────────────────
        private class OutlinePass : ScriptableRenderPass
        {
            private readonly Material _material;

            public OutlinePass(Material material)
            {
                _material = material;
                // Force an intermediate colour texture so we never blit the back buffer onto itself.
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (_material == null) return;

                var resourceData = frameData.Get<UniversalResourceData>();
                if (resourceData.isActiveTargetBackBuffer) return;

                TextureHandle source = resourceData.activeColorTexture;

                var desc = renderGraph.GetTextureDesc(source);
                desc.name            = "ScreenSpaceOutlineTarget";
                desc.clearBuffer     = false;
                desc.depthBufferBits = 0;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                // Blit source → destination through the outline material (edge detection reads
                // the camera depth/normals textures internally), then make it the active colour.
                var blitParams = new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0);
                renderGraph.AddBlitPass(blitParams, "ScreenSpaceOutline");

                resourceData.cameraColor = destination;
            }
        }
    }
}
