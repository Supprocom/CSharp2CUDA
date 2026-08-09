# Third-party dependency notices

This document records the dependency identities reviewed for CSharp2CUDA on
August 9, 2026. It does not replace applicable license text.

The `AGPL-3.0-only` license applies to CSharp2CUDA source. It does not change
any third-party license.

## Production project

[`Microsoft.CodeAnalysis.CSharp` 5.6.0][roslyn-csharp] is the direct production
dependency. It resolves [`Microsoft.CodeAnalysis.Common` 5.6.0][roslyn-common].
Both packages use the MIT expression.

[`Microsoft.CodeAnalysis.Analyzers` 5.3.0][roslyn-analyzers] is a resolved build
dependency. Its package metadata uses the MIT expression.

The CSharp2CUDA package declares its production dependencies. It does not
include Microsoft binaries.

## Test project

[`Microsoft.NET.Test.Sdk` 17.14.1](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/17.14.1)
resolves Microsoft.CodeCoverage, Microsoft.TestPlatform.ObjectModel, and
Microsoft.TestPlatform.TestHost at version 17.14.1. These Microsoft packages
use the MIT expression.

[`Newtonsoft.Json` 13.0.3](https://www.nuget.org/packages/Newtonsoft.Json/13.0.3)
is a resolved test dependency. It uses the MIT expression.

[`xunit` 2.9.3](https://www.nuget.org/packages/xunit/2.9.3) resolves its core,
assertion, abstraction, analyzer, and execution packages. These packages use
Apache-2.0 terms.

[`xunit.runner.visualstudio` 3.1.5](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5)
is a direct test dependency. It uses the Apache-2.0 expression.

## Dependency acquisition

NuGet downloads the declared packages into the user's package cache. The Git
repository does not contain or redistribute these package archives.

A user who distributes resolved dependencies must review each applicable
third-party license. NuGet metadata does not replace notices inside dependency
archives.

[roslyn-csharp]: https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/5.6.0
[roslyn-common]: https://www.nuget.org/packages/Microsoft.CodeAnalysis.Common/5.6.0
[roslyn-analyzers]: https://www.nuget.org/packages/Microsoft.CodeAnalysis.Analyzers/5.3.0
