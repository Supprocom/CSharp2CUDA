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

struct MathBlockSequencePathRun;

struct MathBlockSequencePathRun
{
    int start;
    int length;
    double value;
};

__device__ bool mathblocks_sequence_positive_integer(double value, int* result);
__device__ void mathblocks_sequence_set_vector_shape(MathBlockSlot* output, int count);
__device__ void mathblocks_sequence_set_matrix_shape(
    MathBlockSlot* output,
    int rows,
    int columns);
__device__ unsigned long long mathblocks_sequence_order_key(double value);
__device__ bool mathblocks_sequence_is_power_of_two(int value);
__device__ void mathblocks_sequence_prepare_order_ranks(
    const double* values,
    int count,
    unsigned char* scratch,
    MathBlockSlot* output);
__device__ void mathblocks_sequence_heap_swap(
    int* heap,
    int left,
    int right,
    int* positions);
__device__ bool mathblocks_sequence_heap_precedes(
    int left,
    int right,
    const int* ranks,
    bool maximum);
__device__ void mathblocks_sequence_heap_sift_up(
    int* heap,
    int position,
    int* positions,
    const int* ranks,
    bool maximum);
__device__ void mathblocks_sequence_heap_sift_down(
    int* heap,
    int count,
    int position,
    int* positions,
    const int* ranks,
    bool maximum);
__device__ void mathblocks_sequence_heap_insert(
    int* heap,
    int* count,
    int item,
    int kind,
    int* positions,
    int* kinds,
    const int* ranks,
    bool maximum);
__device__ void mathblocks_sequence_heap_remove(
    int* heap,
    int* count,
    int item,
    int* positions,
    int* kinds,
    const int* ranks,
    bool maximum);
__device__ void mathblocks_sequence_rebalance_heaps(
    int* lower_heap,
    int* lower_count,
    int* upper_heap,
    int* upper_count,
    int target_lower_count,
    int* positions,
    int* kinds,
    const int* ranks);
__device__ void mathblocks_sequence_rolling_extreme(
    const double* values,
    int count,
    int width,
    double* result,
    int* deque,
    bool minimum);
__device__ void mathblocks_sequence_rolling_sum(
    const double* values,
    int count,
    int width,
    double* result);
extern "C" __global__ void mathblocks_sequence_path(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output);

__device__ bool mathblocks_sequence_positive_integer(double value, int* result)
{
    return mathblocks_nonnegative_integer(value, result) && *result > 0;
}

__device__ void mathblocks_sequence_set_vector_shape(MathBlockSlot* output, int count)
{
    output->rows = count;
    output->columns = 0;
    output->count = count;
    if (count < 0 || count > output->capacity)
    {
        output->valid = 0;
        return;
    }
}

__device__ void mathblocks_sequence_set_matrix_shape(
    MathBlockSlot* output,
    int rows,
    int columns)
{
    long long count = csharp2cuda_i64_mul((long long)rows, columns);
    output->rows = rows;
    output->columns = columns;
    output->count = count > 2147483647LL ? -1 : csharp2cuda_i32_from_bits((unsigned int)(count));
    if (rows < 0 || columns < 0 || count > output->capacity)
    {
        output->valid = 0;
        return;
    }
}

__device__ unsigned long long mathblocks_sequence_order_key(double value)
{
    unsigned long long bits = (unsigned long long)__double_as_longlong(value);
    if (value == 0.0)
        bits = 0ull;
    return (bits & 0x8000000000000000ull) != 0ull
        ? ~bits
        : bits ^ 0x8000000000000000ull;
}

__device__ bool mathblocks_sequence_is_power_of_two(int value)
{
    return value > 0 && (csharp2cuda_i32_and(value, (csharp2cuda_i32_sub(value, 1)))) == 0;
}

