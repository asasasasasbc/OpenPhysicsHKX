# Known limits

- Experimental cloth preview, not a complete Havok/HCT replacement or game certification.
- Five baseline kernels and two opt-in approximate kernels. The public API rejects unknown classes. The compatibility
  CLI preserves legacy skipping, fallback binding and execution-index filtering.
- BendLink/Transition approximations are documented separately in `APPROXIMATIONS.md`;
  they do not inherit the baseline's Python bit-parity claim.
- No binary HKX reader/writer, runtime operator graph, bone transform composition,
  animated pins/colliders, native wind, or display-mesh deformation.
- Public inputs are explicit; reference vertices currently index rest particles.
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
