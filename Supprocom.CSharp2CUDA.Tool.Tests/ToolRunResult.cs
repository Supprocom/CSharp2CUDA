using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Supprocom.CSharp2CUDA.Tool.Tests;

internal sealed record ToolRunResult(
    int ExitCode,
    string Output,
    string Error);
