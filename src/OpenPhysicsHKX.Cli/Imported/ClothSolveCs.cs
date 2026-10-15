// Mechanically extracted from the hash-pinned C# delivery. See docs/PROVENANCE.md.
using OpenPhysicsHKX.Internal;
// ClothSolveCs -- the C# port of implementation/src/clothsolve.py (Layer 1 preview solver replacement).
//
// STAGE S0: CLI parsing, the two presets, the post-preset resolution, and the config record.
// No solving yet. The plan and per-stage acceptance criteria are in deliverables/CS-PORT-PLAN.md.
//
// Why a port is possible at all, measured before writing a line (CS-PORT-PLAN.md section 0):
//   * clothsolve.py's f32() is DEAD CODE (defined, never called), struct. appears only inside it, and
//     there is no numpy and no array('f') -- so the solver runs entirely in Python float64 and there is
//     NO float32 quantisation schedule to mirror. C# double is the exact analogue.
//   * math.* is used exactly twice: math.sqrt (19x) and math.pow (4x). Everything else is + - * /.
//     Math.Sqrt/Math.Pow are the same IEEE-754 operations.
//   * the two presets contain only 12 executable lines, surrounded by ~102 lines of archaeology.
//
// FIDELITY RULES carried over from the acceptance work (CS-PORT-PLAN.md section 4) -- each of these
// silently changed the answer when it was wrong in Python:
//   * --prev0: bb needs p0, ds3 needs prev. (Wrong value cost bb 42.5%.)
//   * --damping-form legacy: damp = pow(1 - damping, dt) applied PER SUBSTEP (dt is the substep dt).
//   * --preset bb writes link_form=raw and stretch_sign=onesided only when they are still null.
//   * after the presets, a null link_form resolves to "mass".
//   * the constraintExecution order is taken from the asset; out-of-range indices are dropped silently.
//   * fixed particles are re-pinned at the END of each substep.
//
// INPUT ENCODINGS (must match the Python reader): constraints/summary/compare are UTF-8, while
// collidables and prev_file are read with utf-8-sig, i.e. they must TOLERATE a UTF-8 BOM.
// The --out file is written as UTF-8 with no BOM.
//
// DELIBERATE DIFFERENCE, recorded rather than hidden: the Python config record hard-codes
// "solver": "clothsolve.py". This port writes its own name, and the parity checker lists
// meta.config.solver as the single allowed difference (see CS-PORT-PLAN.md section 2).
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace OpenPhysicsHKX.Cli
{
internal static class ClothSolveCs
{
    // ---------------------------------------------------------------- args

    internal sealed class Args
    {
        public string Case, Constraints, Summary, Out;
        public int Frames = 120;
        public double FrameDt = 1.0 / 60.0;
        public bool DumpConfig;
        public bool DumpParse;
        public bool DumpKernels;
        public string Compare;
        public double GravityScale = 1.0;
        public double SlowMotion = 1.0;
        public double WorldStepping = 1.0;
        public bool PinFixed = true;                 // --no-pin-fixed flips it
        public List<string> DisableSet = new List<string>();
        public List<string> Disable = new List<string>();
        public string ReportFrames = "";
        public string DumpFrames = "";
        public string LinkForm;                      // raw | normalized | mass | null
        public double? LinkScale;
        public bool Verbose;
        public bool FiringTest;
        public bool PrevOffsetTest;
        public string PrevFile;
        public string Start = "p0";
        public string DampingForm = "legacy";
        public string Prev0 = "p0";
        public string Collidables;
        public bool NoCollide;
        public string ExecOrder;
        public string LocalrangeReference;
        public double? ParticleRadius;
        public string ParticleRadiusSource = "scalar";
        public double? CollisionTolerance;
        public string CollisionToleranceMode = "offset";
        public string CollisionToleranceSource = "scalar";
        public double BendScale = 1.0;
        public double? StretchScale;
        public double BendMaxGain = 1.0;
        public string BendNormal = "bisector";
        public string BendScalar = "grad";
        public bool BendNatScale;
        public string BendNormalBySim = "";
        public string BendScaleBySim = "";
        public string BendMaxgainBySim = "";
        public string Preset = "none";
        public bool NoBend;
        public string PinTarget = "p0";
        public string CompressMode = "deadband";
        public List<string> StretchSignBySim = new List<string>();
        public string StretchSign;                   // onesided | twosided | null
        public string StretchForm = "a3c";
        public string WeightMode = "native";
        public string LocalrangeNormal = "native";
        public string SeedKey = "initialPrevParticles";
        public int SubstepsOverride;
        public int ItersOverride;
    }

    static string Need(string[] a, ref int i, string name)
    {
        if (i + 1 >= a.Length) throw new ArgumentException("argument " + name + ": expected one argument");
        return a[++i];
    }

    static bool IsFlag(string tok, string name) { return tok == name; }

    // argparse allows unambiguous PREFIX ABBREVIATIONS; this parser deliberately does NOT. The parity
    // checker always passes full names, and that restriction is recorded in CS-PORT-PLAN.md section 2.
    static readonly HashSet<string> BoolFlags = new HashSet<string>(new[] {
        "--dump-config","--dump-parse","--dump-kernels","--verbose","--firing-test","--prev-offset-test","--no-collide",
        "--bend-nat-scale","--no-bend","--no-pin-fixed"
    });

    static Args Parse(string[] a)
    {
        var r = new Args();
        for (int i = 0; i < a.Length; i++)
        {
            string tok = a[i];
            string inline = null;
            int eq = tok.IndexOf('=');
            if (tok.StartsWith("--") && eq > 0) { inline = tok.Substring(eq + 1); tok = tok.Substring(0, eq); }
            Func<string> val = () => inline ?? Need(a, ref i, tok);

            if (!tok.StartsWith("--")) throw new ArgumentException("unexpected positional argument: " + tok);
            if (inline == null && !BoolFlags.Contains(tok) && tok != "--disable-set" && tok != "--disable" &&
                tok != "--stretch-sign-by-sim")
            {
                // value-taking options are validated by the switch below
            }
            switch (tok)
            {
                case "--case": r.Case = val(); break;
                case "--constraints": r.Constraints = val(); break;
                case "--summary": r.Summary = val(); break;
                case "--out": r.Out = val(); break;
                case "--frames": r.Frames = int.Parse(val()); break;
                case "--frame-dt": r.FrameDt = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--dump-config": r.DumpConfig = true; break;
                case "--dump-parse": r.DumpParse = true; break;
                case "--dump-kernels": r.DumpKernels = true; break;
                case "--compare": r.Compare = val(); break;
                case "--gravity-scale": r.GravityScale = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--slow-motion": r.SlowMotion = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--world-stepping": r.WorldStepping = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--no-pin-fixed": r.PinFixed = false; break;
                case "--disable-set": r.DisableSet.Add(val()); break;
                case "--disable": r.Disable.Add(val()); break;
                case "--report-frames": r.ReportFrames = val(); break;
                case "--dump-frames": r.DumpFrames = val(); break;
                case "--link-form": r.LinkForm = val(); break;
                case "--link-scale": r.LinkScale = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--verbose": r.Verbose = true; break;
                case "--firing-test": r.FiringTest = true; break;
                case "--prev-offset-test": r.PrevOffsetTest = true; break;
                case "--prev-file": r.PrevFile = val(); break;
                case "--start": r.Start = val(); break;
                case "--damping-form": r.DampingForm = val(); break;
                case "--prev0": r.Prev0 = val(); break;
                case "--collidables": r.Collidables = val(); break;
                case "--no-collide": r.NoCollide = true; break;
                case "--exec-order": r.ExecOrder = val(); break;
                case "--localrange-reference": r.LocalrangeReference = val(); break;
                case "--particle-radius": r.ParticleRadius = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--particle-radius-source": r.ParticleRadiusSource = val(); break;
                case "--collision-tolerance": r.CollisionTolerance = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--collision-tolerance-mode": r.CollisionToleranceMode = val(); break;
                case "--collision-tolerance-source": r.CollisionToleranceSource = val(); break;
                case "--bend-scale": r.BendScale = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--stretch-scale": r.StretchScale = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--bend-max-gain": r.BendMaxGain = double.Parse(val(), System.Globalization.CultureInfo.InvariantCulture); break;
                case "--bend-normal": r.BendNormal = val(); break;
                case "--bend-scalar": r.BendScalar = val(); break;
                case "--bend-nat-scale": r.BendNatScale = true; break;
                case "--bend-normal-by-sim": r.BendNormalBySim = val(); break;
                case "--bend-scale-by-sim": r.BendScaleBySim = val(); break;
                case "--bend-maxgain-by-sim": r.BendMaxgainBySim = val(); break;
                case "--preset": r.Preset = val(); break;
                case "--no-bend": r.NoBend = true; break;
                case "--pin-target": r.PinTarget = val(); break;
                case "--compress-mode": r.CompressMode = val(); break;
                case "--stretch-sign-by-sim": r.StretchSignBySim.Add(val()); break;
                case "--stretch-sign": r.StretchSign = val(); break;
                case "--stretch-form": r.StretchForm = val(); break;
                case "--weight-mode": r.WeightMode = val(); break;
                case "--localrange-normal": r.LocalrangeNormal = val(); break;
                case "--seed-key": r.SeedKey = val(); break;
                case "--substeps-override": r.SubstepsOverride = int.Parse(val()); break;
                case "--iters-override": r.ItersOverride = int.Parse(val()); break;
                default: throw new ArgumentException("unknown option: " + tok);
            }
        }
        return r;
    }

    // ------------------------------------------------- the presets (12 lines in Python, verbatim)

    static void ApplyPresets(Args args)
    {
        // a62/a83: the ds3 tuning. a82's retraction of the 143:cross normal came from a scan that
        // omitted --collidables; with the collidable segments restored, a62 reproduces bit-exactly.
        if (args.Preset == "ds3")
        {
            if (string.IsNullOrEmpty(args.BendNormalBySim)) args.BendNormalBySim = "143:cross";
            if (string.IsNullOrEmpty(args.BendMaxgainBySim)) args.BendMaxgainBySim = "143:0.25,111:0.02,5:0.02,43:0.05";
        }
        // a69's guard rule: a preset may only write a flag whose DEFAULT is null/empty, so that an
        // explicitly passed value is never silently overwritten (a68 wrote link_scale behind
        // `== 1.0`, which WAS the default, and made the grid unreadable).
        // a81: this block used to be indented under the inner `if not args.bend_normal_by_sim:` and was
        // therefore DEAD CODE. a112 settled the contents: a3c stands (no stretch_form write), onesided
        // stands, and raw is the link form.
        if (args.Preset == "bb")
        {
            if (args.StretchSign == null) args.StretchSign = "onesided";
            if (args.LinkForm == null) args.LinkForm = "raw";
        }
        // resolved AFTER the presets, so a preset cannot be defeated by the default.
        if (args.LinkForm == null) args.LinkForm = "mass";
    }

    // ------------------------------------------------------------------ config

    static string Sha256File(string path)
    {
        // Python's _sha256_file opens with `if not path: return None` BEFORE the try, and catches OSError.
        // S1's first run crashed here with ArgumentNullException: FileStream path cannot be null -- an
        // unset --prev-file (which is legal, it is optional) reached File.OpenRead. The guard is the fix,
        // and it matters because the config record must carry a null hash for an unset input rather than
        // failing the run. UnauthorizedAccessException is separate from IOException in .NET while Python's
        // OSError covers it, so both are caught to match.
        if (string.IsNullOrEmpty(path)) return null;
        try
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    static Dictionary<string, object> BuildConfig(Args a)
    {
        var flags = new Dictionary<string, object>
        {
            {"case", a.Case}, {"frames", a.Frames}, {"frame_dt", a.FrameDt},
            {"gravity_scale", a.GravityScale}, {"particle_radius", a.ParticleRadius},
            {"localrange_reference", a.LocalrangeReference},
            {"exec_order", a.ExecOrder},
            {"particle_radius_source", a.ParticleRadiusSource},
            {"collision_tolerance", a.CollisionTolerance},
            {"collision_tolerance_source", a.CollisionToleranceSource},
            {"collision_tolerance_mode", a.CollisionToleranceMode},
            {"link_form", a.LinkForm}, {"link_scale", a.LinkScale},
            {"stretch_scale", a.StretchScale}, {"stretch_sign", a.StretchSign},
            {"stretch_sign_by_sim", Sorted(a.StretchSignBySim)},
            {"stretch_form", a.StretchForm}, {"weight_mode", a.WeightMode},
            {"compress_mode", a.CompressMode}, {"preset", a.Preset},
            {"start", a.Start}, {"prev0", a.Prev0}, {"seed_key", a.SeedKey},
            {"pin_target", a.PinTarget}, {"pin_fixed", a.PinFixed},
            {"no_collide", a.NoCollide}, {"substeps_override", a.SubstepsOverride},
            {"iters_override", a.ItersOverride},
            {"bend_normal", a.BendNormal}, {"bend_normal_by_sim", a.BendNormalBySim},
            {"bend_max_gain", a.BendMaxGain}, {"bend_maxgain_by_sim", a.BendMaxgainBySim},
            {"bend_scale", a.BendScale}, {"bend_scale_by_sim", a.BendScaleBySim},
            {"bend_nat_scale", a.BendNatScale},
            // a69: --disable takes class names and is action="append", so `--disable a,b` arrives as ONE
            // string; the Python code re-splits every element on commas and de-duplicates. --disable-set
            // is de-duplicated and sorted. Both are reproduced exactly here.
            {"disable", Sorted(SplitAll(a.Disable))},
            {"disable_set", Sorted(a.DisableSet)},
        };
        var inputs = new Dictionary<string, object>();
        var files = new Dictionary<string, string>
        {
            {"constraints", a.Constraints}, {"summary", a.Summary},
            {"prev_file", a.PrevFile}, {"collidables", a.Collidables}
        };
        foreach (var kv in files)
            inputs[kv.Key] = new Dictionary<string, object> { {"path", kv.Value}, {"sha256", Sha256File(kv.Value)} };
        return new Dictionary<string, object>
        {
            {"solver", "ClothSolveCs.cs"},   // see the header note: the single allowed difference
            {"flags", flags},
            {"inputs", inputs},
        };
    }

    static List<string> Sorted(IEnumerable<string> xs)
    {
        var l = new List<string>(xs);
        l.Sort(StringComparer.Ordinal);
        return l;
    }

    static List<string> SplitAll(IEnumerable<string> xs)
    {
        var seen = new List<string>();
        foreach (var x in xs)
            foreach (var p in (x ?? "").Split(','))
                if (p.Length > 0 && !seen.Contains(p)) seen.Add(p);
        return seen;
    }

    // ------------------------------------------------- Python-compatible stringification
    //
    // S0's parity check caught this on its first run: Python's `%s` renders None as "None" and a bool as
    // "True"/"False", while C#'s composite formatting renders a null string as "" and a bool as
    // "True"/"False" only via ToString(). The config line therefore differed in exactly one token
    // (stretch=a3c/  vs  stretch=a3c/None) -- and note that the STRUCTURAL JSON comparison passed in all
    // three cases, because the JSON carries a real null on both sides. Only the character-level stdout
    // comparison could see it, which is why the plan asks for both.
    // Python's "%.<n>g": n significant digits, lower-case exponent, trailing zeros removed. Used for the
    // [collide] diagnostics, which are compared line by line against the Python solver's own output.
    internal static string PyG(double d, int n)
    {
        string s = d.ToString("G" + n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
        return s.Replace("E", "e");
    }

    static string PyStr(object v)
    {
        if (v == null) return "None";
        if (v is bool) return ((bool)v) ? "True" : "False";
        if (v is double) return PyFloat((double)v);
        if (v is float) return PyFloat((float)v);
        return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
    }

    // Python's repr/str for floats is the SHORTEST decimal that round-trips. .NET Framework's default
    // ToString() is not guaranteed to be that, so "R" is the closest available; where the two differ the
    // difference will show up in a comparison and must be handled explicitly rather than papered over.
    static string PyFloat(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsPositiveInfinity(d)) return "inf";
        if (double.IsNegativeInfinity(d)) return "-inf";
        string s = d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0 && s.IndexOf('e') < 0) s += ".0";
        return s;
    }

    // =====================================================================================
// S1 -- input loading
// =====================================================================================
//
// Faithfulness notes, each earned by a defect recorded in the Python source:
//   * struct_entries must accept the field-name key `fields` OR `keys`, and rows that are lists,
//     objects, or "k:v,k:v" strings. Looking only for `keys` yielded zero rows and made every
//     constraint set unbindable (sets=0).
//   * simToConstraintSets is a LIST of {simObjectIndex, staticConstraintSets:[...]}, not a dict.
//     Treating it as a dict is the same shape error that hit per_sim and skin-ds3.json.
//   * a constraint set whose class has no array member (hclTransitionConstraintSet) is skipped here;
//     it is unmodelled, and that is recorded rather than papered over.
//   * --particle-radius has NO safe implicit default: 0.015 is a ds3-only tuning that leaked into bb
//     and moved the Cape 5.83 mm (a101). An absent value is a fatal error unless the source is "auto".
//   * an out-of-range constraintExecution index is dropped silently; the Python keeps only v >= 0 and
//     then indexes c.sets, so index 4 with 5 sets is in range while -1 is not.

internal static class Json
{
    static readonly JavaScriptSerializer Ser = new JavaScriptSerializer { MaxJsonLength = 256 * 1024 * 1024 };

    // JSON numbers are routed through PyNum rather than through JavaScriptSerializer's own number
    // handling. Measured reason (round 4, tools/.stage/cs-s4-parse-fix.py): the serializer produces a
    // Decimal for literals like -0.21947240829467773, and .NET Framework's decimal->double conversion is
    // not correctly rounded, so 378 of 7498 real literals came out one ULP wrong -- about 5% of the floats
    // in these files, which is exactly the profile the end-to-end comparison showed.
    //
    // The mechanism is a pre-pass that quotes every bare number, so the STRUCTURE still comes from a
    // tested library while the DIGITS come from the exactly-rounded reader. Strings are walked character
    // by character (with escape handling) so digits inside them are never touched.
    static string QuoteNumbers(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length + 64);
        bool inStr = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (inStr)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < s.Length) sb.Append(s[++i]);
                else if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; sb.Append(c); continue; }
            if (c == '-' || c == '+' || (c >= '0' && c <= '9'))
            {
                int j = (c == '-' || c == '+') ? i + 1 : i;
                if (j < s.Length && ((s[j] >= '0' && s[j] <= '9') || s[j] == '.'))
                {
                    // An explicit scanner, because the first version was wrong: it consumed the `e` of an
                    // exponent and then broke on the sign, so `-0.5e-3` became the two numbers "-0.5e" and
                    // "-3", which corrupted the whole document (the standalone test in round 5 shows it).
                    // Grammar:  sign? digits? ( '.' digits? )? ( [eE] sign? digits )?
                    int start = i;
                    int n = s.Length;
                    bool dig = false;
                    if (i < n && (s[i] == '+' || s[i] == '-')) i++;
                    while (i < n && s[i] >= '0' && s[i] <= '9') { i++; dig = true; }
                    if (i < n && s[i] == '.')
                    {
                        i++;
                        while (i < n && s[i] >= '0' && s[i] <= '9') { i++; dig = true; }
                    }
                    if (dig && i < n && (s[i] == 'e' || s[i] == 'E'))
                    {
                        int save = i;
                        i++;
                        if (i < n && (s[i] == '+' || s[i] == '-')) i++;
                        if (i < n && s[i] >= '0' && s[i] <= '9')
                        {
                            while (i < n && s[i] >= '0' && s[i] <= '9') i++;
                        }
                        else i = save;          // no digits: the e was not an exponent after all
                    }
                    sb.Append('"').Append(s, start, i - start).Append('"');
                    i--;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    // collidables and prev_file are read by the Python with utf-8-sig, i.e. a BOM is tolerated there.
    // Decoding bytes ourselves (rather than File.ReadAllText) makes that explicit and identical.
    public static object Load(string path)
    {
        byte[] b = File.ReadAllBytes(path);
        int off = 0;
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) off = 3;
        string s = new UTF8Encoding(false).GetString(b, off, b.Length - off);
        return Ser.DeserializeObject(QuoteNumbers(s));
    }

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


internal static class Loader
{
    public static List<int> Order;
    public static Dictionary<int, Cloth> Cloths;

    public static int Idx(List<string> keys, string name)
    {
        int i = keys.IndexOf(name);
        if (i < 0) throw new ArgumentException("missing column " + name);
        return i;
    }

    public static void LoadCase(Args args, Dictionary<string, object> opts,
        Func<string, List<string>, List<object[]>, Cloth, Func<int, double[]>, Dictionary<string, object>, Action<double>> kernelBuilder)
    {
        var cons = Json.Dict(Json.Load(args.Constraints));
        var summ = Json.Dict(Json.Load(args.Summary));

        Order = new List<int>();
        var counts = new Dictionary<int, int>();
        foreach (var e in Json.List(Json.Get(summ, "simulation_particle_sets")))
        {
            var d = Json.Dict(e);
            int sim = Conv.ToInt(Json.Get(d, "sim"));
            Order.Add(sim);
            counts[sim] = Conv.ToInt(Json.Get(d, "particles"));
        }
        var per = new Dictionary<int, List<double[]>>();
        foreach (var e in Json.List(Json.Get(Json.Dict(Json.Get(summ, "rest_pose")), "per_sim")))
        {
            var d = Json.Dict(e);
            int sim = Conv.ToInt(Json.Get(d, "sim"));
            var pos = new List<double[]>();
            foreach (var v in Json.List(Json.Get(d, "positions")))
            {
                var a = Json.List(v);
                pos.Add(new double[] { Conv.ToDouble(a[0]), Conv.ToDouble(a[1]), Conv.ToDouble(a[2]) });
            }
            per[sim] = pos;
        }
        Console.Out.Write("  [load] %d cloths, %d particles total\n", Order.Count,
            Sum(Order.ConvertAll(s => counts[s])));

        Cloths = new Dictionary<int, Cloth>();
        foreach (int s in Order)
        {
            var c = new Cloth { Sim = s, N = per[s].Count, P0 = per[s] };
            foreach (var v in per[s]) { c.P.Add(new double[] { v[0], v[1], v[2] }); c.Prev.Add(new double[] { v[0], v[1], v[2] }); }
            for (int i = 0; i < c.N; i++) { c.InvMass.Add(1.0); c.Mass.Add(1.0); }
            Cloths[s] = c;
        }

        // ---- hclSimClothData
        var scds = Json.List(Json.Get(cons, "simClothData"));
        for (int i = 0; i < Order.Count && i < scds.Count; i++)
        {
            var sc = Json.Dict(scds[i]);
            var c = Cloths[Order[i]];
            c.Name = Convert.ToString(Conv.Unwrap(Json.Get(sc, "name")));
            List<string> keys; List<object[]> rows;
            Conv.StructEntries(Json.Get(sc, "simulationInfo"), out keys, out rows);
            if (rows.Count > 0)
            {
                bool haveG = false; double[] g = null;
                for (int k = 0; k < keys.Count; k++)
                {
                    if (keys[k] == "gravity") { g = Conv.Vec4(rows[0][k]); haveG = true; }
                    else if (keys[k] == "globalDampingPerSecond") c.Damping = Conv.ToDouble(rows[0][k]);
                }
                if (haveG && g != null) { c.GravityX = g[0]; c.GravityY = g[1]; c.GravityZ = g[2]; }
            }
            Conv.StructEntries(Json.Get(sc, "particleDatas"), out keys, out rows);
            if (rows.Count > 0)
            {
                int mi = Idx(keys, "mass"), ii = Idx(keys, "invMass");
                c.Mass = new List<double>(); c.InvMass = new List<double>(); c.Fixed = new List<int>();
                for (int j = 0; j < rows.Count; j++)
                {
                    c.Mass.Add(Conv.ToDouble(rows[j][mi]));
                    double im = Conv.ToDouble(rows[j][ii]);
                    c.InvMass.Add(im);
                    if (im == 0.0) c.Fixed.Add(j);
                }
                c.PinRef = new Dictionary<int, double[]>();
                foreach (int j in c.Fixed) c.PinRef[j] = new double[] { c.P0[j][0], c.P0[j][1], c.P0[j][2] };
                if (keys.Contains("radius"))
                {
                    int ri = Idx(keys, "radius");
                    if (rows.Count > 0) c.AuthoredParticleRadius = Conv.ToDouble(rows[0][ri]);
                }
            }
            var mr = Json.Dict(Json.Get(sc, "maxParticleRadius"));
            if (mr != null && mr.ContainsKey("value"))
            {
                try { c.MaxParticleRadius = Convert.ToDouble(Conv.Unwrap(Json.Get(mr, "value")), System.Globalization.CultureInfo.InvariantCulture); }
                catch (Exception) { }
            }
            Conv.StructEntries(Json.Get(sc, "simulationInfo"), out keys, out rows);
            if (rows.Count > 0 && keys.Contains("collisionTolerance"))
                c.AuthoredCollisionTolerance = Conv.ToDouble(rows[0][Idx(keys, "collisionTolerance")]);
        }

        // ---- MoveParticles: the authoritative pinning list (must agree with invMass==0)
        foreach (var opO in Json.List(Json.Get(cons, "moveParticlesOperators")))
        {
            var op = Json.Dict(opO);
            List<string> keys; List<object[]> rows;
            Conv.StructEntries(Json.Get(op, "vertexParticlePairs"), out keys, out rows);
            if (rows.Count == 0) continue;
            int pi = Idx(keys, "particleIndex");
            var idxs = new List<int>();
            foreach (var r in rows) idxs.Add(Conv.ToInt(r[pi]));
            idxs.Sort();
            var cand = new List<int>();
            foreach (int s in Order)
                if (idxs.Count == Cloths[s].Fixed.Count && idxs[idxs.Count - 1] < Cloths[s].N) cand.Add(s);
            if (cand.Count == 1)
            {
                bool same = idxs.Count == Cloths[cand[0]].Fixed.Count;
                if (same) for (int i = 0; i < idxs.Count; i++) if (idxs[i] != Cloths[cand[0]].Fixed[i]) { same = false; break; }
                Console.Out.Write("  [pin] {0} MoveParticles pairs={1} match invMass==0: {2}\n",
                    Cloths[cand[0]].Name, idxs.Count, same ? "True" : "False");
                if (!same) Console.Out.Write("        WARNING: pin sets disagree; using invMass==0\n");
            }
        }

        // ---- constraint sets, bound through the asset's own map
        var setobjToSim = new Dictionary<int, int>();
        object mapsO = Json.Get(cons, "simToConstraintSets");
        if (Json.IsList(mapsO))
        {
            foreach (var mO in Json.List(mapsO))
            {
                var m = Json.Dict(mO);
                object simobjO = Json.Get(m, "simObjectIndex");
                if (simobjO == null) continue;
                int simobj = Conv.ToInt(simobjO);
                if (!Cloths.ContainsKey(simobj)) continue;
                foreach (var so in Json.List(Json.Get(m, "staticConstraintSets")))
                    setobjToSim[Conv.ToInt(so)] = simobj;
            }
        }
        Console.Out.Write("  [bind] simToConstraintSets covers {0} constraint-set objects\n", setobjToSim.Count);

        int bound = 0, unbound = 0;
        foreach (var csO in Json.List(Json.Get(cons, "constraintSets")))
        {
            var cs = Json.Dict(csO);
            var members = Json.Dict(Json.Get(cs, "members"));
            object arr = null;
            foreach (var cand in new[] { "localConstraints", "links", "constraints", "perParticle", "memberLinks" })
                if (members != null && members.ContainsKey(cand)) { arr = Json.Get(members, cand); break; }
            if (arr == null) continue;
            List<string> fields; List<object[]> entries;
            Conv.StructEntries(arr, out fields, out entries);
            if (entries.Count == 0 || fields.Count == 0) continue;

            int objIndex = Conv.ToInt(Json.Get(cs, "objectIndex"));
            int target;
            if (!setobjToSim.TryGetValue(objIndex, out target))
            {
                int ic = fields.IndexOf("particleIndex");
                int iA = fields.IndexOf("particleA");
                int probe = ic >= 0 ? ic : iA;
                if (probe < 0) continue;
                int mx = int.MinValue;
                foreach (var e in entries) mx = Math.Max(mx, Conv.ToInt(e[probe]));
                var csCand = new List<int>();
                foreach (int s in Order) if (counts[s] > mx) csCand.Add(s);
                if (csCand.Count == 0) continue;
                int best = csCand[0];
                foreach (int s in csCand) if (counts[s] < counts[best]) best = s;
                target = best;
                unbound++;
            }
            else bound++;

            var c = Cloths[target];
            string kind = Convert.ToString(Json.Get(cs, "class"));
            var dis = (opts.ContainsKey("disable") ? (List<string>)opts["disable"] : new List<string>());
            var dsStr = new HashSet<string>();
            if (opts.ContainsKey("disable_set"))
                foreach (var x in (List<string>)opts["disable_set"]) dsStr.Add(x);
            if (dis.Contains(kind) || dsStr.Contains(Convert.ToString(objIndex)))
            {
                c.Sets.Add(new SetRec { Type = TypeId(kind), Class = kind, N = entries.Count, Disabled = true, ProbeIndex = objIndex });
                continue;
            }
            c.Sets.Add(new SetRec
            {
                Type = TypeId(kind), Class = kind, N = entries.Count, Disabled = false,
                ProbeIndex = objIndex, Fields = fields, Entries = entries
            });
            // ---- S2: build the kernel for this set, on a COPY of opts with the per-set overrides the
            // Python applies (a61's per-cloth bend knobs, then shape_type / apply_normal / reference /
            // normal_bounds). The copy matters: the Python copies opts per SET, so a bend override for
            // one cloth cannot leak into another set.
            var st = c.Sets[c.Sets.Count - 1];
            if (kernelBuilder != null)
            {
                var setopts = new Dictionary<string, object>(opts);
                if (kind.Contains("Bend"))
                {
                    var bn = opts.ContainsKey("bend_normal_by_sim") ? (Dictionary<int, string>)opts["bend_normal_by_sim"] : new Dictionary<int, string>();
                    var bs = opts.ContainsKey("bend_scale_by_sim") ? (Dictionary<int, double>)opts["bend_scale_by_sim"] : new Dictionary<int, double>();
                    var bg = opts.ContainsKey("bend_maxgain_by_sim") ? (Dictionary<int, double>)opts["bend_maxgain_by_sim"] : new Dictionary<int, double>();
                    string v;
                    if (bn.TryGetValue(target, out v)) setopts["bend_normal"] = v;
                    double dv;
                    if (bs.TryGetValue(target, out dv))
                        setopts["bend_scale"] = (opts.ContainsKey("bend_scale") ? (double)opts["bend_scale"] : 1.0) * dv;
                    if (bg.TryGetValue(target, out dv)) setopts["bend_max_gain"] = dv;
                }
                object shapeOv = members != null ? Json.Get(members, "shapeType") : null;
                setopts["shape_type"] = Convert.ToString(Conv.Unwrap(shapeOv)) ?? "SHAPE_SPHERE";
                object anOv = members != null ? Json.Get(members, "applyNormalComponent") : null;
                setopts["apply_normal"] = anOv != null ? Conv.ToBool(anOv) : true;
                setopts["reference"] = ((Func<Cloth, Func<int, int, double[]>>)opts["reference_factory"])(c);
                setopts["normal_bounds"] = opts.ContainsKey("localrange_normal") ? opts["localrange_normal"] : "native";
                var nrm = ((Func<Cloth, Func<int, double[]>>)opts["normals_factory"])(c);
                st.Solve = kernelBuilder(kind, fields, entries, c, nrm, setopts);
                if (st.Solve == null && opts.ContainsKey("verbose") && (bool)opts["verbose"])
                    Console.Out.Write("  [skip] {0,-38} sim#{1,-4} n={2,-4} (not modelled)\n", kind, target, entries.Count);
            }
        }
        Console.Out.Write("  [bind] {0} constraint sets bound via simToConstraintSets, {1} via index-range fallback\n", bound, unbound);

        // ---- simulate operators: substeps, iterations, order, adaptation
        var simOps = Json.List(Json.Get(cons, "simulateOperators"));
        if (simOps.Count > 0)
            Console.Out.Write("  [bind] {0} simulate operators for {1} cloths -> positional binding{2}\n",
                simOps.Count, Order.Count, simOps.Count == Order.Count ? "" : " (COUNT MISMATCH)");
        for (int i = 0; i < simOps.Count; i++)
        {
            var c = Cloths[Order[i % Order.Count]];
            var op = Json.Dict(simOps[i]);
            object ss = Json.Get(op, "subSteps");
            c.Substeps = Math.Max(1, ss == null ? 1 : Conv.ToInt(ss));
            object ni = Json.Get(op, "numberOfSolveIterations");
            c.Iterations = Math.Max(1, ni == null ? 1 : Conv.ToInt(ni));
            c.Adapt = Conv.ToBool(Json.Get(op, "adaptConstraintStiffness"));
            var exec = new List<int>();
            var eng = Json.Dict(Json.Get(op, "constraintExecution"));
            if (eng != null)
            {
                string txt = (Convert.ToString(Json.Get(eng, "text")) ?? "").Trim().Trim('(', ')');
                foreach (var tok in txt.Replace(",", " ").Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    double v;
                    if (!double.TryParse(tok, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) continue;
                    int iv = (int)v;
                    if (iv >= 0) exec.Add(iv);
                }
            }
            c.Execution = exec.Count > 0 ? exec : Range(c.Sets.Count);
        }
        foreach (int s in Order)
        {
            var c = Cloths[s];
            if (c.Execution == null || c.Execution.Count == 0) c.Execution = Range(c.Sets.Count);
        }

        // ---- collidables
        string collPath = opts.ContainsKey("collidables") ? (string)opts["collidables"] : null;
        bool noCollide = opts.ContainsKey("no_collide") && (bool)opts["no_collide"];
        if (collPath != null && !noCollide)
        {
            try
            {
                var cj = Json.Dict(Json.Load(collPath));
                var caps = Json.Dict(Json.Get(cj, "capsules"));
                var perSim = Json.Dict(Json.Get(cj, "perSim"));
                int loaded = 0;
                foreach (int s in Order)
                {
                    object entO = perSim != null && perSim.ContainsKey(Convert.ToString(s)) ? perSim[Convert.ToString(s)] : null;
                    if (entO == null) continue;
                    var c = Cloths[s];
                    foreach (var refO in Json.List(Json.Get(Json.Dict(entO), "collidables")))
                    {
                        object capO = caps != null && caps.ContainsKey(Convert.ToString(refO)) ? caps[Convert.ToString(refO)] : null;
                        if (capO == null) continue;
                        var cap = Json.Dict(capO);
                        var a = Json.List(Json.Get(cap, "a"));
                        var b = Json.List(Json.Get(cap, "b"));
                        c.Collidables.Add(new double[] {
                            Conv.ToDouble(a[0]), Conv.ToDouble(a[1]), Conv.ToDouble(a[2]),
                            Conv.ToDouble(b[0]), Conv.ToDouble(b[1]), Conv.ToDouble(b[2]),
                            Convert.ToDouble(Conv.Unwrap(Json.Get(cap, "ra")), System.Globalization.CultureInfo.InvariantCulture),
                            Convert.ToDouble(Conv.Unwrap(Json.Get(cap, "rb")), System.Globalization.CultureInfo.InvariantCulture) });
                        loaded++;
                    }
                }
                double? pr = opts.ContainsKey("particle_radius") ? (double?)opts["particle_radius"] : null;
                string prSrc = opts.ContainsKey("particle_radius_source") ? (string)opts["particle_radius_source"] : "scalar";
                if (pr == null && prSrc != "auto")
                    throw new ApplicationException(
                        "FATAL: --particle-radius was not given and --particle-radius-source is not 'auto'. " +
                        "There is NO safe implicit default: 0.015 is a ds3-only tuning and it leaked into bb (a101).");
                if (pr == null) pr = 0.015;
                double? ctol = opts.ContainsKey("collision_tolerance") ? (double?)opts["collision_tolerance"] : null;
                if (ctol == null) ctol = 0.0;
                string cmode = opts.ContainsKey("collision_tolerance_mode") ? (string)opts["collision_tolerance_mode"] : "offset";
                foreach (int s in Order) Cloths[s].CollisionToleranceMode = cmode;
                string csrc = opts.ContainsKey("collision_tolerance_source") ? (string)opts["collision_tolerance_source"] : "scalar";
                if (csrc == "auto")
                {
                    foreach (int s in Order)
                    {
                        var c = Cloths[s];
                        c.CollisionTolerance = c.AuthoredCollisionTolerance.HasValue
                            ? c.AuthoredCollisionTolerance.Value : ctol.Value;
                    }
                    var parts = new List<string>();
                    foreach (int s in Order)
                        if (Cloths[s].Collidables.Count > 0)
                            parts.Add(Cloths[s].Name + "=" + PyG(Cloths[s].CollisionTolerance, 6));
                    Console.Out.Write("  [collide] tolerance AUTO: {0}\n", string.Join(", ", parts));
                }
                else
                {
                    foreach (int s in Order) Cloths[s].CollisionTolerance = ctol.Value;
                    if (ctol.Value != 0.0)
                        Console.Out.Write("  [collide] tolerance {0}\n", PyG(ctol.Value, 6));
                }
                var withColl = new List<int>();
                foreach (int s in Order) if (Cloths[s].Collidables.Count > 0) withColl.Add(s);
                var missing = new List<string>();
                if (prSrc == "auto")
                {
                    foreach (int s in Order)
                    {
                        var c = Cloths[s];
                        if (!c.AuthoredParticleRadius.HasValue) { c.ParticleRadius = pr.Value; missing.Add(c.Name); }
                        else c.ParticleRadius = c.AuthoredParticleRadius.Value;
                    }
                }
                else
                {
                    foreach (int s in Order) Cloths[s].ParticleRadius = pr.Value;
                }
                // Python's %g / %.6g: six significant digits, shortest of %e/%f, and a LOWERCASE exponent.
                // .NET's G6 matches the digit rule but upper-cases the exponent, so the E is folded back.
                // This matters because the [collide] lines are compared line by line: 1E-05 vs 1e-05 would
                // read as a behavioural difference when it is only a formatting one.
                string tail;
                if (prSrc == "auto")
                {
                    var shown = new List<string>();
                    foreach (int s in Order)
                        if (Cloths[s].Collidables.Count > 0)
                            shown.Add(Cloths[s].Name + "=" + PyG(Cloths[s].ParticleRadius, 6));
                    tail = "particle radius AUTO: " + string.Join(", ", shown)
                         + (missing.Count > 0 ? "; fell back to " + PyG(pr.Value, 6) + " for " + string.Join(", ", missing) : "");
                }
                else
                {
                    tail = "particle radius " + PyG(pr.Value, 6);
                }
                Console.Out.Write("  [collide] {0} capsules over {1} cloths from {2} ({3})\n",
                    loaded, withColl.Count, collPath, tail);
            }
            catch (Exception exc)
            {
                Console.Out.Write("  [collide] FAILED to load {0}: {1}\n", collPath, exc.Message);
            }
        }
        else
        {
            Console.Out.Write("  [collide] disabled (no --collidables or --no-collide)\n");
        }
    }

    static List<int> Range(int n)
    {
        var l = new List<int>();
        for (int i = 0; i < n; i++) l.Add(i);
        return l;
    }

    static int Sum(List<int> xs) { int t = 0; foreach (int x in xs) t += x; return t; }

    static readonly Dictionary<string, int> TypeIds = new Dictionary<string, int>
    {
        {"hclStandardLinkConstraintSet", 1}, {"hclStretchLinkConstraintSet", 2}, {"hclBendLinkConstraintSet", 3},
        {"hclBendStiffnessConstraintSet", 4}, {"hclLocalRangeConstraintSet", 5}, {"hclVolumeConstraintSet", 6},
        {"hclTransitionConstraintSet", 8}, {"hclBonePlanesConstraintSet", 10},
        {"hclStandardLinkConstraintSetMx", 13}, {"hclBendLinkConstraintSetMx", 14},
        {"hclStretchLinkConstraintSetMx", 15}, {"hclBendStiffnessConstraintSetMx", 16},
        {"hclVolumeConstraintSetMx", 17}, {"hclAntiPinchConstraintSet", 19},
        {"hclCompressibleLinkConstraintSet", 20}, {"hclCompressibleLinkConstraintSetMx", 21},
    };

    public static int TypeId(string kind)
    {
        int v;
        return (kind != null && TypeIds.TryGetValue(kind, out v)) ? v : 0;
    }
}


// -----------------------------------------------------------------------------------
// main

    internal static int Run(string[] argv)
    {
        Args args;
        try { args = Parse(argv); }
        catch (Exception e) { Console.Error.WriteLine("usage error: " + e.Message); return 2; }

        if (args.Case == null || args.Constraints == null || args.Summary == null || args.Out == null)
        {
            Console.Error.WriteLine("usage: ClothSolveCs --case C --constraints F --summary F --out F [...]");
            return 2;
        }

        ApplyPresets(args);
        var config = BuildConfig(args);

        if (args.DumpConfig)
        {
            // Python prints json.dumps(CONFIG, indent=1, sort_keys=True) and then CONTINUES; it does not
            // exit. The port reproduces the same control flow. Byte formatting differs (the checker
            // compares parsed structures, see CS-PORT-PLAN.md section 2), so JavaScriptSerializer -- the
            // library the other C# tools in this tree already use -- is good enough here.
            var ser = new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 };
            Console.Out.Write(ser.Serialize(config));
            Console.Out.Write("\n");
        }
        else
        {
            // must be CHARACTER-IDENTICAL to the Python line, hence PyStr (which renders a null
            // stretch_sign as the literal "None" the way Python's %s does) and InvariantCulture.
            Console.Out.Write("  config: preset={0} collidables={1} link_form={2} stretch={3}/{4} weight_mode={5} damping={6}\n",
                args.Preset,
                string.IsNullOrEmpty(args.Collidables) ? "-" : Path.GetFileName(args.Collidables),
                PyStr(args.LinkForm), PyStr(args.StretchForm), PyStr(args.StretchSign),
                PyStr(args.WeightMode), PyStr(args.DampingForm));
        }

        Console.Error.WriteLine("S1: input loading only; the solver stages (S2-S4) are not implemented yet.");

        // ---- S1: load the case, then print a bit-exact digest for comparison with the Python loader.
        //
        // Floats are printed as the hex of their IEEE-754 bit pattern rather than as decimals. Two reasons:
        // (1) it removes every float->string formatting difference between %g/repr and "R"/ToString from the
        // comparison, and (2) equality of the hex IS equality of the double, so the digest proves bit
        // equality instead of merely suggesting it. Sums are printed the same way, which also exposes any
        // difference in accumulation order.
        if (args.DumpParse)
        {
            var opts = new Dictionary<string, object>();
            opts["disable"] = SplitAll(args.Disable);
            opts["disable_set"] = args.DisableSet;
            opts["collidables"] = args.Collidables;
            opts["no_collide"] = args.NoCollide;
            opts["particle_radius"] = args.ParticleRadius;
            opts["particle_radius_source"] = args.ParticleRadiusSource;
            opts["collision_tolerance"] = args.CollisionTolerance;
            opts["collision_tolerance_mode"] = args.CollisionToleranceMode;
            opts["collision_tolerance_source"] = args.CollisionToleranceSource;
            try
            {
                // kernelBuilder = null, matching the Python S1 driver (tools/remote/830-cs-s1-digest.py),
                // which patches build_solver to None: S1 is about what was READ, so no kernel is built.
                //
                // Passing Solvers.BuildSolver here was a latent regression of mine. The S2 and solve paths
                // add reference_factory and normals_factory to their own opts; with a non-null
                // kernelBuilder, LoadCase enters the kernel-building branch and looks those up in THIS
                // opts, which does not have them, so it died with "The given key was not present in the
                // dictionary" at the reference_factory lookup. S1 had not been re-run since those factories
                // were introduced, which is why it went unseen for several rounds.
                Loader.LoadCase(args, opts, null);
            }
            catch (Exception e)
            {
                Console.Out.Write("  [digest] FATAL {0}\n{1}\n", e.Message, e.StackTrace);
                return 1;
            }
            Console.Out.Write("\n  ===== S1 digest (doubles as IEEE-754 hex, little-endian byte order) =====\n");
            foreach (int s in Loader.Order)
            {
                var c = Loader.Cloths[s];
                double p0s = 0, ms = 0;
                foreach (var v in c.P0) p0s += v[0] + v[1] + v[2];
                foreach (var m in c.Mass) ms += m;
                var ex = new List<string>();
                foreach (int e in c.Execution) ex.Add(Convert.ToString(e));
                Console.Out.Write(
                    "  cloth sim={0} name={1} n={2} gravity=({3} {4} {5}) damping={6} fixed={7} mass_sum={8} p0_sum={9}\n",
                    s, c.Name, c.N, Hx(c.GravityX), Hx(c.GravityY), Hx(c.GravityZ), Hx(c.Damping),
                    c.Fixed.Count, Hx(ms), Hx(p0s));
                Console.Out.Write(
                    "        authored_radius={0} max_radius={1} authored_ctol={2} ctot={3} mode={4} substeps={5} iters={6} adapt={7} exec=[{8}] collidables={9} particle_radius={10}\n",
                    c.AuthoredParticleRadius.HasValue ? Hx(c.AuthoredParticleRadius.Value) : "None",
                    c.MaxParticleRadius.HasValue ? Hx(c.MaxParticleRadius.Value) : "None",
                    c.AuthoredCollisionTolerance.HasValue ? Hx(c.AuthoredCollisionTolerance.Value) : "None",
                    Hx(c.CollisionTolerance), c.CollisionToleranceMode, c.Substeps, c.Iterations,
                    c.Adapt ? "True" : "False", string.Join(",", ex), c.Collidables.Count, Hx(c.ParticleRadius));
                for (int i = 0; i < c.Collidables.Count; i++)
                {
                    var k = c.Collidables[i];
                    Console.Out.Write("        cap[{0}] a=({1} {2} {3}) b=({4} {5} {6}) ra={7} rb={8}\n",
                        i, Hx(k[0]), Hx(k[1]), Hx(k[2]), Hx(k[3]), Hx(k[4]), Hx(k[5]), Hx(k[6]), Hx(k[7]));
                }
            }
            foreach (int s in Loader.Order)
                foreach (var st in Loader.Cloths[s].Sets)
                    Console.Out.Write("  set sim={0} idx={1} class={2} type={3} n={4} disabled={5}\n",
                        s, st.ProbeIndex, st.Class, st.Type, st.N, st.Disabled ? "True" : "False");
        }

        // ---- S2: weight_for, the vertex normals, and a SINGLE application of each set's kernel.
        //
        // The kernel test is deliberately one pass on a deterministic synthetic state rather than a full
        // solve: it isolates the kernels from the integrator and the substep/iteration loop (which is
        // S3's subject), so a mismatch here can only mean the kernel translation is wrong.
        if (args.DumpKernels)
        {
            var pnAll = Json.List(Json.Get(Json.Dict(Json.Load(args.Constraints)), "pnArrays"));

            // --localrange-reference defaults to "authored"; the other three modes exist for the a99/a109
            // leash experiments, and reproducing them here keeps the port usable for those.
            string lrr = args.LocalrangeReference ?? "authored";
            Func<Cloth, Func<int, int, double[]>> refFactory;
            if (lrr == "authored") refFactory = c => (i, rv) => c.P0[rv];
            else if (lrr == "seed") refFactory = c => (i, rv) => (c.RefSeed ?? c.P0)[rv];
            else if (lrr == "current") refFactory = c => (i, rv) => c.P[rv];
            else if (lrr == "prev") refFactory = c => (i, rv) => c.Prev[rv];
            else { Console.Error.WriteLine("bad --localrange-reference " + lrr); return 2; }

            var nrmCache = new Dictionary<int, List<double[]>>();
            Func<Cloth, Func<int, double[]>> nrmFactory = c =>
            {
                List<double[]> tbl = null;
                if (pnAll != null && pnAll.Count > 0 && (args.LocalrangeNormal ?? "native") != "ignore")
                {
                    if (!nrmCache.ContainsKey(c.Sim))
                        nrmCache[c.Sim] = Solvers.BuildVertexNormals(pnAll, c.Sim, c.P.Count);
                    tbl = nrmCache[c.Sim];
                }
                var tt = tbl;
                if (tt == null) return i => null;
                return i => (i >= 0 && i < tt.Count) ? tt[i] : null;
            };

            var opts2 = new Dictionary<string, object>();
            opts2["disable"] = SplitAll(args.Disable);
            opts2["disable_set"] = args.DisableSet;
            opts2["collidables"] = args.Collidables;
            opts2["no_collide"] = args.NoCollide;
            opts2["particle_radius"] = args.ParticleRadius;
            opts2["particle_radius_source"] = args.ParticleRadiusSource;
            opts2["collision_tolerance"] = args.CollisionTolerance;
            opts2["collision_tolerance_mode"] = args.CollisionToleranceMode;
            opts2["collision_tolerance_source"] = args.CollisionToleranceSource;
            opts2["link_scale"] = args.LinkScale;
            opts2["stretch_scale"] = args.StretchScale;
            opts2["stretch_form"] = args.StretchForm;
            opts2["stretch_sign"] = args.StretchSign;
            opts2["stretch_sign_by_sim"] = SimMap(args.StretchSignBySim, s => s);
            opts2["link_form"] = args.LinkForm;
            opts2["bend_scalar"] = args.BendScalar;
            opts2["bend_nat_scale"] = args.BendNatScale;
            opts2["bend_normal"] = args.BendNormal;
            opts2["bend_scale"] = args.BendScale;
            opts2["bend_max_gain"] = args.BendMaxGain;
            opts2["bend_normal_by_sim"] = _simMapStr(args.BendNormalBySim);
            opts2["bend_scale_by_sim"] = _simMapDbl(args.BendScaleBySim);
            opts2["bend_maxgain_by_sim"] = _simMapDbl(args.BendMaxgainBySim);
            opts2["compress_mode"] = args.CompressMode;
            opts2["localrange_normal"] = args.LocalrangeNormal;
            opts2["reference_factory"] = refFactory;
            opts2["normals_factory"] = nrmFactory;
            opts2["verbose"] = args.Verbose;
            opts2["exec_mode"] = args.ExecOrder ?? "native";
            try { Loader.LoadCase(args, opts2, Solvers.BuildSolver); }
            catch (Exception e) { Console.Out.Write("  [digest] FATAL {0}\n{1}\n", e.Message, e.StackTrace); return 1; }

            Console.Out.Write("\n  ===== S2 digest (doubles as IEEE-754 hex) =====\n");
            // weight_for across the branches, including the pow() branch that only bb reaches
            var grid = new int[][] { new int[] { 1, 1 }, new int[] { 2, 1 }, new int[] { 2, 2 }, new int[] { 2, 5 },
                                     new int[] { 2, 8 }, new int[] { 2, 10 } };
            foreach (var g in grid)
                foreach (int sub in new int[] { 0, 1, 3 })
                    foreach (double wsc in new double[] { 1.0, 0.5 })
                        Console.Out.Write("  weight modelType={0} type={1} sub={2} of 4 slow=1 world={3} -> {4}\n",
                            g[0], g[1], sub, PyStr(wsc), Hx(Solvers.WeightFor(g[0], g[1], sub, 4, 1.0, wsc)));

            foreach (int s in Loader.Order)
            {
                var c = Loader.Cloths[s];
                var nf = nrmFactory(c);
                double nsum = 0, nw = 0;
                int ncnt = 0;
                for (int i = 0; i < c.N; i++)
                {
                    var nv = nf(i);
                    if (nv == null) continue;
                    ncnt++;
                    nsum += nv[0] + nv[1] + nv[2];
                    nw += nv[0] * (1 + i * 0.5) + nv[1] * (1 + i * 0.75) + nv[2] * (1 + i * 0.25);
                }
                Console.Out.Write("  normals sim={0} count={1} sum={2} wsum={3}\n", s, ncnt, Hx(nsum), Hx(nw));
            }

            foreach (int s in Loader.Order)
            {
                var c = Loader.Cloths[s];
                foreach (var st in c.Sets)
                {
                    if (st.Disabled || st.Solve == null)
                    {
                        Console.Out.Write("  kernel sim={0} idx={1} class={2} n={3} NO-KERNEL\n", s, st.ProbeIndex, st.Class, st.N);
                        continue;
                    }
                    // deterministic synthetic state: p0 plus a fixed pattern, identical in both languages
                    for (int i = 0; i < c.N; i++)
                        for (int k = 0; k < 3; k++)
                            c.P[i][k] = c.P0[i][k] + (((i * 3 + k) % 7) - 3) * 1e-3;
                    double w = Solvers.WeightFor(2, st.Type, 0, c.Substeps, 1.0, 1.0);
                    st.Solve(w);
                    double psum = 0, pw = 0;
                    for (int i = 0; i < c.N; i++)
                        for (int k = 0; k < 3; k++)
                        {
                            psum += c.P[i][k];
                            pw += c.P[i][k] * (1 + i * 0.5 + k * 0.25);
                        }
                    Console.Out.Write("  kernel sim={0} idx={1} class={2} n={3} w={4} psum={5} pwsum={6}\n",
                        s, st.ProbeIndex, st.Class, st.N, Hx(w), Hx(psum), Hx(pw));
                }
            }
        }
        // ------------------------------------------------------------------- solve
        if (!args.DumpParse && !args.DumpKernels && !args.DumpConfig)
        {
            // the full path: build the same opts/loader as S2, seed the state, solve, and write the dump
            var pnAll = Json.List(Json.Get(Json.Dict(Json.Load(args.Constraints)), "pnArrays"));
            string lrr = args.LocalrangeReference ?? "authored";
            Func<Cloth, Func<int, int, double[]>> refFactory;
            if (lrr == "authored") refFactory = c => (i, rv) => c.P0[rv];
            else if (lrr == "seed") refFactory = c => (i, rv) => (c.RefSeed ?? c.P0)[rv];
            else if (lrr == "current") refFactory = c => (i, rv) => c.P[rv];
            else if (lrr == "prev") refFactory = c => (i, rv) => c.Prev[rv];
            else { Console.Error.WriteLine("bad --localrange-reference " + lrr); return 2; }
            var nrmCache = new Dictionary<int, List<double[]>>();
            Func<Cloth, Func<int, double[]>> nrmFactory = c =>
            {
                List<double[]> tbl = null;
                if (pnAll != null && pnAll.Count > 0 && (args.LocalrangeNormal ?? "native") != "ignore")
                {
                    if (!nrmCache.ContainsKey(c.Sim))
                        nrmCache[c.Sim] = Solvers.BuildVertexNormals(pnAll, c.Sim, c.P.Count);
                    tbl = nrmCache[c.Sim];
                }
                var tt = tbl;
                if (tt == null) return i => null;
                return i => (i >= 0 && i < tt.Count) ? tt[i] : null;
            };
            var o = new Dictionary<string, object>();
            o["disable"] = SplitAll(args.Disable);
            o["disable_set"] = args.DisableSet;
            o["collidables"] = args.Collidables;
            o["no_collide"] = args.NoCollide;
            o["particle_radius"] = args.ParticleRadius;
            o["particle_radius_source"] = args.ParticleRadiusSource;
            o["collision_tolerance"] = args.CollisionTolerance;
            o["collision_tolerance_mode"] = args.CollisionToleranceMode;
            o["collision_tolerance_source"] = args.CollisionToleranceSource;
            o["link_scale"] = args.LinkScale;
            o["stretch_scale"] = args.StretchScale;
            o["stretch_form"] = args.StretchForm;
            o["stretch_sign"] = args.StretchSign;
            o["stretch_sign_by_sim"] = _simMapStr(string.Join(",", args.StretchSignBySim));
            o["link_form"] = args.LinkForm;
            o["bend_scalar"] = args.BendScalar;
            o["bend_nat_scale"] = args.BendNatScale;
            o["bend_normal"] = args.BendNormal;
            o["bend_scale"] = args.BendScale;
            o["bend_max_gain"] = args.BendMaxGain;
            o["bend_normal_by_sim"] = _simMapStr(args.BendNormalBySim);
            o["bend_scale_by_sim"] = _simMapDbl(args.BendScaleBySim);
            o["bend_maxgain_by_sim"] = _simMapDbl(args.BendMaxgainBySim);
            o["compress_mode"] = args.CompressMode;
            o["localrange_normal"] = args.LocalrangeNormal;
            o["reference_factory"] = refFactory;
            o["normals_factory"] = nrmFactory;
            o["verbose"] = args.Verbose;
            o["exec_mode"] = args.ExecOrder ?? "native";
            try { Loader.LoadCase(args, o, Solvers.BuildSolver); }
            catch (Exception e) { Console.Error.WriteLine("load failed: " + e.Message); return 1; }

            // a104: --exec-order native is the shipped behaviour; identity runs every loaded set
            foreach (int s in Loader.Order) Loader.Cloths[s].ExecMode = o["exec_mode"] as string;

            if (args.SubstepsOverride > 0 || args.ItersOverride > 0)
            {
                foreach (int s in Loader.Order)
                {
                    if (args.SubstepsOverride > 0) Loader.Cloths[s].Substeps = args.SubstepsOverride;
                    if (args.ItersOverride > 0) Loader.Cloths[s].Iterations = args.ItersOverride;
                }
                Console.Out.Write("  OVERRIDE substeps={0} iters={1}\n",
                    args.SubstepsOverride > 0 ? args.SubstepsOverride.ToString() : "keep",
                    args.ItersOverride > 0 ? args.ItersOverride.ToString() : "keep");
            }
            foreach (int s in Loader.Order)
            {
                var c = Loader.Cloths[s];
                Console.Out.Write("  {0,-22} n={1,-4} fixed={2,-4} gravity=({3} {4} {5}) damping={6} substeps={7} iters={8} adapt={9} sets={10} order=[{11}]\n",
                    c.Name, c.N, c.Fixed.Count, PyG(c.GravityX, 3), PyG(c.GravityY, 3), PyG(c.GravityZ, 3),
                    PyG(c.Damping, 4), c.Substeps, c.Iterations, c.Adapt ? "True" : "False", c.Sets.Count,
                    string.Join(", ", Stepper.ExecOrder(c).ConvertAll(x => x.ToString()).ToArray()));
                for (int j = 0; j < c.Sets.Count; j++)
                {
                    var st = c.Sets[j];
                    Console.Out.Write("      set[{0}] probeIndex={1,-4} XML=#{2,-4} {3,-34} n={4,-5} solve={5}{6}\n",
                        j, st.ProbeIndex, st.ProbeIndex + 1, st.Class, st.N, st.Solve != null ? "yes" : "NO",
                        st.Disabled ? "  DISABLED" : "");
                }
            }

            // ---- seed the state from the prev-file. a118: --prev0 decides what `prev` starts as, and the
            // two games need DIFFERENT values: the golden's initialParticles is the state after one native
            // step, so bb must start with prev = p0 (the seed carries an implied initial velocity), while
            // ds3 needs prev = the seed itself (zero initial velocity). Forcing prev for bb cost 42.5%.
            if (args.PrevFile != null)
            {
                var pj = Json.Dict(Json.Load(args.PrevFile));
                var pv = Json.List(Json.Get(pj, args.SeedKey));
                if (pv.Count == 0)
                    throw new ApplicationException("--prev-file has no " + args.SeedKey);
                int off = 0;
                foreach (int s in Loader.Order)
                {
                    var c = Loader.Cloths[s];
                    for (int i = 0; i < c.N; i++)
                    {
                        int q = (off + i) * 3;
                        double v0 = Conv.ToDouble(pv[q]), v1 = Conv.ToDouble(pv[q + 1]), v2 = Conv.ToDouble(pv[q + 2]);
                        if (args.Start == "prev") { c.P[i][0] = v0; c.P[i][1] = v1; c.P[i][2] = v2; }
                        if (args.Prev0 == "prev") { c.Prev[i][0] = v0; c.Prev[i][1] = v1; c.Prev[i][2] = v2; }
                        else if (args.Prev0 == "zero") { c.Prev[i][0] = 0.0; c.Prev[i][1] = 0.0; c.Prev[i][2] = 0.0; }
                    }
                    off += c.N;
                }
                Console.Out.Write("  seeded from {0} [{1}] : start={2} prev0={3}\n",
                    Path.GetFileName(args.PrevFile), args.SeedKey, args.Start, args.Prev0);
            }
            if (args.PinTarget == "seed")
            {
                int tot = 0;
                foreach (int s in Loader.Order)
                {
                    var c = Loader.Cloths[s];
                    c.PinRef = new Dictionary<int, double[]>();
                    foreach (int i in c.Fixed) c.PinRef[i] = new double[] { c.P[i][0], c.P[i][1], c.P[i][2] };
                    tot += c.Fixed.Count;
                }
                Console.Out.Write("  pin target re-captured from the seed ({0} fixed particles per cloth)\n", tot);
            }

            var opts = new Dictionary<string, object>();
            opts["slow_motion"] = args.SlowMotion;
            opts["world_stepping"] = args.WorldStepping;
            opts["gravity_scale"] = args.GravityScale;
            opts["pin_fixed"] = args.PinFixed;
            opts["no_collide"] = args.NoCollide;
            opts["damping_form"] = args.DampingForm;
            opts["weight_mode"] = args.WeightMode;
            var rf = new HashSet<int>();
            foreach (var x in (args.ReportFrames ?? "").Split(','))
            {
                int v;
                if (x.Trim().Length > 0 && int.TryParse(x.Trim(), out v)) rf.Add(v);
            }
            opts["report_frames"] = rf;
            opts["frame_trace"] = args.DumpFrames != null && args.DumpFrames.Length > 0
                ? new List<object>() : null;

            List<double> initial;
            var final = Stepper.Step(Loader.Order, Loader.Cloths, args.Frames, args.FrameDt, opts, out initial);
            if (initial == null) initial = Stepper.Snapshot(Loader.Order, Loader.Cloths);

            var meta = new Dictionary<string, object>
            {
                {"case", args.Case}, {"frames", args.Frames}, {"frameDt", args.FrameDt},
                {"solver", "ClothSolveCs.cs"}, {"gravityScale", args.GravityScale},
                {"pinFixed", args.PinFixed}, {"substeps", args.SubstepsOverride},
                {"iters", args.ItersOverride}, {"pinTarget", args.PinTarget},
                {"linkForm", args.LinkForm}, {"compressMode", args.CompressMode},
                {"config", BuildConfig(args)},
            };
            var modelled = new Dictionary<string, object>();
            foreach (int s in Loader.Order)
            {
                var classes = new List<string>();
                foreach (var st in Loader.Cloths[s].Sets) if (st.Solve != null) classes.Add(st.Class);
                modelled[Convert.ToString(s)] = classes;
            }
            meta["modelledSets"] = modelled;

            var dump = new Dictionary<string, object>
            {
                {"initialParticles", initial}, {"finalParticles", final},
                {"frameTrace", (opts["frame_trace"] as List<object>) ?? new List<object>()},
                {"meta", meta},
            };
            var ser = new JavaScriptSerializer { MaxJsonLength = 512 * 1024 * 1024 };
            File.WriteAllText(args.Out, ser.Serialize(dump), new UTF8Encoding(false));
            Console.Out.Write("  wrote {0} ({1} floats per array)\n", args.Out, final.Count);
            return 0;
        }

        return 0;
    }

    static Dictionary<int, string> _simMapStr(string text)
    {
        var o = new Dictionary<int, string>();
        foreach (var part in (text ?? "").Split(','))
        {
            var p = part.Trim();
            if (p.Length == 0) continue;
            int c = p.IndexOf(':');
            if (c < 0) throw new ArgumentException("bad --*-by-sim entry " + p);
            o[int.Parse(p.Substring(0, c).Trim())] = p.Substring(c + 1).Trim();
        }
        return o;
    }

    static Dictionary<int, double> _simMapDbl(string text)
    {
        var o = new Dictionary<int, double>();
        foreach (var part in (text ?? "").Split(','))
        {
            var p = part.Trim();
            if (p.Length == 0) continue;
            int c = p.IndexOf(':');
            if (c < 0) throw new ArgumentException("bad --*-by-sim entry " + p);
            o[int.Parse(p.Substring(0, c).Trim())] =
                double.Parse(p.Substring(c + 1).Trim(), System.Globalization.CultureInfo.InvariantCulture);
        }
        return o;
    }

    static Dictionary<int, string> SimMap(List<string> xs, Func<string, string> conv)
    {
        var o = new Dictionary<int, string>();
        foreach (var part in xs)
            foreach (var p in (part ?? "").Split(','))
            {
                var q = p.Trim();
                if (q.Length == 0) continue;
                int c = q.IndexOf(':');
                if (c < 0) throw new ArgumentException("bad --*-by-sim entry " + q);
                o[int.Parse(q.Substring(0, c).Trim())] = conv(q.Substring(c + 1).Trim());
            }
        return o;
    }

    static string Hx(double d)
    {
        return BitConverter.DoubleToInt64Bits(d).ToString("x16");
    }
}
}