__device__ void mathblocks_sequence_prepare_order_ranks(
    const double* values,
    int count,
    unsigned char* scratch,
    MathBlockSlot* output)
{
    int thread = (int)threadIdx.x;
    unsigned long long* first_keys = (unsigned long long*)scratch;
    unsigned long long* second_keys = csharp2cuda_pointer_add(first_keys, count);
    int* first_indexes = (int*)(csharp2cuda_pointer_add(second_keys, count));
    int* second_indexes = csharp2cuda_pointer_add(first_indexes, count);
    int* ranks = csharp2cuda_pointer_add(second_indexes, count);
    int* lower_heap = csharp2cuda_pointer_add(ranks, count);
    int* upper_heap = csharp2cuda_pointer_add(lower_heap, output->boolean_value);
    int* positions = csharp2cuda_pointer_add(upper_heap, output->boolean_value);
    int* kinds = csharp2cuda_pointer_add(positions, count);
    int* zero_counts = csharp2cuda_pointer_add(kinds, count);
    int* zero_prefix = csharp2cuda_pointer_add(zero_counts, 128);

    for (int index = thread; index < count; csharp2cuda_i32_add_assign(index, blockDim.x))
    {
        if (!isfinite(values[index]))
            atomicExch(&output->valid, 0);
        first_keys[index] = mathblocks_sequence_order_key(values[index]);
        first_indexes[index] = index;
    }
    __syncthreads();
    if (!output->valid)
        return;

    unsigned long long* source_keys = first_keys;
    unsigned long long* destination_keys = second_keys;
    int* source_indexes = first_indexes;
    int* destination_indexes = second_indexes;
    for (int bit = 0; bit < 64; csharp2cuda_i32_post_increment(bit))
    {
        int begin = csharp2cuda_i32_from_bits((unsigned int)((csharp2cuda_i64_div((csharp2cuda_i64_mul((long long)count, thread)), blockDim.x))));
        int end = csharp2cuda_i32_from_bits((unsigned int)((csharp2cuda_i64_div((csharp2cuda_i64_mul((long long)count, (csharp2cuda_i32_add(thread, 1)))), blockDim.x))));
        int zeros = 0;
        for (int index = begin; index < end; csharp2cuda_i32_post_increment(index))
            if (((csharp2cuda_u64_shr(source_keys[index], bit)) & 1ull) == 0ull)
                csharp2cuda_i32_post_increment(zeros);
        zero_counts[thread] = zeros;
        __syncthreads();
        if (thread == 0)
        {
            int prefix = 0;
            for (int lane = 0; lane < blockDim.x; csharp2cuda_i32_post_increment(lane))
            {
                zero_prefix[lane] = prefix;
                csharp2cuda_i32_add_assign(prefix, zero_counts[lane]);
            }
            output->columns = prefix;
        }
        __syncthreads();
        int zero_destination = zero_prefix[thread];
        int one_destination = csharp2cuda_i32_sub(csharp2cuda_i32_add(output->columns, begin), zero_destination);
        for (int index = begin; index < end; csharp2cuda_i32_post_increment(index))
        {
            unsigned long long key = source_keys[index];
            int destination = ((csharp2cuda_u64_shr(key, bit)) & 1ull) == 0ull
                ? csharp2cuda_i32_post_increment(zero_destination)
                : csharp2cuda_i32_post_increment(one_destination);
            destination_keys[destination] = key;
            destination_indexes[destination] = source_indexes[index];
        }
        __syncthreads();
        unsigned long long* key_swap = source_keys;
        source_keys = destination_keys;
        destination_keys = key_swap;
        int* index_swap = source_indexes;
        source_indexes = destination_indexes;
        destination_indexes = index_swap;
    }
    for (int index = thread; index < count; csharp2cuda_i32_add_assign(index, blockDim.x))
        ranks[source_indexes[index]] = index;
    __syncthreads();
    if (thread == 0)
        output->columns = 0;
    __syncthreads();
}

__device__ void mathblocks_sequence_heap_swap(
    int* heap,
    int left,
    int right,
    int* positions)
{
    int value = heap[left];
    heap[left] = heap[right];
    heap[right] = value;
    positions[heap[left]] = left;
    positions[heap[right]] = right;
}

__device__ bool mathblocks_sequence_heap_precedes(
    int left,
    int right,
    const int* ranks,
    bool maximum)
{
    return maximum
        ? ranks[left] > ranks[right]
        : ranks[left] < ranks[right];
}

__device__ void mathblocks_sequence_heap_sift_up(
    int* heap,
    int position,
    int* positions,
    const int* ranks,
    bool maximum)
{
    while (position > 0)
    {
        int parent = csharp2cuda_i32_shr((csharp2cuda_i32_sub(position, 1)), 1);
        if (!mathblocks_sequence_heap_precedes(
                heap[position],
                heap[parent],
                ranks,
                maximum))
        {
            break;
        }
        mathblocks_sequence_heap_swap(heap, position, parent, positions);
        position = parent;
    }
}

__device__ void mathblocks_sequence_heap_sift_down(
    int* heap,
    int count,
    int position,
    int* positions,
    const int* ranks,
    bool maximum)
{
    while (true)
    {
        int left = csharp2cuda_i32_add(csharp2cuda_i32_mul(position, 2), 1);
        if (left >= count)
            return;
        int right = csharp2cuda_i32_add(left, 1);
        int selected = right < count && mathblocks_sequence_heap_precedes(
                heap[right],
                heap[left],
                ranks,
                maximum)
            ? right
            : left;
        if (!mathblocks_sequence_heap_precedes(
                heap[selected],
                heap[position],
                ranks,
                maximum))
        {
            return;
        }
        mathblocks_sequence_heap_swap(heap, position, selected, positions);
        position = selected;
    }
}

__device__ void mathblocks_sequence_heap_insert(
    int* heap,
    int* count,
    int item,
    int kind,
    int* positions,
    int* kinds,
    const int* ranks,
    bool maximum)
{
    int position = csharp2cuda_i32_post_increment((*count));
    heap[position] = item;
    positions[item] = position;
    kinds[item] = kind;
    mathblocks_sequence_heap_sift_up(
        heap,
        position,
        positions,
        ranks,
        maximum);
}

