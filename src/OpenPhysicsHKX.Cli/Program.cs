using System;

namespace OpenPhysicsHKX.Cli
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 1 && (args[0] == "--help" || args[0] == "-h"))
            {
                Console.WriteLine("OpenPhysicsHKX compatibility CLI (research JSON input, not binary HKX)");
                Console.WriteLine("Required: --case <bb|ds3> --constraints <json> --summary <json> --out <new-json>");
                Console.WriteLine("Comparison: --preset bb --prev0 p0 (DS3: --preset ds3 --prev0 prev)");
                Console.WriteLine("  --start prev --damping-form legacy --gravity-scale 0 --particle-radius 0");
                Console.WriteLine("  --frames 119 --seed-key initialParticles --prev-file <json> --collidables <json>");
                Console.WriteLine("Diagnostics: --dump-config | --dump-parse | --dump-kernels; trace: --dump-frames all");
                Console.WriteLine("Legacy behavior and output-overwrite semantics retained. See docs/API.md.");
                return 0;
            }
            try { return ClothSolveCs.Run(args); }
            catch (Exception e) { Console.Error.WriteLine("solve failed: " + e.Message); return 1; }
        }
    }
}
