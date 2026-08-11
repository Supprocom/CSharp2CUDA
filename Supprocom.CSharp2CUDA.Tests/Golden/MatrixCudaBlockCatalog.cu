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

__device__ void mathblocks_matrix_shape(MathBlockSlot* output, int rows, int columns);
__device__ bool mathblocks_matrix_compatible(
    const MathBlockSlot* left,
    const MathBlockSlot* right);
__device__ void mathblocks_matrix_copy(const double* source, double* destination, int count);
__device__ void mathblocks_matrix_swap_rows(
    double* values,
    int columns,
    int left,
    int right);
__device__ double mathblocks_matrix_determinant(
    const double* source,
    int size,
    double* work);
__device__ bool mathblocks_matrix_try_solve(
    const double* matrix,
    const double* right,
    int size,
    double* augmented,
    double* solution);
__device__ bool mathblocks_matrix_try_solve_basis(
    const double* matrix,
    int size,
    int basis,
    double* augmented,
    double* solution);
__device__ bool mathblocks_matrix_is_symmetric(const double* values, int rows, int columns);
__device__ bool mathblocks_matrix_is_positive_definite(
    const double* values,
    int size,
    double* lower);
__device__ void mathblocks_matrix_symmetric_eigenvalues(
    const double* source,
    int size,
    double* work,
    double* eigenvalues);
__device__ int mathblocks_matrix_rank(const double* source, int rows, int columns, double* work);
__device__ void mathblocks_matrix_multiply_square(
    const double* left,
    const double* right,
    int size,
    double* destination);
__device__ int mathblocks_pop_count(int value);
__device__ void mathblocks_matrix_submatrix_from_masks(
    const double* source,
    int source_columns,
    int row_mask,
    int column_mask,
    int order,
    double* destination);
extern "C" __global__ void mathblocks_matrix(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output);

__device__ void mathblocks_matrix_shape(MathBlockSlot* output, int rows, int columns)
{
    long long count = csharp2cuda_i64_mul((long long)rows, columns);
    output->rows = rows;
    output->columns = columns;
    output->count = count > 2147483647LL ? -1 : csharp2cuda_i32_from_bits((unsigned int)(count));
    if (rows <= 0 || columns <= 0 || count > (long long)(output->capacity))
        output->valid = 0;
}

__device__ bool mathblocks_matrix_compatible(
    const MathBlockSlot* left,
    const MathBlockSlot* right)
{
    return left->rows == right->rows && left->columns == right->columns;
}

__device__ void mathblocks_matrix_copy(const double* source, double* destination, int count)
{
    for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
        destination[index] = source[index];
}

__device__ void mathblocks_matrix_swap_rows(
    double* values,
    int columns,
    int left,
    int right)
{
    if (left == right)
        return;
    for (int column = 0; column < columns; csharp2cuda_i32_post_increment(column))
    {
        double temporary = values[csharp2cuda_i32_add(csharp2cuda_i32_mul(left, columns), column)];
        values[csharp2cuda_i32_add(csharp2cuda_i32_mul(left, columns), column)] = values[csharp2cuda_i32_add(csharp2cuda_i32_mul(right, columns), column)];
        values[csharp2cuda_i32_add(csharp2cuda_i32_mul(right, columns), column)] = temporary;
    }
}

__device__ double mathblocks_matrix_determinant(
    const double* source,
    int size,
    double* work)
{
    mathblocks_matrix_copy(source, work, csharp2cuda_i32_mul(size, size));
    double determinant = 1.0;
    for (int pivot = 0; pivot < size; csharp2cuda_i32_post_increment(pivot))
    {
        int pivot_row = pivot;
        for (int row = csharp2cuda_i32_add(pivot, 1); row < size; csharp2cuda_i32_post_increment(row))
            if (fabs(work[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), pivot)]) > fabs(work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, size), pivot)]))
                pivot_row = row;
        if (work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, size), pivot)] == 0.0)
            return 0.0;
        if (pivot_row != pivot)
        {
            mathblocks_matrix_swap_rows(work, size, pivot, pivot_row);
            determinant = -determinant;
        }
        double diagonal = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot, size), pivot)];
        determinant *= diagonal;
        for (int row = csharp2cuda_i32_add(pivot, 1); row < size; csharp2cuda_i32_post_increment(row))
        {
            double scale = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), pivot)] / diagonal;
            for (int column = csharp2cuda_i32_add(pivot, 1); column < size; csharp2cuda_i32_post_increment(column))
                work[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)] -= scale * work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot, size), column)];
        }
    }
    return determinant;
}

__device__ bool mathblocks_matrix_try_solve(
    const double* matrix,
    const double* right,
    int size,
    double* augmented,
    double* solution)
{
    int columns = csharp2cuda_i32_add(size, 1);
    for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
    {
        for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
            augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)] = matrix[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)];
        augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), size)] = right[row];
    }
    for (int pivot = 0; pivot < size; csharp2cuda_i32_post_increment(pivot))
    {
        int pivot_row = pivot;
        for (int row = csharp2cuda_i32_add(pivot, 1); row < size; csharp2cuda_i32_post_increment(row))
            if (fabs(augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), pivot)]) >
                fabs(augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, columns), pivot)]))
            {
                pivot_row = row;
            }
        if (augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, columns), pivot)] == 0.0)
            return false;
        if (pivot_row != pivot)
            mathblocks_matrix_swap_rows(augmented, columns, pivot, pivot_row);
        double diagonal = augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot, columns), pivot)];
        for (int column = pivot; column <= size; csharp2cuda_i32_post_increment(column))
            augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot, columns), column)] /= diagonal;
        for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
        {
            if (row == pivot)
                continue;
            double scale = augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), pivot)];
            for (int column = pivot; column <= size; csharp2cuda_i32_post_increment(column))
                augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)] -=
                    scale * augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot, columns), column)];
        }
    }
    for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
    {
        solution[row] = augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), size)];
        if (!isfinite(solution[row]))
            return false;
    }
    return true;
}

