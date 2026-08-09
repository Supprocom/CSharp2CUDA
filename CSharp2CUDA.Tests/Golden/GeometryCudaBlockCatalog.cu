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

struct MathBlockGeometryEdge;

struct MathBlockGeometryEdge
{
    int from;
    int to;
    double weight;
};

__device__ double mathblocks_geometry_distance_coordinates(
    double left_x,
    double left_y,
    double right_x,
    double right_y);
__device__ double mathblocks_geometry_distance(
    const double* left,
    int left_index,
    const double* right,
    int right_index);
__device__ double mathblocks_geometry_cross(
    double origin_x,
    double origin_y,
    double left_x,
    double left_y,
    double right_x,
    double right_y);
__device__ double mathblocks_geometry_point_to_segment(
    double point_x,
    double point_y,
    double start_x,
    double start_y,
    double end_x,
    double end_y);
__device__ void mathblocks_geometry_barycentric(
    double point_x,
    double point_y,
    double first_x,
    double first_y,
    double second_x,
    double second_y,
    double third_x,
    double third_y,
    double* result);
__device__ bool mathblocks_geometry_try_circumcircle(
    const double* points,
    int first,
    int second,
    int third,
    double* center_x,
    double* center_y,
    double* radius_square);
__device__ bool mathblocks_geometry_point_less(
    const double* points,
    int left,
    int right);
__device__ void mathblocks_geometry_sort_indices(
    const double* points,
    int count,
    int* indices);
__device__ int mathblocks_geometry_find(int* parent, int value);
__device__ bool mathblocks_geometry_edge_less(
    const MathBlockGeometryEdge& left,
    const MathBlockGeometryEdge& right);
extern "C" __global__ void mathblocks_geometry(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output);

__device__ double mathblocks_geometry_distance_coordinates(
    double left_x,
    double left_y,
    double right_x,
    double right_y)
{
    double x = left_x - right_x;
    double y = left_y - right_y;
    return mathblocks_square_root(x * x + y * y);
}

__device__ double mathblocks_geometry_distance(
    const double* left,
    int left_index,
    const double* right,
    int right_index)
{
    return mathblocks_geometry_distance_coordinates(
        left[csharp2cuda_i32_mul(2, left_index)],
        left[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, left_index), 1)],
        right[csharp2cuda_i32_mul(2, right_index)],
        right[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, right_index), 1)]);
}

__device__ double mathblocks_geometry_cross(
    double origin_x,
    double origin_y,
    double left_x,
    double left_y,
    double right_x,
    double right_y)
{
    return (left_x - origin_x) * (right_y - origin_y) -
           (left_y - origin_y) * (right_x - origin_x);
}

__device__ double mathblocks_geometry_point_to_segment(
    double point_x,
    double point_y,
    double start_x,
    double start_y,
    double end_x,
    double end_y)
{
    double x = end_x - start_x;
    double y = end_y - start_y;
    double length_square = x * x + y * y;
    if (length_square == 0.0)
        return mathblocks_geometry_distance_coordinates(point_x, point_y, start_x, start_y);
    double projection = ((point_x - start_x) * x + (point_y - start_y) * y) /
        length_square;
    projection = projection < 0.0 ? 0.0 : projection > 1.0 ? 1.0 : projection;
    return mathblocks_geometry_distance_coordinates(
        point_x,
        point_y,
        start_x + projection * x,
        start_y + projection * y);
}

__device__ void mathblocks_geometry_barycentric(
    double point_x,
    double point_y,
    double first_x,
    double first_y,
    double second_x,
    double second_y,
    double third_x,
    double third_y,
    double* result)
{
    double denominator = (second_y - third_y) * (first_x - third_x) +
                         (third_x - second_x) * (first_y - third_y);
    double first_weight = ((second_y - third_y) * (point_x - third_x) +
                           (third_x - second_x) * (point_y - third_y)) / denominator;
    double second_weight = ((third_y - first_y) * (point_x - third_x) +
                            (first_x - third_x) * (point_y - third_y)) / denominator;
    result[0] = first_weight;
    result[1] = second_weight;
    result[2] = 1.0 - first_weight - second_weight;
}

