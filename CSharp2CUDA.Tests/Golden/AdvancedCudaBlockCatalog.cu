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

__device__ bool mathblocks_advanced_power_of_two(int value);
__device__ int mathblocks_advanced_log_two(int value);
__device__ int mathblocks_advanced_popcount(int value);
__device__ double mathblocks_advanced_factorial(int value);
__device__ bool mathblocks_advanced_distribution(const double* values, int count);
__device__ bool mathblocks_advanced_transition(
    const double* values,
    int rows,
    int columns);
__device__ void mathblocks_advanced_sort_descending(
    const double* values,
    int count,
    double* result);
extern "C" __global__ void mathblocks_advanced(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output);

__device__ bool mathblocks_advanced_power_of_two(int value)
{
    return value > 0 && (csharp2cuda_i32_and(value, (csharp2cuda_i32_sub(value, 1)))) == 0;
}

__device__ int mathblocks_advanced_log_two(int value)
{
    int result = 0;
    while (value > 1)
    {
        csharp2cuda_i32_shr_assign(value, 1);
        csharp2cuda_i32_post_increment(result);
    }
    return result;
}

__device__ int mathblocks_advanced_popcount(int value)
{
    int result = 0;
    while (value != 0)
    {
        csharp2cuda_i32_add_assign(result, csharp2cuda_i32_and(value, 1));
        csharp2cuda_i32_shr_assign(value, 1);
    }
    return result;
}

__device__ double mathblocks_advanced_factorial(int value)
{
    double result = 1.0;
    for (int index = 2; index <= value; csharp2cuda_i32_post_increment(index))
        result *= index;
    return result;
}

__device__ bool mathblocks_advanced_distribution(const double* values, int count)
{
    return mathblocks_probability_distribution(values, count);
}

__device__ bool mathblocks_advanced_transition(
    const double* values,
    int rows,
    int columns)
{
    if (rows != columns)
        return false;
    for (int row = 0; row < rows; csharp2cuda_i32_post_increment(row))
    {
        double sum = 0.0;
        for (int column = 0; column < columns; csharp2cuda_i32_post_increment(column))
        {
            double value = values[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)];
            if (value < 0.0)
                return false;
            sum += value;
        }
        if (fabs(sum - 1.0) > 1e-10)
            return false;
    }
    return true;
}

__device__ void mathblocks_advanced_sort_descending(
    const double* values,
    int count,
    double* result)
{
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
    {
        double value = values[index];
        int position = index;
        while (position > 0 && result[csharp2cuda_i32_sub(position, 1)] < value)
        {
            result[position] = result[csharp2cuda_i32_sub(position, 1)];
            csharp2cuda_i32_post_decrement(position);
        }
        result[position] = value;
    }
}

