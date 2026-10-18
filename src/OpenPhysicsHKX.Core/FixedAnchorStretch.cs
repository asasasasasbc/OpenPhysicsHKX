using System;
using System.Collections.Generic;
using System.Linq;
using OpenPhysicsHKX.Internal;

namespace OpenPhysicsHKX.Internal
{
    // Opt-in correction for the legacy A-only stretch path. An authored fixed
    // endpoint is an anchor, never the particle to move. Other record arithmetic
    // and ordering remain legacy-compatible; this is not a general HCT kernel.
    internal static class FixedAnchorStretch
    {
        internal static Action<double> Build(ConstraintDefinition def, Cloth cloth, Dictionary<string, object> options)
        {
            int ai = Array.IndexOf(def.Fields, "particleA"), bi = Array.IndexOf(def.Fields, "particleB");
            int li = Array.IndexOf(def.Fields, "restLength"), ki = Array.IndexOf(def.Fields, "stiffness");
            double scale = (double)options["stretch_scale"];
            bool oneSided = (string)options["stretch_sign"] == "onesided";
            var actions = new List<Action<double>>();
            foreach (var row in def.Records)
            {
                int a = (int)row[ai], b = (int)row[bi];
                bool fixedA = cloth.InvMass[a] == 0, fixedB = cloth.InvMass[b] == 0;
                if (fixedA && fixedB) continue;
                if (!fixedA && !fixedB)
                {
                    actions.Add(Solvers.BuildSolver(def.ClassName, def.Fields.ToList(),
                        new List<object[]> { row.Select(v => (object)v).ToArray() }, cloth, null, options));
                    continue;
                }
                int anchor = fixedA ? a : b, particle = fixedA ? b : a;
                double length = row[li], stiffness = row[ki] * scale;
                actions.Add(weight =>
                {
                    var target = cloth.P[anchor]; var p = cloth.P[particle];
                    double dx = p[0] - target[0], dy = p[1] - target[1], dz = p[2] - target[2];
                    double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (distance <= 0) return;
                    double error = distance - length;
                    if (oneSided && error <= 0) return;
                    double correction = weight * stiffness * error / distance;
                    p[0] -= correction * dx; p[1] -= correction * dy; p[2] -= correction * dz;
                });
            }
            return weight => { foreach (var action in actions) action(weight); };
        }
    }
}
