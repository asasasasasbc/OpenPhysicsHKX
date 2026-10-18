using System;
using System.Collections.Generic;

namespace OpenPhysicsHKX
{
    /// <summary>Explicit inputs in one caller-chosen coordinate system. No HKX or bone inference.</summary>
    public sealed class ClothDefinition
    {
        public ClothDefinition()
        {
            Name = "Cloth"; Gravity = new[] { 0.0, -9.81, 0.0 }; Substeps = 1; Iterations = 1;
            Constraints = new ConstraintDefinition[0]; Capsules = new double[0][];
        }
        public int Id { get; set; }
        public string Name { get; set; }
        public double[][] RestPositions { get; set; }
        public double[][] Positions { get; set; }
        public double[][] PreviousPositions { get; set; }
        /// <summary>Optional explicit rest/reference buffer; referenceVertex indexes this array.</summary>
        public double[][] ReferencePositions { get; set; }
        /// <summary>Optional independently indexed authored reference buffers. Buffer 0 aliases ReferencePositions.</summary>
        public Dictionary<int, ReferenceBufferData> ReferenceBuffers { get; set; }
        public double[] Masses { get; set; }
        public double[] InverseMasses { get; set; }
        public double[] Gravity { get; set; }
        public double Damping { get; set; }
        public int Substeps { get; set; }
        public int Iterations { get; set; }
        public bool AdaptConstraintStiffness { get; set; }
        public ConstraintDefinition[] Constraints { get; set; }
        /// <summary>Indices in Constraints, in execution order. Null means all; invalid indices are rejected.</summary>
        public int[] ExecutionOrder { get; set; }
        /// <summary>Explicitly permit an authored inactive constraint schedule. Default false prevents accidental omission.</summary>
        public bool AllowEmptyExecution { get; set; }
        /// <summary>Capsules as ax, ay, az, bx, by, bz, radiusA, radiusB. Already in solver space.</summary>
        public double[][] Capsules { get; set; }
        public double ParticleRadius { get; set; }
        public double CollisionTolerance { get; set; }
        /// <summary>Optional explicitly mapped normals, indexed by particle. No legacy vertex mapping.</summary>
        public double[][] ParticleNormals { get; set; }
    }

    /// <summary>Numeric records with explicit column names; copied and validated at session creation.</summary>
    public sealed class ConstraintDefinition
    {
        public ConstraintDefinition() { Shape = "SHAPE_SPHERE"; ApplyNormal = true; BendScale = 1; BendMaxGain = 1; BendNormal = "bisector"; ToSimulationPeriod = 1; ToAnimationPeriod = 1; }
        public string ClassName { get; set; }
        public string[] Fields { get; set; }
        public double[][] Records { get; set; }
        public string Shape { get; set; }
        public bool ApplyNormal { get; set; }
        public double BendScale { get; set; }
        public double BendMaxGain { get; set; }
        public string BendNormal { get; set; }
        public double ToSimulationPeriod { get; set; }
        public double ToAnimationPeriod { get; set; }
        public bool TransitionToAnimation { get; set; }
        public int ReferenceBufferId { get; set; }
        /// <summary>Optional particle-to-reference-normal indices for this set; -1 uses the legacy particle-normal channel.</summary>
        public int[] NormalReferenceVertices { get; set; }
    }

    public sealed class SolverOptions
    {
        public SolverOptions()
        {
            FrameDuration = 1.0 / 60.0; SlowMotion = 1; WorldStepping = 1; GravityScale = 1;
            PinFixed = true; EnableCollisions = true; LinkForm = "raw"; LinkScale = 1;
            StretchForm = "a3c"; StretchSign = "onesided"; CompressMode = "deadband"; CollisionToleranceMode = "offset";
        }
        public double FrameDuration { get; set; }
        public double SlowMotion { get; set; }
        public double WorldStepping { get; set; }
        public double GravityScale { get; set; }
        public bool PinFixed { get; set; }
        public bool EnableCollisions { get; set; }
        /// <summary>Opt in to independently designed BendLink/Transition/VolumeMx approximations, not HCT equivalence.</summary>
        public bool AllowApproximateConstraints { get; set; }
        /// <summary>raw, normalized, mass. Explicit: game presets belong to the compatibility CLI.</summary>
        public string LinkForm { get; set; }
        public double LinkScale { get; set; }
        public double? StretchScale { get; set; }
        public string StretchForm { get; set; }
        /// <summary>Correct A-only stretch records with one fixed endpoint by moving the dynamic endpoint. Default false retains legacy arithmetic.</summary>
        public bool RespectFixedStretchAnchors { get; set; }
        public string StretchSign { get; set; }
        public string CompressMode { get; set; }
        public string CollisionToleranceMode { get; set; }
        internal SolverOptions Copy() { return (SolverOptions)MemberwiseClone(); }
    }
}
