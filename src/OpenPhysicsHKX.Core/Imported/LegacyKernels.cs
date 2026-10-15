// Mechanically extracted from the hash-pinned C# delivery. See docs/PROVENANCE.md.
using System;
using System.Collections.Generic;
namespace OpenPhysicsHKX.Internal
{
internal sealed class Cloth
{
    public int Sim;
    public int N;
    public List<double[]> P0 = new List<double[]>();
    public List<double[]> P = new List<double[]>();
    public List<double[]> Prev = new List<double[]>();
    public List<double> InvMass = new List<double>();
    public List<double> Mass = new List<double>();
    public List<int> Fixed = new List<int>();
    public double GravityX = 0.0, GravityY = -9.81, GravityZ = 0.0;
    public double Damping = 0.0;
    public List<SetRec> Sets = new List<SetRec>();
    public List<int> Execution = new List<int>();
    public int Substeps = 1;
    public int Iterations = 1;
    public bool Adapt;
    public string Name = "sim#?";
    public List<double[]> Collidables = new List<double[]>();   // ax,ay,az,bx,by,bz,ra,rb
    public double ParticleRadius = 0.0;
    public Dictionary<int, double[]> PinRef = new Dictionary<int, double[]>();
    public double? AuthoredParticleRadius;
    public double? MaxParticleRadius;
    public double? AuthoredCollisionTolerance;
    public double CollisionTolerance = 0.0;
    public string CollisionToleranceMode = "offset";
    public List<double[]> RefSeed;   // --localrange-reference seed (a109); null unless set
    public string ExecMode = "native";
}

internal sealed class SetRec
{
    public int Type;
    public string Class;
    public int N;
    public bool Disabled;
    public int ProbeIndex;
    public List<string> Fields;
    public List<object[]> Entries;
    public Action<double> Solve;          // null when the class is unmodelled (e.g. Transition)
    public int Calls;                     // a104: makes a silently-dropped exec index visible
}
internal static class DataShape
{
    public static Dictionary<string, object> Dict(object o)
    {
        return o as Dictionary<string, object>;
    }

    public static List<object> List(object o)
    {
        var arr = o as object[];
        if (arr != null) return new List<object>(arr);
        var al = o as System.Collections.ArrayList;
        if (al != null) return new List<object>(al.ToArray());
        return new List<object>();
    }

    public static bool IsList(object o) { return o is object[] || o is System.Collections.ArrayList; }

