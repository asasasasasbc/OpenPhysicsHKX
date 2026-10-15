using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenPhysicsHKX;
using OpenPhysicsHKX.Internal;

internal static class Program
{
    private static int passed;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        passed++; Console.WriteLine("PASS: " + label);
    }
    private static void Reject<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new Exception("Expected " + typeof(T).Name + ": " + label);
    }
    private static ClothDefinition Scene()
    {
        return new ClothDefinition {
            Id = 7, RestPositions = new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 1.0, 0.0, 0.0 } },
            Positions = new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 1.2, 0.1, 0.0 } },
            Masses = new[] { 0.0, 1.0 }, InverseMasses = new[] { 0.0, 1.0 },
            Substeps = 2, Iterations = 2, Damping = 0.2,
            Constraints = new[] { new ConstraintDefinition {
                ClassName = "hclStandardLinkConstraintSet", Fields = new[] { "particleA", "particleB", "restLength", "stiffness" },
                Records = new[] { new[] { 0.0, 1.0, 1.0, 0.3 } }
            } }
        };
    }
    private static bool Bits(double[][] a, double[][] b)
    {
        return a.Length == b.Length && a.Length > 0 && a.Zip(b, (x, y) => x.Length == y.Length &&
            x.Zip(y, (u, v) => BitConverter.DoubleToInt64Bits(u) == BitConverter.DoubleToInt64Bits(v)).All(z => z)).All(z => z);
    }
    private static Cloth Legacy(ClothDefinition d)
    {
        Func<double[][], List<double[]>> rows = a => a.Select(r => (double[])r.Clone()).ToList();
        var c = new Cloth { Sim = 7, N = 2, P0 = rows(d.RestPositions), P = rows(d.Positions), Prev = rows(d.RestPositions),
            Mass = d.Masses.ToList(), InvMass = d.InverseMasses.ToList(), GravityY = -9.81, Damping = 0.2, Substeps = 2, Iterations = 2 };
        c.Fixed.Add(0); c.PinRef.Add(0, new[] { 0.0, 0.0, 0.0 });
        var f = d.Constraints[0].Fields.ToList();
        var e = d.Constraints[0].Records.Select(r => r.Select(v => (object)v).ToArray()).ToList();
        c.Sets.Add(new SetRec { Type = 1, Solve = Solvers.BuildSolver("hclStandardLinkConstraintSet", f, e, c, i => null,
            new Dictionary<string, object> { { "link_form", "raw" } }) });
        c.Execution.Add(0);
        return c;
    }
    private static int Main()
    {
        try
        {
            var d = Scene(); var a = new ClothSession(new[] { d }); var b = new ClothSession(new[] { d });
            a.Step(119); for (int i = 0; i < 119; i++) b.Step();
            Check(Bits(a.GetPositions()[7], b.GetPositions()[7]), "119 batched frames == 119 single frames (bits)");
            var c = Legacy(d); List<double> initial;
            Stepper.Step(new List<int> { 7 }, new Dictionary<int, Cloth> { { 7, c } }, 119, 1.0 / 60.0,
                new Dictionary<string, object>(), out initial);
            Check(Bits(a.GetPositions()[7], c.P.ToArray()), "public API == imported stepper (bits)");
            Check(a.GetPositions()[7][0].All(v => v == 0), "fixed particle re-pinned");
            Check(a.FrameCount == 119, "persistent frame count");
            var final = a.GetPositions()[7]; a.Reset(); a.Step(119);
            Check(Bits(final, a.GetPositions()[7]), "reset reproduces initial state");
            var detached = a.GetPositions(); detached[7][1][0] = 100;
            Check(Bits(final, a.GetPositions()[7]), "output buffers detached");
            d.RestPositions[1][0] = 900; d.Constraints[0].Records[0][3] = 900;
            a.Reset(); a.Step(119); Check(Bits(final, a.GetPositions()[7]), "input buffers detached including constraints");
            var opts = new SolverOptions(); var owned = new ClothSession(new[] { Scene() }, opts); opts.GravityScale = 999;
            owned.Step(119); Check(Bits(final, owned.GetPositions()[7]), "options detached");
            var s1 = new ClothSession(new[] { Scene() }); var s2 = new ClothSession(new[] { Scene() });
            Task.WaitAll(Task.Run(() => s1.Step(119)), Task.Run(() => s2.Step(119)));
            Check(Bits(s1.GetPositions()[7], s2.GetPositions()[7]), "independent sessions in parallel");
            var multi = Scene(); multi.Id = 8;
            var both = new ClothSession(new[] { Scene(), multi }); both.Step(119);
            Check(Bits(both.GetPositions()[7], both.GetPositions()[8]), "multiple domains independent");
            Reject<ArgumentException>(() => new ClothSession(new[] { Scene(), Scene() }), "duplicate IDs rejected");
            Reject<ArgumentException>(() => new ClothSession(new ClothDefinition[0]), "empty scene rejected");
            Reject<ArgumentOutOfRangeException>(() => a.Step(0), "zero frame comparison rejected");
            var bad = Scene(); bad.Constraints[0].ClassName = "hclUnknownConstraintSet";
            Reject<NotSupportedException>(() => new ClothSession(new[] { bad }), "unsupported constraint rejected");
            bad = Scene(); bad.ExecutionOrder = new[] { 10 };
            Reject<ArgumentException>(() => new ClothSession(new[] { bad }), "out-of-range execution rejected");
            bad = Scene(); bad.Constraints[0].Records[0][0] = -1;
            Reject<ArgumentException>(() => new ClothSession(new[] { bad }), "negative particle index rejected");
            bad = Scene(); bad.RestPositions[0][0] = double.NaN;
            Reject<ArgumentException>(() => new ClothSession(new[] { bad }), "nonfinite input rejected");
            bad = Scene(); bad.Substeps = 0;
            Reject<ArgumentException>(() => new ClothSession(new[] { bad }), "zero substeps rejected");
            Reject<ArgumentException>(() => new ClothSession(new[] { Scene() }, new SolverOptions { FrameDuration = double.NaN }), "nonfinite options rejected");
            Reject<ArgumentException>(() => new ClothSession(new[] { Scene() }, new SolverOptions { LinkForm = "unknown" }), "unknown numerical mode rejected");
            var explosive = Scene(); explosive.Constraints = new ConstraintDefinition[0]; explosive.Gravity = new[] { 0.0, -double.MaxValue, 0.0 };
            var failing = new ClothSession(new[] { explosive }, new SolverOptions { FrameDuration = 100 });
            Reject<ArgumentException>(() => failing.Step(), "nonfinite solve rejected");
            Check(failing.IsFaulted, "nonfinite solve faults session");
            Reject<InvalidOperationException>(() => failing.GetPositions(), "faulted positions unavailable");
            Reject<InvalidOperationException>(() => failing.Step(), "faulted state cannot continue");
            failing.Reset(); Check(!failing.IsFaulted && failing.FrameCount == 0, "reset recovers from fault");
            var gravity = Scene(); gravity.Constraints = new ConstraintDefinition[0]; gravity.Positions = null;
            var fall = new ClothSession(new[] { gravity }); fall.Step();
            Check(fall.GetPositions()[7][1][1] < 0, "authored gravity moves dynamic particle");
            gravity.Gravity = new[] { 0.0, 0.0, 0.0 }; gravity.Positions = new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 0.1, 0.0, 0.0 } };
            gravity.PreviousPositions = gravity.Positions;
            gravity.Capsules = new[] { new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.5, 0.5 } };
            var cap = new ClothSession(new[] { gravity }); cap.Step();
            Check(cap.GetPositions()[7][1][0] >= 0.5, "capsule collision projection");
            var noCap = new ClothSession(new[] { gravity }, new SolverOptions { EnableCollisions = false }); noCap.Step();
            Check(noCap.GetPositions()[7][1][0] == 0.1, "collisions can be disabled");
            // Every supported kernel is constructed and stepped through the public boundary.
            KernelSmoke("hclStretchLinkConstraintSet", new[] { "particleA", "particleB", "restLength", "stiffness" }, new[] { 1.0, 0.0, 1.0, 0.3 });
            KernelSmoke("hclCompressibleLinkConstraintSet", new[] { "particleA", "particleB", "restLength", "compressionLength", "stiffness" }, new[] { 1.0, 0.0, 1.0, 0.9, 0.3 });
            KernelSmoke("hclLocalRangeConstraintSet", new[] { "particleIndex", "referenceVertex", "maximumDistance", "maxNormalDistance", "minNormalDistance" }, new[] { 1.0, 1.0, 0.1, 0.1, -0.1 });
            var bend = Scene(); bend.RestPositions = new[] { new[] { 0.0, 1.0, 0.0 }, new[] { 0.0, -1.0, 0.1 }, new[] { -1.0, 0.0, 0.0 }, new[] { 1.0, 0.0, 0.0 } };
            bend.Positions = null; bend.Masses = new[] { 1.0, 1.0, 1.0, 1.0 }; bend.InverseMasses = new[] { 1.0, 1.0, 1.0, 1.0 };
            bend.Constraints = new[] { new ConstraintDefinition { ClassName = "hclBendStiffnessConstraintSet",
                Fields = new[] { "particleA", "particleB", "particleC", "particleD", "weightA", "weightB", "weightC", "weightD", "bendStiffness" },
                Records = new[] { new[] { 0.0, 1.0, 2.0, 3.0, 0.5, 0.5, 0.5, 0.5, 0.1 } } } };
            var bs = new ClothSession(new[] { bend }); bs.Step(4); Check(bs.FrameCount == 4, "bend kernel smoke");
            // Decimal parser regression, not a claim about all possible decimal literals.
            Check(BitConverter.DoubleToInt64Bits(PyNum.Parse("-0.21947240829467773")) == unchecked((long)0xbfcc17ac00000000UL), "delivered decimal rounding regression");
            var coreRefs = typeof(ClothSession).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
            Check(!coreRefs.Contains("System.Web.Extensions") && !coreRefs.Contains("System.Windows.Forms"), "Core has no JSON serializer or GUI dependency");
            ApproximationChecks();
            FrameInputChecks();
            Console.WriteLine("ALL PASS: " + passed); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void KernelSmoke(string name, string[] fields, double[] record)
    {
        var d = Scene(); d.Constraints = new[] { new ConstraintDefinition { ClassName = name, Fields = fields, Records = new[] { record } } };
        var s = new ClothSession(new[] { d }); s.Step(4); Check(s.FrameCount == 4, name + " public API smoke");
    }
    private static void ApproximationChecks()
    {
        Func<double,ClothDefinition> bend = distance => new ClothDefinition { Id=0,
            RestPositions=new[]{new[]{0.0,0.0,0.0},new[]{1.0,0.0,0.0}},
            Positions=new[]{new[]{0.0,0.0,0.0},new[]{distance,0.0,0.0}},
            PreviousPositions=new[]{new[]{0.0,0.0,0.0},new[]{distance,0.0,0.0}},
            Masses=new[]{1.0,1.0},InverseMasses=new[]{1.0,1.0},Gravity=new[]{0.0,0.0,0.0},
            Constraints=new[]{new ConstraintDefinition {ClassName="hclBendLinkConstraintSet",
                Fields=new[]{"particleA","particleB","bendMinLength","stretchMaxLength","bendStiffness","stretchStiffness"},
                Records=new[]{new[]{0.0,1.0,0.8,1.2,1.0,1.0}} }} };
        var opt=new SolverOptions {AllowApproximateConstraints=true};
        Reject<NotSupportedException>(()=>new ClothSession(new[]{bend(2)}),"approximate kernels require explicit API opt-in");
        var a=new ClothSession(new[]{bend(2)},opt);a.Step();var p=a.GetPositions()[0];
        Check(Math.Abs(p[1][0]-p[0][0]-1.2)<1e-12,"BendLink limits extension to interval");
        Check(Math.Abs(p[1][0]+p[0][0]-2)<1e-12,"BendLink preserves equal-mass center");
        var b=new ClothSession(new[]{bend(0.2)},opt);b.Step();p=b.GetPositions()[0];
        Check(Math.Abs(p[1][0]-p[0][0]-0.8)<1e-12,"BendLink resists compression");
        var dead=new ClothSession(new[]{bend(1)},opt);dead.Step();Check(dead.GetPositions()[0][1][0]==1,"BendLink interval dead zone");
        var collapsed=new ClothSession(new[]{bend(0)},opt);collapsed.Step();p=collapsed.GetPositions()[0];
        Check(Math.Abs(p[1][0]-p[0][0]-0.8)<1e-12,"BendLink separates collapsed pair using rest direction");
        var pinned=bend(2);pinned.InverseMasses[0]=0;var fixedSession=new ClothSession(new[]{pinned},opt);fixedSession.Step();p=fixedSession.GetPositions()[0];
        Check(p[0][0]==0&&Math.Abs(p[1][0]-1.2)<1e-12,"BendLink leaves fixed endpoint untouched");
        var batch=new ClothSession(new[]{bend(2)},opt);var single=new ClothSession(new[]{bend(2)},opt);batch.Step(120);for(int i=0;i<120;i++)single.Step();
        Check(Bits(batch.GetPositions()[0],single.GetPositions()[0]),"approximate batch/single frame parity");
        var final=batch.GetPositions()[0];batch.Reset();batch.Step(120);Check(Bits(final,batch.GetPositions()[0]),"approximate Reset deterministic");
        Check(batch.ApproximateConstraintClasses.SequenceEqual(new[]{"hclBendLinkConstraintSet"}),"API reports approximate classes");
        Func<ClothDefinition> transition=()=>{var d=bend(2);d.Constraints=new[]{new ConstraintDefinition {ClassName="hclTransitionConstraintSet",
            Fields=new[]{"particleIndex","referenceVertex","toAnimDelay","toSimDelay","toSimMaxDistance"},Records=new[]{new[]{1.0,1.0,0.0,0.0,0.1}},ToSimulationPeriod=1}};return d;};
        var toSim=new ClothSession(new[]{transition()},new SolverOptions {AllowApproximateConstraints=true,FrameDuration=0.25});toSim.Step();
        Check(Math.Abs(toSim.GetPositions()[0][1][0]-(1+0.1/3))<1e-12,"Transition expands rest envelope by simulation time");
        var released=new ClothSession(new[]{transition()},new SolverOptions {AllowApproximateConstraints=true,FrameDuration=1});released.Step();
        Check(released.GetPositions()[0][1][0]==2,"Transition fully releases simulation after period");
        var td=transition();td.Constraints[0].TransitionToAnimation=true;td.Constraints[0].ToAnimationPeriod=0.25;
        var toAnim=new ClothSession(new[]{td},new SolverOptions {AllowApproximateConstraints=true,FrameDuration=0.25});toAnim.Step();
        Check(toAnim.GetPositions()[0][1][0]==1,"Transition to-animation converges to reference");
        var ext=transition();ext.ReferencePositions=new[]{new[]{0.0,0.0,0.0},new[]{1.0,0.0,0.0},new[]{3.0,0.0,0.0}};
        ext.Constraints[0].Records[0][1]=2;ext.Constraints[0].TransitionToAnimation=true;ext.Constraints[0].ToAnimationPeriod=0;
        var external=new ClothSession(new[]{ext},opt);external.Step();Check(external.GetPositions()[0][1][0]==3,"Transition accepts explicit external reference buffer");
        var invalid=bend(2);invalid.Constraints[0].Records[0][2]=1.5;
        Reject<ArgumentException>(()=>new ClothSession(new[]{invalid},opt),"invalid BendLink interval rejected");
    }
    private static void FrameInputChecks()
    {
        var d=Scene();d.Constraints=new ConstraintDefinition[0];d.Gravity=new[]{0.0,0.0,0.0};d.Damping=0;d.Substeps=1;d.Iterations=1;
        d.Positions=new[]{new[]{0.0,0.0,0.0},new[]{1.2,0.0,0.0}};
        var s=new ClothSession(new[]{d});s.Step();double x=s.GetPositions()[7][1][0];
        var targets=new Dictionary<int,double[]> {{0,new[]{0.0,0.15,0.0}}};
        s.SetFrameInputs(new[]{new ClothFrameInput {ClothId=7,FixedTargets=targets}});targets[0][1]=100;
        Check(s.FrameCount==1&&s.GetPositions()[7][0][1]==0,"frame inputs do not step/reset the session");
        s.Step();Check(s.GetPositions()[7][0][1]==0.15,"moving fixed target uses owned copied data");
        Check(Math.Abs(s.GetPositions()[7][1][0]-(x+0.2))<1e-12,"frame input preserves dynamic Verlet history");
        var peer=Scene();peer.Id=8;var both=new ClothSession(new[]{Scene(),peer});
        Reject<ArgumentException>(()=>both.SetFrameInputs(new[]{
            new ClothFrameInput {ClothId=7,FixedTargets=new Dictionary<int,double[]>{{0,new[]{0.0,0.5,0.0}}}},
            new ClothFrameInput {ClothId=8,FixedTargets=new Dictionary<int,double[]>{{1,new[]{0.0,0.5,0.0}}}}
        }),"invalid live target rejects complete batch");
        both.Step();Check(both.GetPositions()[7][0][1]==0,"failed frame-input batch leaves earlier cloth unchanged");
        Reject<ArgumentException>(()=>s.SetFrameInputs(new[]{new ClothFrameInput {ClothId=999}}),"unknown frame-input cloth rejected");
        Reject<ArgumentException>(()=>s.SetFrameInputs(new[]{new ClothFrameInput {ClothId=7,ReferencePositions=new[]{new[]{0.0,0.0,0.0}}}}),"reference buffer dimensions fixed at construction");
        Reject<ArgumentException>(()=>s.SetFrameInputs(new[]{new ClothFrameInput {ClothId=7,ParticleNormals=new[]{new[]{double.NaN,0.0,0.0},new[]{0.0,1.0,0.0}}}}),"nonfinite frame normal rejected");
        var transition=Scene();transition.Constraints=new[]{new ConstraintDefinition {ClassName="hclTransitionConstraintSet",TransitionToAnimation=true,ToAnimationPeriod=0,
            Fields=new[]{"particleIndex","referenceVertex","toAnimDelay","toSimDelay","toSimMaxDistance"},Records=new[]{new[]{1.0,1.0,0.0,0.0,0.1}}}};
        var moving=new ClothSession(new[]{transition},new SolverOptions {AllowApproximateConstraints=true});moving.Step();
        var references=new[]{new[]{0.0,0.0,0.0},new[]{1.0,0.25,0.0}};
        moving.SetFrameInputs(new[]{new ClothFrameInput {ClothId=7,ReferencePositions=references}});references[1][1]=100;moving.Step();
        Check(moving.GetPositions()[7][1][1]==0.25&&moving.FrameCount==2,"Transition reads updated reference buffer without reset");
        moving.Reset();moving.Step();Check(moving.GetPositions()[7][1][1]==0,"Reset restores original reference buffer");
        var local=Scene();local.Constraints=new[]{new ConstraintDefinition {ClassName="hclLocalRangeConstraintSet",ApplyNormal=false,
            Fields=new[]{"particleIndex","referenceVertex","maximumDistance","maxNormalDistance","minNormalDistance"},Records=new[]{new[]{1.0,1.0,0.0,0.0,0.0}}}};
        var range=new ClothSession(new[]{local});range.SetFrameInputs(new[]{new ClothFrameInput {ClothId=7,ReferencePositions=new[]{new[]{0.0,0.0,0.0},new[]{2.0,0.0,0.0}}}});range.Step();
        Check(Math.Abs(range.GetPositions()[7][1][0]-2)<1e-5,"legacy LocalRange reads live explicit reference buffer");
    }
}
