namespace OpenPhysicsHKX
{
    /// <summary>Explicit caller-evaluated vertex data. IDs and indices are not inferred from particles.</summary>
    public sealed class ReferenceBufferData
    {
        public double[][] Positions { get; set; }
        public double[][] Normals { get; set; }
    }
}
