using System;

namespace TerraKit.Samples
{
    internal enum DemoCameraMode
    {
        Stationary,
        Orbiting,
        Explore,
        FreeMove
    }

    internal sealed class RuntimeWorldSettings
    {
        public ulong Seed;
        public int CellsPerRegion;
        public float SampleSpacing;
        public int StreamRadius;
        public int RegionsPerStreamingBatch;

        public DemoCameraMode CameraMode;
        public float CameraHeight;
        public float OrbitSpeedDegrees;
        public float OrbitRadiusRegions;
        public float ExploreSpeed;
        public float ExploreTurnSpeed;
        public float FreeMoveSpeed;
        public float FreeLookSensitivity;

        public double RegionWorldSize
        {
            get { return Math.Max(0.0001, CellsPerRegion * (double)SampleSpacing); }
        }
    }
}