__device__ bool mathblocks_geometry_try_circumcircle(
    const double* points,
    int first,
    int second,
    int third,
    double* center_x,
    double* center_y,
    double* radius_square)
{
    double first_x = points[csharp2cuda_i32_mul(2, first)];
    double first_y = points[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, first), 1)];
    double second_x = points[csharp2cuda_i32_mul(2, second)];
    double second_y = points[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, second), 1)];
    double third_x = points[csharp2cuda_i32_mul(2, third)];
    double third_y = points[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, third), 1)];
    double denominator = 2.0 * (first_x * (second_y - third_y) +
                                second_x * (third_y - first_y) +
                                third_x * (first_y - second_y));
    if (denominator == 0.0)
    {
        *center_x = 0.0;
        *center_y = 0.0;
        *radius_square = 0.0;
        return false;
    }
    double first_square = first_x * first_x + first_y * first_y;
    double second_square = second_x * second_x + second_y * second_y;
    double third_square = third_x * third_x + third_y * third_y;
    *center_x = (first_square * (second_y - third_y) +
                 second_square * (third_y - first_y) +
                 third_square * (first_y - second_y)) / denominator;
    *center_y = (first_square * (third_x - second_x) +
                 second_square * (first_x - third_x) +
                 third_square * (second_x - first_x)) / denominator;
    double x = first_x - *center_x;
    double y = first_y - *center_y;
    *radius_square = x * x + y * y;
    return true;
}

__device__ bool mathblocks_geometry_point_less(
    const double* points,
    int left,
    int right)
{
    double left_x = points[csharp2cuda_i32_mul(2, left)];
    double right_x = points[csharp2cuda_i32_mul(2, right)];
    if (left_x < right_x)
        return true;
    if (right_x < left_x)
        return false;
    double left_y = points[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, left), 1)];
    double right_y = points[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, right), 1)];
    if (left_y < right_y)
        return true;
    if (right_y < left_y)
        return false;
    return left < right;
}

__device__ void mathblocks_geometry_sort_indices(
    const double* points,
    int count,
    int* indices)
{
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
    {
        int value = index;
        int position = index;
        while (position > 0 && mathblocks_geometry_point_less(points, value, indices[csharp2cuda_i32_sub(position, 1)]))
        {
            indices[position] = indices[csharp2cuda_i32_sub(position, 1)];
            csharp2cuda_i32_post_decrement(position);
        }
        indices[position] = value;
    }
}

__device__ int mathblocks_geometry_find(int* parent, int value)
{
    while (parent[value] != value)
    {
        parent[value] = parent[parent[value]];
        value = parent[value];
    }
    return value;
}

__device__ bool mathblocks_geometry_edge_less(
    const MathBlockGeometryEdge& left,
    const MathBlockGeometryEdge& right)
{
    if (left.weight < right.weight)
        return true;
    if (right.weight < left.weight)
        return false;
    if (left.from < right.from)
        return true;
    if (right.from < left.from)
        return false;
    return left.to < right.to;
}

