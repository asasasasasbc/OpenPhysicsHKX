# Experimental approximations

These kernels are separate from the imported Python/C# numerical baseline.
Enable them explicitly with `SolverOptions.AllowApproximateConstraints = true`.
`ClothSession.ApproximateConstraintClasses` reports which ones are present.
The compatibility CLI and its original kernels are unchanged.

## BendLink

`hclBendLinkConstraintSet` is approximated as a position-based distance interval:

- Below `bendMinLength`, separate the pair using `bendStiffness`.
- Above `stretchMaxLength`, pull the pair together using `stretchStiffness`.
- Inside the interval, do nothing.

Corrections are distributed by normalized inverse mass. Gain is clamped to 0..1;
fixed particles do not move. Collapsed pairs use their rest direction, or +X if
the rest pair is also coincident. This is not a recovered native algorithm.

## Transition

`hclTransitionConstraintSet` uses a time-controlled reference envelope. The
default direction releases rest/reference constraints toward full simulation;
`ConstraintDefinition.TransitionToAnimation` reverses it toward the supplied
reference, not an automatically decoded animation clip.

Each particle's delay and the selected set period determine progress. With
release fraction `s`, the envelope radius is `toSimMaxDistance * s / (1-s)`.
At zero release, position/history are pinned to the reference; at full release,
the envelope is inactive. Intermediate corrections shift position/history
equally to avoid injecting an extra Verlet impulse. Time advances once per
frame, not once per constraint invocation or solver iteration.

Only these documented delays/periods are used; native transition states,
plus-delay period fields, moving reference buffers and native interpolation are
not replicated. Reset restarts the transition. This is a preview approximation.

## VolumeMx (0.2)

`hclVolumeConstraintMx` uses independent weighted **rigid shape matching**. It is
not the native VolumeMx algorithm or a claim of volume/force-response equivalence.
Each numeric row contains `particleIndex, frameX, frameY, frameZ, weight,
stiffness, role`, with contiguous XYZ columns. Role 0 contributes a frame sample;
role 1 applies a correction. Frame and apply subsets may differ.

For positive frame weights, compute the weighted current centroid `c` and the
weighted authored frame centroid `q0`. Fit a proper rotation `R` maximizing the
correlation of `(q-q0)` and `(p-c)`, using a Davenport quaternion matrix and a
deterministic symmetric Jacobi eigensolve. An apply sample targets
`c + R * (q-q0)`. Its correction is scaled by authored stiffness times invocation
gain, clamped to [0,1]. Fixed particles are not moved. There is no scaling fit,
reflection fit, or inference of reference vertices from particle-rest positions.

Frame weight sums must be positive and finite; malformed roles, indices,
duplicate particles within a role, and stiffness outside [0,1] are rejected.
The adapter, not Core, decodes HKX SIMD batch/single records. Editor status lists
VolumeMx as approximate. Old imported arithmetic and the CLI remain unchanged.

## Checks

Analytic tests cover distance limits, compression, dead zones, collapsed/fixed
pairs, deterministic batch/reset behavior, transition timing/release and explicit
reference buffers. No native-equivalence tolerance is claimed.
Volume tests additionally cover rigid rest preservation, translated 90/180-degree
rotations, uniform-distortion restoration, fixed points, deterministic batch/reset
behavior and malformed inputs.
