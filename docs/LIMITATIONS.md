# Known limits

- Experimental cloth preview, not a complete Havok/HCT replacement or game certification.
- Five baseline kernels and three opt-in approximate kernels. The public API rejects unknown classes. The compatibility
  CLI preserves legacy skipping, fallback binding and execution-index filtering.
- BendLink/Transition/VolumeMx approximations are documented separately in `APPROXIMATIONS.md`;
  they do not inherit the baseline's Python bit-parity claim.
- No binary HKX reader/writer, runtime operator graph, bone transform composition,
  automatic HKX animation binding, native wind, or display-mesh deformation.
- Explicit per-step reference/pin/capsule updates are available through
  `SetFrameInputs`; decoding or generating those bindings remains the caller's job.
- Version 0.3 supports independent named reference buffers and per-set normal
  mappings. Core still does not parse or execute an HKX graph; a caller must
  supply the correct current/previous-frame outputs in authored order.
- Public inputs are explicit; reference vertices index `ReferencePositions` when supplied,
  otherwise the rest-position array. The caller must evaluate and map authored buffers.
  Inter-domain dependencies are not evaluated.
- The delivered Python parity baseline intentionally retains known issues:
  legacy vertex/particle normal mapping (CLI), Stretch mass gain behavior,
  LocalRange epsilon offset, and BB tuning/residual limitations. No correction
  to those numerical behaviors is part of the initial split.
- `PyNum` is retained for compatibility. Its fallback and extreme-boundary handling
  need further review; sampled parity is not a proof for all decimal strings.
- Float64 operation order is retained. Bitwise parity is environment/configuration
  specific, not a guarantee across JITs, platforms, runtimes or future optimizations.
- The CLI retains historical diagnostic text and default seed/frame settings.
  Supply full comparison arguments; successful exit alone is not validation.
- Core 0.3.1 offers `RespectFixedStretchAnchors` to repair the legacy A-only
  Stretch path for fixed/dynamic pairs. The Editor opts in; default API/CLI
  arithmetic remains unchanged. Dynamic/dynamic Stretch behavior and full native
  equivalence are not established by this targeted correction.
