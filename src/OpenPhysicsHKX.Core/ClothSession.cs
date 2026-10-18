using System;
using System.Collections.Generic;
using System.Linq;
using OpenPhysicsHKX.Internal;

namespace OpenPhysicsHKX
{
    /// <summary>Owned persistent simulation state. Inputs and outputs are copied. Methods are serialized.</summary>
    public sealed class ClothSession
    {
        private readonly object gate = new object();
        private readonly ClothDefinition[] definitions;
        private readonly SolverOptions options;
        private List<int> order;
        private Dictionary<int, Cloth> cloths;
        private Dictionary<string, object> stepOptions;
        private bool faulted;
        private long frames;
        private readonly ApproximationClock approximationClock = new ApproximationClock();
        private bool hasApproximations;
        private Dictionary<int, double[][]> referenceBuffers = new Dictionary<int, double[][]>();
        private Dictionary<int, Dictionary<int, ReferenceBufferData>> referenceSets = new Dictionary<int, Dictionary<int, ReferenceBufferData>>();
        private Dictionary<int, double[][]> normalBuffers = new Dictionary<int, double[][]>();

        public long FrameCount { get { lock (gate) return frames; } }
        public bool IsFaulted { get { lock (gate) return faulted; } }
        public string[] ApproximateConstraintClasses { get { return definitions.SelectMany(d => d.Constraints).Select(c => c.ClassName).Where(ApproximateKernels.IsApproximate).Distinct().ToArray(); } }

        public ClothSession(IEnumerable<ClothDefinition> scene, SolverOptions options = null)
        {
            if (scene == null) throw new ArgumentNullException("scene");
            this.options = (options ?? new SolverOptions()).Copy();
            ValidateOptions(this.options);
            definitions = scene.Select(CopyDefinition).ToArray();
            hasApproximations = definitions.Any(d => d.Constraints.Any(c => ApproximateKernels.IsApproximate(c.ClassName)));
            if (hasApproximations && !this.options.AllowApproximateConstraints)
                throw new NotSupportedException("BendLink/Transition/VolumeMx require AllowApproximateConstraints=true; these are approximate kernels.");
            if (definitions.Length == 0) throw new ArgumentException("Scene must contain a cloth.");
            if (definitions.Select(d => d.Id).Distinct().Count() != definitions.Length)
                throw new ArgumentException("Cloth IDs must be unique.");
            Reset();
        }

        public void Reset()
        {
            lock (gate)
            {
                var next = new Dictionary<int, Cloth>();
                referenceBuffers = new Dictionary<int, double[][]>();
                referenceSets = new Dictionary<int, Dictionary<int, ReferenceBufferData>>();
                normalBuffers = new Dictionary<int, double[][]>();
                foreach (var d in definitions) next.Add(d.Id, Build(d));
                cloths = next;
                order = definitions.Select(d => d.Id).ToList();
                stepOptions = new Dictionary<string, object> {
                    {"slow_motion", options.SlowMotion}, {"world_stepping", options.WorldStepping},
                    {"gravity_scale", options.GravityScale}, {"pin_fixed", options.PinFixed},
                    {"no_collide", !options.EnableCollisions}, {"damping_form", "legacy"},
                    {"weight_mode", "native"}
                };
                frames = 0;
                approximationClock.Time = 0;
                faulted = false;
            }
        }

        /// <summary>Advance fixed-duration frames. On failure call Reset before continuing.</summary>
        public void Step(int frameCount = 1)
        {
            if (frameCount < 1 || frameCount > 10000) throw new ArgumentOutOfRangeException("frameCount");
            lock (gate)
            {
                if (faulted) throw new InvalidOperationException("Session faulted; call Reset.");
                try
                {
                    List<double> initial;
                    if (!hasApproximations)
                        Stepper.Step(order, cloths, frameCount, options.FrameDuration, stepOptions, out initial);
                    else
                        for (int i = 0; i < frameCount; i++)
                        {
                            approximationClock.Time += options.FrameDuration;
                            Stepper.Step(order, cloths, 1, options.FrameDuration, stepOptions, out initial);
                        }
                    foreach (var c in cloths.Values)
                        foreach (var p in c.P) CheckFinite(p, "solved position");
                    frames = checked(frames + frameCount);
                }
                catch { faulted = true; throw; }
            }
        }