    public static object Get(Dictionary<string, object> d, string k)
    {
        object v;
        return (d != null && d.TryGetValue(k, out v)) ? v : null;
    }
}
internal static class Conv
{
    // Python: float(v). Strings are accepted everywhere because the probe format stores scalars as
    // strings in some arrays and as numbers in others -- and after the QuoteNumbers pre-pass, EVERY JSON
    // number also arrives as a string, so this single path is what gives both languages the same bits.
    public static double ToDouble(object x)
    {
        x = Unwrap(x);
        if (x == null) return 0.0;
        if (x is string) return PyNum.Parse((string)x);
        if (x is bool) return ((bool)x) ? 1.0 : 0.0;
        // anything that still arrives as a real number (not produced by the pre-pass) keeps the
        // Framework conversion, and PyNum is preferred for decimal for the reason in its header.
        if (x is decimal) return (double)(decimal)x;
        return Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static int ToInt(object x)
    {
        x = Unwrap(x);
        if (x == null) return 0;
        if (x is string) return (int)double.Parse(((string)x).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        if (x is bool) return ((bool)x) ? 1 : 0;
        return (int)Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Python: bool(v) on a non-empty string is True -- "" is the only falsey string.
    public static bool ToBool(object x)
    {
        x = Unwrap(x);
        if (x == null) return false;
        if (x is bool) return (bool)x;
        if (x is string)
        {
            string s = ((string)x).Trim().ToLowerInvariant();
            return s == "true" || s == "1";
        }
        if (x is int) return (int)x != 0;
        double d = Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture);
        return d != 0.0;
    }

    // probe scalars arrive as {"kind":"scalar","value":...}; structArray/packedArray nodes pass through
    public static object Unwrap(object v)
    {
        var d = v as Dictionary<string, object>;
        if (d != null)
        {
            object kind = DataShape.Get(d, "kind");
            string k = kind as string;
            if (k == "scalar") return DataShape.Get(d, "value");
            if (k == "structArray" || k == "packedArray") return d;
        }
        return v;
    }

    // "(0 -10 0 0)" -> double[4]; shorter input is zero-padded, longer is truncated -- like Python.
    public static double[] Vec4(object text)
    {
        var vals = new List<double>();
        if (DataShape.IsList(text))
        {
            foreach (var t in DataShape.List(text)) vals.Add(ToDouble(t));
        }
        else
        {
            string t = Convert.ToString(Unwrap(text), System.Globalization.CultureInfo.InvariantCulture) ?? "";
            t = t.Trim().Trim('(', ')');
            foreach (var p in t.Replace(",", " ").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                vals.Add(double.Parse(p, System.Globalization.CultureInfo.InvariantCulture));
        }
        while (vals.Count < 4) vals.Add(0.0);
        return new double[] { vals[0], vals[1], vals[2], vals[3] };
    }

    // -> (fieldNames, rows).  Handles list rows, dict rows, and "k:v,k:v" string rows.
    public static void StructEntries(object node, out List<string> keys, out List<object[]> rows)
    {
        keys = new List<string>();
        rows = new List<object[]>();
        var v = Unwrap(node) as Dictionary<string, object>;
        if (v == null) return;
        object entsO = DataShape.Get(v, "entries");
        if (!DataShape.IsList(entsO)) return;
        var ents = DataShape.List(entsO);
        if (ents.Count == 0) return;

        object kO = DataShape.Get(v, "fields") ?? DataShape.Get(v, "keys");
        if (DataShape.IsList(kO))
            foreach (var k in DataShape.List(kO)) keys.Add(Convert.ToString(k).Trim('"'));

        object first = ents[0];
        if (first is Dictionary<string, object>)
        {
            if (keys.Count == 0)
                foreach (var kv in (Dictionary<string, object>)first) keys.Add(kv.Key);
            foreach (var e in ents)
            {
                var d = (Dictionary<string, object>)e;
                var row = new object[keys.Count];
                for (int i = 0; i < keys.Count; i++) row[i] = DataShape.Get(d, keys[i]);
                rows.Add(row);
            }
            return;
        }
        if (DataShape.IsList(first))
        {
            foreach (var e in ents) rows.Add(DataShape.List(e).ToArray());
            return;
        }
        if (first is string)
        {
            foreach (var e in ents)
            {
                var d = new Dictionary<string, object>();
                foreach (var part in Convert.ToString(e).Split(','))
                {
                    int c = part.IndexOf(':');
                    if (c < 0) continue;
                    d[part.Substring(0, c).Trim().Trim('"')] = part.Substring(c + 1).Trim();
                }
                if (keys.Count == 0) foreach (var kv in d) keys.Add(kv.Key);
                var row = new object[keys.Count];
                for (int i = 0; i < keys.Count; i++) row[i] = d.ContainsKey(keys[i]) ? d[keys[i]] : null;
                rows.Add(row);
            }
            return;
        }
        foreach (var e in ents) rows.Add(DataShape.List(e).ToArray());
    }
}
// =====================================================================================
// S2 -- geometry and the constraint kernels
// =====================================================================================
//
// Each Python kernel is a closure over the set's entries and the cloth; C# lambdas capture locals the
// same way, so the translation keeps the capture semantics identical rather than restructuring them.
//
// Faithfulness notes, each from a defect recorded in the Python source:
//   * Stretch's a3c form moves A ONLY and never reads invMass (a3c). The sign was historically INVERTED
//     (a52): t = max(d - L, 0) resists EXTENSION, so a link already shorter than restLength is inert.
//   * The a69 rule: link_scale and stretch_scale are separate, and stretch_scale defaults to link_scale
//     so that every pre-existing invocation stays bit-identical.
//   * stretch_sign defaults to "onesided" and is overridden per cloth by --stretch-sign-by-sim FIRST,
//     then by the global flag (a94).
//   * The `normalized` gain form must carry k*w (a66 fixed it in gain() but a69 found the Stretch copy
//     still dropping them, which is what made a68's "the Shawl is driven by Stretch" wrong).
//   * LocalRange adds +1.19209290e-07 to each component before measuring (a15's pseudocode), and its
//     normal band is dead unless build_vertex_normals returns a table.
//   * The bend gain is capped by MAGNITUDE, not by signed value (a55): `if g > max_gain` alone never
//     fired for a negative g and left an unbounded gain on one side of the sign test.
//   * The bend setup precomputes each link's coefficients ONCE from p0, so it is a rest-pose-referenced
//     stabiliser -- which is why it is exactly zero at p0 (a10/a11/a14 observing "relaxed" was a
//     statement about p0, not about the constraint).
internal static class Solvers
{
    // a15/a3c: FUN_1807CE980, with the modelType==1 correction. modelType 1 is the constant-1.0 table
    // slot and is what the hclSimClothInstance ctor installs; it becomes 2 only when
    // adaptConstraintStiffness != 0, so ds3 returns 1.0 unconditionally while bb reaches the pow().
    public static double WeightFor(int modelType, int typeId, int subStepIndex, int subSteps, double slow, double world)
    {
        if (modelType == 1) return 1.0;
        if (subSteps == 1 && slow == 1.0 && world == 1.0) return 1.0;
        if (typeId == 8) return (subStepIndex + 1) / (double)subSteps;
        if (typeId == 5 || typeId == 10) return (subStepIndex == subSteps - 1) ? 1.0 : 0.0;
        // THE one place libm and .NET could disagree: pow(x, -1.725) may differ in the last ULP.
        return Math.Pow(subSteps * slow * world, -1.725);
    }

    static double[] Sub(double[] u, double[] v) { return new double[] { u[0] - v[0], u[1] - v[1], u[2] - v[2] }; }

    static double[] Cross(double[] u, double[] v)
    {
        return new double[] { u[1] * v[2] - u[2] * v[1], u[2] * v[0] - u[0] * v[2], u[0] * v[1] - u[1] * v[0] };
    }

    static double[] Unit(double[] u)
    {
        double L = Math.Sqrt(u[0] * u[0] + u[1] * u[1] + u[2] * u[2]);
        if (L < 1e-12) return null;
        return new double[] { u[0] / L, u[1] / L, u[2] / L };
    }

    public static Action<double> BuildSolver(string kind, List<string> fields, List<object[]> entries,
        Cloth cloth, Func<int, double[]> normals, Dictionary<string, object> opts)
    {
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < fields.Count; i++) idx[fields[i]] = i;
        double linkScale = opts.ContainsKey("link_scale") && opts["link_scale"] != null ? (double)opts["link_scale"] : 1.0;
        string scalarMode = opts.ContainsKey("bend_scalar") ? (string)opts["bend_scalar"] : "grad";
        bool natScale = opts.ContainsKey("bend_nat_scale") && (bool)opts["bend_nat_scale"];
        var bySim = opts.ContainsKey("stretch_sign_by_sim")
            ? (Dictionary<int, string>)opts["stretch_sign_by_sim"] : new Dictionary<int, string>();
        string stSign;
        if (!bySim.TryGetValue(cloth.Sim, out stSign) || stSign == null)
            stSign = (opts.ContainsKey("stretch_sign") ? (string)opts["stretch_sign"] : null) ?? "onesided";
        string stForm = opts.ContainsKey("stretch_form") ? (string)opts["stretch_form"] : "a3c";
        double stScale = opts.ContainsKey("stretch_scale") && opts["stretch_scale"] != null
            ? (double)opts["stretch_scale"] : linkScale;

        Func<string, string, List<object[]>> records = (pa, pb) => entries;   // the index pair is read below

        if (kind == "hclStandardLinkConstraintSet")
        {
            string form = opts.ContainsKey("link_form") ? (string)opts["link_form"] : "raw";
            Func<int, int, double, double, double[]> gain = (a, b, k, w) =>
            {
                if (form == "normalized")
                {
                    double tot = cloth.InvMass[a] + cloth.InvMass[b];
                    if (tot <= 0.0) return new double[] { 0.0, 0.0 };
                    double kw = k * w;
                    return new double[] { cloth.InvMass[a] / tot * kw, cloth.InvMass[b] / tot * kw };
                }
                if (form == "mass") return new double[] { cloth.Mass[a] * k * w, cloth.Mass[b] * k * w };
                return new double[] { cloth.InvMass[a] * k * w, cloth.InvMass[b] * k * w };
            };
            int iA = idx["particleA"], iB = idx["particleB"], iL = idx["restLength"], iK = idx["stiffness"];
            return (w) =>
            {
                var p = cloth.P;
                foreach (var e in entries)
                {
                    int a = (int)Conv.ToDouble(e[iA]), b = (int)Conv.ToDouble(e[iB]);
                    double L = Conv.ToDouble(e[iL]);
                    double k = Conv.ToDouble(e[iK]) * linkScale;
                    double ax = p[a][0], ay = p[a][1], az = p[a][2];
                    double bx = p[b][0], by = p[b][1], bz = p[b][2];
                    double dx = bx - ax, dy = by - ay, dz = bz - az;
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d <= 0.0) continue;
                    double t = (d - L) / d;
                    double[] c = gain(a, b, k, w);
                    double ca = c[0] * t, cb = c[1] * t;
                    p[a][0] = ax + ca * dx; p[a][1] = ay + ca * dy; p[a][2] = az + ca * dz;
                    p[b][0] = bx - cb * dx; p[b][1] = by - cb * dy; p[b][2] = bz - cb * dz;
                }
            };
        }

        if (kind == "hclStretchLinkConstraintSet")
        {
            string form = opts.ContainsKey("link_form") ? (string)opts["link_form"] : "raw";
            Func<int, int, double, double, double[]> sgain = (a, b, k, w) =>
            {
                if (form == "normalized")
                {
                    double tot = cloth.InvMass[a] + cloth.InvMass[b];
                    if (tot <= 0.0) return new double[] { 0.0, 0.0 };
                    double kw = k * w;
                    return new double[] { cloth.InvMass[a] / tot * kw, cloth.InvMass[b] / tot * kw };
                }
                if (form == "mass") return new double[] { cloth.Mass[a] * k * w, cloth.Mass[b] * k * w };
                return new double[] { cloth.InvMass[a] * k * w, cloth.InvMass[b] * k * w };
            };
            int iA = idx["particleA"], iB = idx["particleB"], iL = idx["restLength"], iK = idx["stiffness"];
            return (w) =>
            {
                var p = cloth.P;
                foreach (var e in entries)
                {
                    int a = (int)Conv.ToDouble(e[iA]), b = (int)Conv.ToDouble(e[iB]);
                    double L = Conv.ToDouble(e[iL]);
                    double k = Conv.ToDouble(e[iK]) * stScale;
                    double ax = p[a][0], ay = p[a][1], az = p[a][2];
                    double bx = p[b][0], by = p[b][1], bz = p[b][2];
                    double dx = bx - ax, dy = by - ay, dz = bz - az;
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d <= 0.0) continue;
                    double t = stSign == "onesided" ? Math.Max(d - L, 0.0) : (d - L);
                    if (t == 0.0) continue;
                    if (stForm == "symmetric")
                    {
                        double[] c = sgain(a, b, k, w);
                        double ca = c[0] * (t / d), cb = c[1] * (t / d);
                        p[a][0] = ax + ca * dx; p[a][1] = ay + ca * dy; p[a][2] = az + ca * dz;
                        p[b][0] = bx - cb * dx; p[b][1] = by - cb * dy; p[b][2] = bz - cb * dz;
                        continue;
                    }
                    double cc = (w * k * t) / d;
                    p[a][0] = ax + cc * dx; p[a][1] = ay + cc * dy; p[a][2] = az + cc * dz;
                }
            };
        }

        if (kind == "hclCompressibleLinkConstraintSet")
        {
            string form = opts.ContainsKey("link_form") ? (string)opts["link_form"] : "raw";
            Func<int, int, double, double, double[]> cgain = (a, b, k, w) =>
            {
                if (form == "normalized")
                {
                    double tot = cloth.InvMass[a] + cloth.InvMass[b];
                    if (tot <= 0.0) return new double[] { 0.0, 0.0 };
                    double kw = k * w;
                    return new double[] { cloth.InvMass[a] / tot * kw, cloth.InvMass[b] / tot * kw };
                }
                if (form == "mass") return new double[] { cloth.Mass[a] * k * w, cloth.Mass[b] * k * w };
                return new double[] { cloth.InvMass[a] * k * w, cloth.InvMass[b] * k * w };
            };
            int iA = idx["particleA"], iB = idx["particleB"], iL = idx["restLength"], iK = idx["stiffness"];
            bool hasC = idx.ContainsKey("compressionLength");
            int iC = hasC ? idx["compressionLength"] : iL;
            string mode = opts.ContainsKey("compress_mode") ? (string)opts["compress_mode"] : "deadband";
            return (w) =>
            {
                var p = cloth.P;
                foreach (var e in entries)
                {
                    int a = (int)Conv.ToDouble(e[iA]), b = (int)Conv.ToDouble(e[iB]);
                    double L = Conv.ToDouble(e[iL]);
                    double Cmp = Conv.ToDouble(e[iC]);
                    double k = Conv.ToDouble(e[iK]) * linkScale;
                    double ax = p[a][0], ay = p[a][1], az = p[a][2];
                    double bx = p[b][0], by = p[b][1], bz = p[b][2];
                    double dx = bx - ax, dy = by - ay, dz = bz - az;
                    double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                    if (d <= 0.0) continue;
                    double over;
                    if (d > L) over = d - L;
                    else if (mode == "restlength") over = d - L;
                    else if (d <= Cmp) over = d - Cmp;
                    else continue;
                    double[] c = cgain(a, b, k, w);
                    double ca = c[0] * (over / d), cb = c[1] * (over / d);
                    p[a][0] = ax + ca * dx; p[a][1] = ay + ca * dy; p[a][2] = az + ca * dz;
                    p[b][0] = bx - cb * dx; p[b][1] = by - cb * dy; p[b][2] = bz - cb * dz;
                }
            };
        }

        if (kind == "hclLocalRangeConstraintSet")
        {
            // exact a15 pseudocode: a hard position projection, no invMass anywhere
            int iPi = idx["particleIndex"], iRv = idx["referenceVertex"];
            int iMax = idx["maximumDistance"], iMaxN = idx["maxNormalDistance"], iMinN = idx["minNormalDistance"];
            string shape = (string)opts["shape_type"];
            bool applyNormal = (bool)opts["apply_normal"];
            var reference = (Func<int, int, double[]>)opts["reference"];
            string nBounds = opts.ContainsKey("normal_bounds") ? (string)opts["normal_bounds"] : "native";
            return (w) =>
            {
                var p = cloth.P;
                foreach (var e in entries)
                {
                    int i = (int)Conv.ToDouble(e[iPi]);
                    double[] r = reference(i, (int)Conv.ToDouble(e[iRv]));
                    double px = p[i][0], py = p[i][1], pz = p[i][2];
                    double dx = (px - r[0]) + 1.19209290e-07;
                    double dy = (py - r[1]) + 1.19209290e-07;
                    double dz = (pz - r[2]) + 1.19209290e-07;
                    double len2 = dx * dx + dy * dy + dz * dz;
                    if (len2 <= 0.0) continue;
                    double rinv = 1.0 / Math.Sqrt(len2);
                    double ln = rinv * len2;
                    double[] nv = (applyNormal && shape == "SHAPE_SPHERE") ? normals(i) : null;
                    double tx, ty, tz;
                    if (shape == "SHAPE_SPHERE" || nv == null) { tx = dx; ty = dy; tz = dz; }
                    else
                    {
                        double dn = nv[0] * dx + nv[1] * dy + nv[2] * dz;
                        tx = dx - dn * nv[0]; ty = dy - dn * nv[1]; tz = dz - dn * nv[2];
                    }
                    double tl = Math.Sqrt(tx * tx + ty * ty + tz * tz);
                    if (tl <= 0.0) continue;
                    double s = Math.Min((Conv.ToDouble(e[iMax]) - tl) * w, 0.0);
                    double ux = tx / tl, uy = ty / tl, uz = tz / tl;
                    double nx = px + s * ux, ny = py + s * uy, nz = pz + s * uz;
                    if (nv != null && nBounds != "ignore")
                    {
                        double dhx = dx * rinv, dhy = dy * rinv, dhz = dz * rinv;
                        double proj = (nv[0] * dhx + nv[1] * dhy + nv[2] * dhz) * (s + ln);
                        double slo = Math.Min(proj - Conv.ToDouble(e[iMinN]), 0.0);
                        double shi = Math.Min(Conv.ToDouble(e[iMaxN]) - proj, 0.0);
                        nx = nx - slo * nv[0] + shi * nv[0];
                        ny = ny - slo * nv[1] + shi * nv[1];
                        nz = nz - slo * nv[2] + shi * nv[2];
                    }
                    p[i][0] = nx; p[i][1] = ny; p[i][2] = nz;
                }
            };
        }

        if (kind == "hclBendStiffnessConstraintSet")
        {
            int iA = idx["particleA"], iB = idx["particleB"], iC = idx["particleC"], iD = idx["particleD"];
            int iS = idx["bendStiffness"];
            int iWA = idx["weightA"], iWB = idx["weightB"], iWC = idx["weightC"], iWD = idx["weightD"];
            double scale = opts.ContainsKey("bend_scale") ? (double)opts["bend_scale"] : 1.0;
            double maxGain = opts.ContainsKey("bend_max_gain") ? (double)opts["bend_max_gain"] : 1.0;
            var p0 = cloth.P0;
            var inv = cloth.InvMass;
            string nrmMode = opts.ContainsKey("bend_normal") ? (string)opts["bend_normal"] : "cross";

            // setup: per-link coefficients, computed ONCE from p0
            var armsL = new List<int[]>();
            var coefL = new List<double[][]>();
            var k0L = new List<double>();
            var ksL = new List<double>();
            var denL = new List<double>();
            var natL = new List<double[]>();
            foreach (var e in entries)
            {
                int a_ = (int)Conv.ToDouble(e[iA]), b_ = (int)Conv.ToDouble(e[iB]);
                int c_ = (int)Conv.ToDouble(e[iC]), d_ = (int)Conv.ToDouble(e[iD]);
                double wA = Conv.ToDouble(e[iWA]), wB = Conv.ToDouble(e[iWB]);
                double wC = -Math.Abs(Conv.ToDouble(e[iWC])), wD = -Math.Abs(Conv.ToDouble(e[iWD]));
                double ks = Math.Abs(Conv.ToDouble(e[iS])) * scale;
                if (ks == 0.0) continue;
                double[] A = p0[a_], B = p0[b_], C = p0[c_], D = p0[d_];
                double nx, ny, nz;
                double[] cd = null;
                if (nrmMode == "bisector")
                {
                    cd = Sub(D, C);
                    double[] u1 = Unit(Cross(Sub(A, C), cd));
                    double[] u2 = Unit(Cross(Sub(B, C), cd));
                    if (u1 == null || u2 == null) continue;
                    double sx = u1[0] + u2[0], sy = u1[1] + u2[1], sz = u1[2] + u2[2];
                    double sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                    if (sl < 1e-12) continue;
                    nx = sx / sl; ny = sy / sl; nz = sz / sl;
                }
                else
                {
                    double[] ab = Sub(B, A), ac = Sub(C, A);
                    nx = ab[1] * ac[2] - ab[2] * ac[1];
                    ny = ab[2] * ac[0] - ab[0] * ac[2];
                    nz = ab[0] * ac[1] - ab[1] * ac[0];
                    double nl = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    if (nl < 1e-12) continue;
                    nx /= nl; ny /= nl; nz /= nl;
                }
                double ab0 = B[0] - A[0], ab1 = B[1] - A[1], ab2 = B[2] - A[2];
                // x*x, NOT Math.Pow(x,2): measured over 200k random doubles, Python's **2 equals x*x
                // exactly while Math.Pow(x,2) differs on 329 of them (by 1 ULP). Using Pow here would
                // inject a 1-ULP error into every bend coefficient, which is exactly the drift the
                // end-to-end comparison was showing.
                double L2 = ab0 * ab0 + ab1 * ab1 + ab2 * ab2;
                if (L2 < 1e-12) continue;
                var coef = new double[][] {
                    new double[] { wA * nx / L2, wA * ny / L2, wA * nz / L2 },
                    new double[] { wB * nx / L2, wB * ny / L2, wB * nz / L2 },
                    new double[] { wC * nx / L2, wC * ny / L2, wC * nz / L2 },
                    new double[] { wD * nx / L2, wD * ny / L2, wD * nz / L2 } };
                var arms = new int[] { a_, b_, c_, d_ };
                double k0 = 0.0, den = 0.0;
                for (int j = 0; j < 4; j++)
                {
                    double[] cj = coef[j]; double[] pj = p0[arms[j]];
                    k0 += cj[0] * pj[0] + cj[1] * pj[1] + cj[2] * pj[2];
                    den += inv[arms[j]] * (cj[0] * cj[0] + cj[1] * cj[1] + cj[2] * cj[2]);
                }
                if (den <= 0.0) continue;
                double[] wts = new double[] { wA, wB, wC, wD };
                double v0x = 0, v0y = 0, v0z = 0;
                for (int j = 0; j < 4; j++)
                {
                    double[] pj = p0[arms[j]]; double wj = wts[j];
                    v0x += wj * pj[0]; v0y += wj * pj[1]; v0z += wj * pj[2];
                }
                double dcl2;
                if (nrmMode == "bisector") dcl2 = cd[0] * cd[0] + cd[1] * cd[1] + cd[2] * cd[2];
                else { double[] ac_ = Sub(C, A); dcl2 = ac_[0] * ac_[0] + ac_[1] * ac_[1] + ac_[2] * ac_[2]; }
                double sl2 = nx * nx + ny * ny + nz * nz;
                if (sl2 <= 1e-12) continue;
                double kappa0 = -(v0x * nx + v0y * ny + v0z * nz) / sl2;
                armsL.Add(arms); coefL.Add(coef); k0L.Add(k0); ksL.Add(ks); denL.Add(den);
                natL.Add(new double[] { nx, ny, nz, kappa0, wA, wB, wC, wD, dcl2 });
            }

            return (w) =>
            {
                var p = cloth.P;
                for (int li = 0; li < armsL.Count; li++)
                {
                    var arms = armsL[li]; var coef = coefL[li];
                    double k0 = k0L[li], ks = ksL[li], den = denL[li]; var nat = natL[li];
                    double g = w * ks;
                    // a55: cap by MAGNITUDE. `if g > max_gain` alone never fired for a negative g.
                    if (g > maxGain) g = maxGain;
                    else if (g < -maxGain) g = -maxGain;
                    if (g == 0.0) continue;
                    if (scalarMode == "native")
                    {
                        double sxx = nat[0], syy = nat[1], szz = nat[2], kappa0 = nat[3];
                        double[] wts = new double[] { nat[4], nat[5], nat[6], nat[7] };
                        double dcl2 = nat[8];
                        double sdot = sxx * sxx + syy * syy + szz * szz;
                        double vx = 0, vy = 0, vz = 0;
                        for (int j = 0; j < 4; j++)
                        {
                            double wj = wts[j]; double[] pj = p[arms[j]];
                            vx += wj * pj[0]; vy += wj * pj[1]; vz += wj * pj[2];
                        }
                        double qx = vx + kappa0 * sxx, qy = vy + kappa0 * syy, qz = vz + kappa0 * szz;
                        if (natScale)
                        {
                            double smag = Math.Sqrt(sdot), dcm = Math.Sqrt(dcl2);
                            if (smag < 1e-12 || dcm < 1e-12) continue;
                            double f0 = 1.0 / (smag * dcl2);
                            qx *= f0; qy *= f0; qz *= f0;
                        }
                        for (int j = 0; j < 4; j++)
                        {
                            double im = inv[arms[j]];
                            if (im == 0.0) continue;
                            double f = g * im * wts[j];
                            double[] pj = p[arms[j]];
                            pj[0] += f * qx; pj[1] += f * qy; pj[2] += f * qz;
                        }
                        continue;
                    }
                    double kk = 0.0;
                    for (int j = 0; j < 4; j++)
                    {
                        double[] cj = coef[j]; double[] pj = p[arms[j]];
                        kk += cj[0] * pj[0] + cj[1] * pj[1] + cj[2] * pj[2];
                    }
                    double Cv = kk - k0;
                    if (Cv == 0.0) continue;
                    double lam = -g * Cv / den;
                    for (int j = 0; j < 4; j++)
                    {
                        double im = inv[arms[j]];
                        if (im == 0.0) continue;
                        double[] cj = coef[j]; double[] pj = p[arms[j]];
                        pj[0] += lam * im * cj[0]; pj[1] += lam * im * cj[1]; pj[2] += lam * im * cj[2];
                    }
                }
            };
        }

        return null;   // hclTransitionConstraintSet and anything unmodelled
    }

