using System.Collections.Generic;

namespace OpenPhysicsHKX
{
    /// <summary>Explicit mapped inputs for the next step. No bone/vertex mapping is inferred.</summary>
    public sealed class ClothFrameInput
    {
        public int ClothId { get; set; }
        /// <summary>Null leaves the current reference buffer unchanged; shape must match construction.</summary>
        public double[][] ReferencePositions { get; set; }
        /// <summary>Atomic replacement of declared buffers; positions required, null normals retain current normals.</summary>
        public Dictionary<int, ReferenceBufferData> ReferenceBuffers { get; set; }
        public double[][] ParticleNormals { get; set; }
        /// <summary>Only particles whose authored inverse mass is zero may be targeted.</summary>
        public Dictionary<int, double[]> FixedTargets { get; set; }
        /// <summary>Optional replacement capsules, preserving the original collider count and order.</summary>
        public double[][] Capsules { get; set; }
    }
}
