#ifndef CSHARP2CUDA_INTEGER_SEMANTICS_0_1
#define CSHARP2CUDA_INTEGER_SEMANTICS_0_1
static_assert(sizeof(int) == 4, "CSharp2CUDA requires a 32-bit CUDA int.");
static_assert(sizeof(long long) == 8, "CSharp2CUDA requires a 64-bit CUDA long long.");

static __device__ __forceinline__ int csharp2cuda_i32_from_bits(unsigned int bits)
{
    return bits <= 0x7fffffffu ? (int)bits : -1 - (int)(~bits);
}

static __device__ __forceinline__ long long csharp2cuda_i64_from_bits(unsigned long long bits)
{
    return bits <= 0x7fffffffffffffffull ? (long long)bits : -1LL - (long long)(~bits);
}

template <typename T>
static __device__ __forceinline__ T* csharp2cuda_pointer_add(T* pointer, int offset)
{
    unsigned long long address = (unsigned long long)pointer;
    unsigned long long displacement =
        (unsigned long long)(long long)offset * (unsigned long long)sizeof(T);
    return (T*)(address + displacement);
}

template <typename T>
static __device__ __forceinline__ T* csharp2cuda_pointer_add_reverse(int offset, T* pointer)
{
    return csharp2cuda_pointer_add(pointer, offset);
}

static __device__ __forceinline__ double csharp2cuda_f64_maximum(double left, double right)
{
    if (left != right)
    {
        if (!isnan(left))
            return right < left ? left : right;
        return left;
    }
    return signbit(right) ? left : right;
}

static __device__ __forceinline__ double csharp2cuda_f64_minimum(double left, double right)
{
    if (left != right)
    {
        if (!isnan(left))
            return left < right ? left : right;
        return left;
    }
    return signbit(left) ? left : right;
}

static __device__ __forceinline__ int csharp2cuda_i32_add(int left, int right)
{
    return csharp2cuda_i32_from_bits((unsigned int)left + (unsigned int)right);
}

static __device__ __forceinline__ int csharp2cuda_i32_sub(int left, int right)
{
    return csharp2cuda_i32_from_bits((unsigned int)left - (unsigned int)right);
}

static __device__ __forceinline__ int csharp2cuda_i32_mul(int left, int right)
{
    return csharp2cuda_i32_from_bits((unsigned int)left * (unsigned int)right);
}

static __device__ __forceinline__ int csharp2cuda_i32_div(int left, int right)
{
    if (right == 0 || (left == (-2147483647 - 1) && right == -1))
    {
        __trap();
        return 0;
    }
    return left / right;
}

static __device__ __forceinline__ int csharp2cuda_i32_rem(int left, int right)
{
    if (right == 0)
    {
        __trap();
        return 0;
    }
    if (left == (-2147483647 - 1) && right == -1)
        return 0;
    return left % right;
}

static __device__ __forceinline__ int csharp2cuda_i32_and(int left, int right)
{
    return csharp2cuda_i32_from_bits((unsigned int)left & (unsigned int)right);
}

static __device__ __forceinline__ int csharp2cuda_i32_or(int left, int right)
{
    return csharp2cuda_i32_from_bits((unsigned int)left | (unsigned int)right);
}

static __device__ __forceinline__ int csharp2cuda_i32_xor(int left, int right)
{
    return csharp2cuda_i32_from_bits((unsigned int)left ^ (unsigned int)right);
}

static __device__ __forceinline__ int csharp2cuda_i32_not(int value)
{
    return csharp2cuda_i32_from_bits(~(unsigned int)value);
}

static __device__ __forceinline__ int csharp2cuda_i32_neg(int value)
{
    return csharp2cuda_i32_from_bits(0u - (unsigned int)value);
}

static __device__ __forceinline__ int csharp2cuda_i32_shl(int value, int count)
{
    unsigned int shift = (unsigned int)count & 31u;
    return csharp2cuda_i32_from_bits((unsigned int)value << shift);
}

static __device__ __forceinline__ int csharp2cuda_i32_shr(int value, int count)
{
    unsigned int shift = (unsigned int)count & 31u;
    if (shift == 0u)
        return value;
    unsigned int bits = (unsigned int)value >> shift;
    if (value < 0)
        bits |= ~0u << (32u - shift);
    return csharp2cuda_i32_from_bits(bits);
}

static __device__ __forceinline__ unsigned int csharp2cuda_u32_div(unsigned int left, unsigned int right)
{
    if (right == 0u)
    {
        __trap();
        return 0u;
    }
    return left / right;
}

