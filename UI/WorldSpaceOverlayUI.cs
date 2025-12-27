using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace HPVR.UI
{
    [RegisterTypeInIl2Cpp(true)]
    public class WorldSpaceOverlayUI : MonoBehaviour
    {
        private const string shaderTestMode = "unity_GUIZTestMode"; //The magic property we need to set
        readonly UnityEngine.Rendering.CompareFunction desiredUIComparison = UnityEngine.Rendering.CompareFunction.Always; //If you want to try out other effects
        Graphic[] uiGraphicsToApplyTo = Array.Empty<Graphic>();
        TextMeshProUGUI[] uiTextsToApplyTo = Array.Empty<TextMeshProUGUI>();
        //Allows us to reuse materials
        private readonly Dictionary<Material, Material> materialMappings = new();
        public virtual void Start()
        {
            uiGraphicsToApplyTo = gameObject.GetComponentsInChildren<Graphic>();
            uiTextsToApplyTo = gameObject.GetComponentsInChildren<TextMeshProUGUI>();
            foreach (var graphic in uiGraphicsToApplyTo)
            {
                Material material = graphic.materialForRendering;
                if (material == null)
                {
                    MelonLogger.Error($"{nameof(WorldSpaceOverlayUI)}: skipping target without material {graphic.name}.{graphic.GetType().Name}");
                    continue;
                }
                if (!materialMappings.TryGetValue(material, out Material? materialCopy))
                {
                    materialCopy = new Material(material);
                    materialMappings.Add(material, materialCopy);
                }
                materialCopy.SetInt(shaderTestMode, (int)desiredUIComparison);
                graphic.material = materialCopy;
            }
            foreach (var text in uiTextsToApplyTo)
            {
                Material material = text.fontMaterial;
                if (material == null)
                {
                    MelonLogger.Error($"{nameof(WorldSpaceOverlayUI)}: skipping target without material {text.name}.{text.GetType().Name}");
                    continue;
                }
                if (!materialMappings.TryGetValue(material, out Material? materialCopy))
                {
                    materialCopy = new Material(material);
                    materialMappings.Add(material, materialCopy);
                }
                materialCopy.SetInt(shaderTestMode, (int)desiredUIComparison);
                text.fontMaterial = materialCopy;
            }
        }
    }
}