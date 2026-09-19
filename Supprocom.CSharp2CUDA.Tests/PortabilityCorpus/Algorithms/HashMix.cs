namespace Supprocom.CSharp2CUDA.Tests.PortabilityCorpus;

public static class HashMix
{
    public static uint Calculate(uint value)
    {
        value ^= value >> 16;
        value *= 0x7feb352du;
        value ^= value >> 15;
        value *= 0x846ca68bu;
        return value ^ (value >> 16);
    }
}
