using System;
using Silk;

namespace SilkRegression
{
    public static class ProbeState
    {
        public static int InitializeCalls;
        public static int InitializeInstanceId;
        public static int AwakeCalls;
        public static int AwakeInstanceId;
        public static bool AwakeSawInitialized;
        public static int UpdateCalls;
        public static int FailedInitializeCalls;
        public static int FailedAwakeCalls;
    }

    [SilkMod("Silk lifecycle probe", new[] { "Silk tests" }, "1.0.0", "0.7.0", "SilkRegressionProbe", 3)]
    public sealed class LifecycleProbe : SilkMod
    {
        private bool initialized;

        public override void Initialize()
        {
            ProbeState.InitializeCalls++;
            ProbeState.InitializeInstanceId = GetInstanceID();
            initialized = true;
        }

        private void Awake()
        {
            ProbeState.AwakeCalls++;
            ProbeState.AwakeInstanceId = GetInstanceID();
            ProbeState.AwakeSawInitialized = initialized;
        }

        private void Update() => ProbeState.UpdateCalls++;
    }

    [SilkMod("Silk failure probe", new[] { "Silk tests" }, "1.0.0", "0.7.0", "SilkFailureProbe", 3)]
    public sealed class FailureProbe : SilkMod
    {
        public override void Initialize()
        {
            ProbeState.FailedInitializeCalls++;
            throw new InvalidOperationException("Expected Silk regression probe failure");
        }

        private void Awake() => ProbeState.FailedAwakeCalls++;
    }
}
