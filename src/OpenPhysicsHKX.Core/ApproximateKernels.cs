using System;
using System.Linq;
using OpenPhysicsHKX.Internal;

namespace OpenPhysicsHKX.Internal
{
    internal sealed class ApproximationClock { internal double Time; }

    // Independent, elementary position-based approximations. NOT ported HCT kernels.
    // Imported/ stays untouched; the compatibility CLI does not use these methods.
    internal static class ApproximateKernels
    {
        internal static bool IsApproximate(string name)
        {
            return name == "hclBendLinkConstraintSet" || name == "hclTransitionConstraintSet";
        }
        internal static Action<double> Build(ConstraintDefinition def, Cloth c, double[][] reference, ApproximationClock clock)
        {
            Func<string, int> col = name => Array.IndexOf(def.Fields, name);
            if (def.ClassName == "hclBendLinkConstraintSet")
            {
                int ai=col("particleA"), bi=col("particleB"), li=col("bendMinLength"), hi=col("stretchMaxLength"), kb=col("bendStiffness"), ks=col("stretchStiffness");
                return weight =>
                {
                    foreach (var r in def.Records)
                    {
                        int a=(int)r[ai], b=(int)r[bi]; double ia=c.InvMass[a], ib=c.InvMass[b], sum=ia+ib;
                        if (sum<=0) continue;
                        double dx=c.P[b][0]-c.P[a][0], dy=c.P[b][1]-c.P[a][1], dz=c.P[b][2]-c.P[a][2];
                        double distance=Math.Sqrt(dx*dx+dy*dy+dz*dz);
                        double target, stiffness;
                        if (distance<r[li]) { target=r[li]; stiffness=r[kb]; }
                        else if (distance>r[hi]) { target=r[hi]; stiffness=r[ks]; }
                        else continue;
                        if (distance<1e-12)
                        {
                            dx=c.P0[b][0]-c.P0[a][0];dy=c.P0[b][1]-c.P0[a][1];dz=c.P0[b][2]-c.P0[a][2];
                            double rest=Math.Sqrt(dx*dx+dy*dy+dz*dz);
                            if (rest<1e-12) { dx=1;dy=dz=0; } else { dx/=rest;dy/=rest;dz/=rest; }
                        }
                        else { dx/=distance;dy/=distance;dz/=distance; }
                        // Normalized inverse masses and bounded gain avoid overshoot under large authored masses.
                        double correction=(distance-target)*Math.Max(0,Math.Min(1,stiffness*weight));
                        double ca=ia/sum*correction, cb=ib/sum*correction;
                        c.P[a][0]+=ca*dx;c.P[a][1]+=ca*dy;c.P[a][2]+=ca*dz;
                        c.P[b][0]-=cb*dx;c.P[b][1]-=cb*dy;c.P[b][2]-=cb*dz;
                    }
                };
            }
            if (def.ClassName == "hclTransitionConstraintSet")
            {
                int pi=col("particleIndex"), ri=col("referenceVertex"), delay=col(def.TransitionToAnimation?"toAnimDelay":"toSimDelay"), md=col("toSimMaxDistance");
                double period=def.TransitionToAnimation?def.ToAnimationPeriod:def.ToSimulationPeriod;
                return weight =>
                {
                    foreach (var r in def.Records)
                    {
                        int i=(int)r[pi]; if (c.InvMass[i]==0) continue;
                        double elapsed=clock.Time-r[delay];
                        double blend=elapsed<0?0:period==0?1:Math.Max(0,Math.Min(1,elapsed/period));
                        double release=def.TransitionToAnimation?1-blend:blend;
                        if (release>=1) continue; // fully released simulation, not a permanent leash
                        var target=reference[(int)r[ri]];
                        if (release<=0)
                        {
                            for (int k=0;k<3;k++) { c.P[i][k]=target[k];c.Prev[i][k]=target[k]; }
                            continue;
                        }
                        double radius=r[md]*release/Math.Max(1e-12,1-release);
                        double dx=c.P[i][0]-target[0],dy=c.P[i][1]-target[1],dz=c.P[i][2]-target[2];
                        double length=Math.Sqrt(dx*dx+dy*dy+dz*dz);
                        if (length<=radius||length<1e-12) continue;
                        double amount=(1-radius/length)*Math.Max(0,Math.Min(1,weight));
                        // Correct history equally: this blend should not inject an artificial Verlet impulse.
                        double x=dx*amount,y=dy*amount,z=dz*amount;
                        c.P[i][0]-=x;c.P[i][1]-=y;c.P[i][2]-=z;
                        c.Prev[i][0]-=x;c.Prev[i][1]-=y;c.Prev[i][2]-=z;
                    }
                };
            }
            return null;
        }
    }
}
