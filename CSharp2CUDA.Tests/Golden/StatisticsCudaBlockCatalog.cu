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

__device__ double mathblocks_statistics_mean(const double* values, int count);
__device__ double mathblocks_statistics_population_variance(
    const double* values,
    int count);
__device__ double mathblocks_statistics_population_covariance(
    const double* left,
    const double* right,
    int count);
__device__ double mathblocks_statistics_pearson(
    const double* left,
    const double* right,
    int count);
__device__ void mathblocks_statistics_sort_copy(
    const double* values,
    int count,
    double* result);
__device__ void mathblocks_statistics_sort_in_place(double* values, int count);
__device__ double mathblocks_statistics_sorted_quantile(
    const double* sorted,
    int count,
    double probability);
__device__ double mathblocks_statistics_median(double* values, int count);
__device__ void mathblocks_statistics_rank(
    const double* values,
    int count,
    double* result);
__device__ void mathblocks_statistics_center_distance(
    const double* values,
    int count,
    double* result,
    double* row_means);
extern "C" __global__ void mathblocks_statistics(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output);

__device__ double mathblocks_statistics_mean(const double* values, int count)
{
    return mathblocks_compensated_sum(values, count) / count;
}

__device__ double mathblocks_statistics_population_variance(
    const double* values,
    int count)
{
    double mean = mathblocks_statistics_mean(values, count);
    double sum = 0.0;
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
    {
        double difference = values[index] - mean;
        sum += difference * difference;
    }
    return sum / count;
}

__device__ double mathblocks_statistics_population_covariance(
    const double* left,
    const double* right,
    int count)
{
    double left_mean = mathblocks_statistics_mean(left, count);
    double right_mean = mathblocks_statistics_mean(right, count);
    double sum = 0.0;
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
        sum += (left[index] - left_mean) * (right[index] - right_mean);
    return sum / count;
}

__device__ double mathblocks_statistics_pearson(
    const double* left,
    const double* right,
    int count)
{
    return mathblocks_statistics_population_covariance(left, right, count) /
        (mathblocks_square_root(mathblocks_statistics_population_variance(left, count)) *
         mathblocks_square_root(mathblocks_statistics_population_variance(right, count)));
}

__device__ void mathblocks_statistics_sort_copy(
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

__device__ void mathblocks_statistics_sort_in_place(double* values, int count)
{
    for (int index = 1; index < count; csharp2cuda_i32_post_increment(index))
    {
        double value = values[index];
        int position = index;
        while (position > 0 && values[csharp2cuda_i32_sub(position, 1)] > value)
        {
            values[position] = values[csharp2cuda_i32_sub(position, 1)];
            csharp2cuda_i32_post_decrement(position);
        }
        values[position] = value;
    }
}

__device__ double mathblocks_statistics_sorted_quantile(
    const double* sorted,
    int count,
    double probability)
{
    if (count == 1)
        return sorted[0];
    double position = probability * (csharp2cuda_i32_sub(count, 1));
    int lower = (int)floor(position);
    int upper = (int)ceil(position);
    double weight = position - lower;
    return sorted[lower] * (1.0 - weight) + sorted[upper] * weight;
}

__device__ double mathblocks_statistics_median(double* values, int count)
{
    mathblocks_statistics_sort_in_place(values, count);
    return mathblocks_statistics_sorted_quantile(values, count, 0.5);
}

__device__ void mathblocks_statistics_rank(
    const double* values,
    int count,
    double* result)
{
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
    {
        int lower = 0;
        int equal = 0;
        for (int candidate = 0; candidate < count; csharp2cuda_i32_post_increment(candidate))
        {
            if (values[candidate] < values[index])
                csharp2cuda_i32_post_increment(lower);
            else if (values[candidate] == values[index])
                csharp2cuda_i32_post_increment(equal);
        }
        result[index] = lower + (equal + 1.0) / 2.0;
    }
}

__device__ void mathblocks_statistics_center_distance(
    const double* values,
    int count,
    double* result,
    double* row_means)
{
    double total_mean = 0.0;
    for (int row = 0; row < count; csharp2cuda_i32_post_increment(row))
    {
        row_means[row] = 0.0;
        for (int column = 0; column < count; csharp2cuda_i32_post_increment(column))
        {
            double distance = fabs(values[row] - values[column]);
            result[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, count), column)] = distance;
            row_means[row] += distance;
            total_mean += distance;
        }
        row_means[row] /= count;
    }
    total_mean /= csharp2cuda_i32_mul(count, count);
    for (int row = 0; row < count; csharp2cuda_i32_post_increment(row))
        for (int column = 0; column < count; csharp2cuda_i32_post_increment(column))
            result[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, count), column)] -=
                row_means[row] + row_means[column] - total_mean;
}