        /// <summary>Fresh arrays of XYZ triples, keyed by cloth ID. No simulation buffers escape.</summary>
        public Dictionary<int, double[][]> GetPositions()
        {
            lock (gate)
            {
                if (faulted) throw new InvalidOperationException("Session faulted; call Reset.");
                return order.ToDictionary(id => id, id => CopyRows(cloths[id].P.ToArray()));
            }
        }

        /// <summary>
        /// Atomically validate/copy mapped frame inputs without rebuilding kernels or clearing history.
        /// Fixed targets take effect during the next Step; reference buffers do not imply a dependency graph.
        /// </summary>
        public void SetFrameInputs(IEnumerable<ClothFrameInput> inputs)
        {
            if (inputs == null) throw new ArgumentNullException("inputs");
            lock (gate)
            {
                if (faulted) throw new InvalidOperationException("Session faulted; call Reset.");
                var snapshots = new List<ClothFrameInput>();
                var seen = new HashSet<int>();
                foreach (var input in inputs)
                {
                    if (input == null || !cloths.ContainsKey(input.ClothId) || !seen.Add(input.ClothId))
                        throw new ArgumentException("Unknown, null or duplicate cloth frame input.");
                    var c = cloths[input.ClothId];
                    if (input.ReferencePositions != null)
                        ValidateRows(input.ReferencePositions, referenceBuffers[input.ClothId].Length, 3, "frame references");
                    Dictionary<int,ReferenceBufferData> buffers=null;
                    if(input.ReferenceBuffers!=null)
                    {
                        buffers=new Dictionary<int,ReferenceBufferData>();
                        foreach(var pair in input.ReferenceBuffers)
                        {
                            ReferenceBufferData current;
                            if(pair.Value==null||!referenceSets[input.ClothId].TryGetValue(pair.Key,out current)||(pair.Key==0&&input.ReferencePositions!=null))throw new ArgumentException("Unknown, null or conflicting frame reference buffer.");
                            ValidateRows(pair.Value.Positions,current.Positions.Length,3,"frame reference buffer");
                            if(pair.Value.Normals!=null){if(current.Normals==null)throw new ArgumentException("Reference normal channel was not declared.");ValidateRows(pair.Value.Normals,current.Normals.Length,3,"frame reference normals");}
                            buffers.Add(pair.Key,new ReferenceBufferData{Positions=CopyRows(pair.Value.Positions),Normals=CopyRows(pair.Value.Normals??current.Normals)});
                        }
                    }
                    if (input.ParticleNormals != null)
                        ValidateRows(input.ParticleNormals, c.N, 3, "frame normals");
                    if (input.Capsules != null)
                    {
                        ValidateRows(input.Capsules, c.Collidables.Count, 8, "frame capsules");
                        if (input.Capsules.Any(p => p[6] < 0 || p[7] < 0)) throw new ArgumentException("Negative frame capsule radius.");
                    }
                    Dictionary<int, double[]> pins = null;
                    if (input.FixedTargets != null)
                    {
                        pins = new Dictionary<int, double[]>();
                        foreach (var pin in input.FixedTargets)
                        {
                            if (pin.Key < 0 || pin.Key >= c.N || c.InvMass[pin.Key] != 0 || pin.Value == null || pin.Value.Length != 3)
                                throw new ArgumentException("Frame target must name an authored fixed particle and supply XYZ.");
                            CheckFinite(pin.Value, "fixed target");
                            pins.Add(pin.Key, (double[])pin.Value.Clone());
                        }
                    }
                    snapshots.Add(new ClothFrameInput { ClothId = input.ClothId,
                        ReferencePositions = CopyRows(input.ReferencePositions), ParticleNormals = CopyRows(input.ParticleNormals),
                        FixedTargets = pins, Capsules = CopyRows(input.Capsules), ReferenceBuffers=buffers });
                }
                if (snapshots.Count == 0) throw new ArgumentException("At least one frame input is required.");
                // Nothing above mutates a live buffer; a failure cannot partially apply other cloths.
                foreach (var input in snapshots)
                {
                    int id = input.ClothId;
                    if (input.ReferencePositions != null){referenceBuffers[id] = input.ReferencePositions;referenceSets[id][0].Positions=input.ReferencePositions;}
                    if(input.ReferenceBuffers!=null)foreach(var pair in input.ReferenceBuffers){referenceSets[id][pair.Key]=pair.Value;if(pair.Key==0)referenceBuffers[id]=pair.Value.Positions;}
                    if (input.ParticleNormals != null) normalBuffers[id] = input.ParticleNormals;
                    if (input.Capsules != null) cloths[id].Collidables = input.Capsules.ToList();
                    if (input.FixedTargets != null)
                        foreach (var pin in input.FixedTargets) cloths[id].PinRef[pin.Key] = pin.Value;
                }
            }
        }

