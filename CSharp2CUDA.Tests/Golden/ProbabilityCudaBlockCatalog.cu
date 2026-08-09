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

__device__ bool mathblocks_probability_integer(double value, int* result);
__device__ double mathblocks_probability_binomial(int n, int k);
__device__ bool mathblocks_probability_distribution(const double* values, int count);
__device__ double mathblocks_probability_entropy(const double* values, int count);
__device__ double mathblocks_probability_kl(
    const double* probabilities,
    const double* reference,
    int count);
__device__ double mathblocks_probability_log_gamma_core(double value);
__device__ double mathblocks_probability_log_gamma(double value);
__device__ double mathblocks_probability_beta_fraction(double x, double left, double right);
__device__ double mathblocks_probability_incomplete_beta(
    double x,
    double left,
    double right);
__device__ MathBlockComplexValue mathblocks_probability_complex_cube_root(
    MathBlockComplexValue value);
extern "C" __global__ void mathblocks_probability(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output);

__device__ bool mathblocks_probability_integer(double value, int* result)
{
    if (value < -2147483648.0 || value > 2147483647.0 || value != trunc(value))
        return false;
    *result = (int)value;
    return true;
}

__device__ double mathblocks_probability_binomial(int n, int k)
{
    if (k < 0 || k > n)
        return 0.0;
    if (k > csharp2cuda_i32_sub(n, k))
        k = csharp2cuda_i32_sub(n, k);
    double result = 1.0;
    for (int index = 1; index <= k; csharp2cuda_i32_post_increment(index))
        result = result * (csharp2cuda_i32_add(csharp2cuda_i32_sub(n, k), index)) / index;
    return result;
}

__device__ bool mathblocks_probability_distribution(const double* values, int count)
{
    if (count <= 0)
        return false;
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
        if (values[index] < 0.0) return false;
    return fabs(mathblocks_compensated_sum(values, count) - 1.0) <= 1e-10;
}

__device__ double mathblocks_probability_entropy(const double* values, int count)
{
    double entropy = 0.0;
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
        if (values[index] > 0.0)
            entropy -= values[index] * mathblocks_natural_logarithm(values[index]);
    return entropy;
}

__device__ double mathblocks_probability_kl(
    const double* probabilities,
    const double* reference,
    int count)
{
    double result = 0.0;
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
        if (probabilities[index] > 0.0)
            result += probabilities[index] *
                mathblocks_natural_logarithm(probabilities[index] / reference[index]);
    return result;
}

__device__ double mathblocks_probability_log_gamma_core(double value)
{
    const double coefficients[8] =
    {
        676.5203681218851,
        -1259.1392167224028,
        771.32342877765313,
        -176.61502916214059,
        12.507343278686905,
        -0.13857109526572012,
        9.9843695780195716e-6,
        1.5056327351493116e-7
    };
    value -= 1.0;
    double sum = 0.99999999999980993;
    for (int index = 0; index < 8; csharp2cuda_i32_post_increment(index))
        sum += coefficients[index] / (value + index + 1.0);
    double t = value + 7.5;
    return 0.5 * mathblocks_natural_logarithm(
               2.0 * 3.141592653589793238462643383279502884) +
           (value + 0.5) * mathblocks_natural_logarithm(t) - t +
           mathblocks_natural_logarithm(sum);
}

__device__ double mathblocks_probability_log_gamma(double value)
{
    if (value < 0.5)
    {
        return mathblocks_natural_logarithm(3.141592653589793238462643383279502884) -
            mathblocks_natural_logarithm(
                mathblocks_sine(3.141592653589793238462643383279502884 * value)) -
            mathblocks_probability_log_gamma_core(1.0 - value);
    }
    return mathblocks_probability_log_gamma_core(value);
}

