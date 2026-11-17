using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ChillRoad.Rendering
{
    /// <summary>
    /// Contours façon dessin animé : passe plein écran (shader ChillRoad/Outline) qui lit la profondeur et les normales
    /// de la scène, juste avant le post-traitement. Activée ou non par le réglage de qualité (<see cref="Enabled"/>).
    /// Les caméras de rendu hors jeu (aperçus, reflets) ne la reçoivent pas.
    /// </summary>
    public class OutlineFeature : ScriptableRendererFeature
    {
        public Material material;
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;

        /// <summary>Contours actifs (qualité graphique). Coupés, la passe et les textures de normales disparaissent.</summary>
        public static bool Enabled = true;

        OutlinePass pass;

        public override void Create()
        {
            pass = new OutlinePass { renderPassEvent = passEvent };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!Enabled || !material) return;
            var type = renderingData.cameraData.cameraType;
            if (type == CameraType.Preview || type == CameraType.Reflection) return;
            // Caméras qui rendent dans une texture sans profondeur exploitable (cubemap du ciel) : pas de contours
            if (renderingData.cameraData.camera.targetTexture && renderingData.cameraData.camera.targetTexture.dimension == TextureDimension.Cube) return;
            pass.material = material;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
            renderer.EnqueuePass(pass);
        }

        class OutlinePass : ScriptableRenderPass
        {
            public Material material;
            static readonly Vector4 ScaleBias = new Vector4(1f, 1f, 0f, 0f);

            class PassData
            {
                public TextureHandle source;
                public Material material;
            }

            public OutlinePass()
            {
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (resources.isActiveTargetBackBuffer) return;
                if (!resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid()) return;

                TextureHandle source = resources.activeColorTexture;
                var desc = renderGraph.GetTextureDesc(source);
                desc.name = "_ChillRoadOutlineColor";
                desc.clearBuffer = false;
                desc.depthBufferBits = 0;
                TextureHandle destination = renderGraph.CreateTexture(desc);

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Chill Road Outline", out var data))
                {
                    data.source = source;
                    data.material = material;
                    builder.UseTexture(source, AccessFlags.Read);
                    builder.UseTexture(resources.cameraDepthTexture, AccessFlags.Read);
                    builder.UseTexture(resources.cameraNormalsTexture, AccessFlags.Read);
                    builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        Blitter.BlitTexture(ctx.cmd, d.source, ScaleBias, d.material, 0);
                    });
                }
                resources.cameraColor = destination;
            }
        }
    }
}