        private Cloth Build(ClothDefinition d)
        {
            int n = d.RestPositions.Length;
            referenceBuffers[d.Id] = CopyRows(d.ReferencePositions ?? d.RestPositions);
            referenceSets[d.Id]=d.ReferenceBuffers==null?new Dictionary<int,ReferenceBufferData>():CopyReferenceBuffers(d.ReferenceBuffers);
            if(!referenceSets[d.Id].ContainsKey(0))referenceSets[d.Id].Add(0,new ReferenceBufferData{Positions=referenceBuffers[d.Id]});
            else referenceBuffers[d.Id]=referenceSets[d.Id][0].Positions;
            normalBuffers[d.Id] = CopyRows(d.ParticleNormals);
            var c = new Cloth {
                Sim = d.Id, Name = d.Name, N = n, P0 = CopyRows(d.RestPositions).ToList(),
                P = CopyRows(d.Positions ?? d.RestPositions).ToList(),
                Prev = CopyRows(d.PreviousPositions ?? d.RestPositions).ToList(),
                Mass = d.Masses.ToList(), InvMass = d.InverseMasses.ToList(),
                GravityX = d.Gravity[0], GravityY = d.Gravity[1], GravityZ = d.Gravity[2],
                Damping = d.Damping, Substeps = d.Substeps, Iterations = d.Iterations,
                Adapt = d.AdaptConstraintStiffness, ParticleRadius = d.ParticleRadius,
                CollisionTolerance = d.CollisionTolerance, CollisionToleranceMode = options.CollisionToleranceMode,
                Collidables = CopyRows(d.Capsules).ToList()
            };
            for (int i = 0; i < n; i++)
                if (c.InvMass[i] == 0.0) { c.Fixed.Add(i); c.PinRef.Add(i, (double[])c.P0[i].Clone()); }
            foreach (var def in d.Constraints)
            {
                var fields = def.Fields.ToList();
                var records = def.Records.Select(r => r.Select(v => (object)v).ToArray()).ToList();
                var opts = new Dictionary<string, object> {
                    {"link_form", options.LinkForm}, {"link_scale", options.LinkScale},
                    {"stretch_scale", options.StretchScale}, {"stretch_form", options.StretchForm},
                    {"stretch_sign", options.StretchSign}, {"compress_mode", options.CompressMode},
                    {"bend_normal", def.BendNormal}, {"bend_scale", def.BendScale},
                    {"bend_max_gain", def.BendMaxGain}, {"bend_scalar", "grad"}, {"bend_nat_scale", false},
                    {"shape_type", def.Shape}, {"apply_normal", def.ApplyNormal},
                    {"reference", new Func<int, int, double[]>((i, rv) => referenceSets[d.Id][def.ReferenceBufferId].Positions[rv])},
                    {"normal_bounds", "native"}
                };
                Func<int, double[]> normals = i => def.NormalReferenceVertices!=null&&def.NormalReferenceVertices[i]>=0?referenceSets[d.Id][def.ReferenceBufferId].Normals[def.NormalReferenceVertices[i]]:normalBuffers[d.Id] == null ? null : normalBuffers[d.Id][i];
                var solve = Solvers.BuildSolver(def.ClassName, fields, records, c, normals, opts);
                if (options.RespectFixedStretchAnchors && options.StretchForm == "a3c" && def.ClassName == "hclStretchLinkConstraintSet")
                {
                    var anchorOptions = new Dictionary<string, object>(opts);
                    anchorOptions["stretch_scale"] = options.StretchScale ?? options.LinkScale;
                    solve = FixedAnchorStretch.Build(def, c, anchorOptions);
                }
                if (solve == null && options.AllowApproximateConstraints)
                    solve = ApproximateKernels.Build(def, c, i => referenceSets[d.Id][def.ReferenceBufferId].Positions[i], approximationClock);
                if (solve == null) throw new NotSupportedException("Unsupported constraint: " + def.ClassName);
                c.Sets.Add(new SetRec { Class = def.ClassName, Fields = fields, Entries = records,
                    Type = TypeId(def.ClassName), N = records.Count, ProbeIndex = c.Sets.Count, Solve = solve });
            }
            c.Execution = (d.ExecutionOrder ?? Enumerable.Range(0, c.Sets.Count).ToArray()).ToList();
            return c;
        }

