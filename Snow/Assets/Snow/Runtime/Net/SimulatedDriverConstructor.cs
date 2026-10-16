using Unity.Collections;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Utilities;

namespace Snow.Net
{
    /// <summary>
    /// NET-03, §11.2: delay, jitter and packet loss through the Unity Transport simulator pipeline stage.
    /// NGO 2.13 made UnityTransport.SetDebugSimulatorParameters a no-op (it points to the Multiplayer Tools package);
    /// this constructor uses the public driver-constructor hook instead and keeps NGO's default pipelines.
    /// Applied on one process, both directions: the added round trip is about 2 x DelayMs.
    /// </summary>
    public class SimulatedDriverConstructor : INetworkStreamDriverConstructor
    {
        readonly int _delayMs, _jitterMs, _lossPercent;

        public SimulatedDriverConstructor(int delayMs, int jitterMs, int lossPercent)
        {
            _delayMs = delayMs;
            _jitterMs = jitterMs;
            _lossPercent = lossPercent;
        }

        public void CreateDriver(UnityTransport transport, out NetworkDriver driver,
            out NetworkPipeline unreliableFragmentedPipeline,
            out NetworkPipeline unreliableSequencedFragmentedPipeline,
            out NetworkPipeline reliableSequencedPipeline)
        {
            var settings = transport.GetDefaultNetworkSettings();
            settings.WithSimulatorStageParameters(
                maxPacketCount: 1000,
                mode: ApplyMode.AllPackets,
                packetDelayMs: _delayMs,
                packetJitterMs: _jitterMs,
                packetDropPercentage: _lossPercent,
                randomSeed: (uint)System.Environment.TickCount);
            driver = NetworkDriver.Create(new UDPNetworkInterface(), settings);

            transport.GetDefaultPipelineConfigurations(ref driver,
                out var unreliableFragmented, out var unreliableSequencedFragmented, out var reliableSequenced);
            unreliableFragmentedPipeline = driver.CreatePipeline(WithSimulator(unreliableFragmented));
            unreliableSequencedFragmentedPipeline = driver.CreatePipeline(WithSimulator(unreliableSequencedFragmented));
            reliableSequencedPipeline = driver.CreatePipeline(WithSimulator(reliableSequenced));
        }

        static NativeArray<NetworkPipelineStageId> WithSimulator(NativeArray<NetworkPipelineStageId> stages)
        {
            var result = new NativeArray<NetworkPipelineStageId>(stages.Length + 1, Allocator.Temp);
            for (int i = 0; i < stages.Length; i++) result[i] = stages[i];
            result[stages.Length] = NetworkPipelineStageId.Get<SimulatorPipelineStage>();
            return result;
        }
    }
}