extern "C" __global__ void mathblocks_statistics(
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
    if (thread == 0)
    {
        output->scalar_value = 0.0;
        output->boolean_value = 0;
        output->rows = 0;
        output->columns = 0;
        output->count = 0;
        output->valid = first == nullptr || first->valid;
        if (second != nullptr)
            output->valid = output->valid && second->valid;
    }
    __syncthreads();
    if (!output->valid)
        return;

    const double* a = first == nullptr ? nullptr : (double*)first->data_pointer;
    const double* b = second == nullptr ? nullptr : (double*)second->data_pointer;
    double* result = (double*)output->data_pointer;
    double* scratch = (double*)output->scratch_pointer;

    if (thread == 0)
    {
        switch (opcode)
        {
            case 0:
            {
                int lag = 0;
                if (!mathblocks_sequence_positive_integer(second->scalar_value, &lag) || lag >= first->count)
                {
                    output->valid = 0;
                    break;
                }
                int count = csharp2cuda_i32_sub(first->count, lag);
                output->scalar_value = mathblocks_statistics_pearson(a, csharp2cuda_pointer_add(a, lag), count);
                break;
            }
            case 1:
            case 18:
            {
                int order = 0;
                if (first->count <= 0 ||
                    !mathblocks_nonnegative_integer(second->scalar_value, &order))
                {
                    output->valid = 0;
                    break;
                }
                double mean = opcode == 1 ? mathblocks_statistics_mean(a, first->count) : 0.0;
                double sum = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    sum += mathblocks_power(a[index] - mean, (double)order);
                output->scalar_value = sum / first->count;
                break;
            }
            case 2:
            {
                int rows = first->rows;
                int columns = first->columns;
                mathblocks_sequence_set_matrix_shape(output, columns, columns);
                if (rows <= 0 || columns <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                for (int column = 0; column < columns; csharp2cuda_i32_post_increment(column))
                {
                    scratch[column] = 0.0;
                    for (int row = 0; row < rows; csharp2cuda_i32_post_increment(row))
                        scratch[column] += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)] / rows;
                }
                for (int left = 0; left < columns; csharp2cuda_i32_post_increment(left))
                {
                    for (int right = left; right < columns; csharp2cuda_i32_post_increment(right))
                    {
                        double sum = 0.0;
                        for (int row = 0; row < rows; csharp2cuda_i32_post_increment(row))
                        {
                            sum += (a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), left)] - scratch[left]) *
                                   (a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), right)] - scratch[right]);
                        }
                        double covariance = sum / rows;
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(left, columns), right)] = covariance;
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(right, columns), left)] = covariance;
                    }
                }
                break;
            }
            case 3:
            {
                int count = first->count;
                if (count <= 0 || count != second->count || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                double* left_distances = scratch;
                double* right_distances = csharp2cuda_pointer_add(scratch, csharp2cuda_i32_mul(count, count));
                double* row_means = csharp2cuda_pointer_add(right_distances, csharp2cuda_i32_mul(count, count));
                mathblocks_statistics_center_distance(a, count, left_distances, row_means);
                mathblocks_statistics_center_distance(b, count, right_distances, row_means);
                double covariance_square = 0.0;
                double left_variance_square = 0.0;
                double right_variance_square = 0.0;
                for (int index = 0; index < csharp2cuda_i32_mul(count, count); csharp2cuda_i32_post_increment(index))
                {
                    covariance_square += left_distances[index] * right_distances[index];
                    left_variance_square += left_distances[index] * left_distances[index];
                    right_variance_square += right_distances[index] * right_distances[index];
                }
                covariance_square /= csharp2cuda_i32_mul(count, count);
                left_variance_square /= csharp2cuda_i32_mul(count, count);
                right_variance_square /= csharp2cuda_i32_mul(count, count);
                output->scalar_value = mathblocks_square_root(
                    covariance_square /
                    mathblocks_square_root(left_variance_square * right_variance_square));
                break;
            }
            case 4:
            {
                mathblocks_sequence_set_vector_shape(output, csharp2cuda_i32_add(second->count, 1));
                for (int index = 1; index < second->count; csharp2cuda_i32_post_increment(index))
                {
                    if (b[index] <= b[csharp2cuda_i32_sub(index, 1)])
                    {
                        output->valid = 0;
                        break;
                    }
                }
                for (int index = 0; output->valid && index < output->count; csharp2cuda_i32_post_increment(index))
                    result[index] = 0.0;
                for (int value_index = 0; output->valid && value_index < first->count; csharp2cuda_i32_post_increment(value_index))
                {
                    int lower = 0;
                    int upper = second->count;
                    while (lower < upper)
                    {
                        int middle = csharp2cuda_i32_add(lower, csharp2cuda_i32_div((csharp2cuda_i32_sub(upper, lower)), 2));
                        if (a[value_index] <= b[middle])
                            upper = middle;
                        else
                            lower = csharp2cuda_i32_add(middle, 1);
                    }
                    result[lower] += 1.0;
                }
                break;
            }
            case 5:
            {
                if (first->count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                mathblocks_statistics_sort_copy(a, first->count, scratch);
                output->scalar_value =
                    mathblocks_statistics_sorted_quantile(scratch, first->count, 0.75) -
                    mathblocks_statistics_sorted_quantile(scratch, first->count, 0.25);
                break;
            }
            case 6:
            {
                if (first->count != second->count)
                {
                    output->valid = 0;
                    break;
                }
                long long concordant = 0;
                long long discordant = 0;
                long long left_ties = 0;
                long long right_ties = 0;
                for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                {
                    for (int right = csharp2cuda_i32_add(left, 1); right < first->count; csharp2cuda_i32_post_increment(right))
                    {
                        double left_difference = a[left] - a[right];
                        double right_difference = b[left] - b[right];
                        int left_sign = left_difference > 0.0 ? 1 : left_difference < 0.0 ? -1 : 0;
                        int right_sign = right_difference > 0.0 ? 1 : right_difference < 0.0 ? -1 : 0;
                        if (left_sign == 0 && right_sign == 0)
                            continue;
                        if (left_sign == 0)
                            csharp2cuda_i64_post_increment(left_ties);
                        else if (right_sign == 0)
                            csharp2cuda_i64_post_increment(right_ties);
                        else if (left_sign == right_sign)
                            csharp2cuda_i64_post_increment(concordant);
                        else
                            csharp2cuda_i64_post_increment(discordant);
                    }
                }
                output->scalar_value = (double)(csharp2cuda_i64_sub(concordant, discordant)) /
                    mathblocks_square_root(
                        (double)(csharp2cuda_i64_add(csharp2cuda_i64_add(concordant, discordant), left_ties)) *
                        (csharp2cuda_i64_add(csharp2cuda_i64_add(concordant, discordant), right_ties)));
                break;
            }
            case 7:
            case 8:
            case 9:
            case 11:
            case 12:
            case 20:
            {
                if (first->count <= 0 || first->count != second->count)
                {
                    output->valid = 0;
                    break;
                }
                double covariance = mathblocks_statistics_population_covariance(a, b, first->count);
                if (opcode == 12 || opcode == 20)
                {
                    output->scalar_value = opcode == 12
                        ? covariance
                        : covariance * first->count / (first->count - 1.0);
                }
                else if (opcode == 9)
                {
                    output->scalar_value = covariance /
                        mathblocks_statistics_population_variance(a, first->count);
                }
                else if (opcode == 7)
                {
                    double slope = covariance /
                        mathblocks_statistics_population_variance(a, first->count);
                    output->scalar_value = mathblocks_statistics_mean(b, first->count) -
                        slope * mathblocks_statistics_mean(a, first->count);
                }
                else
                {
                    double correlation = covariance /
                        (mathblocks_square_root(mathblocks_statistics_population_variance(a, first->count)) *
                         mathblocks_square_root(mathblocks_statistics_population_variance(b, first->count)));
                    output->scalar_value = opcode == 8 ? correlation * correlation : correlation;
                }
                break;
            }
            case 10:
            {
                if (first->count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                double* sorted = scratch;
                double* deviations = csharp2cuda_pointer_add(scratch, first->count);
                mathblocks_statistics_sort_copy(a, first->count, sorted);
                double median = mathblocks_statistics_sorted_quantile(sorted, first->count, 0.5);
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    deviations[index] = fabs(a[index] - median);
                mathblocks_statistics_sort_copy(deviations, first->count, sorted);
                output->scalar_value = mathblocks_statistics_sorted_quantile(sorted, first->count, 0.5);
                break;
            }
            case 13:
            case 14:
            {
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
                double mean = mathblocks_statistics_mean(a, first->count);
                double second_moment = 0.0;
                double higher_moment = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    double difference = a[index] - mean;
                    double square = difference * difference;
                    second_moment += square;
                    higher_moment += opcode == 13 ? square * square : square * difference;
                }
                second_moment /= first->count;
                higher_moment /= first->count;
                output->scalar_value = opcode == 13
                    ? higher_moment / (second_moment * second_moment) - 3.0
                    : higher_moment / mathblocks_power(second_moment, 1.5);
                break;
            }
            case 15:
            case 16:
            case 21:
            case 22:
            {
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
                double variance = mathblocks_statistics_population_variance(a, first->count);
                if (opcode == 21 || opcode == 22)
                    variance = variance * first->count / (first->count - 1.0);
                output->scalar_value = opcode == 15 || opcode == 21
                    ? mathblocks_square_root(variance)
                    : variance;
                break;
            }
            case 17:
            {
                if (first->count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                int count = 0;
                for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                    for (int right = left; right < first->count; csharp2cuda_i32_post_increment(right))
                        scratch[csharp2cuda_i32_post_increment(count)] = (a[left] + a[right]) / 2.0;
                output->scalar_value = mathblocks_statistics_median(scratch, count);
                break;
            }
            case 19:
                output->scalar_value = mathblocks_square_root(
                    mathblocks_compensated_product_sum(a, a, first->count) / first->count);
                break;
            case 23:
            {
                if (first->count <= 0 || first->count != second->count || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                double* left_ranks = scratch;
                double* right_ranks = csharp2cuda_pointer_add(scratch, first->count);
                mathblocks_statistics_rank(a, first->count, left_ranks);
                mathblocks_statistics_rank(b, first->count, right_ranks);
                output->scalar_value = mathblocks_statistics_pearson(
                    left_ranks,
                    right_ranks,
                    first->count);
                break;
            }
            case 24:
            {
                if (first->count != second->count || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                int count = 0;
                for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                {
                    for (int right = csharp2cuda_i32_add(left, 1); right < first->count; csharp2cuda_i32_post_increment(right))
                    {
                        double difference = a[right] - a[left];
                        if (difference != 0.0)
                            scratch[csharp2cuda_i32_post_increment(count)] = (b[right] - b[left]) / difference;
                    }
                }
                if (count == 0)
                    output->valid = 0;
                else
                    output->scalar_value = mathblocks_statistics_median(scratch, count);
                break;
            }
            case 25:
            case 26:
            {
                if (first->count <= 0 || first->count != second->count)
                {
                    output->valid = 0;
                    break;
                }
                double weight_sum = mathblocks_compensated_sum(b, first->count);
                double mean = mathblocks_compensated_product_sum(a, b, first->count) / weight_sum;
                if (opcode == 25)
                {
                    output->scalar_value = mean;
                }
                else
                {
                    double numerator = 0.0;
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        double difference = a[index] - mean;
                        numerator += b[index] * difference * difference;
                    }
                    output->scalar_value = numerator / weight_sum;
                }
                break;
            }
        }
        if (output->valid && opcode != 2 && opcode != 4 && !isfinite(output->scalar_value))
            output->valid = 0;
    }
}