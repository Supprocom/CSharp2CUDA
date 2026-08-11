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

__device__ void mathblocks_transport_sort_values(
    const double* values,
    int count,
    double* result);
__device__ void mathblocks_transport_sort_indices(
    const double* locations,
    int count,
    int* result);
__device__ double mathblocks_transport_mean_pairwise(
    const double* left,
    int left_count,
    const double* right,
    int right_count);
extern "C" __global__ void mathblocks_transport(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output);

__device__ void mathblocks_transport_sort_values(
    const double* values,
    int count,
    double* result)
{
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
    {
        double value = values[index];
        int position = index;
        while (position > 0 && result[csharp2cuda_i32_sub(position, 1)] > value)
        {
            result[position] = result[csharp2cuda_i32_sub(position, 1)];
            csharp2cuda_i32_post_decrement(position);
        }
        result[position] = value;
    }
}

__device__ void mathblocks_transport_sort_indices(
    const double* locations,
    int count,
    int* result)
{
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
    {
        int position = index;
        while (position > 0 && locations[result[csharp2cuda_i32_sub(position, 1)]] > locations[index])
        {
            result[position] = result[csharp2cuda_i32_sub(position, 1)];
            csharp2cuda_i32_post_decrement(position);
        }
        result[position] = index;
    }
}

__device__ double mathblocks_transport_mean_pairwise(
    const double* left,
    int left_count,
    const double* right,
    int right_count)
{
    double sum = 0.0;
    for (int left_index = 0; left_index < left_count; csharp2cuda_i32_post_increment(left_index))
        for (int right_index = 0; right_index < right_count; csharp2cuda_i32_post_increment(right_index))
            sum += fabs(left[left_index] - right[right_index]);
    return sum / (csharp2cuda_i32_mul(left_count, right_count));
}

