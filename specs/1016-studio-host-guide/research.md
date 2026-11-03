# Existing decisions reused

No open research question or new technology choice. The accepted [hosting contract](../../docs/reports/studio-hosting-contract.md), delivered public package READMEs and #3364/#3365 actual candidate/runtime receipts settle the design.

Use the contract's Dashboard starter because it exercises package/module composition with a small application-owned root. Keep a `.csproj.in` template outside solution discovery: developers materialize it externally, and no new shipped assembly or artificial test project is introduced. Require explicit candidate version/preview sources; original CI archives are distinct from retained fault mutants. Existing optional/native proofs remain the evidence for those broader profiles; a successful basic sample does not replace them.