        private static int TypeId(string name)
        {
            switch (name) {
                case "hclStandardLinkConstraintSet": return 1;
                case "hclStretchLinkConstraintSet": return 2;
                case "hclBendStiffnessConstraintSet": return 4;
                case "hclLocalRangeConstraintSet": return 5;
                case "hclCompressibleLinkConstraintSet": return 20;
                case "hclBendLinkConstraintSet": return 3;
                case "hclTransitionConstraintSet": return 8;
                case "hclVolumeConstraintMx": return 7; // generic stiffness adaptation for the independent approximation
                default: throw new NotSupportedException("Unsupported constraint: " + name);
            }
        }

        private static string[] Required(string name)
        {
            TypeId(name);
            if (name == "hclVolumeConstraintMx")
                return new[] { "particleIndex", "frameX", "frameY", "frameZ", "weight", "stiffness", "role" };
            if (name == "hclBendLinkConstraintSet")
                return new[] { "particleA", "particleB", "bendMinLength", "stretchMaxLength", "bendStiffness", "stretchStiffness" };
            if (name == "hclTransitionConstraintSet")
                return new[] { "particleIndex", "referenceVertex", "toAnimDelay", "toSimDelay", "toSimMaxDistance" };
            if (name == "hclLocalRangeConstraintSet")
                return new[] { "particleIndex", "referenceVertex", "maximumDistance", "maxNormalDistance", "minNormalDistance" };
            if (name == "hclBendStiffnessConstraintSet")
                return new[] { "particleA", "particleB", "particleC", "particleD", "weightA", "weightB", "weightC", "weightD", "bendStiffness" };
            return new[] { "particleA", "particleB", "restLength", "stiffness" };
        }