__device__ void mathblocks_sequence_heap_remove(
    int* heap,
    int* count,
    int item,
    int* positions,
    int* kinds,
    const int* ranks,
    bool maximum)
{
    int position = positions[item];
    int replacement = heap[csharp2cuda_i32_pre_decrement((*count))];
    kinds[item] = -1;
    positions[item] = -1;
    if (position >= *count)
        return;
    heap[position] = replacement;
    positions[replacement] = position;
    if (position > 0 && mathblocks_sequence_heap_precedes(
            heap[position],
            heap[csharp2cuda_i32_shr((csharp2cuda_i32_sub(position, 1)), 1)],
            ranks,
            maximum))
    {
        mathblocks_sequence_heap_sift_up(
            heap,
            position,
            positions,
            ranks,
            maximum);
    }
    else
    {
        mathblocks_sequence_heap_sift_down(
            heap,
            *count,
            position,
            positions,
            ranks,
            maximum);
    }
}

__device__ void mathblocks_sequence_rebalance_heaps(
    int* lower_heap,
    int* lower_count,
    int* upper_heap,
    int* upper_count,
    int target_lower_count,
    int* positions,
    int* kinds,
    const int* ranks)
{
    while (*lower_count > target_lower_count)
    {
        int item = lower_heap[0];
        mathblocks_sequence_heap_remove(
            lower_heap,
            lower_count,
            item,
            positions,
            kinds,
            ranks,
            true);
        mathblocks_sequence_heap_insert(
            upper_heap,
            upper_count,
            item,
            1,
            positions,
            kinds,
            ranks,
            false);
    }
    while (*lower_count < target_lower_count)
    {
        int item = upper_heap[0];
        mathblocks_sequence_heap_remove(
            upper_heap,
            upper_count,
            item,
            positions,
            kinds,
            ranks,
            false);
        mathblocks_sequence_heap_insert(
            lower_heap,
            lower_count,
            item,
            0,
            positions,
            kinds,
            ranks,
            true);
    }
}

__device__ void mathblocks_sequence_rolling_extreme(
    const double* values,
    int count,
    int width,
    double* result,
    int* deque,
    bool minimum)
{
    int head = 0;
    int tail = 0;
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
    {
        while (head < tail && deque[head] <= csharp2cuda_i32_sub(index, width))
            csharp2cuda_i32_post_increment(head);
        while (head < tail && (minimum
            ? values[deque[csharp2cuda_i32_sub(tail, 1)]] > values[index]
            : values[deque[csharp2cuda_i32_sub(tail, 1)]] < values[index]))
        {
            csharp2cuda_i32_post_decrement(tail);
        }
        deque[csharp2cuda_i32_post_increment(tail)] = index;
        if (index >= csharp2cuda_i32_sub(width, 1))
            result[csharp2cuda_i32_add(csharp2cuda_i32_sub(index, width), 1)] = values[deque[head]];
    }
}

__device__ void mathblocks_sequence_rolling_sum(
    const double* values,
    int count,
    int width,
    double* result)
{
    double sum = 0.0;
    for (int index = 0; index < width; csharp2cuda_i32_post_increment(index))
        sum += values[index];
    result[0] = sum;
    for (int index = width; index < count; csharp2cuda_i32_post_increment(index))
    {
        sum += values[index] - values[csharp2cuda_i32_sub(index, width)];
        result[csharp2cuda_i32_add(csharp2cuda_i32_sub(index, width), 1)] = sum;
    }
}

