using System;
using System.Linq;

namespace OpenPhysicsHKX.Internal
{
    // Independent weighted rigid shape-matching approximation, not a Havok kernel.
    // Frame and apply records are explicit; no inferred rest-particle correspondences.
    internal static class VolumeShapeMatching
    {
        internal static Action<double> Build(ConstraintDefinition def, Cloth cloth)
        {
            Func<string,int> column = name => Array.IndexOf(def.Fields, name);
            int pi=column("particleIndex"), x=column("frameX"), y=column("frameY"), z=column("frameZ"),
                wi=column("weight"), si=column("stiffness"), role=column("role");
            var frame=def.Records.Where(r=>r[role]==0).ToArray();
            var apply=def.Records.Where(r=>r[role]==1).ToArray();
            double sum=frame.Sum(r=>r[wi]);
            var origin=new double[3];
            foreach(var row in frame)for(int k=0;k<3;k++)origin[k]+=row[x+k]*row[wi]/sum;
            return gain =>
            {
                if(gain<=0)return;
                var center=new double[3];
                foreach(var row in frame)for(int k=0;k<3;k++)center[k]+=cloth.P[(int)row[pi]][k]*row[wi]/sum;
                var h=new double[3,3];
                foreach(var row in frame)
                {
                    var p=cloth.P[(int)row[pi]];
                    for(int i=0;i<3;i++)for(int j=0;j<3;j++)h[i,j]+=(row[x+i]-origin[i])*(p[j]-center[j])*row[wi]/sum;
                }
                var rotation=Rotation(h);
                // Compute every target from the same frozen fit before modifying any particle.
                foreach(var row in apply)
                {
                    int index=(int)row[pi];if(cloth.InvMass[index]==0)continue;
                    double stiffness=Math.Max(0,Math.Min(1,row[si]*gain));
                    for(int i=0;i<3;i++)
                    {
                        double target=center[i];for(int j=0;j<3;j++)target+=rotation[i,j]*(row[x+j]-origin[j]);
                        cloth.P[index][i]+=(target-cloth.P[index][i])*stiffness;
                    }
                }
            };
        }

        // Davenport quaternion fit. Symmetric Jacobi eigensolve avoids the sign/
        // convergence problems of power iteration at 180 degrees or flat frames.
        static double[,] Rotation(double[,] h)
        {
            double trace=h[0,0]+h[1,1]+h[2,2];
            var a=new double[4,4];a[0,0]=trace;
            a[0,1]=a[1,0]=h[1,2]-h[2,1];a[0,2]=a[2,0]=h[2,0]-h[0,2];a[0,3]=a[3,0]=h[0,1]-h[1,0];
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)a[i+1,j+1]=h[i,j]+h[j,i]-(i==j?trace:0);
            var v=new double[4,4];for(int i=0;i<4;i++)v[i,i]=1;
            for(int sweep=0;sweep<32;sweep++)
            {
                double scale=0;for(int i=0;i<4;i++)scale=Math.Max(scale,Math.Abs(a[i,i]));
                bool changed=false;
                for(int p=0;p<3;p++)for(int q=p+1;q<4;q++)
                {
                    double apq=a[p,q];if(Math.Abs(apq)<=Math.Max(1e-30,scale*1e-14))continue;
                    double theta=.5*Math.Atan2(2*apq,a[q,q]-a[p,p]),c=Math.Cos(theta),s=Math.Sin(theta);
                    double pp=a[p,p],qq=a[q,q];a[p,p]=c*c*pp-2*s*c*apq+s*s*qq;a[q,q]=s*s*pp+2*s*c*apq+c*c*qq;
                    a[p,q]=a[q,p]=0;
                    for(int k=0;k<4;k++)
                    {
                        if(k!=p&&k!=q){double kp=a[k,p],kq=a[k,q];a[k,p]=a[p,k]=c*kp-s*kq;a[k,q]=a[q,k]=s*kp+c*kq;}
                        double vp=v[k,p],vq=v[k,q];v[k,p]=c*vp-s*vq;v[k,q]=s*vp+c*vq;
                    }
                    changed=true;
                }
                if(!changed)break;
            }
            int best=0;for(int i=1;i<4;i++)if(a[i,i]>a[best,best])best=i;
            double w=v[0,best],x=v[1,best],y=v[2,best],z=v[3,best];
            return new[,]{{1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)},
                {2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)},
                {2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)}};
        }
    }
}
