using MelonLoader;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace HPVR.FSR3
{
    [RegisterTypeInIl2Cpp(true)]
    //public class FsrHDRP : CustomPass
    public class FsrPreRefraction : FullScreenCustomPass
    {
        public static event Action? OnExecute;

        public static CustomPassContext? Context { get; private set; }

        public override void Setup(ScriptableRenderContext renderContext, CommandBuffer cmd) { }

        public override void Execute(CustomPassContext ctx)
        {
            Context = ctx;
            OnExecute?.Invoke();
        }

        public override void Cleanup() { }
    }
}
