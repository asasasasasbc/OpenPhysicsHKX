# Source notes

The initial kernels and compatibility CLI were split mechanically from the
user-supplied C# port of `implementation/src/clothsolve.py`:

- `implementation/src-cs/ClothSolveCs.cs`
- `implementation/src-cs/PyNum.cs`

Their exact hashes and the initial generated-file hashes are recorded in
`import-manifest.json`. The local research workspace retains regeneration and
private-fixture validation tools; those tools and their evidence are not part of
this source-only repository. Python is not required to build or use the library.
Imported comments are retained, including references to the underlying
interoperability research. This project does not assert a strict clean-room process.

MIT is selected for this repository at the user's direction. That license covers
rights the contributors can grant; it does not license third-party runtimes,
game data or research materials. The two supplied C# files contain no embedded
license header; source provenance should be reviewed before a public release.

No FLVER Editor implementation, HCT binaries, game assets, decompilation reports,
or research evidence archive is included. Private comparison outputs stay under
the ignored `.local/` directory.