extern "C" __global__ void mathblocks_sequence_path(
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
        if (third != nullptr)
            output->valid = output->valid && third->valid;
    }
    __syncthreads();
    if (!output->valid)
        return;

    const double* a = first == nullptr ? nullptr : (double*)first->data_pointer;
    const double* b = second == nullptr ? nullptr : (double*)second->data_pointer;
    const int* boolean_a = first == nullptr ? nullptr : (int*)first->data_pointer;
    double* result = (double*)output->data_pointer;
    double* scratch = (double*)output->scratch_pointer;

    switch (opcode)
    {
        case 0:
            if (thread == 0)
            {
                int count = csharp2cuda_i32_sub(csharp2cuda_i32_add(first->count, second->count), 1);
                mathblocks_sequence_set_vector_shape(output, count);
                if (first->count <= 0 || second->count <= 0)
                    output->valid = 0;
                for (int index = 0; output->valid && index < count; csharp2cuda_i32_post_increment(index))
                    result[index] = 0.0;
                for (int left = 0; output->valid && left < first->count; csharp2cuda_i32_post_increment(left))
                {
                    for (int right = 0; right < second->count; csharp2cuda_i32_post_increment(right))
                    {
                        result[csharp2cuda_i32_add(left, right)] += a[left] * b[right];
                        if (!isfinite(result[csharp2cuda_i32_add(left, right)]))
                            output->valid = 0;
                    }
                }
            }
            break;
        case 1:
        {
            int lag = 0;
            if (thread == 0)
            {
                if (!mathblocks_sequence_positive_integer(second->scalar_value, &lag) || lag >= first->count)
                    output->valid = 0;
                else
                    mathblocks_sequence_set_vector_shape(output, csharp2cuda_i32_sub(first->count, lag));
                output->scalar_value = (double)lag;
            }
            __syncthreads();
            lag = (int)output->scalar_value;
            for (int index = thread; output->valid && index < output->count; csharp2cuda_i32_add_assign(index, blockDim.x))
                result[index] = a[csharp2cuda_i32_add(index, lag)] - a[index];
            break;
        }
        case 2:
            if (thread == 0)
            {
                double alpha = second->scalar_value;
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (first->count <= 0 || !(alpha > 0.0 && alpha <= 1.0))
                    output->valid = 0;
                if (output->valid)
                {
                    result[0] = a[0];
                    for (int index = 1; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        result[index] = alpha * a[index] + (1.0 - alpha) * result[csharp2cuda_i32_sub(index, 1)];
                        if (!isfinite(result[index]))
                            output->valid = 0;
                    }
                }
            }
            break;
        case 3:
        case 6:
            if (thread == 0)
            {
                int width = 0;
                if (!mathblocks_sequence_positive_integer(second->scalar_value, &width) ||
                    width > first->count || scratch == nullptr)
                {
                    output->valid = 0;
                }
                else
                {
                    mathblocks_sequence_set_vector_shape(output, csharp2cuda_i32_add(csharp2cuda_i32_sub(first->count, width), 1));
                    int* deque = (int*)scratch;
                    int head = 0;
                    int tail = 0;
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        while (head < tail && deque[head] <= csharp2cuda_i32_sub(index, width))
                            csharp2cuda_i32_post_increment(head);
                        while (head < tail && (opcode == 6
                            ? a[deque[csharp2cuda_i32_sub(tail, 1)]] >= a[index]
                            : a[deque[csharp2cuda_i32_sub(tail, 1)]] <= a[index]))
                        {
                            csharp2cuda_i32_post_decrement(tail);
                        }
                        deque[csharp2cuda_i32_post_increment(tail)] = index;
                        if (index >= csharp2cuda_i32_sub(width, 1))
                            result[csharp2cuda_i32_add(csharp2cuda_i32_sub(index, width), 1)] = a[deque[head]];
                    }
                }
            }
            break;
        case 4:
        case 9:
            if (thread == 0)
            {
                int width = 0;
                if (!mathblocks_sequence_positive_integer(second->scalar_value, &width) || width > first->count)
                {
                    output->valid = 0;
                }
                else
                {
                    mathblocks_sequence_set_vector_shape(output, csharp2cuda_i32_add(csharp2cuda_i32_sub(first->count, width), 1));
                    mathblocks_sequence_rolling_sum(a, first->count, width, result);
                    if (opcode == 4)
                    {
                        double scale = 1.0 / width;
                        for (int index = 0; index < output->count; csharp2cuda_i32_post_increment(index))
                            result[index] *= scale;
                    }
                }
            }
            break;
        case 5:
        case 7:
        {
            int width = 0;
            double probability = 0.0;
            if (thread == 0)
            {
                probability = opcode == 5 ? 0.5 : third->scalar_value;
                if (!mathblocks_sequence_positive_integer(second->scalar_value, &width) ||
                    width > first->count || !(probability >= 0.0 && probability <= 1.0) ||
                    (width > 1 && scratch == nullptr))
                {
                    output->valid = 0;
                }
                else
                {
                    mathblocks_sequence_set_vector_shape(output, csharp2cuda_i32_add(csharp2cuda_i32_sub(first->count, width), 1));
                }
                output->scalar_value = probability;
                output->boolean_value = width;
            }
            __syncthreads();
            width = output->boolean_value;
            probability = output->scalar_value;
            if (!output->valid)
                break;
            if (width == 1)
            {
                for (int index = thread; index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
                    result[index] = a[index];
                __syncthreads();
                if (thread == 0)
                {
                    output->scalar_value = (double)first->count;
                    output->boolean_value = 0;
                }
                break;
            }
            if (probability == 0.0 || probability == 1.0)
            {
                if (thread == 0)
                {
                    mathblocks_sequence_rolling_extreme(
                        a,
                        first->count,
                        width,
                        result,
                        (int*)scratch,
                        probability == 0.0);
                    output->scalar_value = (double)(
                        csharp2cuda_i64_add(csharp2cuda_i64_mul((long long)first->count, 3), output->count));
                    output->boolean_value = 0;
                }
                __syncthreads();
                break;
            }

            unsigned char* order_scratch = (unsigned char*)scratch;
            mathblocks_sequence_prepare_order_ranks(
                a,
                first->count,
                order_scratch,
                output);
            if (!output->valid)
                break;
            unsigned long long* first_keys = (unsigned long long*)order_scratch;
            unsigned long long* second_keys = csharp2cuda_pointer_add(first_keys, first->count);
            int* first_indexes = (int*)(csharp2cuda_pointer_add(second_keys, first->count));
            int* second_indexes = csharp2cuda_pointer_add(first_indexes, first->count);
            int* ranks = csharp2cuda_pointer_add(second_indexes, first->count);
            int* lower_heap = csharp2cuda_pointer_add(ranks, first->count);
            int* upper_heap = csharp2cuda_pointer_add(lower_heap, width);
            int* positions = csharp2cuda_pointer_add(upper_heap, width);
            int* kinds = csharp2cuda_pointer_add(positions, first->count);
            double quantile_position = probability * (csharp2cuda_i32_sub(width, 1));
            int lower_index = (int)floor(quantile_position);
            int upper_index = (int)ceil(quantile_position);
            double weight = quantile_position - lower_index;

            if (output->count == 1)
            {
                if (thread == 0)
                {
                    double lower_value = a[first_indexes[lower_index]];
                    double upper_value = a[first_indexes[upper_index]];
                    result[0] = lower_value * (1.0 - weight) + upper_value * weight;
                    output->valid = isfinite(result[0]);
                    output->scalar_value = (double)(csharp2cuda_i64_add(csharp2cuda_i64_mul((long long)first->count, 64), 2));
                    output->boolean_value = 64;
                }
                __syncthreads();
                break;
            }

            for (int index = thread; index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
            {
                positions[index] = -1;
                kinds[index] = -1;
            }
            __syncthreads();
            if (thread == 0)
            {
                int lower_count = 0;
                int upper_count = 0;
                int target_lower_count = csharp2cuda_i32_add(lower_index, 1);
                for (int index = 0; index < width; csharp2cuda_i32_post_increment(index))
                {
                    if (lower_count == 0 && upper_count == 0)
                    {
                        mathblocks_sequence_heap_insert(
                            lower_heap,
                            &lower_count,
                            index,
                            0,
                            positions,
                            kinds,
                            ranks,
                            true);
                    }
                    else if (lower_count == 0)
                    {
                        mathblocks_sequence_heap_insert(
                            upper_heap,
                            &upper_count,
                            index,
                            1,
                            positions,
                            kinds,
                            ranks,
                            false);
                    }
                    else if (ranks[index] <= ranks[lower_heap[0]])
                    {
                        mathblocks_sequence_heap_insert(
                            lower_heap,
                            &lower_count,
                            index,
                            0,
                            positions,
                            kinds,
                            ranks,
                            true);
                    }
                    else
                    {
                        mathblocks_sequence_heap_insert(
                            upper_heap,
                            &upper_count,
                            index,
                            1,
                            positions,
                            kinds,
                            ranks,
                            false);
                    }
                    int active_target = target_lower_count < csharp2cuda_i32_add(index, 1)
                        ? target_lower_count
                        : csharp2cuda_i32_add(index, 1);
                    mathblocks_sequence_rebalance_heaps(
                        lower_heap,
                        &lower_count,
                        upper_heap,
                        &upper_count,
                        active_target,
                        positions,
                        kinds,
                        ranks);
                }
                for (int start = 0; start < output->count; csharp2cuda_i32_post_increment(start))
                {
                    double lower_value = a[lower_heap[0]];
                    double upper_value = lower_index == upper_index
                        ? lower_value
                        : a[upper_heap[0]];
                    result[start] = lower_value * (1.0 - weight) + upper_value * weight;
                    if (!isfinite(result[start]))
                    {
                        output->valid = 0;
                        break;
                    }
                    if (csharp2cuda_i32_add(start, 1) == output->count)
                        break;
                    int outgoing = start;
                    int incoming = csharp2cuda_i32_add(start, width);
                    if (kinds[outgoing] == 0)
                    {
                        mathblocks_sequence_heap_remove(
                            lower_heap,
                            &lower_count,
                            outgoing,
                            positions,
                            kinds,
                            ranks,
                            true);
                    }
                    else
                    {
                        mathblocks_sequence_heap_remove(
                            upper_heap,
                            &upper_count,
                            outgoing,
                            positions,
                            kinds,
                            ranks,
                            false);
                    }
                    if (lower_count == 0)
                    {
                        mathblocks_sequence_heap_insert(
                            upper_heap,
                            &upper_count,
                            incoming,
                            1,
                            positions,
                            kinds,
                            ranks,
                            false);
                    }
                    else if (ranks[incoming] <= ranks[lower_heap[0]])
                    {
                        mathblocks_sequence_heap_insert(
                            lower_heap,
                            &lower_count,
                            incoming,
                            0,
                            positions,
                            kinds,
                            ranks,
                            true);
                    }
                    else
                    {
                        mathblocks_sequence_heap_insert(
                            upper_heap,
                            &upper_count,
                            incoming,
                            1,
                            positions,
                            kinds,
                            ranks,
                            false);
                    }
                    mathblocks_sequence_rebalance_heaps(
                        lower_heap,
                        &lower_count,
                        upper_heap,
                        &upper_count,
                        target_lower_count,
                        positions,
                        kinds,
                        ranks);
                }
                int heap_height = 1;
                for (int value = width; value > 1; value = csharp2cuda_i32_shr((csharp2cuda_i32_add(value, 1)), 1))
                    csharp2cuda_i32_post_increment(heap_height);
                long long heap_bound = csharp2cuda_i64_mul((csharp2cuda_i64_add((long long)width, csharp2cuda_i64_mul(2LL, (csharp2cuda_i32_sub(first->count, width))))), heap_height);
                long long selection_bound = csharp2cuda_i64_mul((long long)output->count, 2);
                output->scalar_value = (double)(
                    csharp2cuda_i64_add(csharp2cuda_i64_add(csharp2cuda_i64_mul((long long)first->count, 64), heap_bound), selection_bound));
                output->boolean_value = 64;
            }
            __syncthreads();
            break;
        }
        case 8:
        case 10:
            if (thread == 0)
            {
                int width = 0;
                if (!mathblocks_sequence_positive_integer(second->scalar_value, &width) || width > first->count)
                {
                    output->valid = 0;
                }
                else
                {
                    mathblocks_sequence_set_vector_shape(output, csharp2cuda_i32_add(csharp2cuda_i32_sub(first->count, width), 1));
                    for (int start = 0; start < output->count; csharp2cuda_i32_post_increment(start))
                    {
                        double mean = 0.0;
                        for (int index = 0; index < width; csharp2cuda_i32_post_increment(index))
                            mean += a[csharp2cuda_i32_add(start, index)];
                        mean /= width;
                        double sum_squares = 0.0;
                        for (int index = 0; index < width; csharp2cuda_i32_post_increment(index))
                        {
                            double difference = a[csharp2cuda_i32_add(start, index)] - mean;
                            sum_squares += difference * difference;
                        }
                        double deviation = mathblocks_square_root(sum_squares / width);
                        result[start] = opcode == 8 ? deviation : deviation * deviation;
                        if (!isfinite(result[start]))
                            output->valid = 0;
                    }
                }
            }
            break;
        case 11:
            if (thread == 0)
            {
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (!mathblocks_sequence_is_power_of_two(first->count) || scratch == nullptr)
                {
                    output->valid = 0;
                }
                else
                {
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                        result[index] = a[index];
                    int length = first->count;
                    double scale = 1.0 / mathblocks_square_root(2.0);
                    while (length > 1)
                    {
                        int half = csharp2cuda_i32_div(length, 2);
                        for (int index = 0; index < half; csharp2cuda_i32_post_increment(index))
                        {
                            scratch[index] = (result[csharp2cuda_i32_mul(2, index)] + result[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)]) * scale;
                            scratch[csharp2cuda_i32_add(half, index)] = (result[csharp2cuda_i32_mul(2, index)] - result[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)]) * scale;
                        }
                        for (int index = 0; index < length; csharp2cuda_i32_post_increment(index))
                            result[index] = scratch[index];
                        length = half;
                    }
                }
            }
            break;
        case 12:
            if (thread == 0)
            {
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (!mathblocks_sequence_is_power_of_two(first->count))
                {
                    output->valid = 0;
                }
                else
                {
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                        result[index] = a[index];
                    for (int width = 1; width < first->count; csharp2cuda_i32_mul_assign(width, 2))
                    {
                        for (int start = 0; start < first->count; csharp2cuda_i32_add_assign(start, csharp2cuda_i32_mul(2, width)))
                        {
                            for (int offset = 0; offset < width; csharp2cuda_i32_post_increment(offset))
                            {
                                double left = result[csharp2cuda_i32_add(start, offset)];
                                double right = result[csharp2cuda_i32_add(csharp2cuda_i32_add(start, width), offset)];
                                result[csharp2cuda_i32_add(start, offset)] = left + right;
                                result[csharp2cuda_i32_add(csharp2cuda_i32_add(start, width), offset)] = left - right;
                            }
                        }
                    }
                    double scale = 1.0 / mathblocks_square_root((double)first->count);
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                        result[index] *= scale;
                }
            }
            break;
        case 13:
            if (thread == 0)
            {
                mathblocks_sequence_set_vector_shape(output, first->count);
                double sum = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    sum += a[index] - second->scalar_value;
                    result[index] = sum;
                    if (!isfinite(sum))
                        output->valid = 0;
                }
            }
            break;
        case 14:
            if (thread == 0)
            {
                if (first->count <= 0 || second->count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                }
                else
                {
                    int width = csharp2cuda_i32_add(second->count, 1);
                    double* previous = scratch;
                    double* current = csharp2cuda_pointer_add(scratch, width);
                    for (int index = 0; index < width; csharp2cuda_i32_post_increment(index))
                        previous[index] = mathblocks_positive_infinity();
                    previous[0] = 0.0;
                    for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                    {
                        current[0] = mathblocks_positive_infinity();
                        for (int right = 0; right < second->count; csharp2cuda_i32_post_increment(right))
                        {
                            double minimum = previous[csharp2cuda_i32_add(right, 1)] < current[right]
                                ? previous[csharp2cuda_i32_add(right, 1)]
                                : current[right];
                            minimum = minimum < previous[right] ? minimum : previous[right];
                            current[csharp2cuda_i32_add(right, 1)] = fabs(a[left] - b[right]) + minimum;
                        }
                        double* swap = previous;
                        previous = current;
                        current = swap;
                    }
                    output->scalar_value = previous[second->count];
                    if (!isfinite(output->scalar_value))
                        output->valid = 0;
                }
            }
            break;
        case 15:
            if (thread == 0)
            {
                int result_index = -1;
                double threshold = second->scalar_value;
                bool at_or_above = third->boolean_value != 0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    if (at_or_above ? a[index] >= threshold : a[index] <= threshold)
                    {
                        result_index = index;
                        break;
                    }
                }
                output->scalar_value = (double)result_index;
            }
            break;
        case 16:
            if (thread == 0)
            {
                double lower = second->scalar_value;
                double upper = third->scalar_value;
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (!(lower < upper))
                {
                    output->valid = 0;
                }
                else
                {
                    double state = 0.0;
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        if (a[index] >= upper)
                            state = 1.0;
                        else if (a[index] <= lower)
                            state = -1.0;
                        result[index] = state;
                    }
                }
            }
            break;
        case 17:
            if (thread == 0)
            {
                int rows = first->count == 0 ? 0 : csharp2cuda_i32_sub(csharp2cuda_i32_mul(2, first->count), 1);
                mathblocks_sequence_set_matrix_shape(output, rows, 2);
                if (first->count <= 0)
                {
                    output->valid = 0;
                }
                else
                {
                    result[0] = a[0];
                    result[1] = a[0];
                    int row = 0;
                    for (int index = 1; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        csharp2cuda_i32_post_increment(row);
                        result[csharp2cuda_i32_mul(row, 2)] = a[index];
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, 2), 1)] = a[csharp2cuda_i32_sub(index, 1)];
                        csharp2cuda_i32_post_increment(row);
                        result[csharp2cuda_i32_mul(row, 2)] = a[index];
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, 2), 1)] = a[index];
                    }
                }
            }
            break;
        case 18:
            if (thread == 0)
            {
                int maximum = 0;
                int current = 0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    current = boolean_a[index] ? csharp2cuda_i32_add(current, 1) : 0;
                    maximum = maximum > current ? maximum : current;
                }
                output->scalar_value = (double)maximum;
            }
            break;
        case 19:
        case 20:
            if (thread == 0)
            {
                if (first->count <= 0)
                {
                    output->valid = 0;
                }
                else
                {
                    double maximum = a[0];
                    double decline = 0.0;
                    for (int index = 1; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        maximum = maximum > a[index] ? maximum : a[index];
                        double candidate = opcode == 19
                            ? maximum - a[index]
                            : (maximum - a[index]) / maximum;
                        decline = decline > candidate ? decline : candidate;
                    }
                    output->scalar_value = decline;
                    if (!isfinite(decline))
                        output->valid = 0;
                }
            }
            break;
        case 21:
        case 22:
        case 29:
            if (thread == 0)
            {
                if (first->count <= 0)
                {
                    output->valid = 0;
                }
                else
                {
                    double order = opcode == 22 ? 2.0 : second == nullptr ? 1.0 : second->scalar_value;
                    if (!(order > 0.0))
                    {
                        output->valid = 0;
                    }
                    else
                    {
                        double total = 0.0;
                        for (int index = 1; index < first->count; csharp2cuda_i32_post_increment(index))
                        {
                            double change = fabs(a[index] - a[csharp2cuda_i32_sub(index, 1)]);
                            total += opcode == 29 ? change : mathblocks_power(change, order);
                        }
                        output->scalar_value = total;
                        if (!isfinite(total))
                            output->valid = 0;
                    }
                }
            }
            break;
        case 23:
            if (thread == 0)
            {
                double threshold = second->scalar_value;
                if (first->count <= 0 || threshold < 0.0)
                {
                    output->valid = 0;
                }
                else
                {
                    long long recurrent = 0;
                    long long total = csharp2cuda_i64_mul((long long)first->count, first->count);
                    for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                        for (int right = 0; right < first->count; csharp2cuda_i32_post_increment(right))
                            if (fabs(a[left] - a[right]) <= threshold)
                                csharp2cuda_i64_post_increment(recurrent);
                    output->scalar_value = (double)recurrent / total;
                }
            }
            break;
        case 24:
            if (thread == 0)
            {
                mathblocks_sequence_set_vector_shape(output, first->count);
                if (first->count <= 0)
                {
                    output->valid = 0;
                }
                else
                {
                    double cumulative = 0.0;
                    double minimum = 0.0;
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        cumulative += a[index];
                        minimum = minimum < cumulative ? minimum : cumulative;
                        result[index] = cumulative - minimum;
                        if (!isfinite(result[index]))
                            output->valid = 0;
                    }
                }
            }
            break;
        case 25:
            if (thread == 0)
            {
                MathBlockSequencePathRun* runs = (MathBlockSequencePathRun*)output->data_pointer;
                if (first->count == 0)
                {
                    output->count = 0;
                    output->rows = 0;
                }
                else
                {
                    int start = 0;
                    int count = 0;
                    for (int index = 1; index <= first->count; csharp2cuda_i32_post_increment(index))
                    {
                        if (index < first->count && a[index] == a[start])
                            continue;
                        if (count >= output->capacity)
                        {
                            output->count = output->capacity == 2147483647
                                ? -1
                                : csharp2cuda_i32_add(output->capacity, 1);
                            output->rows = output->count;
                            output->valid = 0;
                            break;
                        }
                        runs[count].start = start;
                        runs[count].length = csharp2cuda_i32_sub(index, start);
                        runs[count].value = a[start];
                        csharp2cuda_i32_post_increment(count);
                        start = index;
                    }
                    if (output->valid)
                    {
                        output->count = count;
                        output->rows = count;
                    }
                }
            }
            break;
        case 26:
            if (thread == 0)
            {
                mathblocks_sequence_set_vector_shape(output, first->columns);
                if (first->rows <= 0)
                {
                    output->valid = 0;
                }
                else
                {
                    for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                        result[column] = a[csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(first->rows, 1)), first->columns), column)] - a[column];
                }
            }
            break;
        case 27:
            if (thread == 0)
            {
                int dimension = first->columns;
                int count = csharp2cuda_i32_mul(csharp2cuda_i32_mul(dimension, dimension), dimension);
                mathblocks_sequence_set_vector_shape(output, count);
                if (first->rows < 1 || scratch == nullptr)
                {
                    output->valid = 0;
                }
                else
                {
                    double* level_one = scratch;
                    double* level_two = csharp2cuda_pointer_add(scratch, dimension);
                    double* increment = csharp2cuda_pointer_add(level_two, csharp2cuda_i32_mul(dimension, dimension));
                    for (int index = 0; index < dimension; csharp2cuda_i32_post_increment(index))
                        level_one[index] = 0.0;
                    for (int index = 0; index < csharp2cuda_i32_mul(dimension, dimension); csharp2cuda_i32_post_increment(index))
                        level_two[index] = 0.0;
                    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
                        result[index] = 0.0;
                    for (int row = 1; row < first->rows; csharp2cuda_i32_post_increment(row))
                    {
                        for (int index = 0; index < dimension; csharp2cuda_i32_post_increment(index))
                            increment[index] = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, dimension), index)] - a[csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(row, 1)), dimension), index)];
                        for (int left = 0; left < dimension; csharp2cuda_i32_post_increment(left))
                        {
                            for (int middle = 0; middle < dimension; csharp2cuda_i32_post_increment(middle))
                            {
                                for (int right = 0; right < dimension; csharp2cuda_i32_post_increment(right))
                                {
                                    int target = csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_add(csharp2cuda_i32_mul(left, dimension), middle)), dimension), right);
                                    result[target] +=
                                        level_two[csharp2cuda_i32_add(csharp2cuda_i32_mul(left, dimension), middle)] * increment[right] +
                                        level_one[left] * increment[middle] * increment[right] / 2.0 +
                                        increment[left] * increment[middle] * increment[right] / 6.0;
                                }
                                level_two[csharp2cuda_i32_add(csharp2cuda_i32_mul(left, dimension), middle)] +=
                                    level_one[left] * increment[middle] +
                                    increment[left] * increment[middle] / 2.0;
                            }
                            level_one[left] += increment[left];
                        }
                    }
                }
            }
            break;
        case 28:
            if (thread == 0)
            {
                int dimension = first->columns;
                mathblocks_sequence_set_matrix_shape(output, dimension, dimension);
                if (scratch == nullptr)
                {
                    output->valid = 0;
                }
                else
                {
                    double* cumulative = scratch;
                    double* increment = csharp2cuda_pointer_add(scratch, dimension);
                    for (int index = 0; index < dimension; csharp2cuda_i32_post_increment(index))
                        cumulative[index] = 0.0;
                    for (int index = 0; index < csharp2cuda_i32_mul(dimension, dimension); csharp2cuda_i32_post_increment(index))
                        result[index] = 0.0;
                    for (int row = 1; row < first->rows; csharp2cuda_i32_post_increment(row))
                    {
                        for (int column = 0; column < dimension; csharp2cuda_i32_post_increment(column))
                            increment[column] = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, dimension), column)] - a[csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(row, 1)), dimension), column)];
                        for (int left = 0; left < dimension; csharp2cuda_i32_post_increment(left))
                        {
                            for (int right = 0; right < dimension; csharp2cuda_i32_post_increment(right))
                            {
                                result[csharp2cuda_i32_add(csharp2cuda_i32_mul(left, dimension), right)] +=
                                    cumulative[left] * increment[right] +
                                    0.5 * increment[left] * increment[right];
                            }
                        }
                        for (int column = 0; column < dimension; csharp2cuda_i32_post_increment(column))
                            cumulative[column] += increment[column];
                    }
                }
            }
            break;
        case 30:
        case 31:
            if (thread == 0)
            {
                int count = 0;
                int previous = 0;
                int start = opcode == 30 ? 1 : 0;
                for (int index = start; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    double difference = opcode == 30 ? a[index] - a[csharp2cuda_i32_sub(index, 1)] : a[index];
                    int current = difference > 0.0 ? 1 : difference < 0.0 ? -1 : 0;
                    if (current == 0)
                        continue;
                    if (previous != 0 && current != previous)
                        csharp2cuda_i32_post_increment(count);
                    previous = current;
                }
                output->scalar_value = (double)count;
            }
            break;
        case 32:
            if (thread == 0)
            {
                int state_count = 0;
                if (!mathblocks_sequence_positive_integer(second->scalar_value, &state_count) || state_count > 4096)
                {
                    output->valid = 0;
                }
                else
                {
                    mathblocks_sequence_set_matrix_shape(output, state_count, state_count);
                    for (int index = 0; output->valid && index < output->count; csharp2cuda_i32_post_increment(index))
                        result[index] = 0.0;
                    for (int index = 0; output->valid && index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        int state = 0;
                        if (!mathblocks_nonnegative_integer(a[index], &state) || state >= state_count)
                            output->valid = 0;
                    }
                    for (int index = 1; output->valid && index < first->count; csharp2cuda_i32_post_increment(index))
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul((int)a[csharp2cuda_i32_sub(index, 1)], state_count), (int)a[index])] += 1.0;
                }
            }
            break;
    }
}