    // a109: vertex normals for the LocalRange normal-band clamp.
    //
    // NOTE (a117, unresolved): this function's vertex<->particle mapping is known to be wrong -- the
    // localUnpackedPNs entries sit 0.39-0.54 m (nearest neighbour) from p0 and only vertex 0 coincides
    // with particle 0, so neither "the first half is p0 in particle order" nor "vertex index == particle
    // index" holds. It is ported AS IS so the two implementations stay comparable; the defect is
    // recorded here and in the deliverables rather than silently repaired on one side only. For bb the
    // band is provably dead code regardless (maxNormalDistance == maximumDistance and
    // minNormalDistance == -maximumDistance), so nothing measurable depends on it today.
    public static List<double[]> BuildVertexNormals(List<object> pnArrays, int simIndex, int particleCount)
    {
        if (pnArrays == null || pnArrays.Count == 0) return null;
        List<int> tris = null;
        List<double[]> verts = null;
        for (int i = 0; i < pnArrays.Count; i++)
        {
            var a = DataShape.Dict(pnArrays[i]);
            string cls = Convert.ToString(DataShape.Get(a, "class")) ?? "";
            if (cls == "hclSimClothData" && Convert.ToString(DataShape.Get(a, "objectIndex")) == Convert.ToString(simIndex))
            {
                tris = new List<int>();
                foreach (var x in DataShape.List(DataShape.Get(a, "values"))) tris.Add(Conv.ToInt(x));
                for (int k = i + 1; k < pnArrays.Count; k++)
                {
                    var b = DataShape.Dict(pnArrays[k]);
                    if ((Convert.ToString(DataShape.Get(b, "class")) ?? "") == "hclObjectSpaceSkinPNOperator")
                    {
                        var vals = new List<double>();
                        foreach (var x in DataShape.List(DataShape.Get(b, "values"))) vals.Add(Conv.ToDouble(x));
                        int nv = vals.Count / 8;
                        verts = new List<double[]>();
                        for (int q = 0; q < nv; q++)
                            verts.Add(new double[] { vals[4 * q], vals[4 * q + 1], vals[4 * q + 2] });
                        break;
                    }
                }
                break;
            }
        }
        if (tris == null || verts == null || tris.Count == 0 || verts.Count == 0) return null;
        int n = verts.Count;
        var acc = new List<double[]>();
        for (int i = 0; i < n; i++) acc.Add(new double[] { 0.0, 0.0, 0.0 });
        for (int t = 0; t + 2 < tris.Count; t += 3)
        {
            int ia = tris[t], ib = tris[t + 1], ic = tris[t + 2];
            if (!(ia >= 0 && ia < n && ib >= 0 && ib < n && ic >= 0 && ic < n)) continue;
            double[] A = verts[ia], B = verts[ib], C = verts[ic];
            double ux = B[0] - A[0], uy = B[1] - A[1], uz = B[2] - A[2];
            double vx = C[0] - A[0], vy = C[1] - A[1], vz = C[2] - A[2];
            double nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            foreach (int k in new int[] { ia, ib, ic })
            {
                acc[k][0] += nx; acc[k][1] += ny; acc[k][2] += nz;
            }
        }
        var outp = new List<double[]>();
        foreach (var v in acc)
        {
            double L = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            outp.Add(L > 0.0 ? new double[] { v[0] / L, v[1] / L, v[2] / L } : new double[] { 0.0, 1.0, 0.0 });
        }
        return outp;
    }
}

// =====================================================================================
// S3 -- the numeric core: integration, execution order, the substep/iteration loop, collision
// =====================================================================================
//
// Every faithfulness note below corresponds to a defect that was measured in the Python source:
//   * verlet: prev must receive the OLD position. Assigning it the NEW one makes (p - prev) exactly
//     zero from the second step on, degenerating the integrator to p += acc*dt^2 and destroying all
//     momentum. The three p components are written from the OLD x,y,z, so they are read out first.
//   * damping is PER SUBSTEP and the legacy form is pow(1 - damping, dt) with dt the SUBSTEP dt (a119).
//     `frame` uses the exponent dt/nsub, which is the same quantity applied once per frame.
//   * MoveFixedParticles pins BEFORE the iteration loop and the cloth is re-pinned AFTER it (a116).
//     The post-solve pin is needed because hclLocalRangeConstraintSet takes no invMass, so nothing in
//     its kernel stops it moving a fixed particle (the fixed set drifted by 2.3e+02 without it).
//   * collision runs AFTER the constraint loop of each ITERATION, not once per substep (a3b).
//   * _exec_order drops any index >= len(sets) SILENTLY, so a loaded set can never run. bb's
//     [0,3,2,1,4] over 4 loaded sets runs [0,3,2,1] and excludes one (a104). The call counter exists
//     to make that visible.
//   * initialParticles is captured AFTER frame 0, because the native does Poses(); run();
//     initial = Particles(). Matching that is what makes the dump directly comparable to a golden.
internal static class Stepper
{
    public static List<double> Snapshot(List<int> order, Dictionary<int, Cloth> cloths)
    {
        var outp = new List<double>();
        foreach (int s in order)
            foreach (var v in cloths[s].P) { outp.Add(v[0]); outp.Add(v[1]); outp.Add(v[2]); }
        return outp;
    }