__device__ bool mathblocks_matrix_try_solve_basis(
    const double* matrix,
    int size,
    int basis,
    double* augmented,
    double* solution)
{
    int columns = csharp2cuda_i32_add(size, 1);
    for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
    {
        for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
            augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)] = matrix[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)];
        augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), size)] = row == basis ? 1.0 : 0.0;
    }
    for (int pivot = 0; pivot < size; csharp2cuda_i32_post_increment(pivot))
    {
        int pivot_row = pivot;
        for (int row = csharp2cuda_i32_add(pivot, 1); row < size; csharp2cuda_i32_post_increment(row))
            if (fabs(augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), pivot)]) >
                fabs(augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, columns), pivot)]))
            {
                pivot_row = row;
            }
        if (augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, columns), pivot)] == 0.0)
            return false;
        if (pivot_row != pivot)
            mathblocks_matrix_swap_rows(augmented, columns, pivot, pivot_row);
        double diagonal = augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot, columns), pivot)];
        for (int column = pivot; column <= size; csharp2cuda_i32_post_increment(column))
            augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot, columns), column)] /= diagonal;
        for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
        {
            if (row == pivot)
                continue;
            double scale = augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), pivot)];
            for (int column = pivot; column <= size; csharp2cuda_i32_post_increment(column))
                augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)] -=
                    scale * augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot, columns), column)];
        }
    }
    for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
    {
        solution[row] = augmented[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), size)];
        if (!isfinite(solution[row]))
            return false;
    }
    return true;
}

__device__ bool mathblocks_matrix_is_symmetric(const double* values, int rows, int columns)
{
    if (rows != columns)
        return false;
    for (int row = 0; row < rows; csharp2cuda_i32_post_increment(row))
        for (int column = csharp2cuda_i32_add(row, 1); column < columns; csharp2cuda_i32_post_increment(column))
            if (values[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)] != values[csharp2cuda_i32_add(csharp2cuda_i32_mul(column, columns), row)])
                return false;
    return true;
}

__device__ bool mathblocks_matrix_is_positive_definite(
    const double* values,
    int size,
    double* lower)
{
    if (!mathblocks_matrix_is_symmetric(values, size, size))
        return false;
    for (int index = 0; index < csharp2cuda_i32_mul(size, size); csharp2cuda_i32_post_increment(index))
        lower[index] = 0.0;
    for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
    {
        for (int column = 0; column <= row; csharp2cuda_i32_post_increment(column))
        {
            double sum = values[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)];
            for (int inner = 0; inner < column; csharp2cuda_i32_post_increment(inner))
                sum -= lower[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), inner)] * lower[csharp2cuda_i32_add(csharp2cuda_i32_mul(column, size), inner)];
            if (row == column)
            {
                if (sum <= 0.0)
                    return false;
                lower[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)] = mathblocks_square_root(sum);
            }
            else
            {
                lower[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)] = sum / lower[csharp2cuda_i32_add(csharp2cuda_i32_mul(column, size), column)];
            }
        }
    }
    return true;
}

__device__ void mathblocks_matrix_symmetric_eigenvalues(
    const double* source,
    int size,
    double* work,
    double* eigenvalues)
{
    mathblocks_matrix_copy(source, work, csharp2cuda_i32_mul(size, size));
    for (int iteration = 0; iteration < csharp2cuda_i32_mul(csharp2cuda_i32_mul(64, size), size); csharp2cuda_i32_post_increment(iteration))
    {
        int pivot_row = 0;
        int pivot_column = 0;
        double largest = 0.0;
        for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
        {
            for (int column = csharp2cuda_i32_add(row, 1); column < size; csharp2cuda_i32_post_increment(column))
            {
                double magnitude = fabs(work[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)]);
                if (magnitude <= largest)
                    continue;
                largest = magnitude;
                pivot_row = row;
                pivot_column = column;
            }
        }
        if (largest == 0.0)
            break;
        double angle = 0.5 * mathblocks_arc_tangent_2(
            2.0 * work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, size), pivot_column)],
            work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_column, size), pivot_column)] -
                work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, size), pivot_row)]);
        double cosine = mathblocks_cosine(angle);
        double sine = mathblocks_sine(angle);
        double aa = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, size), pivot_row)];
        double bb = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_column, size), pivot_column)];
        double ab = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, size), pivot_column)];
        work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, size), pivot_row)] =
            cosine * cosine * aa - 2.0 * sine * cosine * ab + sine * sine * bb;
        work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_column, size), pivot_column)] =
            sine * sine * aa + 2.0 * sine * cosine * ab + cosine * cosine * bb;
        work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, size), pivot_column)] = 0.0;
        work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_column, size), pivot_row)] = 0.0;
        for (int other = 0; other < size; csharp2cuda_i32_post_increment(other))
        {
            if (other == pivot_row || other == pivot_column)
                continue;
            double first = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(other, size), pivot_row)];
            double second = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(other, size), pivot_column)];
            double first_value = cosine * first - sine * second;
            double second_value = sine * first + cosine * second;
            work[csharp2cuda_i32_add(csharp2cuda_i32_mul(other, size), pivot_row)] = first_value;
            work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, size), other)] = first_value;
            work[csharp2cuda_i32_add(csharp2cuda_i32_mul(other, size), pivot_column)] = second_value;
            work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_column, size), other)] = second_value;
        }
    }
    for (int index = 0; index < size; csharp2cuda_i32_post_increment(index))
        eigenvalues[index] = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(index, size), index)];
    for (int index = 1; index < size; csharp2cuda_i32_post_increment(index))
    {
        double value = eigenvalues[index];
        int position = index;
        while (position > 0 && eigenvalues[csharp2cuda_i32_sub(position, 1)] > value)
        {
            eigenvalues[position] = eigenvalues[csharp2cuda_i32_sub(position, 1)];
            csharp2cuda_i32_post_decrement(position);
        }
        eigenvalues[position] = value;
    }
}