static __device__ __forceinline__ unsigned int csharp2cuda_u32_rem(unsigned int left, unsigned int right)
{
    if (right == 0u)
    {
        __trap();
        return 0u;
    }
    return left % right;
}

static __device__ __forceinline__ unsigned int csharp2cuda_u32_shl(unsigned int value, int count)
{
    return value << ((unsigned int)count & 31u);
}

static __device__ __forceinline__ unsigned int csharp2cuda_u32_shr(unsigned int value, int count)
{
    return value >> ((unsigned int)count & 31u);
}

static __device__ __forceinline__ long long csharp2cuda_i64_add(long long left, long long right)
{
    return csharp2cuda_i64_from_bits((unsigned long long)left + (unsigned long long)right);
}

static __device__ __forceinline__ long long csharp2cuda_i64_sub(long long left, long long right)
{
    return csharp2cuda_i64_from_bits((unsigned long long)left - (unsigned long long)right);
}

static __device__ __forceinline__ long long csharp2cuda_i64_mul(long long left, long long right)
{
    return csharp2cuda_i64_from_bits((unsigned long long)left * (unsigned long long)right);
}

static __device__ __forceinline__ long long csharp2cuda_i64_div(long long left, long long right)
{
    if (right == 0LL ||
        (left == (-9223372036854775807LL - 1LL) && right == -1LL))
    {
        __trap();
        return 0LL;
    }
    return left / right;
}

static __device__ __forceinline__ long long csharp2cuda_i64_rem(long long left, long long right)
{
    if (right == 0LL)
    {
        __trap();
        return 0LL;
    }
    if (left == (-9223372036854775807LL - 1LL) && right == -1LL)
        return 0LL;
    return left % right;
}

static __device__ __forceinline__ long long csharp2cuda_i64_and(long long left, long long right)
{
    return csharp2cuda_i64_from_bits((unsigned long long)left & (unsigned long long)right);
}

static __device__ __forceinline__ long long csharp2cuda_i64_or(long long left, long long right)
{
    return csharp2cuda_i64_from_bits((unsigned long long)left | (unsigned long long)right);
}

static __device__ __forceinline__ long long csharp2cuda_i64_xor(long long left, long long right)
{
    return csharp2cuda_i64_from_bits((unsigned long long)left ^ (unsigned long long)right);
}

static __device__ __forceinline__ long long csharp2cuda_i64_not(long long value)
{
    return csharp2cuda_i64_from_bits(~(unsigned long long)value);
}

static __device__ __forceinline__ long long csharp2cuda_i64_neg(long long value)
{
    return csharp2cuda_i64_from_bits(0ull - (unsigned long long)value);
}

static __device__ __forceinline__ long long csharp2cuda_i64_shl(long long value, int count)
{
    unsigned int shift = (unsigned int)count & 63u;
    return csharp2cuda_i64_from_bits((unsigned long long)value << shift);
}

static __device__ __forceinline__ long long csharp2cuda_i64_shr(long long value, int count)
{
    unsigned int shift = (unsigned int)count & 63u;
    if (shift == 0u)
        return value;
    unsigned long long bits = (unsigned long long)value >> shift;
    if (value < 0LL)
        bits |= ~0ull << (64u - shift);
    return csharp2cuda_i64_from_bits(bits);
}

static __device__ __forceinline__ unsigned long long csharp2cuda_u64_div(unsigned long long left, unsigned long long right)
{
    if (right == 0ull)
    {
        __trap();
        return 0ull;
    }
    return left / right;
}

static __device__ __forceinline__ unsigned long long csharp2cuda_u64_rem(unsigned long long left, unsigned long long right)
{
    if (right == 0ull)
    {
        __trap();
        return 0ull;
    }
    return left % right;
}

static __device__ __forceinline__ unsigned long long csharp2cuda_u64_shl(unsigned long long value, int count)
{
    return value << ((unsigned int)count & 63u);
}

static __device__ __forceinline__ unsigned long long csharp2cuda_u64_shr(unsigned long long value, int count)
{
    return value >> ((unsigned int)count & 63u);
}

