using static System.Numerics.Tensors.TensorPrimitives;

namespace Hypernonsense;

internal static class VectorHelper
{
    public static void RandomUnitVector(int seed, Span<float> dest)
    {
        var rng = new Random(seed);
        
        for (var i = 0; i < dest.Length; i++)
            dest[i] = rng.NextSingle() * 2 - 1;
        
        Divide(dest, Norm(dest), dest);
    }
}