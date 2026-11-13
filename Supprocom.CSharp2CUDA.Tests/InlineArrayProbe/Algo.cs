namespace Supprocom.CSharp2CUDA.Tests.InlineArrayProbe;

internal static class Algo
{
    public static void Add(ref DType o, in DType x, in DType y)
    {
        for (int i = 0; i < 4; i++)
        {
            long n = (long)x.num[i] * y.den[i] + (long)y.num[i] * x.den[i];
            long d = (long)x.den[i] * y.den[i];
            if (n > int.MaxValue || n < int.MinValue || d > int.MaxValue) { o.kind = -1; return; }
            o.num[i] = (int)n; o.den[i] = (int)d;
        }
        o.kind = x.kind; o.rows = x.rows; o.columns = x.columns;
    }
}
