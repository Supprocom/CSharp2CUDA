#pragma warning disable CS0078, CS0649

using System;
using Supprocom.CSharp2CUDA;

internal static unsafe class ComplexModule
{
    [CudaExternal]
    public struct MathBlockSlot
    {
        public double scalar_value;
        public ulong data_pointer;
        public ulong scratch_pointer;
        public CudaInt32 boolean_value;
        public CudaInt32 valid;
        public int rows;
        public int columns;
        public int count;
        public int capacity;
    }

    [CudaExternal(IsPure = true)]
    private static double mathblocks_square_root(double value) => throw new NotSupportedException();

    [CudaExternal(IsPure = true)]
    private static double mathblocks_arc_tangent_2(double left, double right) => throw new NotSupportedException();

    [CudaExternal(IsPure = true)]
    private static double mathblocks_exponential(double value) => throw new NotSupportedException();

    [CudaExternal(IsPure = true)]
    private static double mathblocks_cosine(double value) => throw new NotSupportedException();

    [CudaExternal(IsPure = true)]
    private static double mathblocks_sine(double value) => throw new NotSupportedException();

    [CudaExternal(IsPure = true)]
    private static double mathblocks_natural_logarithm(double value) => throw new NotSupportedException();

    public struct MathBlockComplexValue
    {
        public double real;
        public double imaginary;
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_make(double real, double imaginary)
    {
        MathBlockComplexValue result;
        result.real = real;
        result.imaginary = imaginary;
        return result;
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_add(
        MathBlockComplexValue left,
        MathBlockComplexValue right)
    {
        return mathblocks_complex_make(left.real + right.real, left.imaginary + right.imaginary);
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_subtract(
        MathBlockComplexValue left,
        MathBlockComplexValue right)
    {
        return mathblocks_complex_make(left.real - right.real, left.imaginary - right.imaginary);
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_multiply(
        MathBlockComplexValue left,
        MathBlockComplexValue right)
    {
        return mathblocks_complex_make(
            left.real * right.real - left.imaginary * right.imaginary,
            left.real * right.imaginary + left.imaginary * right.real);
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_divide(
        MathBlockComplexValue left,
        MathBlockComplexValue right)
    {
        double denominator = right.real * right.real + right.imaginary * right.imaginary;
        return mathblocks_complex_make(
            (left.real * right.real + left.imaginary * right.imaginary) / denominator,
            (left.imaginary * right.real - left.real * right.imaginary) / denominator);
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_conjugate(MathBlockComplexValue value)
    {
        return mathblocks_complex_make(value.real, -value.imaginary);
    }

    [CudaDevice]
    private static double mathblocks_complex_magnitude(MathBlockComplexValue value)
    {
        double real = Math.Abs(value.real);
        double imaginary = Math.Abs(value.imaginary);
        if (real < imaginary)
        {
            double temporary = real;
            real = imaginary;
            imaginary = temporary;
        }
        if (real == 0.0)
            return 0.0;
        double ratio = imaginary / real;
        return real * mathblocks_square_root(1.0 + ratio * ratio);
    }

    [CudaDevice]
    private static double mathblocks_complex_phase(MathBlockComplexValue value)
    {
        return mathblocks_arc_tangent_2(value.imaginary, value.real);
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_exponential(MathBlockComplexValue value)
    {
        double scale = mathblocks_exponential(value.real);
        return mathblocks_complex_make(
            scale * mathblocks_cosine(value.imaginary),
            scale * mathblocks_sine(value.imaginary));
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_logarithm(MathBlockComplexValue value)
    {
        return mathblocks_complex_make(
            mathblocks_natural_logarithm(mathblocks_complex_magnitude(value)),
            mathblocks_complex_phase(value));
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_square_root(MathBlockComplexValue value)
    {
        if (value.real == 0.0 && value.imaginary == 0.0)
            return mathblocks_complex_make(0.0, value.imaginary);
        double magnitude = mathblocks_complex_magnitude(value);
        return mathblocks_complex_make(
            mathblocks_square_root((magnitude + value.real) / 2.0),
            Math.CopySign(mathblocks_square_root((magnitude - value.real) / 2.0), value.imaginary));
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_power(
        MathBlockComplexValue value,
        MathBlockComplexValue exponent)
    {
        return mathblocks_complex_exponential(
            mathblocks_complex_multiply(exponent, mathblocks_complex_logarithm(value)));
    }

    [CudaDevice]
    private static MathBlockComplexValue mathblocks_complex_from_polar(double magnitude, double phase)
    {
        return mathblocks_complex_make(
            magnitude * mathblocks_cosine(phase),
            magnitude * mathblocks_sine(phase));
    }

    [CudaDevice]
    private static bool mathblocks_complex_finite(MathBlockComplexValue value)
    {
        return double.IsFinite(value.real) && double.IsFinite(value.imaginary);
    }

    [CudaDevice]
    private static void mathblocks_complex_shape(MathBlockSlot* output, int count)
    {
        output->rows = count;
        output->columns = 0;
        output->count = count;
        if (count < 0 || count > output->capacity)
            output->valid = 0;
    }

    [CudaGlobal]
    private static void mathblocks_complex(
        int opcode,
        [CudaReadOnly] MathBlockSlot** inputs,
        int input_count,
        MathBlockSlot* output)
    {
        int thread = Cuda.ThreadIdx.X;
        MathBlockSlot* first = Cuda.ReadOnly(input_count > 0 ? inputs[0] : null);
        MathBlockSlot* second = Cuda.ReadOnly(input_count > 1 ? inputs[1] : null);
        if (thread == 0)
        {
            output->scalar_value = 0.0;
            output->boolean_value = 0;
            output->valid = 1;
            for (int index = 0; index < input_count; index++)
                if (inputs[index] == null || !inputs[index]->valid) output->valid = 0;
        }
        Cuda.SyncThreads();
        if (!output->valid)
            return;

        MathBlockComplexValue* result = (MathBlockComplexValue*)output->data_pointer;
        MathBlockComplexValue* complex_first =
            Cuda.ReadOnly(first == null ? null : (MathBlockComplexValue*)first->data_pointer);
        MathBlockComplexValue* complex_second =
            Cuda.ReadOnly(second == null ? null : (MathBlockComplexValue*)second->data_pointer);

        if (opcode <= 13)
        {
            if (thread != 0)
                return;
            MathBlockComplexValue value = mathblocks_complex_make(0.0, 0.0);
            switch (opcode)
            {
                case 0:
                    value = mathblocks_complex_add(complex_first[0], complex_second[0]);
                    break;
                case 1:
                    value = mathblocks_complex_conjugate(complex_first[0]);
                    break;
                case 2:
                    value = mathblocks_complex_make(first->scalar_value, second->scalar_value);
                    break;
                case 3:
                    value = mathblocks_complex_divide(complex_first[0], complex_second[0]);
                    break;
                case 4:
                    value = mathblocks_complex_exponential(complex_first[0]);
                    break;
                case 5:
                    value = mathblocks_complex_from_polar(first->scalar_value, second->scalar_value);
                    break;
                case 6:
                    output->scalar_value = mathblocks_complex_magnitude(complex_first[0]);
                    output->count = 0;
                    return;
                case 7:
                    value = mathblocks_complex_multiply(complex_first[0], complex_second[0]);
                    break;
                case 8:
                    value = mathblocks_complex_logarithm(complex_first[0]);
                    break;
                case 9:
                    value = mathblocks_complex_make(-complex_first[0].real, -complex_first[0].imaginary);
                    break;
                case 10:
                    output->scalar_value = mathblocks_complex_phase(complex_first[0]);
                    output->count = 0;
                    return;
                case 11:
                    value = mathblocks_complex_power(complex_first[0], complex_second[0]);
                    break;
                case 12:
                    value = mathblocks_complex_square_root(complex_first[0]);
                    break;
                case 13:
                    value = mathblocks_complex_subtract(complex_first[0], complex_second[0]);
                    break;
            }
            output->rows = 0;
            output->columns = 0;
            output->count = 1;
            result[0] = value;
            if (!mathblocks_complex_finite(value)) output->valid = 0;
            return;
        }

        if (opcode == 14)
        {
            if (thread == 0)
            {
                mathblocks_complex_shape(output, first->count);
                if (first->count != second->count) output->valid = 0;
            }
            Cuda.SyncThreads();
            double* real = Cuda.ReadOnly((double*)first->data_pointer);
            double* imaginary = Cuda.ReadOnly((double*)second->data_pointer);
            for (int index = thread; output->valid && index < first->count; index += Cuda.BlockDim.X)
                result[index] = mathblocks_complex_make(real[index], imaginary[index]);
            return;
        }

        if (opcode >= 15 && opcode <= 17)
        {
            if (thread == 0) mathblocks_complex_shape(output, first->count);
            Cuda.SyncThreads();
            double* projected = (double*)output->data_pointer;
            for (int index = thread; output->valid && index < first->count; index += Cuda.BlockDim.X)
            {
                if (opcode == 15) projected[index] = complex_first[index].imaginary;
                else if (opcode == 16) projected[index] = mathblocks_complex_magnitude(complex_first[index]);
                else projected[index] = complex_first[index].real;
            }
            return;
        }

        if (opcode == 18)
        {
            int count = first->count;
            if (thread == 0)
            {
                output->rows = count;
                output->columns = count;
                output->count = count * count;
                if (count <= 0 || count != second->count || output->count > output->capacity)
                    output->valid = 0;
            }
            for (int index = thread; index < count; index += Cuda.BlockDim.X)
            {
                if (mathblocks_complex_magnitude(complex_first[index]) >= 1.0 ||
                    mathblocks_complex_magnitude(complex_second[index]) > 1.0)
                {
                    Cuda.AtomicExchange(ref output->valid, 0);
                }
            }
            Cuda.SyncThreads();
            for (int flat = thread; output->valid && flat < count * count; flat += Cuda.BlockDim.X)
            {
                int row = flat / count;
                int column = flat - row * count;
                MathBlockComplexValue one = mathblocks_complex_make(1.0, 0.0);
                MathBlockComplexValue numerator = mathblocks_complex_subtract(
                    one,
                    mathblocks_complex_multiply(
                        complex_second[row],
                        mathblocks_complex_conjugate(complex_second[column])));
                MathBlockComplexValue denominator = mathblocks_complex_subtract(
                    one,
                    mathblocks_complex_multiply(
                        complex_first[row],
                        mathblocks_complex_conjugate(complex_first[column])));
                result[flat] = mathblocks_complex_divide(numerator, denominator);
                if (!mathblocks_complex_finite(result[flat])) Cuda.AtomicExchange(ref output->valid, 0);
            }
            return;
        }

        if (opcode == 19)
        {
            if (thread == 0)
            {
                mathblocks_complex_shape(output, first->count);
                if (first->count <= 0) output->valid = 0;
            }
            Cuda.SyncThreads();
            double* source = Cuda.ReadOnly((double*)first->data_pointer);
            for (int frequency = thread; output->valid && frequency < first->count; frequency += Cuda.BlockDim.X)
            {
                MathBlockComplexValue sum = mathblocks_complex_make(0.0, 0.0);
                for (int index = 0; index < first->count; index++)
                {
                    double angle = -2.0 * 3.141592653589793238462643383279502884 *
                        frequency * index / first->count;
                    sum = mathblocks_complex_add(
                        sum,
                        mathblocks_complex_multiply(
                            mathblocks_complex_make(source[index], 0.0),
                            mathblocks_complex_from_polar(1.0, angle)));
                }
                result[frequency] = sum;
                if (!mathblocks_complex_finite(sum)) Cuda.AtomicExchange(ref output->valid, 0);
            }
            return;
        }

        if (opcode == 20)
        {
            if (thread == 0)
            {
                mathblocks_complex_shape(output, first->count);
                if (first->count <= 0) output->valid = 0;
            }
            Cuda.SyncThreads();
            for (int index = thread; output->valid && index < first->count; index += Cuda.BlockDim.X)
            {
                MathBlockComplexValue sum = mathblocks_complex_make(0.0, 0.0);
                for (int frequency = 0; frequency < first->count; frequency++)
                {
                    double angle = 2.0 * 3.141592653589793238462643383279502884 *
                        frequency * index / first->count;
                    sum = mathblocks_complex_add(
                        sum,
                        mathblocks_complex_multiply(
                            complex_first[frequency],
                            mathblocks_complex_from_polar(1.0, angle)));
                }
                result[index] = mathblocks_complex_divide(
                    sum,
                    mathblocks_complex_make((double)first->count, 0.0));
                if (!mathblocks_complex_finite(result[index])) Cuda.AtomicExchange(ref output->valid, 0);
            }
        }
    }
}