    public static List<int> ExecOrder(Cloth c)
    {
        if ((c.ExecMode ?? "native") == "identity" || c.Execution == null || c.Execution.Count == 0)
        {
            var l = new List<int>();
            for (int i = 0; i < c.Sets.Count; i++) l.Add(i);
            return l;
        }
        var o = new List<int>();
        foreach (int i in c.Execution) if (i < c.Sets.Count) o.Add(i);
        return o;
    }

    // a3b's FUN_180819B50 "TtCollide And Solve": a one-sided radial push out of each capsule. Only p
    // is perturbed, so the correction feeds the verlet velocity on the next substep exactly as a hard
    // position projection does.
    public static void Collide(Cloth c)
    {
        if (c.Collidables.Count == 0) return;
        double pr = c.ParticleRadius;
        double tol = c.CollisionTolerance;
        int n = c.N;
        var p = c.P;
        var inv = c.InvMass;
        foreach (var cap in c.Collidables)
        {
            double ax = cap[0], ay = cap[1], az = cap[2];
            double bx = cap[3], by = cap[4], bz = cap[5];
            double ra = cap[6], rb = cap[7];
            double dx = bx - ax, dy = by - ay, dz = bz - az;
            double L2 = dx * dx + dy * dy + dz * dz;
            for (int i = 0; i < n; i++)
            {
                if (inv[i] == 0.0) continue;
                double px = p[i][0], py = p[i][1], pz = p[i][2];
                double wx = px - ax, wy = py - ay, wz = pz - az;
                double t;
                if (L2 > 0.0)
                {
                    t = (wx * dx + wy * dy + wz * dz) / L2;
                    if (t < 0.0) t = 0.0;
                    else if (t > 1.0) t = 1.0;
                }
                else t = 0.0;
                double qx = ax + t * dx, qy = ay + t * dy, qz = az + t * dz;
                double ux = px - qx, uy = py - qy, uz = pz - qz;
                double d = Math.Sqrt(ux * ux + uy * uy + uz * uz);
                double rSurf = ra + (rb - ra) * t + pr;
                double r;
                if (c.CollisionToleranceMode == "gate")
                {
                    if (d >= rSurf - tol) continue;
                    r = rSurf;
                }
                else
                {
                    r = rSurf - tol;
                    if (d >= r) continue;
                }
                if (r <= 0.0) continue;
                if (d > 1e-9)
                {
                    double s = r / d;
                    p[i][0] = qx + ux * s; p[i][1] = qy + uy * s; p[i][2] = qz + uz * s;
                }
                else
                {
                    p[i][0] = qx; p[i][1] = qy + r; p[i][2] = qz;
                }
            }
        }
    }