__device__ double mathblocks_probability_beta_fraction(double x, double left, double right)
{
    const int maximum_iterations = 256;
    const double tolerance = 3e-14;
    const double minimum = 1e-300;
    double qab = left + right;
    double qap = left + 1.0;
    double qam = left - 1.0;
    double c = 1.0;
    double d = 1.0 - qab * x / qap;
    if (fabs(d) < minimum) d = minimum;
    d = 1.0 / d;
    double result = d;
    for (int iteration = 1; iteration <= maximum_iterations; csharp2cuda_i32_post_increment(iteration))
    {
        double doubled = 2.0 * iteration;
        double coefficient = iteration * (right - iteration) * x /
            ((qam + doubled) * (left + doubled));
        d = 1.0 + coefficient * d;
        if (fabs(d) < minimum) d = minimum;
        c = 1.0 + coefficient / c;
        if (fabs(c) < minimum) c = minimum;
        d = 1.0 / d;
        result *= d * c;
        coefficient = -(left + iteration) * (qab + iteration) * x /
            ((left + doubled) * (qap + doubled));
        d = 1.0 + coefficient * d;
        if (fabs(d) < minimum) d = minimum;
        c = 1.0 + coefficient / c;
        if (fabs(c) < minimum) c = minimum;
        d = 1.0 / d;
        double delta = d * c;
        result *= delta;
        if (fabs(delta - 1.0) <= tolerance)
            break;
    }
    return result;
}

__device__ double mathblocks_probability_incomplete_beta(
    double x,
    double left,
    double right)
{
    if (x == 0.0) return 0.0;
    if (x == 1.0) return 1.0;
    double front = mathblocks_exponential(
        mathblocks_probability_log_gamma(left + right) -
        mathblocks_probability_log_gamma(left) -
        mathblocks_probability_log_gamma(right) +
        left * mathblocks_natural_logarithm(x) +
        right * mathblocks_log_one_plus(-x));
    return x < (left + 1.0) / (left + right + 2.0)
        ? front * mathblocks_probability_beta_fraction(x, left, right) / left
        : 1.0 - front * mathblocks_probability_beta_fraction(1.0 - x, right, left) / right;
}

__device__ MathBlockComplexValue mathblocks_probability_complex_cube_root(
    MathBlockComplexValue value)
{
    if (value.real == 0.0 && value.imaginary == 0.0)
        return mathblocks_complex_make(0.0, 0.0);
    return mathblocks_complex_from_polar(
        mathblocks_cube_root(mathblocks_complex_magnitude(value)),
        mathblocks_complex_phase(value) / 3.0);
}

