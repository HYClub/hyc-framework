using System;
using Unity.Entities;

namespace HYC.Framework.Dots
{
    /// <summary>
    /// Singleton owned by the simulation harness. Exposes the mode the
    /// application booted into (e.g. live vs headless-simulated).
    /// </summary>
    public struct SimulationState : IComponentData
    {
        public int Mode;              // 1 = live, 2 = simulated
        public ulong Beat;            // incremented once per fixed step in sim mode
        public double ElapsedSeconds;
    }

    /// <summary>
    /// Optional tag for systems that should only run during a simulated
    /// (offline) session — deterministic logic without a network.
    /// </summary>
    public struct SimOnly : IComponentData
    {
    }

    /// <summary>
    /// Boot mode mirror so plain C# (non-ECS) code can read it cheaply.
    /// The runtime host sets the mode once at startup.
    /// </summary>
    public static class SimulationBootstrap
    {
        public const int Live = 1;
        public const int Sim = 2;

        public static int Mode { get; set; } = 0;

        public static bool IsSimulated => Mode == Sim;
    }
}