extern "C" __global__ void mathblocks_geometry(
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
                mathblocks_sequence_set_vector_shape(output, 3);
                if (first->count != 1 || second->count != 3)
                {
                    output->valid = 0;
                    break;
                }
                mathblocks_geometry_barycentric(
                    a[0], a[1], b[0], b[1], b[2], b[3], b[4], b[5], result);
                for (int index = 0; index < 3; csharp2cuda_i32_post_increment(index))
                    if (!isfinite(result[index])) output->valid = 0;
                break;
            case 1:
                output->rows = 1;
                output->count = 1;
                if (first->count <= 0 || output->capacity < 1)
                {
                    output->valid = 0;
                    break;
                }
                result[0] = 0.0;
                result[1] = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    result[0] += a[csharp2cuda_i32_mul(2, index)];
                    result[1] += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)];
                }
                result[0] /= first->count;
                result[1] /= first->count;
                break;
            case 2:
                if (first->count != 3)
                {
                    output->valid = 0;
                    break;
                }
            {
                double first_length = mathblocks_geometry_distance(a, 1, a, 2);
                double second_length = mathblocks_geometry_distance(a, 0, a, 2);
                double third_length = mathblocks_geometry_distance(a, 0, a, 1);
                double cross = mathblocks_geometry_cross(
                    a[0], a[1], a[2], a[3], a[4], a[5]);
                output->scalar_value = first_length * second_length * third_length /
                    (2.0 * fabs(cross));
                break;
            }
            case 3:
                if (first->count < 3 || second->count != 1)
                {
                    output->valid = 0;
                    break;
                }
            {
                bool inside = false;
                double point_x = b[0];
                double point_y = b[1];
                for (int current = 0; current < first->count; csharp2cuda_i32_post_increment(current))
                {
                    int previous = current == 0 ? csharp2cuda_i32_sub(first->count, 1) : csharp2cuda_i32_sub(current, 1);
                    double left_x = a[csharp2cuda_i32_mul(2, current)];
                    double left_y = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, current), 1)];
                    double right_x = a[csharp2cuda_i32_mul(2, previous)];
                    double right_y = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, previous), 1)];
                    if (mathblocks_geometry_point_to_segment(
                        point_x, point_y, left_x, left_y, right_x, right_y) == 0.0)
                    {
                        inside = true;
                        break;
                    }
                    if ((left_y > point_y) != (right_y > point_y) &&
                        point_x < (right_x - left_x) * (point_y - left_y) /
                                  (right_y - left_y) + left_x)
                    {
                        inside = !inside;
                    }
                }
                output->boolean_value = inside ? 1 : 0;
                break;
            }
            case 4:
                if (first->count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                double* sorted = scratch;
                double* hull = csharp2cuda_pointer_add(scratch, csharp2cuda_i32_mul(first->count, 2));
                int unique_count = 0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    double x = a[csharp2cuda_i32_mul(2, index)];
                    double y = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)];
                    int position = unique_count;
                    while (position > 0 &&
                           (sorted[csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(position, 1)))] > x ||
                            (sorted[csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(position, 1)))] == x &&
                             sorted[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(position, 1))), 1)] > y)))
                        csharp2cuda_i32_post_decrement(position);
                    if (position < unique_count && sorted[csharp2cuda_i32_mul(2, position)] == x &&
                        sorted[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, position), 1)] == y)
                    {
                        continue;
                    }
                    for (int move = unique_count; move > position; csharp2cuda_i32_post_decrement(move))
                    {
                        sorted[csharp2cuda_i32_mul(2, move)] = sorted[csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(move, 1)))];
                        sorted[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, move), 1)] = sorted[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(move, 1))), 1)];
                    }
                    sorted[csharp2cuda_i32_mul(2, position)] = x;
                    sorted[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, position), 1)] = y;
                    csharp2cuda_i32_post_increment(unique_count);
                }
                int count = 0;
                if (unique_count <= 1)
                {
                    count = unique_count;
                    if (count == 1)
                    {
                        hull[0] = sorted[0];
                        hull[1] = sorted[1];
                    }
                }
                else
                {
                    for (int index = 0; index < unique_count; csharp2cuda_i32_post_increment(index))
                    {
                        while (count >= 2 && mathblocks_geometry_cross(
                            hull[csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(count, 2)))], hull[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(count, 2))), 1)],
                            hull[csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(count, 1)))], hull[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(count, 1))), 1)],
                            sorted[csharp2cuda_i32_mul(2, index)], sorted[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)]) <= 0.0)
                        {
                            csharp2cuda_i32_post_decrement(count);
                        }
                        hull[csharp2cuda_i32_mul(2, count)] = sorted[csharp2cuda_i32_mul(2, index)];
                        hull[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, count), 1)] = sorted[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)];
                        csharp2cuda_i32_post_increment(count);
                    }
                    int lower_count = count;
                    for (int index = csharp2cuda_i32_sub(unique_count, 2); index >= 0; csharp2cuda_i32_post_decrement(index))
                    {
                        while (count > lower_count && mathblocks_geometry_cross(
                            hull[csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(count, 2)))], hull[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(count, 2))), 1)],
                            hull[csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(count, 1)))], hull[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, (csharp2cuda_i32_sub(count, 1))), 1)],
                            sorted[csharp2cuda_i32_mul(2, index)], sorted[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)]) <= 0.0)
                        {
                            csharp2cuda_i32_post_decrement(count);
                        }
                        hull[csharp2cuda_i32_mul(2, count)] = sorted[csharp2cuda_i32_mul(2, index)];
                        hull[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, count), 1)] = sorted[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)];
                        csharp2cuda_i32_post_increment(count);
                    }
                    csharp2cuda_i32_post_decrement(count);
                }
                output->rows = count;
                output->count = count;
                if (count > output->capacity)
                {
                    output->valid = 0;
                    break;
                }
                for (int index = 0; index < csharp2cuda_i32_mul(count, 2); csharp2cuda_i32_post_increment(index))
                    result[index] = hull[index];
                break;
            }
            case 5:
                if (first->count < 2 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int count = first->count;
                int* adjacency = (int*)scratch;
                int* ordered = csharp2cuda_pointer_add(adjacency, csharp2cuda_i32_mul(count, count));
                for (int index = 0; index < csharp2cuda_i32_mul(count, count); csharp2cuda_i32_post_increment(index))
                    adjacency[index] = 0;
                for (int first_index = 0; first_index < count; csharp2cuda_i32_post_increment(first_index))
                {
                    for (int second_index = csharp2cuda_i32_add(first_index, 1); second_index < count; csharp2cuda_i32_post_increment(second_index))
                    {
                        for (int third_index = csharp2cuda_i32_add(second_index, 1); third_index < count; csharp2cuda_i32_post_increment(third_index))
                        {
                            double center_x;
                            double center_y;
                            double radius_square;
                            if (!mathblocks_geometry_try_circumcircle(
                                a, first_index, second_index, third_index,
                                &center_x, &center_y, &radius_square))
                            {
                                continue;
                            }
                            bool empty = true;
                            for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
                            {
                                if (index == first_index || index == second_index || index == third_index)
                                    continue;
                                double x = a[csharp2cuda_i32_mul(2, index)] - center_x;
                                double y = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)] - center_y;
                                if (x * x + y * y < radius_square)
                                {
                                    empty = false;
                                    break;
                                }
                            }
                            if (!empty)
                                continue;
                            adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(first_index, count), second_index)] = 1;
                            adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(first_index, count), third_index)] = 1;
                            adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(second_index, count), third_index)] = 1;
                        }
                    }
                }
                int edge_count = 0;
                for (int left = 0; left < count; csharp2cuda_i32_post_increment(left))
                    for (int right = csharp2cuda_i32_add(left, 1); right < count; csharp2cuda_i32_post_increment(right))
                        csharp2cuda_i32_add_assign(edge_count, adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(left, count), right)]);
                if (edge_count == 0)
                {
                    mathblocks_geometry_sort_indices(a, count, ordered);
                    for (int index = 1; index < count; csharp2cuda_i32_post_increment(index))
                    {
                        int left = ordered[csharp2cuda_i32_sub(index, 1)] < ordered[index]
                            ? ordered[csharp2cuda_i32_sub(index, 1)]
                            : ordered[index];
                        int right = ordered[csharp2cuda_i32_sub(index, 1)] < ordered[index]
                            ? ordered[index]
                            : ordered[csharp2cuda_i32_sub(index, 1)];
                        adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(left, count), right)] = 1;
                    }
                }
                MathBlockGeometryEdge* edges = (MathBlockGeometryEdge*)output->data_pointer;
                edge_count = 0;
                for (int left = 0; left < count; csharp2cuda_i32_post_increment(left))
                {
                    for (int right = csharp2cuda_i32_add(left, 1); right < count; csharp2cuda_i32_post_increment(right))
                    {
                        if (!((adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(left, count), right)])!=0))
                            continue;
                        if (edge_count >= output->capacity)
                        {
                            output->count = output->capacity == 2147483647
                                ? -1
                                : csharp2cuda_i32_add(output->capacity, 1);
                            output->valid = 0;
                            break;
                        }
                        edges[edge_count].from = left;
                        edges[edge_count].to = right;
                        edges[edge_count].weight = mathblocks_geometry_distance(a, left, a, right);
                        csharp2cuda_i32_post_increment(edge_count);
                    }
                }
                output->rows = count;
                if (output->valid)
                    output->count = edge_count;
                break;
            }
            case 6:
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
            {
                double maximum = 0.0;
                for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                    for (int right = csharp2cuda_i32_add(left, 1); right < first->count; csharp2cuda_i32_post_increment(right))
                    {
                        double distance = mathblocks_geometry_distance(a, left, a, right);
                        maximum = maximum > distance ? maximum : distance;
                    }
                output->scalar_value = maximum;
                break;
            }
            case 7:
                if (first->count <= 0 || second->count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                {
                    for (int right = 0; right < second->count; csharp2cuda_i32_post_increment(right))
                    {
                        double distance = mathblocks_geometry_distance(a, left, b, right);
                        int target = csharp2cuda_i32_add(csharp2cuda_i32_mul(left, second->count), right);
                        if (left == 0 && right == 0)
                            scratch[0] = distance;
                        else if (left == 0)
                            scratch[right] = scratch[csharp2cuda_i32_sub(right, 1)] > distance
                                ? scratch[csharp2cuda_i32_sub(right, 1)]
                                : distance;
                        else if (right == 0)
                            scratch[csharp2cuda_i32_mul(left, second->count)] = scratch[csharp2cuda_i32_mul((csharp2cuda_i32_sub(left, 1)), second->count)] > distance
                                ? scratch[csharp2cuda_i32_mul((csharp2cuda_i32_sub(left, 1)), second->count)]
                                : distance;
                        else
                        {
                            double preceding = scratch[csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(left, 1)), second->count), right)];
                            double candidate = scratch[csharp2cuda_i32_sub(csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(left, 1)), second->count), right), 1)];
                            preceding = preceding < candidate ? preceding : candidate;
                            candidate = scratch[csharp2cuda_i32_sub(csharp2cuda_i32_add(csharp2cuda_i32_mul(left, second->count), right), 1)];
                            preceding = preceding < candidate ? preceding : candidate;
                            scratch[target] = preceding > distance ? preceding : distance;
                        }
                    }
                }
                output->scalar_value = scratch[csharp2cuda_i32_sub(csharp2cuda_i32_mul(first->count, second->count), 1)];
                break;
            }
            case 8:
                if (first->count <= 0 || second->count <= 0)
                    output->valid = 0;
                else
                    output->scalar_value = mathblocks_geometry_distance(a, 0, b, 0);
                break;
            case 9:
                if (first->count <= 0 || first->count != second->count)
                {
                    output->valid = 0;
                    break;
                }
            {
                double affinity = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    affinity += mathblocks_square_root(a[index] * b[index]);
                affinity = affinity < -1.0 ? -1.0 : affinity > 1.0 ? 1.0 : affinity;
                output->scalar_value = 2.0 * mathblocks_arc_cosine(affinity);
                break;
            }
            case 10:
                if (first->count < 2)
                {
                    output->valid = 0;
                    break;
                }
            {
                MathBlockGeometryEdge* edges = (MathBlockGeometryEdge*)output->data_pointer;
                int edge_count = 0;
                for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                {
                    for (int right = csharp2cuda_i32_add(left, 1); right < first->count; csharp2cuda_i32_post_increment(right))
                    {
                        double center_x = (a[csharp2cuda_i32_mul(2, left)] + a[csharp2cuda_i32_mul(2, right)]) / 2.0;
                        double center_y = (a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, left), 1)] + a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, right), 1)]) / 2.0;
                        double radius = mathblocks_geometry_distance(a, left, a, right) / 2.0;
                        bool empty = true;
                        for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                        {
                            if (index != left && index != right &&
                                mathblocks_geometry_distance_coordinates(
                                    a[csharp2cuda_i32_mul(2, index)], a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)], center_x, center_y) < radius)
                            {
                                empty = false;
                                break;
                            }
                        }
                        if (!empty)
                            continue;
                        if (edge_count >= output->capacity)
                        {
                            output->count = output->capacity == 2147483647
                                ? -1
                                : csharp2cuda_i32_add(output->capacity, 1);
                            output->valid = 0;
                            break;
                        }
                        edges[edge_count].from = left;
                        edges[edge_count].to = right;
                        edges[edge_count].weight = 2.0 * radius;
                        csharp2cuda_i32_post_increment(edge_count);
                    }
                }
                output->rows = first->count;
                if (output->valid)
                    output->count = edge_count;
                break;
            }
            case 11:
                if (first->count <= 0 || second->count != 1)
                {
                    output->valid = 0;
                    break;
                }
            {
                double point_x = b[0];
                double point_y = b[1];
                int coincident = 0;
                int vector_count = 0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    double x = a[csharp2cuda_i32_mul(2, index)] - point_x;
                    double y = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)] - point_y;
                    if (x == 0.0 && y == 0.0)
                        csharp2cuda_i32_post_increment(coincident);
                    else
                        csharp2cuda_i32_post_increment(vector_count);
                }
                if (vector_count == 0)
                {
                    output->scalar_value = 1.0;
                    break;
                }
                int maximum = 0;
                for (int pivot = 0; pivot < first->count; csharp2cuda_i32_post_increment(pivot))
                {
                    double pivot_x = a[csharp2cuda_i32_mul(2, pivot)] - point_x;
                    double pivot_y = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, pivot), 1)] - point_y;
                    if (pivot_x == 0.0 && pivot_y == 0.0)
                        continue;
                    int count = 0;
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        double x = a[csharp2cuda_i32_mul(2, index)] - point_x;
                        double y = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)] - point_y;
                        if (x == 0.0 && y == 0.0)
                            continue;
                        double cross = pivot_x * y - pivot_y * x;
                        double dot = pivot_x * x + pivot_y * y;
                        if (cross > 0.0 || (cross == 0.0 && dot > 0.0))
                            csharp2cuda_i32_post_increment(count);
                    }
                    maximum = maximum > count ? maximum : count;
                }
                output->scalar_value = (double)(csharp2cuda_i32_sub(csharp2cuda_i32_add(coincident, vector_count), maximum)) /
                    first->count;
                break;
            }
            case 12:
                if (first->count <= 0 || second->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
            {
                double directed_left = 0.0;
                for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                {
                    double minimum = mathblocks_positive_infinity();
                    for (int right = 0; right < second->count; csharp2cuda_i32_post_increment(right))
                    {
                        double distance = mathblocks_geometry_distance(a, left, b, right);
                        minimum = minimum < distance ? minimum : distance;
                    }
                    directed_left = directed_left > minimum ? directed_left : minimum;
                }
                double directed_right = 0.0;
                for (int right = 0; right < second->count; csharp2cuda_i32_post_increment(right))
                {
                    double minimum = mathblocks_positive_infinity();
                    for (int left = 0; left < first->count; csharp2cuda_i32_post_increment(left))
                    {
                        double distance = mathblocks_geometry_distance(b, right, a, left);
                        minimum = minimum < distance ? minimum : distance;
                    }
                    directed_right = directed_right > minimum ? directed_right : minimum;
                }
                output->scalar_value = directed_left > directed_right
                    ? directed_left
                    : directed_right;
                break;
            }
            case 13:
            case 14:
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
            {
                double total = 0.0;
                int start = opcode == 13 ? 1 : 0;
                for (int index = start; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    int previous = opcode == 13 ? csharp2cuda_i32_sub(index, 1) : index;
                    int next = opcode == 13 ? index : csharp2cuda_i32_rem((csharp2cuda_i32_add(index, 1)), first->count);
                    total += mathblocks_geometry_distance(a, previous, a, next);
                }
                output->scalar_value = total;
                break;
            }
            case 15:
                if (first->count != 1 || second->count != 2)
                {
                    output->valid = 0;
                    break;
                }
                output->scalar_value = mathblocks_geometry_point_to_segment(
                    a[0], a[1], b[0], b[1], b[2], b[3]);
                break;
            case 16:
            case 17:
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
            {
                double twice_area = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    int next = csharp2cuda_i32_rem((csharp2cuda_i32_add(index, 1)), first->count);
                    twice_area += a[csharp2cuda_i32_mul(2, index)] * a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, next), 1)] -
                                  a[csharp2cuda_i32_mul(2, next)] * a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, index), 1)];
                }
                output->scalar_value = opcode == 16 ? fabs(twice_area / 2.0) : twice_area / 2.0;
                break;
            }
            case 18:
                if (first->count < 3 || second->count != 1)
                {
                    output->valid = 0;
                    break;
                }
            {
                int containing = 0;
                int total = 0;
                double coordinates[3];
                for (int one = 0; one < first->count; csharp2cuda_i32_post_increment(one))
                    for (int two = csharp2cuda_i32_add(one, 1); two < first->count; csharp2cuda_i32_post_increment(two))
                        for (int three = csharp2cuda_i32_add(two, 1); three < first->count; csharp2cuda_i32_post_increment(three))
                        {
                            csharp2cuda_i32_post_increment(total);
                            mathblocks_geometry_barycentric(
                                b[0], b[1],
                                a[csharp2cuda_i32_mul(2, one)], a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, one), 1)],
                                a[csharp2cuda_i32_mul(2, two)], a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, two), 1)],
                                a[csharp2cuda_i32_mul(2, three)], a[csharp2cuda_i32_add(csharp2cuda_i32_mul(2, three), 1)],
                                coordinates);
                            if (coordinates[0] >= 0.0 && coordinates[0] <= 1.0 &&
                                coordinates[1] >= 0.0 && coordinates[1] <= 1.0 &&
                                coordinates[2] >= 0.0 && coordinates[2] <= 1.0)
                            {
                                csharp2cuda_i32_post_increment(containing);
                            }
                        }
                output->scalar_value = (double)containing / total;
                break;
            }
            case 19:
                output->rows = first->rows;
                output->count = first->rows;
                if (first->columns != 2 || first->rows > output->capacity)
                {
                    output->valid = 0;
                    break;
                }
                for (int index = 0; index < csharp2cuda_i32_mul(first->rows, 2); csharp2cuda_i32_post_increment(index))
                    result[index] = a[index];
                break;
            case 20:
                mathblocks_sequence_set_matrix_shape(output, first->count, 2);
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
                for (int index = 0; index < csharp2cuda_i32_mul(first->count, 2); csharp2cuda_i32_post_increment(index))
                    result[index] = a[index];
                break;
            case 21:
                if (first->count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                if (first->count == 1)
                {
                    mathblocks_sequence_set_vector_shape(output, 0);
                    break;
                }
            {
                int vertex_count = first->count;
                int edge_capacity = csharp2cuda_i32_div(csharp2cuda_i32_mul(vertex_count, (csharp2cuda_i32_sub(vertex_count, 1))), 2);
                MathBlockGeometryEdge* edges = (MathBlockGeometryEdge*)scratch;
                int edge_count = 0;
                for (int left = 0; left < vertex_count; csharp2cuda_i32_post_increment(left))
                    for (int right = csharp2cuda_i32_add(left, 1); right < vertex_count; csharp2cuda_i32_post_increment(right))
                    {
                        edges[edge_count].from = left;
                        edges[edge_count].to = right;
                        edges[edge_count].weight = mathblocks_geometry_distance(a, left, a, right);
                        csharp2cuda_i32_post_increment(edge_count);
                    }
                for (int index = 1; index < edge_count; csharp2cuda_i32_post_increment(index))
                {
                    MathBlockGeometryEdge value = edges[index];
                    int position = index;
                    while (position > 0 && mathblocks_geometry_edge_less(value, edges[csharp2cuda_i32_sub(position, 1)]))
                    {
                        edges[position] = edges[csharp2cuda_i32_sub(position, 1)];
                        csharp2cuda_i32_post_decrement(position);
                    }
                    edges[position] = value;
                }
                int* parent = (int*)(csharp2cuda_pointer_add(edges, edge_capacity));
                unsigned char* rank = (unsigned char*)(csharp2cuda_pointer_add(parent, vertex_count));
                for (int index = 0; index < vertex_count; csharp2cuda_i32_post_increment(index))
                {
                    parent[index] = index;
                    rank[index] = 0;
                }
                int selected = 0;
                for (int index = 0; index < edge_count && selected < csharp2cuda_i32_sub(vertex_count, 1); csharp2cuda_i32_post_increment(index))
                {
                    int left = mathblocks_geometry_find(parent, edges[index].from);
                    int right = mathblocks_geometry_find(parent, edges[index].to);
                    if (left == right)
                        continue;
                    if (rank[left] < rank[right])
                        parent[left] = right;
                    else if (rank[left] > rank[right])
                        parent[right] = left;
                    else
                    {
                        parent[right] = left;
                        rank[left]++;
                    }
                    result[csharp2cuda_i32_post_increment(selected)] = edges[index].weight;
                }
                mathblocks_sequence_set_vector_shape(output, selected);
                break;
            }
        }
        if (output->valid &&
            opcode != 0 && opcode != 1 && opcode != 3 && opcode != 4 && opcode != 5 &&
            opcode != 10 && opcode != 19 && opcode != 20 && opcode != 21 &&
            !isfinite(output->scalar_value))
        {
            output->valid = 0;
        }
    }
}