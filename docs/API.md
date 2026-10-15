# API notes

`OpenPhysicsHKX.Core.dll` contains no GUI, serializer, game format library, or
vendor runtime dependency. It operates on explicit numeric data, in the caller's
coordinate system. Version 0.1 is experimental; the API is not yet stable.

```csharp
using OpenPhysicsHKX;

var cloth = new ClothDefinition {
    Id = 0,
    RestPositions = new[] {
        new[] { 0.0, 1.0, 0.0 }, new[] { 1.0, 1.0, 0.0 }
    },
    Masses = new[] { 0.0, 1.0 },
    InverseMasses = new[] { 0.0, 1.0 },
    Constraints = new[] {
        new ConstraintDefinition {
            ClassName = "hclStandardLinkConstraintSet",
            Fields = new[] { "particleA", "particleB", "restLength", "stiffness" },
            Records = new[] { new[] { 0.0, 1.0, 1.0, 0.3 } }
        }
    }
};
var session = new ClothSession(new[] { cloth }, new SolverOptions());
session.Step(); // one 1/60-second frame by default
double[][] xyz = session.GetPositions()[0];
session.Reset();
```

- `Positions` and `PreviousPositions` independently default to `RestPositions`.
  To seed zero velocity away from rest, supply the same values to both explicitly.
- Zero inverse mass marks fixed particles; pin targets are their rest positions.
- Mass and inverse mass are explicit independent inputs; neither is inferred or normalized.
- Input arrays, options and returned positions are copied. Sessions own separate states.
- `Step(n)` advances persistent state; `Reset()` restores the construction snapshot.
- `SetFrameInputs(...)` atomically copies explicit reference positions, per-particle
  normals, fixed-particle targets and capsules for subsequent steps without
  rebuilding kernels or discarding dynamic history. Buffer dimensions/counts
  remain fixed. A rejected batch applies nothing; Reset restores original inputs.
- Session operations are serialized. They are synchronous: a GUI should schedule
  work off the UI thread, with cancellation/lifecycle handling in its adapter.
- A failed/nonfinite solve faults the session. `Reset()` is required to continue;
  no automatic rollback is promised.
- `ExecutionOrder` indexes the supplied constraints. Null runs all in input order.
  Invalid indices and unknown classes fail instead of silently dropping work.
- Normals, capsules and reference indices must already be mapped to solver space.
  Only spherical LocalRange is exposed in the initial public API.

Five constraint kernels are exposed: StandardLink, StretchLink, CompressibleLink,
LocalRange and BendStiffness (their `hcl…ConstraintSet` class names).
Two independently designed approximations, BendLink and Transition, are available
with `SolverOptions.AllowApproximateConstraints = true`. See [approximations](APPROXIMATIONS.md).
`ReferencePositions`, if supplied, is a separate explicit buffer indexed by
`referenceVertex`; otherwise references use rest particles.
See `ClothDefinition.cs` for the exact inputs. No game-name detection or automatic
BB/DS3 tuning is performed by the public API.

## Compatibility CLI

`OpenPhysicsHKX.Cli.exe` retains the delivered research JSON interface, presets,
diagnostics and dump metadata names, including known legacy behavior. It is a
separate process tool, not the public library's loader. It may overwrite its
`--out` destination; use new paths. It does not read binary HKX.

For the original 119-frame comparison configuration, explicitly supply:

```text
--case bb --preset bb --prev0 p0
--start prev --damping-form legacy --gravity-scale 0 --particle-radius 0
--frames 119 --seed-key initialParticles
--constraints <json> --summary <json> --prev-file <json>
--collidables <json> --out <new-json>
```

DS3 uses `--case ds3 --preset ds3 --prev0 prev` instead. These are research
comparison settings, not universal preview defaults.

## FLVER Editor

Future integration should translate the current in-memory `ClothDocument` into
these explicit inputs, then adapt double positions to the existing preview frame.
Keep native HCT as a separate backend. No Editor files are changed by this project.

## Live mapped inputs

```csharp
session.SetFrameInputs(new[] {
    new ClothFrameInput {
        ClothId = 0,
        FixedTargets = new Dictionary<int, double[]> {
            { 0, new[] { 0.0, 1.15, 0.0 } }
        }
    }
});
session.Step();
```

The example uses `System.Collections.Generic.Dictionary`.

The caller must supply correct bindings and dependency order. This API does not
decode bone weights, reconstruct compact meshes, execute an HKX operator graph,
or automatically make the current Editor backend animation-capable.