extern "C" __global__ void mathblocks_probability(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    int thread = (int)threadIdx.x;
    const MathBlockSlot* first = input_count > 0 ? inputs[0] : nullptr;
    const MathBlockSlot* second = input_count > 1 ? inputs[1] : nullptr;
    const MathBlockSlot* third = input_count > 2 ? inputs[2] : nullptr;
    const MathBlockSlot* fourth = input_count > 3 ? inputs[3] : nullptr;
    if (thread == 0)
    {
        output->scalar_value = 0.0;
        output->boolean_value = 0;
        output->rows = 0;
        output->columns = 0;
        output->count = 0;
        output->valid = 1;
        for (int index = 0; index < input_count; csharp2cuda_i32_post_increment(index))
            if (inputs[index] == nullptr || !inputs[index]->valid) output->valid = 0;
    }
    __syncthreads();
    if (!output->valid)
        return;

    const double* a = first == nullptr ? nullptr : (double*)first->data_pointer;
    const double* b = second == nullptr ? nullptr : (double*)second->data_pointer;
    double* result = (double*)output->data_pointer;
    double* scratch = (double*)output->scratch_pointer;

    if (opcode == 3)
    {
        if (thread == 0)
        {
            if (first->count > 20)
            {
                output->valid = 0;
                return;
            }
            mathblocks_set_vector_shape(output, csharp2cuda_i32_sub((csharp2cuda_i32_shl(1, first->count)), 1));
        }
        __syncthreads();
        for (int mask = csharp2cuda_i32_add(thread, 1); output->valid && mask <= output->count; csharp2cuda_i32_add_assign(mask, blockDim.x))
        {
            double sum = 0.0;
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                if ((csharp2cuda_i32_and(mask, (csharp2cuda_i32_shl(1, index)))) != 0) sum += a[index];
            result[csharp2cuda_i32_sub(mask, 1)] = sum;
        }
        return;
    }

    if (opcode == 19)
    {
        int count = first->count <= 1 ? 1 : csharp2cuda_i32_sub(first->count, 1);
        if (thread == 0) mathblocks_set_vector_shape(output, count);
        __syncthreads();
        if (first->count <= 1)
        {
            if (thread == 0) result[0] = 0.0;
        }
        else
        {
            for (int index = csharp2cuda_i32_add(thread, 1); index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
                result[csharp2cuda_i32_sub(index, 1)] = index * a[index];
        }
        return;
    }

    if (opcode == 24 || opcode == 27)
    {
        if (thread == 0)
        {
            if (first->count <= 0)
            {
                output->valid = 0;
                return;
            }
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                if (a[index] < 0.0) output->valid = 0;
            mathblocks_set_vector_shape(output, first->count);
            if (opcode == 24)
            {
                double total = mathblocks_compensated_sum(a, first->count);
                for (int index = 0; output->valid && index < first->count; csharp2cuda_i32_post_increment(index))
                    result[index] = a[index] * (1.0 / total);
            }
            else
            {
                double maximum = a[0];
                for (int index = 1; index < first->count; csharp2cuda_i32_post_increment(index))
                    maximum = mathblocks_maximum(maximum, a[index]);
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    result[index] = mathblocks_exponential(a[index] - maximum);
                double total = mathblocks_compensated_sum(result, first->count);
                double scale = 1.0 / total;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    result[index] *= scale;
            }
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                if (!isfinite(result[index])) output->valid = 0;
        }
        return;
    }

    if (thread != 0)
        return;

    if (opcode == 0 || opcode == 1 || opcode == 2)
    {
        int first_integer = 0;
        int second_integer = 0;
        if (!mathblocks_probability_integer(first->scalar_value, &first_integer) || first_integer < 0 ||
            (opcode != 2 && !mathblocks_probability_integer(second->scalar_value, &second_integer)) ||
            (opcode == 2 && first_integer > 170) ||
            (opcode == 0 && (first_integer <= second_integer || second_integer < 0)))
        {
            output->valid = 0;
            return;
        }
        if (opcode == 0)
        {
            output->scalar_value = (double)(csharp2cuda_i32_sub(first_integer, second_integer)) /
                (csharp2cuda_i32_add(first_integer, second_integer)) *
                mathblocks_probability_binomial(csharp2cuda_i32_add(first_integer, second_integer), second_integer);
        }
        else if (opcode == 1)
        {
            output->scalar_value = mathblocks_probability_binomial(first_integer, second_integer);
        }
        else
        {
            double factorial = 1.0;
            for (int index = 2; index <= first_integer; csharp2cuda_i32_post_increment(index)) factorial *= index;
            output->scalar_value = factorial;
        }
        if (!isfinite(output->scalar_value)) output->valid = 0;
        return;
    }

    if (opcode >= 4 && opcode <= 16)
    {
        bool first_distribution = opcode == 12
            ? mathblocks_probability_distribution(a, first->count)
            : mathblocks_probability_distribution(a, first->count);
        bool pair = opcode == 4 || opcode == 7 || opcode == 9 || opcode == 10 ||
            opcode == 11 || opcode == 15;
        if (!first_distribution || (pair &&
            (first->count != second->count || !mathblocks_probability_distribution(b, second->count))))
        {
            output->valid = 0;
            return;
        }
        double value = 0.0;
        if (opcode == 4)
        {
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                value += mathblocks_square_root(a[index] * b[index]);
        }
        else if (opcode == 5)
        {
            value = mathblocks_probability_entropy(a, first->count) /
                mathblocks_natural_logarithm(2.0);
        }
        else if (opcode == 6)
        {
            int first_count = 0;
            int second_count = 0;
            int condition_count = 0;
            if (!mathblocks_probability_integer(second->scalar_value, &first_count) || first_count <= 0 ||
                !mathblocks_probability_integer(third->scalar_value, &second_count) || second_count <= 0 ||
                !mathblocks_probability_integer(fourth->scalar_value, &condition_count) || condition_count <= 0 ||
                csharp2cuda_i64_mul(csharp2cuda_i64_mul((long long)first_count, second_count), condition_count) != (long long)(first->count) || scratch == nullptr)
            {
                output->valid = 0;
                return;
            }
            int first_condition_count = csharp2cuda_i32_mul(first_count, condition_count);
            int second_condition_count = csharp2cuda_i32_mul(second_count, condition_count);
            double* first_condition = scratch;
            double* second_condition = csharp2cuda_pointer_add(first_condition, first_condition_count);
            double* condition = csharp2cuda_pointer_add(second_condition, second_condition_count);
            for (int index = 0; index < csharp2cuda_i32_add(csharp2cuda_i32_add(first_condition_count, second_condition_count), condition_count); csharp2cuda_i32_post_increment(index))
                scratch[index] = 0.0;
            for (int first_index = 0; first_index < first_count; csharp2cuda_i32_post_increment(first_index))
                for (int second_index = 0; second_index < second_count; csharp2cuda_i32_post_increment(second_index))
                    for (int state = 0; state < condition_count; csharp2cuda_i32_post_increment(state))
                    {
                        double probability = a[csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_add(csharp2cuda_i32_mul(first_index, second_count), second_index)), condition_count), state)];
                        first_condition[csharp2cuda_i32_add(csharp2cuda_i32_mul(first_index, condition_count), state)] += probability;
                        second_condition[csharp2cuda_i32_add(csharp2cuda_i32_mul(second_index, condition_count), state)] += probability;
                        condition[state] += probability;
                    }
            for (int first_index = 0; first_index < first_count; csharp2cuda_i32_post_increment(first_index))
                for (int second_index = 0; second_index < second_count; csharp2cuda_i32_post_increment(second_index))
                    for (int state = 0; state < condition_count; csharp2cuda_i32_post_increment(state))
                    {
                        double probability = a[csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_add(csharp2cuda_i32_mul(first_index, second_count), second_index)), condition_count), state)];
                        if (probability == 0.0) continue;
                        value += probability * mathblocks_natural_logarithm(
                            probability * condition[state] /
                            (first_condition[csharp2cuda_i32_add(csharp2cuda_i32_mul(first_index, condition_count), state)] *
                             second_condition[csharp2cuda_i32_add(csharp2cuda_i32_mul(second_index, condition_count), state)]));
                    }
        }
        else if (opcode == 7)
        {
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
            {
                if (a[index] > 0.0 && b[index] == 0.0)
                {
                    output->valid = 0;
                    return;
                }
                if (a[index] > 0.0)
                    value -= a[index] * mathblocks_natural_logarithm(b[index]);
            }
        }
        else if (opcode == 8)
        {
            value = 1.0 - mathblocks_compensated_product_sum(a, a, first->count);
        }
        else if (opcode == 9)
        {
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
            {
                double difference = mathblocks_square_root(a[index]) - mathblocks_square_root(b[index]);
                value += difference * difference;
            }
            value = mathblocks_square_root(value / 2.0);
        }
        else if (opcode == 10)
        {
            if (scratch == nullptr)
            {
                output->valid = 0;
                return;
            }
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                scratch[index] = (a[index] + b[index]) / 2.0;
            value = 0.5 * (mathblocks_probability_kl(a, scratch, first->count) +
                           mathblocks_probability_kl(b, scratch, first->count));
        }
        else if (opcode == 11)
        {
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                if (a[index] > 0.0 && b[index] == 0.0)
                {
                    output->valid = 0;
                    return;
                }
            value = mathblocks_probability_kl(a, b, first->count);
        }
        else if (opcode == 12)
        {
            int rows = first->rows;
            int columns = first->columns;
            if (scratch == nullptr)
            {
                output->valid = 0;
                return;
            }
            double* row_totals = scratch;
            double* column_totals = csharp2cuda_pointer_add(scratch, rows);
            for (int index = 0; index < csharp2cuda_i32_add(rows, columns); csharp2cuda_i32_post_increment(index)) scratch[index] = 0.0;
            for (int row = 0; row < rows; csharp2cuda_i32_post_increment(row))
                for (int column = 0; column < columns; csharp2cuda_i32_post_increment(column))
                {
                    double probability = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)];
                    row_totals[row] += probability;
                    column_totals[column] += probability;
                }
            for (int row = 0; row < rows; csharp2cuda_i32_post_increment(row))
                for (int column = 0; column < columns; csharp2cuda_i32_post_increment(column))
                {
                    double probability = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)];
                    if (probability > 0.0)
                        value += probability * mathblocks_natural_logarithm(
                            probability / (row_totals[row] * column_totals[column]));
                }
        }
        else if (opcode == 13 || opcode == 16)
        {
            double order = second->scalar_value;
            if (order <= 0.0)
            {
                output->valid = 0;
                return;
            }
            if (order == 1.0)
            {
                value = mathblocks_probability_entropy(a, first->count);
            }
            else
            {
                double sum = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    sum += mathblocks_power(a[index], order);
                value = opcode == 13
                    ? mathblocks_natural_logarithm(sum) / (1.0 - order)
                    : (1.0 - sum) / (order - 1.0);
            }
        }
        else if (opcode == 14)
        {
            value = mathblocks_probability_entropy(a, first->count);
        }
        else if (opcode == 15)
        {
            for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                value += fabs(a[index] - b[index]);
            value /= 2.0;
        }
        output->scalar_value = value;
        if (!isfinite(value)) output->valid = 0;
        return;
    }

    if (opcode == 17)
    {
        double parameter = second->scalar_value;
        if (first->count <= 0 || parameter < 0.0 || parameter > 1.0)
        {
            output->valid = 0;
            return;
        }
        int degree = csharp2cuda_i32_sub(first->count, 1);
        double value = 0.0;
        for (int index = 0; index <= degree; csharp2cuda_i32_post_increment(index))
            value += a[index] * mathblocks_probability_binomial(degree, index) *
                mathblocks_power(parameter, (double)index) *
                mathblocks_power(1.0 - parameter, (double)(csharp2cuda_i32_sub(degree, index)));
        output->scalar_value = value;
        if (!isfinite(value)) output->valid = 0;
        return;
    }

    if (opcode == 18)
    {
        if (first->count != 4)
        {
            output->valid = 0;
            return;
        }
        MathBlockComplexValue* roots = (MathBlockComplexValue*)output->data_pointer;
        mathblocks_complex_shape(output, 3);
        double constant = a[0];
        double linear = a[1];
        double quadratic = a[2];
        double leading = a[3];
        if (leading == 0.0)
        {
            output->valid = 0;
            return;
        }
        double normalized_a = quadratic / leading;
        double normalized_b = linear / leading;
        double normalized_c = constant / leading;
        double p = normalized_b - normalized_a * normalized_a / 3.0;
        double q = 2.0 * normalized_a * normalized_a * normalized_a / 27.0 -
            normalized_a * normalized_b / 3.0 + normalized_c;
        MathBlockComplexValue square_root = mathblocks_complex_square_root(
            mathblocks_complex_make(q * q / 4.0 + p * p * p / 27.0, 0.0));
        MathBlockComplexValue u = mathblocks_probability_complex_cube_root(
            mathblocks_complex_add(mathblocks_complex_make(-q / 2.0, 0.0), square_root));
        MathBlockComplexValue v = u.real == 0.0 && u.imaginary == 0.0
            ? mathblocks_probability_complex_cube_root(
                mathblocks_complex_subtract(mathblocks_complex_make(-q / 2.0, 0.0), square_root))
            : mathblocks_complex_divide(
                mathblocks_complex_make(-p, 0.0),
                mathblocks_complex_multiply(mathblocks_complex_make(3.0, 0.0), u));
        MathBlockComplexValue omega = mathblocks_complex_make(
            -0.5,
            mathblocks_square_root(3.0) / 2.0);
        roots[0] = mathblocks_complex_subtract(
            mathblocks_complex_add(u, v),
            mathblocks_complex_make(normalized_a / 3.0, 0.0));
        roots[1] = mathblocks_complex_subtract(
            mathblocks_complex_add(
                mathblocks_complex_multiply(omega, u),
                mathblocks_complex_multiply(mathblocks_complex_conjugate(omega), v)),
            mathblocks_complex_make(normalized_a / 3.0, 0.0));
        roots[2] = mathblocks_complex_subtract(
            mathblocks_complex_add(
                mathblocks_complex_multiply(mathblocks_complex_conjugate(omega), u),
                mathblocks_complex_multiply(omega, v)),
            mathblocks_complex_make(normalized_a / 3.0, 0.0));
        for (int index = 0; index < 3; csharp2cuda_i32_post_increment(index))
            if (!mathblocks_complex_finite(roots[index])) output->valid = 0;
        return;
    }

    if (opcode == 20)
    {
        int order = 0;
        if (!mathblocks_probability_integer(second->scalar_value, &order) ||
            order < 0 || order > first->count || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        for (int index = 0; index <= order; csharp2cuda_i32_post_increment(index)) scratch[index] = 0.0;
        scratch[0] = 1.0;
        for (int value_index = 0; value_index < first->count; csharp2cuda_i32_post_increment(value_index))
        {
            int maximum = order < csharp2cuda_i32_add(value_index, 1) ? order : csharp2cuda_i32_add(value_index, 1);
            for (int degree = maximum; degree >= 1; csharp2cuda_i32_post_decrement(degree))
                scratch[degree] += a[value_index] * scratch[csharp2cuda_i32_sub(degree, 1)];
        }
        output->scalar_value = scratch[order];
        return;
    }

    if (opcode == 21)
    {
        double value = 0.0;
        for (int index = csharp2cuda_i32_sub(first->count, 1); index >= 0; csharp2cuda_i32_post_decrement(index))
            value = value * second->scalar_value + a[index];
        output->scalar_value = value;
        if (!isfinite(value)) output->valid = 0;
        return;
    }

    if (opcode == 22)
    {
        if (first->count <= 0)
        {
            output->valid = 0;
            return;
        }
        double maximum = a[0];
        for (int index = 1; index < first->count; csharp2cuda_i32_post_increment(index))
            maximum = mathblocks_maximum(maximum, a[index]);
        double sum = 0.0;
        for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
            sum += mathblocks_exponential(a[index] - maximum);
        output->scalar_value = maximum + mathblocks_natural_logarithm(sum);
        if (!isfinite(output->scalar_value)) output->valid = 0;
        return;
    }

    if (opcode == 23)
    {
        output->scalar_value = 0.5 * (1.0 + mathblocks_error_function(
            first->scalar_value / mathblocks_square_root(2.0)));
        if (!isfinite(output->scalar_value)) output->valid = 0;
        return;
    }

    if (opcode == 25 || opcode == 26)
    {
        int count = 0;
        double rate = first->scalar_value;
        if (!mathblocks_probability_integer(second->scalar_value, &count) || count < 0 || rate < 0.0)
        {
            output->valid = 0;
            return;
        }
        double value = 0.0;
        int start = opcode == 25 ? 0 : count;
        int end = count;
        for (int index = start; index <= end; csharp2cuda_i32_post_increment(index))
        {
            double probability = rate == 0.0
                ? (index == 0 ? 1.0 : 0.0)
                : mathblocks_exponential(
                    -rate + index * mathblocks_natural_logarithm(rate) -
                    mathblocks_probability_log_gamma(index + 1.0));
            value += probability;
        }
        output->scalar_value = value;
        if (!isfinite(value)) output->valid = 0;
        return;
    }

    if (opcode == 28)
    {
        output->scalar_value = mathblocks_exponential(
            mathblocks_probability_log_gamma(first->scalar_value) +
            mathblocks_probability_log_gamma(second->scalar_value) -
            mathblocks_probability_log_gamma(first->scalar_value + second->scalar_value));
    }
    else if (opcode == 29)
    {
        output->scalar_value = mathblocks_probability_log_gamma(first->scalar_value);
    }
    else if (opcode == 30)
    {
        double x = first->scalar_value;
        double left = second->scalar_value;
        double right = third->scalar_value;
        if (x < 0.0 || x > 1.0 || left <= 0.0 || right <= 0.0)
        {
            output->valid = 0;
            return;
        }
        output->scalar_value = mathblocks_probability_incomplete_beta(x, left, right);
    }
    if (!isfinite(output->scalar_value)) output->valid = 0;
}