extern "C" __global__ void mathblocks_advanced(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    int thread = (int)threadIdx.x;
    if (blockIdx.x != 0)
        return;

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
    const double* c = third == nullptr ? nullptr : (double*)third->data_pointer;
    double* result = (double*)output->data_pointer;
    double* scratch = (double*)output->scratch_pointer;

    if (thread == 0)
    {
        switch (opcode)
        {
            case 0:
                if (first->count <= 0 || first->count >= 31 ||
                    second->count != (csharp2cuda_i32_shl(1, first->count)) || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    if (a[index] < 0.0)
                    {
                        output->valid = 0;
                        break;
                    }
                    int position = index;
                    while (position > 0 && a[(int)scratch[csharp2cuda_i32_sub(position, 1)]] > a[index])
                    {
                        scratch[position] = scratch[csharp2cuda_i32_sub(position, 1)];
                        csharp2cuda_i32_post_decrement(position);
                    }
                    scratch[position] = (double)index;
                }
                if (output->valid)
                {
                    double total = 0.0;
                    double previous = 0.0;
                    for (int position = 0; position < first->count; csharp2cuda_i32_post_increment(position))
                    {
                        int coalition = 0;
                        for (int index = position; index < first->count; csharp2cuda_i32_post_increment(index))
                            csharp2cuda_i32_or_assign(coalition, csharp2cuda_i32_shl(1, (int)scratch[index]));
                        int ordered = (int)scratch[position];
                        total += (a[ordered] - previous) * b[coalition];
                        previous = a[ordered];
                    }
                    output->scalar_value = total;
                }
                break;
            case 1:
                if (!mathblocks_advanced_power_of_two(first->count) || first->count > (csharp2cuda_i32_shl(1, 12)))
                {
                    output->valid = 0;
                    break;
                }
                output->boolean_value = 1;
                for (int left = 0; left < first->count && output->boolean_value; csharp2cuda_i32_post_increment(left))
                    for (int right = 0; right < first->count; csharp2cuda_i32_post_increment(right))
                        if (a[left] + a[right] < a[csharp2cuda_i32_or(left, right)] + a[csharp2cuda_i32_and(left, right)])
                        {
                            output->boolean_value = 0;
                            break;
                        }
                break;
            case 2:
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (!mathblocks_advanced_power_of_two(first->count))
                {
                    output->valid = 0;
                    break;
                }
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    result[index] = a[index];
                for (int bit = 0; bit < mathblocks_advanced_log_two(first->count); csharp2cuda_i32_post_increment(bit))
                    for (int mask = 0; mask < first->count; csharp2cuda_i32_post_increment(mask))
                        if ((csharp2cuda_i32_and(mask, (csharp2cuda_i32_shl(1, bit)))) != 0)
                            result[mask] -= result[csharp2cuda_i32_xor(mask, (csharp2cuda_i32_shl(1, bit)))];
                break;
            case 3:
                if (!mathblocks_advanced_power_of_two(first->count) || first->count > (csharp2cuda_i32_shl(1, 20)))
                {
                    output->valid = 0;
                    break;
                }
            {
                int player_count = mathblocks_advanced_log_two(first->count);
                mathblocks_sequence_set_vector_shape(output, player_count);
                double denominator = mathblocks_advanced_factorial(player_count);
                for (int player = 0; player < player_count; csharp2cuda_i32_post_increment(player))
                {
                    result[player] = 0.0;
                    for (int coalition = 0; coalition < first->count; csharp2cuda_i32_post_increment(coalition))
                    {
                        if ((csharp2cuda_i32_and(coalition, (csharp2cuda_i32_shl(1, player)))) != 0)
                            continue;
                        int size = mathblocks_advanced_popcount(coalition);
                        double weight = mathblocks_advanced_factorial(size) *
                            mathblocks_advanced_factorial(csharp2cuda_i32_sub(csharp2cuda_i32_sub(player_count, size), 1)) /
                            denominator;
                        result[player] += weight *
                            (a[csharp2cuda_i32_or(coalition, (csharp2cuda_i32_shl(1, player)))] - a[coalition]);
                    }
                }
                break;
            }
            case 4:
            case 5:
                mathblocks_sequence_set_vector_shape(output, third->count);
                if (first->count <= 0 || first->count != second->count || fourth->scalar_value < 0.0)
                {
                    output->valid = 0;
                    break;
                }
                for (int query = 0; query < third->count; csharp2cuda_i32_post_increment(query))
                {
                    double selected = opcode == 4
                        ? mathblocks_positive_infinity()
                        : -mathblocks_positive_infinity();
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        double candidate = opcode == 4
                            ? b[index] + fourth->scalar_value * fabs(c[query] - a[index])
                            : b[index] - fourth->scalar_value * fabs(c[query] - a[index]);
                        selected = opcode == 4
                            ? (selected < candidate ? selected : candidate)
                            : (selected > candidate ? selected : candidate);
                    }
                    result[query] = selected;
                }
                break;
            case 6:
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
            {
                double sum = 0.0;
                for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                    for (int right = 0; right < first->count; csharp2cuda_i32_post_increment(right))
                        sum += fabs(a[left] - a[right]);
                output->scalar_value = sum /
                    (2.0 * first->count * mathblocks_compensated_sum(a, first->count));
                break;
            }
            case 7:
                mathblocks_sequence_set_vector_shape(output, csharp2cuda_i32_add(first->count, 1));
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
                result[0] = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    double value = a[index];
                    int position = index;
                    while (position > 0 && result[position] > value)
                    {
                        result[csharp2cuda_i32_add(position, 1)] = result[position];
                        csharp2cuda_i32_post_decrement(position);
                    }
                    result[csharp2cuda_i32_add(position, 1)] = value;
                }
            {
                double total = mathblocks_compensated_sum(csharp2cuda_pointer_add(result, 1), first->count);
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    result[csharp2cuda_i32_add(index, 1)] = result[index] + result[csharp2cuda_i32_add(index, 1)] / total;
                break;
            }
            case 8:
                if (first->rows != first->columns || first->rows != second->count ||
                    !mathblocks_advanced_transition(a, first->rows, first->columns) ||
                    !mathblocks_advanced_distribution(b, second->count))
                {
                    output->valid = 0;
                    break;
                }
            {
                double total = 0.0;
                for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                    for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                    {
                        double forward = b[row] * a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)];
                        double reverse = b[column] * a[csharp2cuda_i32_add(csharp2cuda_i32_mul(column, first->columns), row)];
                        if (forward > 0.0 && reverse == 0.0)
                        {
                            output->valid = 0;
                            break;
                        }
                        if (forward > 0.0 && reverse > 0.0)
                            total += forward * mathblocks_natural_logarithm(forward / reverse);
                    }
                output->scalar_value = total;
                break;
            }
            case 9:
                mathblocks_sequence_set_vector_shape(output, first->rows);
                if (!mathblocks_advanced_transition(a, first->rows, first->columns) || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int iterations = 0;
                if (!mathblocks_sequence_positive_integer(second->scalar_value, &iterations))
                {
                    output->valid = 0;
                    break;
                }
                for (int index = 0; index < first->rows; csharp2cuda_i32_post_increment(index))
                    result[index] = 1.0 / first->rows;
                for (int iteration = 0; iteration < iterations; csharp2cuda_i32_post_increment(iteration))
                {
                    for (int index = 0; index < first->rows; csharp2cuda_i32_post_increment(index))
                        scratch[index] = 0.0;
                    for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                        for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                            scratch[column] += result[row] * a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)];
                    for (int index = 0; index < first->rows; csharp2cuda_i32_post_increment(index))
                        result[index] = scratch[index];
                }
                break;
            }
            case 10:
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (first->count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                double* means = scratch;
                int* weights = (int*)(csharp2cuda_pointer_add(means, first->count));
                int* starts = csharp2cuda_pointer_add(weights, first->count);
                int block_count = 0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    means[block_count] = a[index];
                    weights[block_count] = 1;
                    starts[block_count] = index;
                    csharp2cuda_i32_post_increment(block_count);
                    while (block_count >= 2 && means[csharp2cuda_i32_sub(block_count, 2)] > means[csharp2cuda_i32_sub(block_count, 1)])
                    {
                        int combined_weight = csharp2cuda_i32_add(weights[csharp2cuda_i32_sub(block_count, 2)], weights[csharp2cuda_i32_sub(block_count, 1)]);
                        means[csharp2cuda_i32_sub(block_count, 2)] =
                            (means[csharp2cuda_i32_sub(block_count, 2)] * weights[csharp2cuda_i32_sub(block_count, 2)] +
                             means[csharp2cuda_i32_sub(block_count, 1)] * weights[csharp2cuda_i32_sub(block_count, 1)]) /
                            combined_weight;
                        weights[csharp2cuda_i32_sub(block_count, 2)] = combined_weight;
                        csharp2cuda_i32_post_decrement(block_count);
                    }
                }
                for (int block = 0; block < block_count; csharp2cuda_i32_post_increment(block))
                {
                    int end = csharp2cuda_i32_add(block, 1) < block_count ? starts[csharp2cuda_i32_add(block, 1)] : first->count;
                    for (int index = starts[block]; index < end; csharp2cuda_i32_post_increment(index))
                        result[index] = means[block];
                }
                break;
            }
            case 11:
                if (first->count != second->count || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                double* left_sorted = scratch;
                double* right_sorted = csharp2cuda_pointer_add(scratch, first->count);
                mathblocks_advanced_sort_descending(a, first->count, left_sorted);
                mathblocks_advanced_sort_descending(b, first->count, right_sorted);
                double left_sum = 0.0;
                double right_sum = 0.0;
                bool majorizes = true;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    left_sum += left_sorted[index];
                    right_sum += right_sorted[index];
                    if (index < csharp2cuda_i32_sub(first->count, 1) && left_sum < right_sum)
                        majorizes = false;
                }
                output->boolean_value = majorizes && left_sum == right_sum ? 1 : 0;
                break;
            }
            case 12:
                if (first->count <= 0 || first->count != second->count)
                {
                    output->valid = 0;
                    break;
                }
            {
                double minimum = mathblocks_positive_infinity();
                double maximum = -mathblocks_positive_infinity();
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    if (a[index] <= 0.0 || b[index] <= 0.0)
                    {
                        output->valid = 0;
                        break;
                    }
                    double ratio = a[index] / b[index];
                    minimum = minimum < ratio ? minimum : ratio;
                    maximum = maximum > ratio ? maximum : ratio;
                }
                output->scalar_value = mathblocks_natural_logarithm(maximum / minimum);
                break;
            }
            case 13:
            case 16:
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (first->count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int* hull = (int*)scratch;
                int hull_count = 0;
                bool concave = opcode == 16;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    hull[csharp2cuda_i32_post_increment(hull_count)] = index;
                    while (hull_count >= 3)
                    {
                        int one = hull[csharp2cuda_i32_sub(hull_count, 3)];
                        int middle = hull[csharp2cuda_i32_sub(hull_count, 2)];
                        int last = hull[csharp2cuda_i32_sub(hull_count, 1)];
                        double first_slope = (a[middle] - a[one]) / (csharp2cuda_i32_sub(middle, one));
                        double second_slope = (a[last] - a[middle]) / (csharp2cuda_i32_sub(last, middle));
                        if (concave ? first_slope >= second_slope : first_slope <= second_slope)
                            break;
                        hull[csharp2cuda_i32_sub(hull_count, 2)] = hull[csharp2cuda_i32_sub(hull_count, 1)];
                        csharp2cuda_i32_post_decrement(hull_count);
                    }
                }
                for (int segment = 1; segment < hull_count; csharp2cuda_i32_post_increment(segment))
                {
                    int start = hull[csharp2cuda_i32_sub(segment, 1)];
                    int end = hull[segment];
                    for (int index = start; index <= end; csharp2cuda_i32_post_increment(index))
                    {
                        double weight = (double)(csharp2cuda_i32_sub(index, start)) / (csharp2cuda_i32_sub(end, start));
                        result[index] = a[start] * (1.0 - weight) + a[end] * weight;
                    }
                }
                break;
            }
            case 14:
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    scratch[index] = a[index];
                output->boolean_value = 1;
                for (int order = 0, length = first->count;
                     order < first->count && output->boolean_value;
                     csharp2cuda_i32_post_increment(order), csharp2cuda_i32_post_decrement(length))
                {
                    double sign = (csharp2cuda_i32_and(order, 1)) == 0 ? 1.0 : -1.0;
                    for (int index = 0; index < length; csharp2cuda_i32_post_increment(index))
                        if (sign * scratch[index] < 0.0)
                        {
                            output->boolean_value = 0;
                            break;
                        }
                    for (int index = 1; output->boolean_value && index < length; csharp2cuda_i32_post_increment(index))
                        scratch[csharp2cuda_i32_sub(index, 1)] = scratch[index] - scratch[csharp2cuda_i32_sub(index, 1)];
                }
                break;
            case 15:
                output->boolean_value = 1;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    if (a[index] < 0.0) output->boolean_value = 0;
                for (int index = 1; output->boolean_value && index < csharp2cuda_i32_sub(first->count, 1); csharp2cuda_i32_post_increment(index))
                    if (a[index] * a[index] < a[csharp2cuda_i32_sub(index, 1)] * a[csharp2cuda_i32_add(index, 1)])
                        output->boolean_value = 0;
                break;
            case 17:
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (!mathblocks_advanced_distribution(a, first->count))
                {
                    output->valid = 0;
                    break;
                }
            {
                double survival = 1.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    result[index] = a[index] / survival;
                    survival -= a[index];
                }
                break;
            }
            case 18:
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (first->count != second->count)
                {
                    output->valid = 0;
                    break;
                }
            {
                double survival = 1.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    if (a[index] < 0.0 || b[index] <= 0.0 || a[index] > b[index])
                    {
                        output->valid = 0;
                        break;
                    }
                    survival *= 1.0 - a[index] / b[index];
                    result[index] = survival;
                }
                break;
            }
        }

        if (output->valid &&
            opcode != 1 && opcode != 2 && opcode != 3 && opcode != 4 && opcode != 5 &&
            opcode != 7 && opcode != 9 && opcode != 10 && opcode != 11 && opcode != 13 &&
            opcode != 14 && opcode != 15 && opcode != 16 && opcode != 17 && opcode != 18 &&
            !isfinite(output->scalar_value))
        {
            output->valid = 0;
        }
        if (output->valid &&
            (opcode == 2 || opcode == 3 || opcode == 4 || opcode == 5 || opcode == 7 ||
             opcode == 9 || opcode == 10 || opcode == 13 || opcode == 16 || opcode == 17 ||
             opcode == 18))
        {
            for (int index = 0; index < output->count; csharp2cuda_i32_post_increment(index))
                if (!isfinite(result[index])) output->valid = 0;
        }
    }
}