extern "C" __global__ void mathblocks_transport(
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
    const MathBlockSlot* fifth = input_count > 4 ? inputs[4] : nullptr;
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
    const double* d = fourth == nullptr ? nullptr : (double*)fourth->data_pointer;
    double* result = (double*)output->data_pointer;
    double* scratch = (double*)output->scratch_pointer;

    if (thread == 0)
    {
        switch (opcode)
        {
            case 0:
                if (first->rows != first->columns || second->count != first->rows)
                {
                    output->valid = 0;
                    break;
                }
            {
                double total = 0.0;
                for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                {
                    int column = 0;
                    if (!mathblocks_nonnegative_integer(b[row], &column) || column >= first->columns)
                    {
                        output->valid = 0;
                        break;
                    }
                    total += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)];
                }
                output->scalar_value = total;
                break;
            }
            case 1:
                if (first->rows != second->rows || first->columns != second->columns)
                {
                    output->valid = 0;
                    break;
                }
            {
                double total = 0.0;
                for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                    for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                        total += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)] *
                                 b[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)];
                output->scalar_value = total;
                break;
            }
            case 2:
                if (first->count <= 0 || second->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
            {
                double cross = mathblocks_transport_mean_pairwise(
                    a, first->count, b, second->count);
                double left_within = mathblocks_transport_mean_pairwise(
                    a, first->count, a, first->count);
                double right_within = mathblocks_transport_mean_pairwise(
                    b, second->count, b, second->count);
                double squared = 2.0 * cross - left_within - right_within;
                output->scalar_value = mathblocks_square_root(squared > 0.0 ? squared : 0.0);
                break;
            }
            case 3:
                if (first->rows != first->columns || first->rows > 20 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int size = first->rows;
                int state_count = csharp2cuda_i32_shl(1, size);
                mathblocks_sequence_set_vector_shape(output, size);
                double* values = scratch;
                int* previous_mask = (int*)(csharp2cuda_pointer_add(values, state_count));
                int* chosen_column = csharp2cuda_pointer_add(previous_mask, state_count);
                for (int index = 0; index < state_count; csharp2cuda_i32_post_increment(index))
                {
                    values[index] = mathblocks_positive_infinity();
                    previous_mask[index] = 0;
                    chosen_column[index] = 0;
                }
                values[0] = 0.0;
                for (int mask = 0; mask < state_count; csharp2cuda_i32_post_increment(mask))
                {
                    int row = mathblocks_advanced_popcount(mask);
                    if (row >= size || !isfinite(values[mask]))
                        continue;
                    for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
                    {
                        if ((csharp2cuda_i32_and(mask, (csharp2cuda_i32_shl(1, column)))) != 0)
                            continue;
                        int next = csharp2cuda_i32_or(mask, (csharp2cuda_i32_shl(1, column)));
                        double candidate = values[mask] + a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)];
                        if (candidate >= values[next])
                            continue;
                        values[next] = candidate;
                        previous_mask[next] = mask;
                        chosen_column[next] = column;
                    }
                }
                int current = csharp2cuda_i32_sub(state_count, 1);
                for (int row = csharp2cuda_i32_sub(size, 1); row >= 0; csharp2cuda_i32_post_decrement(row))
                {
                    result[row] = (double)chosen_column[current];
                    current = previous_mask[current];
                }
                break;
            }
            case 4:
                mathblocks_sequence_set_matrix_shape(output, first->count, second->count);
                if (!mathblocks_advanced_distribution(a, first->count) ||
                    !mathblocks_advanced_distribution(b, second->count))
                {
                    output->valid = 0;
                    break;
                }
                for (int index = 0; index < output->count; csharp2cuda_i32_post_increment(index))
                    result[index] = 0.0;
            {
                int left_index = 0;
                int right_index = 0;
                double left_remaining = a[0];
                double right_remaining = b[0];
                while (left_index < first->count && right_index < second->count)
                {
                    double amount = left_remaining < right_remaining
                        ? left_remaining
                        : right_remaining;
                    result[csharp2cuda_i32_add(csharp2cuda_i32_mul(left_index, second->count), right_index)] += amount;
                    left_remaining -= amount;
                    right_remaining -= amount;
                    if (left_remaining == 0.0 && csharp2cuda_i32_pre_increment(left_index) < first->count)
                        left_remaining = a[left_index];
                    if (right_remaining == 0.0 && csharp2cuda_i32_pre_increment(right_index) < second->count)
                        right_remaining = b[right_index];
                }
                break;
            }
            case 5:
                if (first->count != second->count ||
                    !mathblocks_advanced_distribution(a, first->count) ||
                    !mathblocks_advanced_distribution(b, second->count))
                {
                    output->valid = 0;
                    break;
                }
            {
                double cumulative = 0.0;
                double total = 0.0;
                for (int index = 0; index < csharp2cuda_i32_sub(first->count, 1); csharp2cuda_i32_post_increment(index))
                {
                    cumulative += a[index] - b[index];
                    total += fabs(cumulative);
                }
                output->scalar_value = total;
                break;
            }
            case 6:
                mathblocks_sequence_set_matrix_shape(output, first->rows, first->columns);
                if (first->rows != second->count || first->columns != third->count ||
                    !mathblocks_advanced_distribution(b, second->count) ||
                    !mathblocks_advanced_distribution(c, third->count) ||
                    fourth->scalar_value <= 0.0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int iterations = 0;
                if (!mathblocks_sequence_positive_integer(fifth->scalar_value, &iterations) || iterations > 10000)
                {
                    output->valid = 0;
                    break;
                }
                double* kernel = scratch;
                double* left_scale = csharp2cuda_pointer_add(kernel, first->count);
                double* right_scale = csharp2cuda_pointer_add(left_scale, first->rows);
                for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                    for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                        kernel[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)] =
                            mathblocks_exponential(-a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)] /
                                                   fourth->scalar_value);
                for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                    left_scale[row] = 1.0;
                for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                    right_scale[column] = 1.0;
                for (int iteration = 0; iteration < iterations; csharp2cuda_i32_post_increment(iteration))
                {
                    for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                    {
                        double sum = 0.0;
                        for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                            sum += kernel[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)] * right_scale[column];
                        left_scale[row] = b[row] / sum;
                    }
                    for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                    {
                        double sum = 0.0;
                        for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                            sum += kernel[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)] * left_scale[row];
                        right_scale[column] = c[column] / sum;
                    }
                }
                for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                    for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)] =
                            left_scale[row] * kernel[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)] *
                            right_scale[column];
                break;
            }
            case 7:
                if (first->count <= 0 || first->count != second->count ||
                    third->scalar_value < 1.0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                double* left_sorted = scratch;
                double* right_sorted = csharp2cuda_pointer_add(scratch, first->count);
                mathblocks_transport_sort_values(a, first->count, left_sorted);
                mathblocks_transport_sort_values(b, second->count, right_sorted);
                double sum = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    sum += mathblocks_power(
                        fabs(left_sorted[index] - right_sorted[index]),
                        third->scalar_value);
                output->scalar_value = mathblocks_power(
                    sum / first->count,
                    1.0 / third->scalar_value);
                break;
            }
            case 8:
                if (first->count <= 0 || first->count != second->count ||
                    third->count <= 0 || third->count != fourth->count || scratch == nullptr ||
                    !mathblocks_advanced_distribution(b, second->count) ||
                    !mathblocks_advanced_distribution(d, fourth->count))
                {
                    output->valid = 0;
                    break;
                }
            {
                int* left_order = (int*)scratch;
                int* right_order = csharp2cuda_pointer_add(left_order, first->count);
                mathblocks_transport_sort_indices(a, first->count, left_order);
                mathblocks_transport_sort_indices(c, third->count, right_order);
                int left_index = 0;
                int right_index = 0;
                double left_remaining = b[left_order[0]];
                double right_remaining = d[right_order[0]];
                double total = 0.0;
                while (left_index < first->count && right_index < third->count)
                {
                    double amount = left_remaining < right_remaining
                        ? left_remaining
                        : right_remaining;
                    total += amount * fabs(
                        a[left_order[left_index]] - c[right_order[right_index]]);
                    left_remaining -= amount;
                    right_remaining -= amount;
                    if (left_remaining == 0.0 && csharp2cuda_i32_pre_increment(left_index) < first->count)
                        left_remaining = b[left_order[left_index]];
                    if (right_remaining == 0.0 && csharp2cuda_i32_pre_increment(right_index) < third->count)
                        right_remaining = d[right_order[right_index]];
                }
                output->scalar_value = total;
                break;
            }
            case 9:
            case 10:
                mathblocks_sequence_set_matrix_shape(output, first->rows, second->columns);
                if (first->columns != second->rows)
                {
                    output->valid = 0;
                    break;
                }
                for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                    for (int column = 0; column < second->columns; csharp2cuda_i32_post_increment(column))
                    {
                        double selected = opcode == 9
                            ? -mathblocks_positive_infinity()
                            : mathblocks_positive_infinity();
                        for (int inner = 0; inner < first->columns; csharp2cuda_i32_post_increment(inner))
                        {
                            double candidate = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), inner)] +
                                b[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, second->columns), column)];
                            selected = opcode == 9
                                ? (selected > candidate ? selected : candidate)
                                : (selected < candidate ? selected : candidate);
                        }
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, second->columns), column)] = selected;
                    }
                break;
        }

        if (output->valid &&
            opcode != 3 && opcode != 4 && opcode != 6 && opcode != 9 && opcode != 10 &&
            !isfinite(output->scalar_value))
        {
            output->valid = 0;
        }
        if (output->valid && (opcode == 3 || opcode == 4 || opcode == 6 || opcode == 9 || opcode == 10))
            for (int index = 0; index < output->count; csharp2cuda_i32_post_increment(index))
                if (!isfinite(result[index])) output->valid = 0;
    }
}