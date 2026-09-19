namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class GenericSelect
{
    public static T Choose<T>(bool chooseFirst, T first, T second)
        where T : unmanaged => chooseFirst ? first : second;
}
