
# R3 dependencies

The Unity integration is pinned in `Packages/manifest.json`. Unity compiles that integration on the first import, so the managed DLLs from these NuGet packages are checked in with their import settings and redistribution notices. Use the listed target framework when updating the DLLs.

| NuGet package | Version | Target framework |
| --- | --- | --- |
| R3 | 1.3.1 | netstandard2.1 |
| Microsoft.Bcl.AsyncInterfaces | 6.0.0 | netstandard2.1 |
| Microsoft.Bcl.TimeProvider | 8.0.0 | netstandard2.0 |
| System.ComponentModel.Annotations | 5.0.0 | netstandard2.1 |
| System.Threading.Channels | 8.0.0 | netstandard2.1 |
