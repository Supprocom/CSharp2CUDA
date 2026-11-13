using System.Runtime.InteropServices;

namespace Supprocom.CSharp2CUDA.Tests.InlineArrayProbe;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct DType
{
    public int kind;
    public int rows;
    public int columns;
    public Int4 num;
    public Int4 den;
}