static __device__ __forceinline__ int csharp2cuda_i32_add_assign(int& target, int value) { return target = csharp2cuda_i32_add(target, value); }
static __device__ __forceinline__ int csharp2cuda_i32_sub_assign(int& target, int value) { return target = csharp2cuda_i32_sub(target, value); }
static __device__ __forceinline__ int csharp2cuda_i32_mul_assign(int& target, int value) { return target = csharp2cuda_i32_mul(target, value); }
static __device__ __forceinline__ int csharp2cuda_i32_div_assign(int& target, int value) { return target = csharp2cuda_i32_div(target, value); }
static __device__ __forceinline__ int csharp2cuda_i32_rem_assign(int& target, int value) { return target = csharp2cuda_i32_rem(target, value); }
static __device__ __forceinline__ int csharp2cuda_i32_and_assign(int& target, int value) { return target = csharp2cuda_i32_and(target, value); }
static __device__ __forceinline__ int csharp2cuda_i32_or_assign(int& target, int value) { return target = csharp2cuda_i32_or(target, value); }
static __device__ __forceinline__ int csharp2cuda_i32_xor_assign(int& target, int value) { return target = csharp2cuda_i32_xor(target, value); }
static __device__ __forceinline__ int csharp2cuda_i32_shl_assign(int& target, int value) { return target = csharp2cuda_i32_shl(target, value); }
static __device__ __forceinline__ int csharp2cuda_i32_shr_assign(int& target, int value) { return target = csharp2cuda_i32_shr(target, value); }

static __device__ __forceinline__ long long csharp2cuda_i64_add_assign(long long& target, long long value) { return target = csharp2cuda_i64_add(target, value); }
static __device__ __forceinline__ long long csharp2cuda_i64_sub_assign(long long& target, long long value) { return target = csharp2cuda_i64_sub(target, value); }
static __device__ __forceinline__ long long csharp2cuda_i64_mul_assign(long long& target, long long value) { return target = csharp2cuda_i64_mul(target, value); }
static __device__ __forceinline__ long long csharp2cuda_i64_div_assign(long long& target, long long value) { return target = csharp2cuda_i64_div(target, value); }
static __device__ __forceinline__ long long csharp2cuda_i64_rem_assign(long long& target, long long value) { return target = csharp2cuda_i64_rem(target, value); }
static __device__ __forceinline__ long long csharp2cuda_i64_and_assign(long long& target, long long value) { return target = csharp2cuda_i64_and(target, value); }
static __device__ __forceinline__ long long csharp2cuda_i64_or_assign(long long& target, long long value) { return target = csharp2cuda_i64_or(target, value); }
static __device__ __forceinline__ long long csharp2cuda_i64_xor_assign(long long& target, long long value) { return target = csharp2cuda_i64_xor(target, value); }
static __device__ __forceinline__ long long csharp2cuda_i64_shl_assign(long long& target, int value) { return target = csharp2cuda_i64_shl(target, value); }
static __device__ __forceinline__ long long csharp2cuda_i64_shr_assign(long long& target, int value) { return target = csharp2cuda_i64_shr(target, value); }

static __device__ __forceinline__ unsigned int csharp2cuda_u32_div_assign(unsigned int& target, unsigned int value) { return target = csharp2cuda_u32_div(target, value); }
static __device__ __forceinline__ unsigned int csharp2cuda_u32_rem_assign(unsigned int& target, unsigned int value) { return target = csharp2cuda_u32_rem(target, value); }
static __device__ __forceinline__ unsigned int csharp2cuda_u32_shl_assign(unsigned int& target, int value) { return target = csharp2cuda_u32_shl(target, value); }
static __device__ __forceinline__ unsigned int csharp2cuda_u32_shr_assign(unsigned int& target, int value) { return target = csharp2cuda_u32_shr(target, value); }

static __device__ __forceinline__ unsigned long long csharp2cuda_u64_div_assign(unsigned long long& target, unsigned long long value) { return target = csharp2cuda_u64_div(target, value); }
static __device__ __forceinline__ unsigned long long csharp2cuda_u64_rem_assign(unsigned long long& target, unsigned long long value) { return target = csharp2cuda_u64_rem(target, value); }
static __device__ __forceinline__ unsigned long long csharp2cuda_u64_shl_assign(unsigned long long& target, int value) { return target = csharp2cuda_u64_shl(target, value); }
static __device__ __forceinline__ unsigned long long csharp2cuda_u64_shr_assign(unsigned long long& target, int value) { return target = csharp2cuda_u64_shr(target, value); }

static __device__ __forceinline__ int csharp2cuda_i32_pre_increment(int& target) { return target = csharp2cuda_i32_add(target, 1); }
static __device__ __forceinline__ int csharp2cuda_i32_post_increment(int& target) { int result = target; target = csharp2cuda_i32_add(target, 1); return result; }
static __device__ __forceinline__ int csharp2cuda_i32_pre_decrement(int& target) { return target = csharp2cuda_i32_sub(target, 1); }
static __device__ __forceinline__ int csharp2cuda_i32_post_decrement(int& target) { int result = target; target = csharp2cuda_i32_sub(target, 1); return result; }
static __device__ __forceinline__ long long csharp2cuda_i64_pre_increment(long long& target) { return target = csharp2cuda_i64_add(target, 1LL); }
static __device__ __forceinline__ long long csharp2cuda_i64_post_increment(long long& target) { long long result = target; target = csharp2cuda_i64_add(target, 1LL); return result; }
static __device__ __forceinline__ long long csharp2cuda_i64_pre_decrement(long long& target) { return target = csharp2cuda_i64_sub(target, 1LL); }
static __device__ __forceinline__ long long csharp2cuda_i64_post_decrement(long long& target) { long long result = target; target = csharp2cuda_i64_sub(target, 1LL); return result; }
#endif