__device__ int mathblocks_matrix_rank(const double* source, int rows, int columns, double* work)
{
    mathblocks_matrix_copy(source, work, csharp2cuda_i32_mul(rows, columns));
    int rank = 0;
    int pivot_column = 0;
    while (rank < rows && pivot_column < columns)
    {
        int pivot_row = rank;
        for (int row = csharp2cuda_i32_add(rank, 1); row < rows; csharp2cuda_i32_post_increment(row))
            if (fabs(work[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), pivot_column)]) >
                fabs(work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, columns), pivot_column)]))
            {
                pivot_row = row;
            }
        if (work[csharp2cuda_i32_add(csharp2cuda_i32_mul(pivot_row, columns), pivot_column)] == 0.0)
        {
            csharp2cuda_i32_post_increment(pivot_column);
            continue;
        }
        mathblocks_matrix_swap_rows(work, columns, rank, pivot_row);
        double pivot = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(rank, columns), pivot_column)];
        for (int row = csharp2cuda_i32_add(rank, 1); row < rows; csharp2cuda_i32_post_increment(row))
        {
            double scale = work[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), pivot_column)] / pivot;
            for (int column = pivot_column; column < columns; csharp2cuda_i32_post_increment(column))
                work[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, columns), column)] -= scale * work[csharp2cuda_i32_add(csharp2cuda_i32_mul(rank, columns), column)];
        }
        csharp2cuda_i32_post_increment(rank);
        csharp2cuda_i32_post_increment(pivot_column);
    }
    return rank;
}

__device__ void mathblocks_matrix_multiply_square(
    const double* left,
    const double* right,
    int size,
    double* destination)
{
    for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
    {
        for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
        {
            double sum = 0.0;
            for (int inner = 0; inner < size; csharp2cuda_i32_post_increment(inner))
                sum += left[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), inner)] * right[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, size), column)];
            destination[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)] = sum;
        }
    }
}

__device__ int mathblocks_pop_count(int value)
{
    int count = 0;
    while (value != 0)
    {
        csharp2cuda_i32_add_assign(count, csharp2cuda_i32_and(value, 1));
        csharp2cuda_i32_shr_assign(value, 1);
    }
    return count;
}

__device__ void mathblocks_matrix_submatrix_from_masks(
    const double* source,
    int source_columns,
    int row_mask,
    int column_mask,
    int order,
    double* destination)
{
    int output_row = 0;
    for (int row = 0; row < 31; csharp2cuda_i32_post_increment(row))
    {
        if ((csharp2cuda_i32_and(row_mask, (csharp2cuda_i32_shl(1, row)))) == 0)
            continue;
        int output_column = 0;
        for (int column = 0; column < 31; csharp2cuda_i32_post_increment(column))
        {
            if ((csharp2cuda_i32_and(column_mask, (csharp2cuda_i32_shl(1, column)))) == 0)
                continue;
            destination[csharp2cuda_i32_add(csharp2cuda_i32_mul(output_row, order), output_column)] =
                source[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, source_columns), column)];
            csharp2cuda_i32_post_increment(output_column);
        }
        csharp2cuda_i32_post_increment(output_row);
    }
}

