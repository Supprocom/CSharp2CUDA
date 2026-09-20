; Unshipped diagnostic release

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
CS2CUDA033 | Supprocom.CSharp2CUDA | Error | Reports fixed local storage whose identity or lifetime escapes.
CS2CUDA034 | Supprocom.CSharp2CUDA | Error | Reports a closure that would require a managed delegate or escaping environment.
CS2CUDA035 | Supprocom.CSharp2CUDA | Error | Reports a reference whose lifetime or mutability cannot be preserved.
CS2CUDA036 | Supprocom.CSharp2CUDA | Error | Reports a device-internal tuple or record at an external CUDA ABI boundary.
CS2CUDA037 | Supprocom.CSharp2CUDA | Error | Reports a pattern outside the portable subset.
CS2CUDA038 | Supprocom.CSharp2CUDA | Error | Reports a user-defined operator or conversion without a portable reachable body.
CS2CUDA039 | Supprocom.CSharp2CUDA | Error | Reports a range or Span operation whose required semantics cannot be preserved.
CS2CUDA040 | Supprocom.CSharp2CUDA | Error | Reports fixed local storage that exceeds the deterministic per-declaration element limit.
