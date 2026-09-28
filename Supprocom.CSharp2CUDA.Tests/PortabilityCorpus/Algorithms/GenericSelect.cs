namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

#pragma warning disable CA1515 // External test discovery and the C# portability corpus require public types.
public static class GenericSelect
{
#pragma warning restore CA1515
    public static T Choose<T>(bool chooseFirst, T first, T second)
        where T : unmanaged => chooseFirst ? first : second;
}