    public static List<double> Step(List<int> order, Dictionary<int, Cloth> cloths, int frames,
        double frameDt, Dictionary<string, object> opts, out List<double> initial)
    {
        initial = null;
        double slow = opts.ContainsKey("slow_motion") ? (double)opts["slow_motion"] : 1.0;
        double world = opts.ContainsKey("world_stepping") ? (double)opts["world_stepping"] : 1.0;
        double gscale = opts.ContainsKey("gravity_scale") ? (double)opts["gravity_scale"] : 1.0;
        bool pinFixed = !opts.ContainsKey("pin_fixed") || (bool)opts["pin_fixed"];
        bool noCollide = opts.ContainsKey("no_collide") && (bool)opts["no_collide"];
        string dform = opts.ContainsKey("damping_form") ? (string)opts["damping_form"] : "legacy";
        string wmode = opts.ContainsKey("weight_mode") ? (string)opts["weight_mode"] : "native";
        var trace = opts.ContainsKey("frame_trace") ? (List<object>)opts["frame_trace"] : null;
        var reportFrames = opts.ContainsKey("report_frames")
            ? (HashSet<int>)opts["report_frames"] : new HashSet<int>();

        for (int fi = 0; fi < frames; fi++)
        {
            foreach (int s in order)
            {
                var c = cloths[s];
                int nsub = c.Substeps;
                int iters = c.Iterations;
                double dt = frameDt / (nsub * slow);
                double damp;
                if (c.Damping == 0.0) damp = 1.0;
                else if (dform == "per") damp = Math.Pow(Math.Max(0.0, c.Damping), dt);
                else if (dform == "frame") damp = Math.Pow(Math.Max(0.0, 1.0 - c.Damping), dt / (double)nsub);
                else damp = Math.Pow(Math.Max(0.0, 1.0 - c.Damping), dt);
                double gx = c.GravityX * gscale, gy = c.GravityY * gscale, gz = c.GravityZ * gscale;
                if (c.RefSeed == null)
                {
                    c.RefSeed = new List<double[]>();
                    foreach (var v in c.P) c.RefSeed.Add(new double[] { v[0], v[1], v[2] });
                }
                for (int sub = 0; sub < nsub; sub++)
                {
                    var p = c.P;
                    var prev = c.Prev;
                    for (int i = 0; i < c.N; i++)
                    {
                        if (c.InvMass[i] == 0.0) continue;
                        double x = p[i][0], y = p[i][1], z = p[i][2];
                        double vx = x - prev[i][0], vy = y - prev[i][1], vz = z - prev[i][2];
                        p[i][0] = x + vx * damp + gx * dt * dt;
                        p[i][1] = y + vy * damp + gy * dt * dt;
                        p[i][2] = z + vz * damp + gz * dt * dt;
                        prev[i][0] = x; prev[i][1] = y; prev[i][2] = z;
                    }
                    if (pinFixed)
                        foreach (int i in c.Fixed)
                        {
                            p[i][0] = c.PinRef[i][0]; p[i][1] = c.PinRef[i][1]; p[i][2] = c.PinRef[i][2];
                            prev[i][0] = c.PinRef[i][0]; prev[i][1] = c.PinRef[i][1]; prev[i][2] = c.PinRef[i][2];
                        }
                    for (int it = 0; it < iters; it++)
                    {
                        foreach (int si in ExecOrder(c))
                        {
                            if (si >= c.Sets.Count) continue;
                            var st = c.Sets[si];
                            if (st.Solve == null) continue;
                            st.Calls++;
                            double w = Solvers.WeightFor(c.Adapt ? 2 : 1, st.Type, sub, nsub, slow, world);
                            if (wmode == "const" && st.Type != 5 && st.Type != 10) w = 1.0;
                            else if (wmode == "linear" && st.Type != 5 && st.Type != 10) w = (sub + 1) / (double)nsub;
                            if (w <= 0.0) continue;
                            st.Solve(w);
                        }
                        if (!noCollide) Collide(c);
                    }
                    if (pinFixed)
                        foreach (int i in c.Fixed)
                        {
                            p[i][0] = c.PinRef[i][0]; p[i][1] = c.PinRef[i][1]; p[i][2] = c.PinRef[i][2];
                            prev[i][0] = c.PinRef[i][0]; prev[i][1] = c.PinRef[i][1]; prev[i][2] = c.PinRef[i][2];
                        }
                }
            }
            if (fi == 0) initial = Snapshot(order, cloths);
            if (trace != null)
                trace.Add(new Dictionary<string, object> {
                    {"frame", fi + 1}, {"cur", Snapshot(order, cloths)} });
            if (reportFrames.Count > 0 && reportFrames.Contains(fi + 1))
            {
                var parts = new List<string>();
                foreach (int s in order)
                {
                    var c = cloths[s];
                    double mx = 0.0; int nOver = 0;
                    for (int i = 0; i < c.N; i++)
                    {
                        double dd = Math.Sqrt(Math.Pow(c.P[i][0] - c.P0[i][0], 2)
                                            + Math.Pow(c.P[i][1] - c.P0[i][1], 2)
                                            + Math.Pow(c.P[i][2] - c.P0[i][2], 2));
                        mx = Math.Max(mx, dd);
                        if (dd > 0.05) nOver++;
                    }
                    parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "sim#{0} max={1:0.0000e+00} n>0.05={2}", s, mx, nOver));
                }
                Console.Out.Write("    frame {0,-4} {1}\n", fi + 1, string.Join("  |  ", parts));
            }
        }
        return Snapshot(order, cloths);
    }
}
}
