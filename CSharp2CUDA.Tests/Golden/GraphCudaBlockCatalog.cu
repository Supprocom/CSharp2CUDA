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

struct MathBlockGraphKernelEdge;

struct MathBlockGraphKernelEdge
{
    int from;
    int to;
    double weight;
};

__device__ int mathblocks_graph_component_count(
    const MathBlockGraphKernelEdge* edges,
    int edge_count,
    int vertex_count,
    int* visited,
    int* queue);
__device__ bool mathblocks_graph_edge_less(
    const MathBlockGraphKernelEdge& left,
    const MathBlockGraphKernelEdge& right);
__device__ int mathblocks_graph_find(int* parent, int vertex);
extern "C" __global__ void mathblocks_graph(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output);

__device__ int mathblocks_graph_component_count(
    const MathBlockGraphKernelEdge* edges,
    int edge_count,
    int vertex_count,
    int* visited,
    int* queue)
{
    for (int index = 0; index < vertex_count; csharp2cuda_i32_post_increment(index))
        visited[index] = 0;
    int components = 0;
    for (int start = 0; start < vertex_count; csharp2cuda_i32_post_increment(start))
    {
        if (((visited[start])!=0))
            continue;
        csharp2cuda_i32_post_increment(components);
        int head = 0;
        int tail = 0;
        queue[csharp2cuda_i32_post_increment(tail)] = start;
        visited[start] = 1;
        while (head < tail)
        {
            int vertex = queue[csharp2cuda_i32_post_increment(head)];
            for (int edge_index = 0; edge_index < edge_count; csharp2cuda_i32_post_increment(edge_index))
            {
                int neighbor = edges[edge_index].from == vertex
                    ? edges[edge_index].to
                    : edges[edge_index].to == vertex
                        ? edges[edge_index].from
                        : -1;
                if (neighbor < 0 || ((visited[neighbor])!=0))
                    continue;
                visited[neighbor] = 1;
                queue[csharp2cuda_i32_post_increment(tail)] = neighbor;
            }
        }
    }
    return components;
}

__device__ bool mathblocks_graph_edge_less(
    const MathBlockGraphKernelEdge& left,
    const MathBlockGraphKernelEdge& right)
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

__device__ int mathblocks_graph_find(int* parent, int vertex)
{
    while (parent[vertex] != vertex)
    {
        parent[vertex] = parent[parent[vertex]];
        vertex = parent[vertex];
    }
    return vertex;
}