struct MathBlockComplexValue;

struct MathBlockComplexValue
{
    double real;
    double imaginary;
};

__device__ MathBlockComplexValue mathblocks_complex_make(double real, double imaginary);
__device__ MathBlockComplexValue mathblocks_complex_add(
    MathBlockComplexValue left,
    MathBlockComplexValue right);
__device__ MathBlockComplexValue mathblocks_complex_subtract(
    MathBlockComplexValue left,
    MathBlockComplexValue right);
__device__ MathBlockComplexValue mathblocks_complex_multiply(
    MathBlockComplexValue left,
    MathBlockComplexValue right);
__device__ MathBlockComplexValue mathblocks_complex_divide(
    MathBlockComplexValue left,
    MathBlockComplexValue right);
__device__ MathBlockComplexValue mathblocks_complex_conjugate(MathBlockComplexValue value);
__device__ double mathblocks_complex_magnitude(MathBlockComplexValue value);
__device__ double mathblocks_complex_phase(MathBlockComplexValue value);
__device__ MathBlockComplexValue mathblocks_complex_exponential(MathBlockComplexValue value);
__device__ MathBlockComplexValue mathblocks_complex_logarithm(MathBlockComplexValue value);
__device__ MathBlockComplexValue mathblocks_complex_square_root(MathBlockComplexValue value);
__device__ MathBlockComplexValue mathblocks_complex_power(
    MathBlockComplexValue value,
    MathBlockComplexValue exponent);
__device__ MathBlockComplexValue mathblocks_complex_from_polar(double magnitude, double phase);
__device__ bool mathblocks_complex_finite(MathBlockComplexValue value);
__device__ void mathblocks_complex_shape(MathBlockSlot* output, int count);
extern "C" __global__ void mathblocks_complex(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output);

__device__ MathBlockComplexValue mathblocks_complex_make(double real, double imaginary)
{
    MathBlockComplexValue result;
    result.real = real;
    result.imaginary = imaginary;
    return result;
}

__device__ MathBlockComplexValue mathblocks_complex_add(
    MathBlockComplexValue left,
    MathBlockComplexValue right)
{
    return mathblocks_complex_make(left.real + right.real, left.imaginary + right.imaginary);
}

__device__ MathBlockComplexValue mathblocks_complex_subtract(
    MathBlockComplexValue left,
    MathBlockComplexValue right)
{
    return mathblocks_complex_make(left.real - right.real, left.imaginary - right.imaginary);
}

__device__ MathBlockComplexValue mathblocks_complex_multiply(
    MathBlockComplexValue left,
    MathBlockComplexValue right)
{
    return mathblocks_complex_make(
        left.real * right.real - left.imaginary * right.imaginary,
        left.real * right.imaginary + left.imaginary * right.real);
}

__device__ MathBlockComplexValue mathblocks_complex_divide(
    MathBlockComplexValue left,
    MathBlockComplexValue right)
{
    double denominator = right.real * right.real + right.imaginary * right.imaginary;
    return mathblocks_complex_make(
        (left.real * right.real + left.imaginary * right.imaginary) / denominator,
        (left.imaginary * right.real - left.real * right.imaginary) / denominator);
}

__device__ MathBlockComplexValue mathblocks_complex_conjugate(MathBlockComplexValue value)
{
    return mathblocks_complex_make(value.real, -value.imaginary);
}