        private static ClothDefinition CopyDefinition(ClothDefinition d)
        {
            if (d == null) throw new ArgumentException("Null cloth.");
            if (d.RestPositions == null || d.RestPositions.Length == 0) throw new ArgumentException("RestPositions required.");
            int n = d.RestPositions.Length;
            ValidateRows(d.RestPositions, n, 3, "RestPositions");
            if (d.Positions != null) ValidateRows(d.Positions, n, 3, "Positions");
            if (d.PreviousPositions != null) ValidateRows(d.PreviousPositions, n, 3, "PreviousPositions");
            if (d.ReferencePositions != null) ValidateRows(d.ReferencePositions, d.ReferencePositions.Length, 3, "ReferencePositions");
            var referenceData=d.ReferenceBuffers==null?new Dictionary<int,ReferenceBufferData>():CopyReferenceBuffers(d.ReferenceBuffers);
            if(referenceData.ContainsKey(0)&&d.ReferencePositions!=null)throw new ArgumentException("ReferencePositions and ReferenceBuffers[0] conflict.");
            if(!referenceData.ContainsKey(0))referenceData.Add(0,new ReferenceBufferData{Positions=CopyRows(d.ReferencePositions??d.RestPositions)});
            if (d.ParticleNormals != null) ValidateRows(d.ParticleNormals, n, 3, "ParticleNormals");
            if (d.Masses == null || d.InverseMasses == null || d.Masses.Length != n || d.InverseMasses.Length != n)
                throw new ArgumentException("Masses and InverseMasses must match particle count.");
            CheckFinite(d.Masses, "Masses"); CheckFinite(d.InverseMasses, "InverseMasses");
            if (d.Masses.Any(x => x < 0) || d.InverseMasses.Any(x => x < 0)) throw new ArgumentException("Negative mass.");
            if (d.Gravity == null || d.Gravity.Length != 3) throw new ArgumentException("Gravity must be XYZ.");
            CheckFinite(d.Gravity, "Gravity");
            CheckFinite(new[] { d.Damping, d.ParticleRadius, d.CollisionTolerance }, "cloth parameters");
            if (d.Damping < 0 || d.Damping > 1 || d.ParticleRadius < 0 || d.CollisionTolerance < 0 || d.Substeps < 1 || d.Iterations < 1)
                throw new ArgumentException("Invalid cloth parameters.");
            if (d.Capsules == null || d.Constraints == null) throw new ArgumentException("Null collection.");
            ValidateRows(d.Capsules, d.Capsules.Length, 8, "Capsules");
            if (d.Capsules.Any(c => c[6] < 0 || c[7] < 0)) throw new ArgumentException("Negative capsule radius.");
            var defs = new List<ConstraintDefinition>();
            foreach (var set in d.Constraints)
            {
                if (set == null) throw new ArgumentException("Null constraint.");
                var required = Required(set.ClassName);
                if (set.Fields == null || set.Records == null || set.Fields.Any(string.IsNullOrEmpty) ||
                    set.Fields.Distinct().Count() != set.Fields.Length || required.Any(f => !set.Fields.Contains(f)))
                    throw new ArgumentException("Missing or duplicate constraint fields: " + set.ClassName);
                ValidateRows(set.Records, set.Records.Length, set.Fields.Length, "constraint records");
                ReferenceBufferData reference;
                if(set.ReferenceBufferId<0||!referenceData.TryGetValue(set.ReferenceBufferId,out reference))throw new ArgumentException("Constraint references an undeclared buffer.");
                if(set.NormalReferenceVertices!=null&&(set.NormalReferenceVertices.Length!=n||reference.Normals==null||set.NormalReferenceVertices.Any(i=>i< -1||i>=reference.Positions.Length)))throw new ArgumentException("Invalid per-constraint reference normal mapping.");
                if(set.ClassName=="hclVolumeConstraintMx"&&set.Records.Length>0)
                {
                    // The approximation accesses a contiguous XYZ triple; reject reordered triples.
                    int x=Array.IndexOf(set.Fields,"frameX");
                    if(Array.IndexOf(set.Fields,"frameY")!=x+1||Array.IndexOf(set.Fields,"frameZ")!=x+2)throw new ArgumentException("Volume frame XYZ columns must be contiguous.");
                    int role=Array.IndexOf(set.Fields,"role"),weight=Array.IndexOf(set.Fields,"weight"),stiffness=Array.IndexOf(set.Fields,"stiffness");
                    if(set.Records.Any(r=>(r[role]!=0&&r[role]!=1)||r[weight]<0||r[stiffness]<0||r[stiffness]>1))throw new ArgumentException("Invalid Volume role/weight/stiffness.");
                    double sum=set.Records.Where(r=>r[role]==0).Sum(r=>r[weight]);
                    if(double.IsNaN(sum)||double.IsInfinity(sum)||sum<=0||!set.Records.Any(r=>r[role]==1))throw new ArgumentException("Volume requires positive frame weight and apply records.");
                    foreach(int r in new[]{0,1})if(set.Records.Where(row=>row[role]==r).Select(row=>row[Array.IndexOf(set.Fields,"particleIndex")]).Distinct().Count()!=set.Records.Count(row=>row[role]==r))throw new ArgumentException("Duplicate Volume particle within frame/apply records.");
                }
                for (int j = 0; j < set.Fields.Length; j++)
                    if (new[] { "particleA", "particleB", "particleC", "particleD", "particleIndex", "referenceVertex" }.Contains(set.Fields[j]))
                        foreach (var r in set.Records)
                            if (r[j] < 0 || r[j] >= (set.Fields[j] == "referenceVertex" ? reference.Positions.Length : n) || r[j] != Math.Truncate(r[j])) throw new ArgumentException("Invalid particle/reference index.");
                if (set.ClassName == "hclBendLinkConstraintSet")
                    foreach (var r in set.Records)
                    {
                        double lo = r[Array.IndexOf(set.Fields, "bendMinLength")], hi = r[Array.IndexOf(set.Fields, "stretchMaxLength")];
                        if (lo < 0 || hi < lo || r[Array.IndexOf(set.Fields, "bendStiffness")] < 0 || r[Array.IndexOf(set.Fields, "stretchStiffness")] < 0)
                            throw new ArgumentException("Invalid BendLink interval/stiffness.");
                    }
                CheckFinite(new[] { set.ToSimulationPeriod, set.ToAnimationPeriod }, "transition periods");
                if (set.ToSimulationPeriod < 0 || set.ToAnimationPeriod < 0) throw new ArgumentException("Negative transition period.");
                if (set.ClassName == "hclTransitionConstraintSet")
                    foreach (var r in set.Records)
                        foreach (string f in new[] { "toAnimDelay", "toSimDelay", "toSimMaxDistance" })
                            if (r[Array.IndexOf(set.Fields, f)] < 0) throw new ArgumentException("Negative transition delay/distance.");
                Choice(set.Shape, "SHAPE_SPHERE");
                Choice(set.BendNormal, "cross", "bisector");
                CheckFinite(new[] { set.BendScale, set.BendMaxGain }, "bend parameters");
                if (set.BendScale < 0 || set.BendMaxGain < 0) throw new ArgumentException("Negative bend parameters.");
                defs.Add(new ConstraintDefinition { ClassName = set.ClassName, Fields = (string[])set.Fields.Clone(),
                    Records = CopyRows(set.Records), Shape = set.Shape, ApplyNormal = set.ApplyNormal,
                    BendScale = set.BendScale, BendMaxGain = set.BendMaxGain, BendNormal = set.BendNormal,
                    ToSimulationPeriod = set.ToSimulationPeriod, ToAnimationPeriod = set.ToAnimationPeriod,
                    TransitionToAnimation = set.TransitionToAnimation,ReferenceBufferId=set.ReferenceBufferId,
                    NormalReferenceVertices=set.NormalReferenceVertices==null?null:(int[])set.NormalReferenceVertices.Clone() });
            }
            if (d.ExecutionOrder != null && (d.ExecutionOrder.Length == 0 && defs.Count != 0 && !d.AllowEmptyExecution || d.ExecutionOrder.Any(i => i < 0 || i >= defs.Count)))
                throw new ArgumentException("Invalid execution order (empty cannot disable existing constraints).");
            return new ClothDefinition { Id = d.Id, Name = d.Name, RestPositions = CopyRows(d.RestPositions),
                Positions = CopyRows(d.Positions), PreviousPositions = CopyRows(d.PreviousPositions), ReferencePositions = CopyRows(d.ReferencePositions),
                ReferenceBuffers=d.ReferenceBuffers==null?null:CopyReferenceBuffers(d.ReferenceBuffers),
                Masses = (double[])d.Masses.Clone(), InverseMasses = (double[])d.InverseMasses.Clone(),
                Gravity = (double[])d.Gravity.Clone(), Damping = d.Damping, Substeps = d.Substeps,
                Iterations = d.Iterations, AdaptConstraintStiffness = d.AdaptConstraintStiffness,
                Constraints = defs.ToArray(), ExecutionOrder = d.ExecutionOrder == null ? null : (int[])d.ExecutionOrder.Clone(), AllowEmptyExecution = d.AllowEmptyExecution,
                Capsules = CopyRows(d.Capsules), ParticleRadius = d.ParticleRadius,
                CollisionTolerance = d.CollisionTolerance, ParticleNormals = CopyRows(d.ParticleNormals) };
        }

