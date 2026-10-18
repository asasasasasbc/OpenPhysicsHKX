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
            VolumeTests();
            MultiReferenceTests();
            FixedStretchTests();
            Console.WriteLine("ALL PASS: " + passed); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void FixedStretchTests()
    {
        Func<bool, double, ClothDefinition> scene = (reverse, stiffness) => new ClothDefinition {
            Id = 7, RestPositions = new[] { new[] { 0.0, 0.0, 0.0 }, new[] { 2.0, 0.0, 0.0 } },
            Masses = new[] { 0.0, 0.25 }, InverseMasses = new[] { 0.0, 4.0 },
            Gravity = new[] { 0.0, 0.0, 0.0 }, Substeps = 1, Iterations = 1,
            Constraints = new[] { new ConstraintDefinition {
                ClassName = "hclStretchLinkConstraintSet", Fields = new[] { "particleA", "particleB", "restLength", "stiffness" },
                Records = new[] { new[] { reverse ? 1.0 : 0.0, reverse ? 0.0 : 1.0, 1.0, stiffness } }
            } }
        };
        var opts = new SolverOptions { RespectFixedStretchAnchors = true };
        foreach (bool reverse in new[] { false, true })
        {
            var s = new ClothSession(new[] { scene(reverse, 1) }, opts); s.Step();
            Check(s.GetPositions()[7][0][0] == 0 && s.GetPositions()[7][1][0] == 1,
                "fixed stretch moves only dynamic endpoint, anchor " + (reverse ? "B" : "A"));
        }
        var legacy = new ClothSession(new[] { scene(false, 1) }); legacy.Step();
        Check(legacy.GetPositions()[7][1][0] == 2, "legacy stretch default remains unchanged for compatibility");
        var soft = new ClothSession(new[] { scene(false, 0.25) }, opts); soft.Step();
        Check(soft.GetPositions()[7][1][0] == 1.75, "fixed stretch retains authored stiffness without inverse-mass multiplication");
        var scaled = new ClothSession(new[] { scene(false, 0.25) }, new SolverOptions { RespectFixedStretchAnchors = true, StretchScale = 0.5 }); scaled.Step();
        Check(scaled.GetPositions()[7][1][0] == 1.875, "fixed stretch honors explicit stretch scale");
        var slackDef = scene(false, 1); slackDef.RestPositions[1][0] = 0.5;
        var slack = new ClothSession(new[] { slackDef }, opts); slack.Step();
        Check(slack.GetPositions()[7][1][0] == 0.5, "one-sided anchor stretch leaves slack unchanged");
        var twoSided = new ClothSession(new[] { slackDef }, new SolverOptions { RespectFixedStretchAnchors = true, StretchSign = "twosided" }); twoSided.Step();
        Check(twoSided.GetPositions()[7][1][0] == 1, "explicit two-sided anchor stretch retains compression behavior");
        var fixedDef = scene(false, 1); fixedDef.Masses[1] = fixedDef.InverseMasses[1] = 0;
        var fixedPair = new ClothSession(new[] { fixedDef }, opts); fixedPair.Step();
        Check(fixedPair.GetPositions()[7][1][0] == 2, "two fixed stretch endpoints are never moved");
        var dynamicDef = scene(false, 0.1); dynamicDef.Masses[0] = 0.5; dynamicDef.InverseMasses[0] = 2;
        var dynamicOld = new ClothSession(new[] { dynamicDef }); var dynamicNew = new ClothSession(new[] { dynamicDef }, opts);
        dynamicOld.Step(20); dynamicNew.Step(20);
        Check(Bits(dynamicOld.GetPositions()[7], dynamicNew.GetPositions()[7]), "dynamic-dynamic stretch retains legacy arithmetic bit-for-bit");
        var batch = new ClothSession(new[] { scene(false, 0.5) }, opts); var single = new ClothSession(new[] { scene(false, 0.5) }, opts);
        batch.Step(60); for (int i = 0; i < 60; i++) single.Step();
        Check(Bits(batch.GetPositions()[7], single.GetPositions()[7]), "corrected stretch batch/single determinism");
        var end = batch.GetPositions()[7]; batch.Reset(); batch.Step(60);
        Check(Bits(end, batch.GetPositions()[7]), "corrected stretch reset determinism");
        var moved = new ClothSession(new[] { scene(false, 1) }, opts);
        moved.SetFrameInputs(new[] { new ClothFrameInput { ClothId = 7, FixedTargets = new Dictionary<int,double[]> { { 0, new[] { 0.0, 1.0, 0.0 } } } } }); moved.Step();
        var points = moved.GetPositions()[7]; double distance = Math.Sqrt(points[1].Zip(points[0], (a,b) => (a-b)*(a-b)).Sum());
        Check(Math.Abs(distance-1) < 1e-12 && points[0][1] == 1, "corrected stretch uses current animated fixed target");
        var internalCloth = new Cloth { N = 2, P = new List<double[]> { new[] { 0.0,0.0,0.0 }, new[] { 2.0,0.0,0.0 } }, InvMass = new List<double> { 0,4 } };
        FixedAnchorStretch.Build(scene(false,1).Constraints[0], internalCloth,
            new Dictionary<string,object> { { "stretch_scale",1.0 }, { "stretch_sign","onesided" } })(1);
        Check(internalCloth.P[0][0] == 0 && internalCloth.P[1][0] == 1,
            "anchor remains fixed inside solver, not merely re-pinned afterward");
    }

    private static void MultiReferenceTests()
    {
        Func<ClothDefinition> make=()=>new ClothDefinition{
            Id=11,RestPositions=new[]{new[]{0.0,0.0,0.0},new[]{0.0,0.0,0.0}},Masses=new[]{1.0,1.0},InverseMasses=new[]{1.0,1.0},Gravity=new[]{0.0,0.0,0.0},Damping=0,Substeps=1,Iterations=1,
            ReferenceBuffers=new Dictionary<int,ReferenceBufferData>{{3,new ReferenceBufferData{Positions=new[]{new[]{1.0,0.0,0.0}}}},{7,new ReferenceBufferData{Positions=new[]{new[]{10.0,0.0,0.0}}}}},
            Constraints=new[]{3,7}.Select((id,i)=>new ConstraintDefinition{ClassName="hclLocalRangeConstraintSet",ReferenceBufferId=id,ApplyNormal=false,Fields=new[]{"particleIndex","referenceVertex","maximumDistance","maxNormalDistance","minNormalDistance"},Records=new[]{new[]{(double)i,0.0,0.0,0.0,0.0}}}).ToArray()};
        var d=make();var s=new ClothSession(new[]{d});s.Step();var p=s.GetPositions()[11];
        Check(Math.Abs(p[0][0]-1)<1e-6&&Math.Abs(p[1][0]-10)<1e-6,"two identical vertex indices read different declared reference buffers");
        d.ReferenceBuffers[7].Positions[0][0]=999;s.Reset();s.Step();Check(Math.Abs(s.GetPositions()[11][1][0]-10)<1e-6,"reference-buffer declarations are owned copies");
        var update=new ReferenceBufferData{Positions=new[]{new[]{20.0,0.0,0.0}}};s.SetFrameInputs(new[]{new ClothFrameInput{ClothId=11,ReferenceBuffers=new Dictionary<int,ReferenceBufferData>{{7,update}}}});update.Positions[0][0]=999;s.Step();
        Check(Math.Abs(s.GetPositions()[11][1][0]-20)<1e-6&&Math.Abs(s.GetPositions()[11][0][0]-1)<1e-6,"live buffer update is copied and does not alter other buffer IDs");
        Reject<ArgumentException>(()=>s.SetFrameInputs(new[]{new ClothFrameInput{ClothId=11,ReferenceBuffers=new Dictionary<int,ReferenceBufferData>{{3,new ReferenceBufferData{Positions=new[]{new[]{30.0,0.0,0.0}}}},{7,new ReferenceBufferData{Positions=new[]{new[]{4.0,0.0,0.0},new[]{5.0,0.0,0.0}}}}}}}),"invalid second buffer rejects an entire multi-buffer input batch");
        s.Step();Check(Math.Abs(s.GetPositions()[11][0][0]-1)<1e-6&&Math.Abs(s.GetPositions()[11][1][0]-20)<1e-6,"failed multi-buffer update leaves every reference buffer unchanged");
        Reject<ArgumentException>(()=>s.SetFrameInputs(new[]{new ClothFrameInput{ClothId=11,ReferenceBuffers=new Dictionary<int,ReferenceBufferData>{{99,new ReferenceBufferData{Positions=new[]{new[]{0.0,0.0,0.0}}}}}}}),"unknown live reference buffer ID rejected");
        s.Reset();s.Step();Check(Math.Abs(s.GetPositions()[11][1][0]-10)<1e-6,"Reset restores each declared reference buffer independently");
        var bad=make();bad.Constraints[0].ReferenceBufferId=99;Reject<ArgumentException>(()=>new ClothSession(new[]{bad}),"undeclared constraint reference buffer rejected");
        bad=make();bad.Constraints[0].Records[0][1]=1;Reject<ArgumentException>(()=>new ClothSession(new[]{bad}),"reference bounds use selected buffer size, not particle count");
        var normals=make();normals.Positions=new[]{new[]{3.0,4.0,5.0},new[]{3.0,4.0,5.0}};normals.PreviousPositions=normals.Positions.Select(a=>(double[])a.Clone()).ToArray();
        normals.ReferenceBuffers[3]=new ReferenceBufferData{Positions=new[]{new[]{0.0,0.0,0.0}},Normals=new[]{new[]{0.0,1.0,0.0}}};normals.ReferenceBuffers[7]=new ReferenceBufferData{Positions=new[]{new[]{0.0,0.0,0.0}},Normals=new[]{new[]{1.0,0.0,0.0}}};
        for(int i=0;i<2;i++){normals.Constraints[i].ApplyNormal=true;normals.Constraints[i].Records[0][2]=100;normals.Constraints[i].NormalReferenceVertices=new[]{i==0?0:-1,i==1?0:-1};}
        var ns=new ClothSession(new[]{normals});ns.Step();p=ns.GetPositions()[11];Check(Math.Abs(p[0][1])<1e-6&&Math.Abs(p[1][0])<1e-6&&p[0][0]>2&&p[1][1]>3,"LocalRange normal channels are selected independently per constraint buffer");
        Reject<ArgumentException>(()=>ns.SetFrameInputs(new[]{new ClothFrameInput{ClothId=11,ReferenceBuffers=new Dictionary<int,ReferenceBufferData>{{3,new ReferenceBufferData{Positions=new[]{new[]{0.0,0.0,0.0}},Normals=new[]{new[]{double.NaN,0.0,0.0}}}}}}}),"nonfinite live reference normals rejected atomically");
        var liveNormals=new ReferenceBufferData{Positions=new[]{new[]{0.0,0.0,0.0}},Normals=new[]{new[]{0.0,0.0,1.0}}};
        ns.SetFrameInputs(new[]{new ClothFrameInput{ClothId=11,ReferenceBuffers=new Dictionary<int,ReferenceBufferData>{{3,liveNormals}}}});ns.Step();
        Check(Math.Abs(ns.GetPositions()[11][0][2])<1e-6,"LocalRange reads live per-buffer normals, not a cached particle normal");
        bad=make();bad.ReferencePositions=new[]{new[]{0.0,0.0,0.0}};bad.ReferenceBuffers[0]=new ReferenceBufferData{Positions=new[]{new[]{1.0,0.0,0.0}}};Reject<ArgumentException>(()=>new ClothSession(new[]{bad}),"ambiguous legacy/default-buffer declarations rejected");
    }
    private static void VolumeTests()
    {
        Func<ClothDefinition> make=()=>{
            var points=new[]{new[]{1.0,1.0,1.0},new[]{-1.0,-1.0,1.0},new[]{-1.0,1.0,-1.0},new[]{1.0,-1.0,-1.0}};
            var rows=Enumerable.Range(0,4).SelectMany(i=>new[]{new[]{(double)i,points[i][0],points[i][1],points[i][2],.25,0.0,0.0},new[]{(double)i,points[i][0],points[i][1],points[i][2],0.0,1.0,1.0}}).ToArray();
            return new ClothDefinition{Id=1,RestPositions=points,Positions=points.Select(p=>(double[])p.Clone()).ToArray(),PreviousPositions=points.Select(p=>(double[])p.Clone()).ToArray(),Masses=new[]{1.0,1.0,1.0,1.0},InverseMasses=new[]{1.0,1.0,1.0,1.0},Gravity=new[]{0.0,0.0,0.0},Damping=0,Substeps=1,Iterations=1,Constraints=new[]{new ConstraintDefinition{ClassName="hclVolumeConstraintMx",Fields=new[]{"particleIndex","frameX","frameY","frameZ","weight","stiffness","role"},Records=rows}}};
        };
        Func<ClothDefinition,ClothSession> solve=d=>new ClothSession(new[]{d},new SolverOptions{AllowApproximateConstraints=true,EnableCollisions=false});
        Reject<NotSupportedException>(()=>new ClothSession(new[]{make()}),"VolumeMx requires explicit approximation opt-in");
        var rest=make();var s=solve(rest);s.Step();
        Check(s.GetPositions()[1].Zip(rest.RestPositions,(a,b)=>a.Zip(b,(u,v)=>Math.Abs(u-v)<1e-10).All(z=>z)).All(z=>z),"VolumeMx preserves its authored shape");
        foreach(double angle in new[]{Math.PI/2,Math.PI})
        {
            var d=make();d.Positions=d.RestPositions.Select(p=>new[]{3+Math.Cos(angle)*p[0]-Math.Sin(angle)*p[1],4+Math.Sin(angle)*p[0]+Math.Cos(angle)*p[1],5+p[2]}).ToArray();d.PreviousPositions=d.Positions.Select(p=>(double[])p.Clone()).ToArray();var target=d.Positions;var session=solve(d);session.Step();
            Check(session.GetPositions()[1].Zip(target,(a,b)=>a.Zip(b,(u,v)=>Math.Abs(u-v)<1e-9).All(z=>z)).All(z=>z),"VolumeMx is translation/rotation equivariant at "+angle);
        }
        var stretched=make();stretched.Positions=stretched.RestPositions.Select(p=>p.Select(x=>x*2).ToArray()).ToArray();stretched.PreviousPositions=stretched.Positions.Select(p=>(double[])p.Clone()).ToArray();var shrunk=solve(stretched);shrunk.Step();
        Check(shrunk.GetPositions()[1].Zip(stretched.RestPositions,(a,b)=>a.Zip(b,(u,v)=>Math.Abs(u-v)<1e-9).All(z=>z)).All(z=>z),"VolumeMx actively restores uniformly distorted shape");
        var fixedScene=make();fixedScene.InverseMasses[0]=0;fixedScene.Masses[0]=0;fixedScene.Positions[1][0]+=1;fixedScene.PreviousPositions=fixedScene.Positions.Select(p=>(double[])p.Clone()).ToArray();var fixedSession=solve(fixedScene);fixedSession.Step(10);
        Check(Bits(new[]{fixedSession.GetPositions()[1][0]},new[]{fixedScene.RestPositions[0]}),"VolumeMx leaves authored fixed particles pinned");
        var first=solve(make());var second=solve(make());first.Step(12);for(int i=0;i<12;i++)second.Step();Check(Bits(first.GetPositions()[1],second.GetPositions()[1]),"VolumeMx batch/single step parity");
        var end=first.GetPositions()[1];first.Reset();first.Step(12);Check(Bits(end,first.GetPositions()[1]),"VolumeMx Reset is deterministic");
        Check(first.ApproximateConstraintClasses.Contains("hclVolumeConstraintMx"),"VolumeMx is reported as an approximation, not omitted");
        var bad=make();bad.Constraints[0].Records[0][4]=0;bad.Constraints[0].Records[2][4]=0;bad.Constraints[0].Records[4][4]=0;bad.Constraints[0].Records[6][4]=0;Reject<ArgumentException>(()=>solve(bad),"zero-weight Volume frame rejected");
        bad=make();bad.Constraints[0].Records[0][6]=2;Reject<ArgumentException>(()=>solve(bad),"unknown Volume record role rejected");
        bad=make();bad.Constraints[0].Records[0][0]=99;Reject<ArgumentException>(()=>solve(bad),"invalid Volume particle rejected");
        var inactive=Scene();inactive.ExecutionOrder=new int[0];Reject<ArgumentException>(()=>new ClothSession(new[]{inactive}),"empty schedule still refuses accidental constraint omission");
        inactive.AllowEmptyExecution=true;var permitted=new ClothSession(new[]{inactive});permitted.Step();Check(permitted.FrameCount==1,"explicit authored inactive constraint schedule accepted");
        var empty=make();empty.Constraints[0].Records=new double[0][];var emptySession=solve(empty);emptySession.Step();Check(Bits(emptySession.GetPositions()[1],empty.RestPositions),"authored empty Volume set retains a harmless execution slot");
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