__device__ double mathblocks_complex_magnitude(MathBlockComplexValue value)
{
    double real = fabs(value.real);
    double imaginary = fabs(value.imaginary);
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

__device__ double mathblocks_complex_phase(MathBlockComplexValue value)
{
    return mathblocks_arc_tangent_2(value.imaginary, value.real);
}

__device__ MathBlockComplexValue mathblocks_complex_exponential(MathBlockComplexValue value)
{
    double scale = mathblocks_exponential(value.real);
    return mathblocks_complex_make(
        scale * mathblocks_cosine(value.imaginary),
        scale * mathblocks_sine(value.imaginary));
}

__device__ MathBlockComplexValue mathblocks_complex_logarithm(MathBlockComplexValue value)
{
    return mathblocks_complex_make(
        mathblocks_natural_logarithm(mathblocks_complex_magnitude(value)),
        mathblocks_complex_phase(value));
}

__device__ MathBlockComplexValue mathblocks_complex_square_root(MathBlockComplexValue value)
{
    if (value.real == 0.0 && value.imaginary == 0.0)
        return mathblocks_complex_make(0.0, value.imaginary);
    double magnitude = mathblocks_complex_magnitude(value);
    return mathblocks_complex_make(
        mathblocks_square_root((magnitude + value.real) / 2.0),
        copysign(mathblocks_square_root((magnitude - value.real) / 2.0), value.imaginary));
}

__device__ MathBlockComplexValue mathblocks_complex_power(
    MathBlockComplexValue value,
    MathBlockComplexValue exponent)
{
    return mathblocks_complex_exponential(
        mathblocks_complex_multiply(exponent, mathblocks_complex_logarithm(value)));
}

__device__ MathBlockComplexValue mathblocks_complex_from_polar(double magnitude, double phase)
{
    return mathblocks_complex_make(
        magnitude * mathblocks_cosine(phase),
        magnitude * mathblocks_sine(phase));
}

__device__ bool mathblocks_complex_finite(MathBlockComplexValue value)
{
    return isfinite(value.real) && isfinite(value.imaginary);
}

__device__ void mathblocks_complex_shape(MathBlockSlot* output, int count)
{
    output->rows = count;
    output->columns = 0;
    output->count = count;
    if (count < 0 || count > output->capacity)
        output->valid = 0;
}

extern "C" __global__ void mathblocks_complex(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    int thread = threadIdx.x;
    const MathBlockSlot* first = input_count > 0 ? inputs[0] : nullptr;
    const MathBlockSlot* second = input_count > 1 ? inputs[1] : nullptr;
    if (thread == 0)
    {
        output->scalar_value = 0.0;
        output->boolean_value = 0;
        output->valid = 1;
        for (int index = 0; index < input_count; csharp2cuda_i32_post_increment(index))
            if (inputs[index] == nullptr || !inputs[index]->valid) output->valid = 0;
    }
    __syncthreads();
    if (!output->valid)
        return;

    MathBlockComplexValue* result = (MathBlockComplexValue*)output->data_pointer;
    const MathBlockComplexValue* complex_first =
        first == nullptr ? nullptr : (MathBlockComplexValue*)first->data_pointer;
    const MathBlockComplexValue* complex_second =
        second == nullptr ? nullptr : (MathBlockComplexValue*)second->data_pointer;

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
        __syncthreads();
        const double* real = (double*)first->data_pointer;
        const double* imaginary = (double*)second->data_pointer;
        for (int index = thread; output->valid && index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
            result[index] = mathblocks_complex_make(real[index], imaginary[index]);
        return;
    }

    if (opcode >= 15 && opcode <= 17)
    {
        if (thread == 0) mathblocks_complex_shape(output, first->count);
        __syncthreads();
        double* projected = (double*)output->data_pointer;
        for (int index = thread; output->valid && index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
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
            output->count = csharp2cuda_i32_mul(count, count);
            if (count <= 0 || count != second->count || output->count > output->capacity)
                output->valid = 0;
        }
        for (int index = thread; index < count; csharp2cuda_i32_add_assign(index, blockDim.x))
        {
            if (mathblocks_complex_magnitude(complex_first[index]) >= 1.0 ||
                mathblocks_complex_magnitude(complex_second[index]) > 1.0)
            {
                atomicExch(&output->valid, 0);
            }
        }
        __syncthreads();
        for (int flat = thread; output->valid && flat < csharp2cuda_i32_mul(count, count); csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, count);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, count));
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
            if (!mathblocks_complex_finite(result[flat])) atomicExch(&output->valid, 0);
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
        __syncthreads();
        const double* source = (double*)first->data_pointer;
        for (int frequency = thread; output->valid && frequency < first->count; csharp2cuda_i32_add_assign(frequency, blockDim.x))
        {
            MathBlockComplexValue sum = mathblocks_complex_make(0.0, 0.0);
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
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
            if (!mathblocks_complex_finite(sum)) atomicExch(&output->valid, 0);
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
        __syncthreads();
        for (int index = thread; output->valid && index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
        {
            MathBlockComplexValue sum = mathblocks_complex_make(0.0, 0.0);
            for (int frequency = 0; frequency < first->count; csharp2cuda_i32_post_increment(frequency))
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
            if (!mathblocks_complex_finite(result[index])) atomicExch(&output->valid, 0);
        }
    }
}