        private static void ValidateOptions(SolverOptions o)
        {
            CheckFinite(new[] { o.FrameDuration, o.SlowMotion, o.WorldStepping, o.GravityScale, o.LinkScale }, "options");
            if (o.FrameDuration <= 0 || o.SlowMotion <= 0 || o.WorldStepping <= 0 || o.LinkScale < 0) throw new ArgumentException("Invalid solver options.");
            if (o.StretchScale.HasValue) { CheckFinite(new[] { o.StretchScale.Value }, "StretchScale"); if (o.StretchScale.Value < 0) throw new ArgumentException("Negative StretchScale."); }
            Choice(o.LinkForm, "raw", "normalized", "mass");
            Choice(o.StretchForm, "a3c", "symmetric"); Choice(o.StretchSign, "onesided", "twosided");
            Choice(o.CompressMode, "deadband", "restlength"); Choice(o.CollisionToleranceMode, "offset", "gate");
        }

        private static void Choice(string value, params string[] allowed)
        {
            if (!allowed.Contains(value)) throw new ArgumentException("Unsupported option: " + (value ?? "null"));
        }
        private static double[][] CopyRows(double[][] rows) { return rows == null ? null : rows.Select(r => (double[])r.Clone()).ToArray(); }
        private static Dictionary<int,ReferenceBufferData> CopyReferenceBuffers(Dictionary<int,ReferenceBufferData> buffers)
        {
            var result=new Dictionary<int,ReferenceBufferData>();
            foreach(var pair in buffers)
            {
                if(pair.Key<0||pair.Value==null||pair.Value.Positions==null||pair.Value.Positions.Length==0)throw new ArgumentException("Invalid reference buffer declaration.");
                ValidateRows(pair.Value.Positions,pair.Value.Positions.Length,3,"reference buffer");
                if(pair.Value.Normals!=null)ValidateRows(pair.Value.Normals,pair.Value.Positions.Length,3,"reference normals");
                result.Add(pair.Key,new ReferenceBufferData{Positions=CopyRows(pair.Value.Positions),Normals=CopyRows(pair.Value.Normals)});
            }
            return result;
        }
        private static void ValidateRows(double[][] rows, int count, int width, string label)
        {
            if (rows == null || rows.Length != count || rows.Any(r => r == null || r.Length != width)) throw new ArgumentException("Invalid shape: " + label);
            foreach (var row in rows) CheckFinite(row, label);
        }
        private static void CheckFinite(double[] values, string label)
        {
            if (values.Any(x => double.IsNaN(x) || double.IsInfinity(x))) throw new ArgumentException("Nonfinite " + label);
        }
    }
}