extern "C" __global__ void mathblocks_graph(
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

    const MathBlockGraphKernelEdge* edges =
        first == nullptr ? nullptr : (MathBlockGraphKernelEdge*)first->data_pointer;
    const double* matrix = first == nullptr ? nullptr : (double*)first->data_pointer;
    const int* boolean_values = second == nullptr ? nullptr : (int*)second->data_pointer;
    const double* vector = second == nullptr ? nullptr : (double*)second->data_pointer;
    double* result = (double*)output->data_pointer;
    double* scratch = (double*)output->scratch_pointer;

    if (thread == 0)
    {
        int vertex_count = first->rows;
        switch (opcode)
        {
            case 0:
                if (vertex_count <= 1)
                {
                    output->scalar_value = 0.0;
                    break;
                }
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                double* laplacian = scratch;
                double* work = csharp2cuda_pointer_add(laplacian, csharp2cuda_i32_mul(vertex_count, vertex_count));
                double* eigenvalues = csharp2cuda_pointer_add(work, csharp2cuda_i32_mul(vertex_count, vertex_count));
                for (int index = 0; index < csharp2cuda_i32_mul(vertex_count, vertex_count); csharp2cuda_i32_post_increment(index))
                    laplacian[index] = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    int from = edges[index].from;
                    int to = edges[index].to;
                    double weight = edges[index].weight;
                    laplacian[csharp2cuda_i32_add(csharp2cuda_i32_mul(from, vertex_count), from)] += weight;
                    laplacian[csharp2cuda_i32_add(csharp2cuda_i32_mul(to, vertex_count), to)] += weight;
                    laplacian[csharp2cuda_i32_add(csharp2cuda_i32_mul(from, vertex_count), to)] -= weight;
                    laplacian[csharp2cuda_i32_add(csharp2cuda_i32_mul(to, vertex_count), from)] -= weight;
                }
                mathblocks_matrix_symmetric_eigenvalues(
                    laplacian,
                    vertex_count,
                    work,
                    eigenvalues);
                output->scalar_value = eigenvalues[1];
                break;
            }
            case 1:
                if (second->count != vertex_count)
                {
                    output->valid = 0;
                    break;
                }
            {
                bool all = true;
                bool none = true;
                for (int index = 0; index < second->count; csharp2cuda_i32_post_increment(index))
                {
                    all = all && boolean_values[index] != 0;
                    none = none && boolean_values[index] == 0;
                }
                double cut = 0.0;
                double left_volume = 0.0;
                double right_volume = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    if (edges[index].weight < 0.0)
                    {
                        output->valid = 0;
                        break;
                    }
                    if (((boolean_values[edges[index].from])!=0))
                        left_volume += edges[index].weight;
                    else
                        right_volume += edges[index].weight;
                    if (((boolean_values[edges[index].to])!=0))
                        left_volume += edges[index].weight;
                    else
                        right_volume += edges[index].weight;
                    if (boolean_values[edges[index].from] != boolean_values[edges[index].to])
                        cut += edges[index].weight;
                }
                if (all || none)
                    output->valid = 0;
                else
                    output->scalar_value = cut /
                        (left_volume < right_volume ? left_volume : right_volume);
                break;
            }
            case 2:
            case 7:
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int* visited = (int*)scratch;
                int* queue = csharp2cuda_pointer_add(visited, vertex_count);
                int components = mathblocks_graph_component_count(
                    edges,
                    first->count,
                    vertex_count,
                    visited,
                    queue);
                if (opcode == 2)
                    output->scalar_value = (double)components;
                else
                    output->boolean_value = components == 1 ? 1 : 0;
                break;
            }
            case 3:
            case 15:
                mathblocks_sequence_set_vector_shape(output, vertex_count);
                for (int index = 0; index < vertex_count; csharp2cuda_i32_post_increment(index))
                    result[index] = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    double amount = opcode == 3 ? 1.0 : edges[index].weight;
                    result[edges[index].from] += amount;
                    result[edges[index].to] += amount;
                }
                break;
            case 4:
                if (first->rows != first->columns)
                {
                    output->valid = 0;
                    break;
                }
            {
                MathBlockGraphKernelEdge* graph =
                    (MathBlockGraphKernelEdge*)output->data_pointer;
                int edge_count = 0;
                for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                    for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                        if (row != column && matrix[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)] != 0.0)
                        {
                            if (edge_count >= output->capacity)
                            {
                                output->count = output->capacity == 2147483647
                                    ? -1
                                    : csharp2cuda_i32_add(output->capacity, 1);
                                output->valid = 0;
                                break;
                            }
                            graph[edge_count].from = row;
                            graph[edge_count].to = column;
                            graph[edge_count].weight = matrix[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)];
                            csharp2cuda_i32_post_increment(edge_count);
                        }
                output->rows = first->rows;
                if (output->valid)
                    output->count = edge_count;
                break;
            }
            case 5:
                mathblocks_sequence_set_vector_shape(output, vertex_count);
                if (vertex_count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                result[0] = 0.0;
                if (vertex_count == 1)
                    break;
            {
                int size = csharp2cuda_i32_sub(vertex_count, 1);
                double* reduced_matrix = scratch;
                double* right = csharp2cuda_pointer_add(reduced_matrix, csharp2cuda_i32_mul(size, size));
                double* augmented = csharp2cuda_pointer_add(right, size);
                for (int index = 0; index < csharp2cuda_i32_mul(size, size); csharp2cuda_i32_post_increment(index))
                    reduced_matrix[index] = 0.0;
                for (int index = 0; index < size; csharp2cuda_i32_post_increment(index))
                    right[index] = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    int from = edges[index].from;
                    int to = edges[index].to;
                    if (from != 0)
                    {
                        reduced_matrix[csharp2cuda_i32_sub(csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(from, 1)), size), from), 1)] += 1.0;
                        right[csharp2cuda_i32_sub(from, 1)] -= edges[index].weight;
                    }
                    if (to != 0)
                    {
                        reduced_matrix[csharp2cuda_i32_sub(csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(to, 1)), size), to), 1)] += 1.0;
                        right[csharp2cuda_i32_sub(to, 1)] += edges[index].weight;
                    }
                    if (from != 0 && to != 0)
                    {
                        reduced_matrix[csharp2cuda_i32_sub(csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(from, 1)), size), to), 1)] -= 1.0;
                        reduced_matrix[csharp2cuda_i32_sub(csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(to, 1)), size), from), 1)] -= 1.0;
                    }
                }
                if (!mathblocks_matrix_try_solve(
                    reduced_matrix,
                    right,
                    size,
                    augmented,
                    csharp2cuda_pointer_add(result, 1)))
                {
                    output->valid = 0;
                }
                break;
            }
            case 6:
                if (second->count != vertex_count)
                {
                    output->valid = 0;
                    break;
                }
            {
                double sum_squares = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    double residual = vector[edges[index].to] -
                                      vector[edges[index].from] -
                                      edges[index].weight;
                    sum_squares += residual * residual;
                }
                output->scalar_value = mathblocks_square_root(sum_squares);
                break;
            }
            case 8:
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                MathBlockGraphKernelEdge* work = (MathBlockGraphKernelEdge*)scratch;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    work[index] = edges[index];
                for (int index = 1; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    MathBlockGraphKernelEdge value = work[index];
                    int position = index;
                    while (position > 0 && mathblocks_graph_edge_less(value, work[csharp2cuda_i32_sub(position, 1)]))
                    {
                        work[position] = work[csharp2cuda_i32_sub(position, 1)];
                        csharp2cuda_i32_post_decrement(position);
                    }
                    work[position] = value;
                }
                int* parent = (int*)(csharp2cuda_pointer_add(work, first->count));
                unsigned char* rank = (unsigned char*)(csharp2cuda_pointer_add(parent, vertex_count));
                for (int index = 0; index < vertex_count; csharp2cuda_i32_post_increment(index))
                {
                    parent[index] = index;
                    rank[index] = 0;
                }
                MathBlockGraphKernelEdge* selected =
                    (MathBlockGraphKernelEdge*)output->data_pointer;
                int selected_count = 0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    int left = mathblocks_graph_find(parent, work[index].from);
                    int right = mathblocks_graph_find(parent, work[index].to);
                    if (left == right)
                        continue;
                    if ((int)(rank[left]) < (int)(rank[right]))
                        parent[left] = right;
                    else if ((int)(rank[left]) > (int)(rank[right]))
                        parent[right] = left;
                    else
                    {
                        parent[right] = left;
                        rank[left]++;
                    }
                    selected[csharp2cuda_i32_post_increment(selected_count)] = work[index];
                }
                output->rows = vertex_count;
                output->count = selected_count;
                break;
            }
            case 9:
                mathblocks_sequence_set_vector_shape(output, vertex_count);
                if (vertex_count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                double damping = second->scalar_value;
                int iterations = 0;
                if (damping < 0.0 || damping > 1.0 ||
                    !mathblocks_sequence_positive_integer(third->scalar_value, &iterations) ||
                    iterations > 10000)
                {
                    output->valid = 0;
                    break;
                }
                double* outgoing = scratch;
                double* next = csharp2cuda_pointer_add(scratch, vertex_count);
                for (int vertex = 0; vertex < vertex_count; csharp2cuda_i32_post_increment(vertex))
                {
                    result[vertex] = 1.0 / vertex_count;
                    outgoing[vertex] = 0.0;
                }
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    if (edges[index].weight < 0.0)
                    {
                        output->valid = 0;
                        break;
                    }
                    outgoing[edges[index].from] += edges[index].weight;
                }
                for (int iteration = 0; output->valid && iteration < iterations; csharp2cuda_i32_post_increment(iteration))
                {
                    for (int vertex = 0; vertex < vertex_count; csharp2cuda_i32_post_increment(vertex))
                        next[vertex] = (1.0 - damping) / vertex_count;
                    double dangling = 0.0;
                    for (int vertex = 0; vertex < vertex_count; csharp2cuda_i32_post_increment(vertex))
                        if (outgoing[vertex] == 0.0)
                            dangling += result[vertex];
                    double dangling_share = damping * dangling / vertex_count;
                    for (int vertex = 0; vertex < vertex_count; csharp2cuda_i32_post_increment(vertex))
                        next[vertex] += dangling_share;
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                        next[edges[index].to] += damping * result[edges[index].from] *
                            edges[index].weight / outgoing[edges[index].from];
                    for (int vertex = 0; vertex < vertex_count; csharp2cuda_i32_post_increment(vertex))
                        result[vertex] = next[vertex];
                }
                break;
            }
            case 10:
            case 12:
            case 13:
                mathblocks_sequence_set_matrix_shape(output, vertex_count, vertex_count);
                for (int index = 0; index < csharp2cuda_i32_mul(vertex_count, vertex_count); csharp2cuda_i32_post_increment(index))
                    result[index] = 0.0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    int from = edges[index].from;
                    int to = edges[index].to;
                    double weight = edges[index].weight;
                    if (opcode == 10)
                    {
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(from, vertex_count), to)] += weight;
                    }
                    else if (opcode == 12)
                    {
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(from, vertex_count), to)] += weight;
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(to, vertex_count), from)] += weight;
                    }
                    else
                    {
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(from, vertex_count), from)] += weight;
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(to, vertex_count), to)] += weight;
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(from, vertex_count), to)] -= weight;
                        result[csharp2cuda_i32_add(csharp2cuda_i32_mul(to, vertex_count), from)] -= weight;
                    }
                }
                break;
            case 11:
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int* adjacency = (int*)scratch;
                for (int index = 0; index < csharp2cuda_i32_mul(vertex_count, vertex_count); csharp2cuda_i32_post_increment(index))
                    adjacency[index] = 0;
                for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                {
                    adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(edges[index].from, vertex_count), edges[index].to)] = 1;
                    adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(edges[index].to, vertex_count), edges[index].from)] = 1;
                }
                int count = 0;
                for (int one = 0; one < vertex_count; csharp2cuda_i32_post_increment(one))
                    for (int two = csharp2cuda_i32_add(one, 1); two < vertex_count; csharp2cuda_i32_post_increment(two))
                        for (int three = csharp2cuda_i32_add(two, 1); three < vertex_count; csharp2cuda_i32_post_increment(three))
                            if (((adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(one, vertex_count), two)])!=0) &&
                                ((adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(one, vertex_count), three)])!=0) &&
                                ((adjacency[csharp2cuda_i32_add(csharp2cuda_i32_mul(two, vertex_count), three)])!=0))
                            {
                                csharp2cuda_i32_post_increment(count);
                            }
                output->scalar_value = (double)count;
                break;
            }
            case 14:
                mathblocks_sequence_set_vector_shape(output, vertex_count);
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int source = 0;
                if (!mathblocks_nonnegative_integer(second->scalar_value, &source) || source >= vertex_count)
                {
                    output->valid = 0;
                    break;
                }
                int* visited = (int*)scratch;
                for (int vertex = 0; vertex < vertex_count; csharp2cuda_i32_post_increment(vertex))
                {
                    result[vertex] = mathblocks_positive_infinity();
                    visited[vertex] = 0;
                }
                result[source] = 0.0;
                for (int iteration = 0; iteration < vertex_count; csharp2cuda_i32_post_increment(iteration))
                {
                    int vertex = -1;
                    double best = mathblocks_positive_infinity();
                    for (int candidate = 0; candidate < vertex_count; csharp2cuda_i32_post_increment(candidate))
                        if (!((visited[candidate])!=0) && result[candidate] < best)
                        {
                            best = result[candidate];
                            vertex = candidate;
                        }
                    if (vertex < 0)
                        break;
                    visited[vertex] = 1;
                    for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
                    {
                        if (edges[index].weight < 0.0)
                        {
                            output->valid = 0;
                            break;
                        }
                        int neighbor = edges[index].from == vertex
                            ? edges[index].to
                            : edges[index].to == vertex
                                ? edges[index].from
                                : -1;
                        if (neighbor < 0)
                            continue;
                        double candidate = result[vertex] + edges[index].weight;
                        result[neighbor] = result[neighbor] < candidate
                            ? result[neighbor]
                            : candidate;
                    }
                }
                break;
            }
        }

        if (output->valid &&
            opcode != 3 && opcode != 4 && opcode != 5 && opcode != 7 && opcode != 8 &&
            opcode != 9 && opcode != 10 && opcode != 12 && opcode != 13 && opcode != 14 &&
            opcode != 15 && !isfinite(output->scalar_value))
        {
            output->valid = 0;
        }
        if (output->valid &&
            (opcode == 3 || opcode == 5 || opcode == 9 || opcode == 10 || opcode == 12 ||
             opcode == 13 || opcode == 14 || opcode == 15))
        {
            for (int index = 0; index < output->count; csharp2cuda_i32_post_increment(index))
                if (!isfinite(result[index])) output->valid = 0;
        }
    }
}