extern "C" __global__ void mathblocks_matrix(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    int thread = (int)threadIdx.x;
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

    if (opcode == 0 || opcode == 10 || opcode == 22)
    {
        if (thread == 0)
        {
            mathblocks_matrix_shape(output, first->rows, first->columns);
            if (!mathblocks_matrix_compatible(first, second)) output->valid = 0;
        }
        __syncthreads();
        for (int index = thread; output->valid && index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
        {
            double value = opcode == 0 ? a[index] + b[index]
                : opcode == 10 ? a[index] * b[index]
                : a[index] - b[index];
            result[index] = value;
            if (!isfinite(value)) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 1)
    {
        if (thread == 0)
        {
            mathblocks_matrix_shape(output, csharp2cuda_i32_add(first->rows, 1), first->columns);
            if (second->count != first->columns) output->valid = 0;
        }
        __syncthreads();
        for (int index = thread; output->valid && index < output->count; csharp2cuda_i32_add_assign(index, blockDim.x))
            result[index] = index < first->count ? a[index] : b[csharp2cuda_i32_sub(index, first->count)];
        return;
    }

    if (opcode == 2 || opcode == 18)
    {
        int count = opcode == 2 ? first->columns : first->rows;
        if (thread == 0) mathblocks_set_vector_shape(output, count);
        __syncthreads();
        for (int index = thread; output->valid && index < count; csharp2cuda_i32_add_assign(index, blockDim.x))
        {
            double sum = 0.0;
            if (opcode == 2)
            {
                for (int row = 0; row < first->rows; csharp2cuda_i32_post_increment(row))
                    sum += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), index)];
            }
            else
            {
                for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                    sum += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(index, first->columns), column)];
            }
            result[index] = sum;
            if (!isfinite(sum)) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 3 || opcode == 19)
    {
        int selected = 0;
        bool valid_index = mathblocks_nonnegative_integer(second->scalar_value, &selected);
        int limit = opcode == 3 ? first->columns : first->rows;
        int count = opcode == 3 ? first->rows : first->columns;
        if (thread == 0)
        {
            mathblocks_set_vector_shape(output, count);
            if (!valid_index || selected >= limit) output->valid = 0;
        }
        __syncthreads();
        for (int index = thread; output->valid && index < count; csharp2cuda_i32_add_assign(index, blockDim.x))
            result[index] = opcode == 3
                ? a[csharp2cuda_i32_add(csharp2cuda_i32_mul(index, first->columns), selected)]
                : a[csharp2cuda_i32_add(csharp2cuda_i32_mul(selected, first->columns), index)];
        return;
    }

    if (opcode == 4)
    {
        if (thread == 0)
        {
            mathblocks_matrix_shape(output, first->rows, first->columns);
            if (!mathblocks_matrix_compatible(first, second) || first->rows != first->columns)
                output->valid = 0;
        }
        __syncthreads();
        for (int flat = thread; output->valid && flat < output->count; csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, first->columns);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, first->columns));
            double left_product = 0.0;
            double right_product = 0.0;
            for (int inner = 0; inner < first->columns; csharp2cuda_i32_post_increment(inner))
            {
                left_product += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), inner)] *
                    b[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, second->columns), column)];
                right_product += b[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, second->columns), inner)] *
                    a[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, first->columns), column)];
            }
            result[flat] = left_product - right_product;
            if (!isfinite(result[flat])) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 5)
    {
        int size = first->count;
        if (thread == 0)
        {
            mathblocks_matrix_shape(output, size, size);
            if (size <= 0) output->valid = 0;
        }
        __syncthreads();
        for (int flat = thread; output->valid && flat < csharp2cuda_i32_mul(size, size); csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, size);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, size));
            result[flat] = row == column ? a[row] : 0.0;
        }
        return;
    }

    if (opcode == 6)
    {
        int count = first->rows < first->columns ? first->rows : first->columns;
        if (thread == 0) mathblocks_set_vector_shape(output, count);
        __syncthreads();
        for (int index = thread; output->valid && index < count; csharp2cuda_i32_add_assign(index, blockDim.x))
            result[index] = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(index, first->columns), index)];
        return;
    }

    if (opcode == 7)
    {
        if (thread == 0) mathblocks_set_vector_shape(output, first->count);
        __syncthreads();
        for (int index = thread; output->valid && index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
            result[index] = a[index];
        return;
    }

    if (opcode == 8)
    {
        if (thread == 0)
        {
            output->scalar_value = mathblocks_square_root(
                mathblocks_compensated_product_sum(a, a, first->count));
            if (!isfinite(output->scalar_value)) output->valid = 0;
        }
        return;
    }

    if (opcode == 9)
    {
        int size = first->columns;
        if (thread == 0) mathblocks_matrix_shape(output, size, size);
        __syncthreads();
        for (int flat = thread; output->valid && flat < csharp2cuda_i32_mul(size, size); csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, size);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, size));
            double sum = 0.0;
            for (int inner = 0; inner < first->rows; csharp2cuda_i32_post_increment(inner))
                sum += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, first->columns), row)] * a[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, first->columns), column)];
            result[flat] = sum;
            if (!isfinite(sum)) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 11 || opcode == 23)
    {
        if (thread == 0)
        {
            mathblocks_matrix_shape(output, first->count, second->count);
            if (first->count <= 0 || second->count <= 0 ||
                (opcode == 11 ? a[csharp2cuda_i32_sub(first->count, 1)] != b[0] : a[0] != b[0]))
            {
                output->valid = 0;
            }
        }
        __syncthreads();
        for (int flat = thread; output->valid && flat < output->count; csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, second->count);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, second->count));
            if (opcode == 23)
            {
                result[flat] = column >= row ? b[csharp2cuda_i32_sub(column, row)] : a[csharp2cuda_i32_sub(row, column)];
            }
            else
            {
                int index = csharp2cuda_i32_add(row, column);
                result[flat] = index < first->count ? a[index] : b[csharp2cuda_i32_add(csharp2cuda_i32_sub(index, first->count), 1)];
            }
        }
        return;
    }

    if (opcode == 12)
    {
        int size = 0;
        bool valid_size = mathblocks_nonnegative_integer(first->scalar_value, &size);
        if (thread == 0)
        {
            mathblocks_matrix_shape(output, size, size);
            if (!valid_size || size <= 0 || size > 4096) output->valid = 0;
        }
        __syncthreads();
        for (int flat = thread; output->valid && flat < csharp2cuda_i32_mul(size, size); csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, size);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, size));
            result[flat] = row == column ? 1.0 : 0.0;
        }
        return;
    }

    if (opcode == 13)
    {
        int rows = csharp2cuda_i32_mul(first->rows, second->rows);
        int columns = csharp2cuda_i32_mul(first->columns, second->columns);
        if (thread == 0) mathblocks_matrix_shape(output, rows, columns);
        __syncthreads();
        for (int flat = thread; output->valid && flat < csharp2cuda_i32_mul(rows, columns); csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, columns);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, columns));
            int left_row = csharp2cuda_i32_div(row, second->rows);
            int right_row = csharp2cuda_i32_sub(row, csharp2cuda_i32_mul(left_row, second->rows));
            int left_column = csharp2cuda_i32_div(column, second->columns);
            int right_column = csharp2cuda_i32_sub(column, csharp2cuda_i32_mul(left_column, second->columns));
            result[flat] = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(left_row, first->columns), left_column)] *
                b[csharp2cuda_i32_add(csharp2cuda_i32_mul(right_row, second->columns), right_column)];
            if (!isfinite(result[flat])) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 14)
    {
        if (thread == 0)
        {
            mathblocks_set_vector_shape(output, first->rows);
            if (first->columns != second->count) output->valid = 0;
        }
        __syncthreads();
        for (int row = thread; output->valid && row < first->rows; csharp2cuda_i32_add_assign(row, blockDim.x))
        {
            double sum = 0.0;
            for (int column = 0; column < first->columns; csharp2cuda_i32_post_increment(column))
                sum += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), column)] * b[column];
            result[row] = sum;
            if (!isfinite(sum)) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 15)
    {
        if (thread == 0)
        {
            mathblocks_matrix_shape(output, first->rows, second->columns);
            if (first->columns != second->rows) output->valid = 0;
        }
        __syncthreads();
        for (int flat = thread; output->valid && flat < output->count; csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, second->columns);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, second->columns));
            double sum = 0.0;
            for (int inner = 0; inner < first->columns; csharp2cuda_i32_post_increment(inner))
                sum += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, first->columns), inner)] * b[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, second->columns), column)];
            result[flat] = sum;
            if (!isfinite(sum)) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 16)
    {
        if (thread == 0) mathblocks_matrix_shape(output, first->count, second->count);
        __syncthreads();
        for (int flat = thread; output->valid && flat < output->count; csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, second->count);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, second->count));
            result[flat] = a[row] * b[column];
            if (!isfinite(result[flat])) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 17)
    {
        int rows = 0;
        int columns = 0;
        bool valid_rows = mathblocks_nonnegative_integer(second->scalar_value, &rows);
        bool valid_columns = mathblocks_nonnegative_integer(third->scalar_value, &columns);
        if (thread == 0)
        {
            mathblocks_matrix_shape(output, rows, columns);
            if (!valid_rows || !valid_columns || rows <= 0 || columns <= 0 ||
                csharp2cuda_i64_mul((long long)rows, columns) != (long long)(first->count))
            {
                output->valid = 0;
            }
        }
        __syncthreads();
        for (int index = thread; output->valid && index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
            result[index] = a[index];
        return;
    }

    if (opcode == 20)
    {
        if (thread == 0) mathblocks_matrix_shape(output, first->rows, first->columns);
        __syncthreads();
        for (int index = thread; output->valid && index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
        {
            result[index] = a[index] * second->scalar_value;
            if (!isfinite(result[index])) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 21)
    {
        if (thread == 0)
        {
            mathblocks_matrix_shape(output, 2, first->count);
            if (first->count != second->count) output->valid = 0;
        }
        __syncthreads();
        for (int index = thread; output->valid && index < first->count; csharp2cuda_i32_add_assign(index, blockDim.x))
        {
            result[index] = a[index];
            result[csharp2cuda_i32_add(first->count, index)] = b[index];
        }
        return;
    }

    if (opcode == 24)
    {
        if (thread == 0)
        {
            if (first->rows != first->columns)
            {
                output->valid = 0;
                return;
            }
            double trace = 0.0;
            for (int index = 0; index < first->rows; csharp2cuda_i32_post_increment(index))
                trace += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(index, first->columns), index)];
            output->scalar_value = trace;
            if (!isfinite(trace)) output->valid = 0;
        }
        return;
    }

    if (opcode == 25)
    {
        if (thread == 0) mathblocks_matrix_shape(output, first->columns, first->rows);
        __syncthreads();
        for (int flat = thread; output->valid && flat < output->count; csharp2cuda_i32_add_assign(flat, blockDim.x))
        {
            int row = csharp2cuda_i32_div(flat, first->rows);
            int column = csharp2cuda_i32_sub(flat, csharp2cuda_i32_mul(row, first->rows));
            result[flat] = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(column, first->columns), row)];
        }
        return;
    }

    if (thread != 0)
        return;

    if (opcode == 26)
    {
        if (first->rows != first->columns || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        output->scalar_value = mathblocks_matrix_determinant(a, first->rows, scratch);
        if (!isfinite(output->scalar_value)) output->valid = 0;
        return;
    }

    if (opcode == 27)
    {
        int size = first->rows;
        int count = first->count;
        if (size != first->columns || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        mathblocks_matrix_shape(output, size, size);
        double norm = 0.0;
        for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
        {
            double row_sum = 0.0;
            for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
                row_sum += fabs(a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)]);
            if (row_sum > norm) norm = row_sum;
        }
        int scaling = norm > 1.0 ? (int)ceil(mathblocks_binary_logarithm(norm)) : 0;
        if (scaling < 0) scaling = 0;
        double scale = mathblocks_power(2.0, (double)csharp2cuda_i32_neg(scaling));
        double* scaled = scratch;
        double* term = csharp2cuda_pointer_add(scratch, count);
        double* temporary = csharp2cuda_pointer_add(scratch, csharp2cuda_i32_mul(count, 2));
        for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
        {
            scaled[index] = a[index] * scale;
            result[index] = 0.0;
            term[index] = 0.0;
        }
        for (int index = 0; index < size; csharp2cuda_i32_post_increment(index))
        {
            result[csharp2cuda_i32_add(csharp2cuda_i32_mul(index, size), index)] = 1.0;
            term[csharp2cuda_i32_add(csharp2cuda_i32_mul(index, size), index)] = 1.0;
        }
        for (int order = 1; order <= 48; csharp2cuda_i32_post_increment(order))
        {
            mathblocks_matrix_multiply_square(term, scaled, size, temporary);
            double order_scale = 1.0 / order;
            for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
            {
                term[index] = temporary[index] * order_scale;
                result[index] += term[index];
            }
        }
        for (int iteration = 0; iteration < scaling; csharp2cuda_i32_post_increment(iteration))
        {
            mathblocks_matrix_multiply_square(result, result, size, temporary);
            mathblocks_matrix_copy(temporary, result, count);
        }
        for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
            if (!isfinite(result[index])) output->valid = 0;
        return;
    }

    if (opcode == 28)
    {
        int exponent = 0;
        if (first->rows != first->columns ||
            !mathblocks_nonnegative_integer(second->scalar_value, &exponent) ||
            scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        int size = first->rows;
        int count = first->count;
        mathblocks_matrix_shape(output, size, size);
        double* power = scratch;
        double* temporary = csharp2cuda_pointer_add(scratch, count);
        mathblocks_matrix_copy(a, power, count);
        for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
            result[index] = 0.0;
        for (int index = 0; index < size; csharp2cuda_i32_post_increment(index))
            result[csharp2cuda_i32_add(csharp2cuda_i32_mul(index, size), index)] = 1.0;
        while (exponent > 0)
        {
            if ((csharp2cuda_i32_and(exponent, 1)) != 0)
            {
                mathblocks_matrix_multiply_square(result, power, size, temporary);
                mathblocks_matrix_copy(temporary, result, count);
            }
            csharp2cuda_i32_shr_assign(exponent, 1);
            if (exponent > 0)
            {
                mathblocks_matrix_multiply_square(power, power, size, temporary);
                mathblocks_matrix_copy(temporary, power, count);
            }
        }
        for (int index = 0; index < count; csharp2cuda_i32_post_increment(index))
            if (!isfinite(result[index])) output->valid = 0;
        return;
    }

    if (opcode == 29)
    {
        int size = first->rows;
        if (size != first->columns || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        mathblocks_matrix_shape(output, size, size);
        double* augmented = scratch;
        double* solution = csharp2cuda_pointer_add(scratch, csharp2cuda_i32_mul(size, (csharp2cuda_i32_add(size, 1))));
        for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
        {
            if (!mathblocks_matrix_try_solve_basis(a, size, column, augmented, solution))
            {
                output->valid = 0;
                return;
            }
            for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
                result[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)] = solution[row];
        }
        return;
    }

    if (opcode == 30)
    {
        output->boolean_value = first->rows == first->columns && scratch != nullptr &&
            mathblocks_matrix_is_positive_definite(a, first->rows, scratch);
        return;
    }

    if (opcode == 31)
    {
        output->boolean_value = mathblocks_matrix_is_symmetric(a, first->rows, first->columns);
        return;
    }

    if (opcode == 32)
    {
        if (first->rows > 8 || first->columns > 8 || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        double* submatrix = scratch;
        double* work = csharp2cuda_pointer_add(scratch, first->count);
        output->boolean_value = 1;
        for (int order = 1; output->boolean_value && order <=
            (first->rows < first->columns ? first->rows : first->columns); csharp2cuda_i32_post_increment(order))
        {
            int row_limit = csharp2cuda_i32_shl(1, first->rows);
            int column_limit = csharp2cuda_i32_shl(1, first->columns);
            for (int row_mask = 1; output->boolean_value && row_mask < row_limit; csharp2cuda_i32_post_increment(row_mask))
            {
                if (mathblocks_pop_count(row_mask) != order)
                    continue;
                for (int column_mask = 1; column_mask < column_limit; csharp2cuda_i32_post_increment(column_mask))
                {
                    if (mathblocks_pop_count(column_mask) != order)
                        continue;
                    mathblocks_matrix_submatrix_from_masks(
                        a,
                        first->columns,
                        row_mask,
                        column_mask,
                        order,
                        submatrix);
                    if (mathblocks_matrix_determinant(submatrix, order, work) < 0.0)
                    {
                        output->boolean_value = 0;
                        break;
                    }
                }
            }
        }
        return;
    }

    if (opcode == 33 || opcode == 40 || opcode == 43)
    {
        int size = first->rows;
        if (size != first->columns || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        double* eigenvalues = opcode == 43 ? result : csharp2cuda_pointer_add(scratch, first->count);
        if (opcode == 43 && !mathblocks_matrix_is_symmetric(a, size, size))
        {
            output->valid = 0;
            return;
        }
        if (opcode == 43) mathblocks_set_vector_shape(output, size);
        mathblocks_matrix_symmetric_eigenvalues(a, size, scratch, eigenvalues);
        if (opcode == 33) output->scalar_value = eigenvalues[csharp2cuda_i32_sub(size, 1)];
        else if (opcode == 40) output->scalar_value = eigenvalues[0];
        for (int index = 0; index < size; csharp2cuda_i32_post_increment(index))
            if (!isfinite(eigenvalues[index])) output->valid = 0;
        return;
    }

    if (opcode == 34)
    {
        if (first->rows > first->columns || first->columns > 20 || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        double* submatrix = scratch;
        double* work = csharp2cuda_pointer_add(scratch, first->count);
        int row_mask = csharp2cuda_i32_sub((csharp2cuda_i32_shl(1, first->rows)), 1);
        int limit = csharp2cuda_i32_shl(1, first->columns);
        int output_index = 0;
        for (int column_mask = 1; column_mask < limit; csharp2cuda_i32_post_increment(column_mask))
        {
            if (mathblocks_pop_count(column_mask) != first->rows)
                continue;
            mathblocks_matrix_submatrix_from_masks(
                a,
                first->columns,
                row_mask,
                column_mask,
                first->rows,
                submatrix);
            result[csharp2cuda_i32_post_increment(output_index)] = mathblocks_matrix_determinant(
                submatrix,
                first->rows,
                work);
        }
        mathblocks_set_vector_shape(output, output_index);
        return;
    }

    if (opcode == 35 || opcode == 36)
    {
        int iterations = 0;
        int size = first->rows;
        if (size != first->columns ||
            !mathblocks_nonnegative_integer(second->scalar_value, &iterations) ||
            iterations <= 0 || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        for (int index = 0; index < first->count; csharp2cuda_i32_post_increment(index))
            if (a[index] < 0.0) output->valid = 0;
        if (!output->valid)
            return;
        double* vector = opcode == 36 ? result : scratch;
        double* next = opcode == 36 ? scratch : csharp2cuda_pointer_add(scratch, size);
        double* products = opcode == 36 ? csharp2cuda_pointer_add(scratch, size) : csharp2cuda_pointer_add(scratch, csharp2cuda_i32_mul(size, 2));
        for (int index = 0; index < size; csharp2cuda_i32_post_increment(index))
            vector[index] = 1.0 / size;
        for (int iteration = 0; iteration < iterations; csharp2cuda_i32_post_increment(iteration))
        {
            for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
            {
                double sum = 0.0;
                for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
                    sum += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)] * vector[column];
                next[row] = sum + vector[row];
            }
            double norm = mathblocks_compensated_sum(next, size);
            for (int index = 0; index < size; csharp2cuda_i32_post_increment(index))
                vector[index] = next[index] / norm;
        }
        if (opcode == 36)
        {
            mathblocks_set_vector_shape(output, size);
            return;
        }
        for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
        {
            double sum = 0.0;
            for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
                sum += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)] * vector[column];
            next[row] = sum;
        }
        for (int index = 0; index < size; csharp2cuda_i32_post_increment(index))
            products[index] = vector[index] * next[index];
        double numerator = mathblocks_compensated_sum(products, size);
        for (int index = 0; index < size; csharp2cuda_i32_post_increment(index))
            products[index] = vector[index] * vector[index];
        double denominator = mathblocks_compensated_sum(products, size);
        output->scalar_value = numerator / denominator;
        if (!isfinite(output->scalar_value)) output->valid = 0;
        return;
    }

    if (opcode == 37)
    {
        int size = first->rows;
        if (size != first->columns || size > 20 || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        int limit = csharp2cuda_i32_shl(1, size);
        mathblocks_set_vector_shape(output, csharp2cuda_i32_sub(limit, 1));
        double* submatrix = scratch;
        double* work = csharp2cuda_pointer_add(scratch, first->count);
        for (int mask = 1; mask < limit; csharp2cuda_i32_post_increment(mask))
        {
            int order = mathblocks_pop_count(mask);
            mathblocks_matrix_submatrix_from_masks(
                a,
                size,
                mask,
                mask,
                order,
                submatrix);
            result[csharp2cuda_i32_sub(mask, 1)] = mathblocks_matrix_determinant(submatrix, order, work);
        }
        return;
    }

    if (opcode == 38)
    {
        if (scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        output->scalar_value = (double)mathblocks_matrix_rank(
            a,
            first->rows,
            first->columns,
            scratch);
        return;
    }

    if (opcode == 39)
    {
        int retained = 0;
        int size = first->rows;
        if (size != first->columns ||
            !mathblocks_nonnegative_integer(second->scalar_value, &retained) ||
            retained <= 0 || retained >= size || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        int eliminated = csharp2cuda_i32_sub(size, retained);
        double* leading = scratch;
        double* upper = csharp2cuda_pointer_add(leading, csharp2cuda_i32_mul(retained, retained));
        double* lower = csharp2cuda_pointer_add(upper, csharp2cuda_i32_mul(retained, eliminated));
        double* trailing = csharp2cuda_pointer_add(lower, csharp2cuda_i32_mul(eliminated, retained));
        double* inverse = csharp2cuda_pointer_add(trailing, csharp2cuda_i32_mul(eliminated, eliminated));
        double* augmented = csharp2cuda_pointer_add(inverse, csharp2cuda_i32_mul(eliminated, eliminated));
        double* solution = csharp2cuda_pointer_add(augmented, csharp2cuda_i32_mul(eliminated, (csharp2cuda_i32_add(eliminated, 1))));
        double* upper_inverse = csharp2cuda_pointer_add(solution, eliminated);
        double* product = csharp2cuda_pointer_add(upper_inverse, csharp2cuda_i32_mul(retained, eliminated));
        for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
        {
            for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
            {
                double value = a[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)];
                if (row < retained && column < retained)
                    leading[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, retained), column)] = value;
                else if (row < retained)
                    upper[csharp2cuda_i32_sub(csharp2cuda_i32_add(csharp2cuda_i32_mul(row, eliminated), column), retained)] = value;
                else if (column < retained)
                    lower[csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(row, retained)), retained), column)] = value;
                else
                    trailing[csharp2cuda_i32_sub(csharp2cuda_i32_add(csharp2cuda_i32_mul((csharp2cuda_i32_sub(row, retained)), eliminated), column), retained)] = value;
            }
        }
        for (int column = 0; column < eliminated; csharp2cuda_i32_post_increment(column))
        {
            if (!mathblocks_matrix_try_solve_basis(
                trailing,
                eliminated,
                column,
                augmented,
                solution))
            {
                output->valid = 0;
                return;
            }
            for (int row = 0; row < eliminated; csharp2cuda_i32_post_increment(row))
                inverse[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, eliminated), column)] = solution[row];
        }
        for (int row = 0; row < retained; csharp2cuda_i32_post_increment(row))
        {
            for (int column = 0; column < eliminated; csharp2cuda_i32_post_increment(column))
            {
                double sum = 0.0;
                for (int inner = 0; inner < eliminated; csharp2cuda_i32_post_increment(inner))
                    sum += upper[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, eliminated), inner)] *
                        inverse[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, eliminated), column)];
                upper_inverse[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, eliminated), column)] = sum;
            }
        }
        for (int row = 0; row < retained; csharp2cuda_i32_post_increment(row))
        {
            for (int column = 0; column < retained; csharp2cuda_i32_post_increment(column))
            {
                double sum = 0.0;
                for (int inner = 0; inner < eliminated; csharp2cuda_i32_post_increment(inner))
                    sum += upper_inverse[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, eliminated), inner)] *
                        lower[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, retained), column)];
                product[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, retained), column)] = sum;
            }
        }
        mathblocks_matrix_shape(output, retained, retained);
        for (int index = 0; index < csharp2cuda_i32_mul(retained, retained); csharp2cuda_i32_post_increment(index))
        {
            result[index] = leading[index] - product[index];
            if (!isfinite(result[index])) output->valid = 0;
        }
        return;
    }

    if (opcode == 41)
    {
        int size = first->rows;
        if (size != first->columns || second->count != size || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        mathblocks_set_vector_shape(output, size);
        if (!mathblocks_matrix_try_solve(a, b, size, scratch, result))
            output->valid = 0;
        return;
    }

    if (opcode == 42)
    {
        int iterations = 0;
        if (!mathblocks_nonnegative_integer(second->scalar_value, &iterations) ||
            iterations <= 0 || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        int size = first->columns;
        double* gram = scratch;
        double* work = csharp2cuda_pointer_add(gram, csharp2cuda_i32_mul(size, size));
        double* eigenvalues = csharp2cuda_pointer_add(work, csharp2cuda_i32_mul(size, size));
        for (int row = 0; row < size; csharp2cuda_i32_post_increment(row))
        {
            for (int column = 0; column < size; csharp2cuda_i32_post_increment(column))
            {
                double sum = 0.0;
                for (int inner = 0; inner < first->rows; csharp2cuda_i32_post_increment(inner))
                    sum += a[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, first->columns), row)] *
                        a[csharp2cuda_i32_add(csharp2cuda_i32_mul(inner, first->columns), column)];
                gram[csharp2cuda_i32_add(csharp2cuda_i32_mul(row, size), column)] = sum;
            }
        }
        mathblocks_matrix_symmetric_eigenvalues(gram, size, work, eigenvalues);
        double largest = eigenvalues[csharp2cuda_i32_sub(size, 1)];
        if (largest < 0.0) largest = 0.0;
        output->scalar_value = mathblocks_square_root(largest);
        if (!isfinite(output->scalar_value)) output->valid = 0;
    }
}