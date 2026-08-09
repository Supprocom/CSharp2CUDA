__device__ void mathblocks_matrix_shape(MathBlockSlot* output, int rows, int columns)
{
    long long count = (long long)rows * columns;
    output->rows = rows;
    output->columns = columns;
    output->count = count > 2147483647LL ? -1 : (int)count;
    if (rows <= 0 || columns <= 0 || count > output->capacity)
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
    for (int index = 0; index < count; index++)
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
    for (int column = 0; column < columns; column++)
    {
        double temporary = values[left * columns + column];
        values[left * columns + column] = values[right * columns + column];
        values[right * columns + column] = temporary;
    }
}

__device__ double mathblocks_matrix_determinant(
    const double* source,
    int size,
    double* work)
{
    mathblocks_matrix_copy(source, work, size * size);
    double determinant = 1.0;
    for (int pivot = 0; pivot < size; pivot++)
    {
        int pivot_row = pivot;
        for (int row = pivot + 1; row < size; row++)
            if (fabs(work[row * size + pivot]) > fabs(work[pivot_row * size + pivot]))
                pivot_row = row;
        if (work[pivot_row * size + pivot] == 0.0)
            return 0.0;
        if (pivot_row != pivot)
        {
            mathblocks_matrix_swap_rows(work, size, pivot, pivot_row);
            determinant = -determinant;
        }
        double diagonal = work[pivot * size + pivot];
        determinant *= diagonal;
        for (int row = pivot + 1; row < size; row++)
        {
            double scale = work[row * size + pivot] / diagonal;
            for (int column = pivot + 1; column < size; column++)
                work[row * size + column] -= scale * work[pivot * size + column];
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
    int columns = size + 1;
    for (int row = 0; row < size; row++)
    {
        for (int column = 0; column < size; column++)
            augmented[row * columns + column] = matrix[row * size + column];
        augmented[row * columns + size] = right[row];
    }
    for (int pivot = 0; pivot < size; pivot++)
    {
        int pivot_row = pivot;
        for (int row = pivot + 1; row < size; row++)
            if (fabs(augmented[row * columns + pivot]) >
                fabs(augmented[pivot_row * columns + pivot]))
            {
                pivot_row = row;
            }
        if (augmented[pivot_row * columns + pivot] == 0.0)
            return false;
        if (pivot_row != pivot)
            mathblocks_matrix_swap_rows(augmented, columns, pivot, pivot_row);
        double diagonal = augmented[pivot * columns + pivot];
        for (int column = pivot; column <= size; column++)
            augmented[pivot * columns + column] /= diagonal;
        for (int row = 0; row < size; row++)
        {
            if (row == pivot)
                continue;
            double scale = augmented[row * columns + pivot];
            for (int column = pivot; column <= size; column++)
                augmented[row * columns + column] -=
                    scale * augmented[pivot * columns + column];
        }
    }
    for (int row = 0; row < size; row++)
    {
        solution[row] = augmented[row * columns + size];
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
    int columns = size + 1;
    for (int row = 0; row < size; row++)
    {
        for (int column = 0; column < size; column++)
            augmented[row * columns + column] = matrix[row * size + column];
        augmented[row * columns + size] = row == basis ? 1.0 : 0.0;
    }
    for (int pivot = 0; pivot < size; pivot++)
    {
        int pivot_row = pivot;
        for (int row = pivot + 1; row < size; row++)
            if (fabs(augmented[row * columns + pivot]) >
                fabs(augmented[pivot_row * columns + pivot]))
            {
                pivot_row = row;
            }
        if (augmented[pivot_row * columns + pivot] == 0.0)
            return false;
        if (pivot_row != pivot)
            mathblocks_matrix_swap_rows(augmented, columns, pivot, pivot_row);
        double diagonal = augmented[pivot * columns + pivot];
        for (int column = pivot; column <= size; column++)
            augmented[pivot * columns + column] /= diagonal;
        for (int row = 0; row < size; row++)
        {
            if (row == pivot)
                continue;
            double scale = augmented[row * columns + pivot];
            for (int column = pivot; column <= size; column++)
                augmented[row * columns + column] -=
                    scale * augmented[pivot * columns + column];
        }
    }
    for (int row = 0; row < size; row++)
    {
        solution[row] = augmented[row * columns + size];
        if (!isfinite(solution[row]))
            return false;
    }
    return true;
}

__device__ bool mathblocks_matrix_is_symmetric(const double* values, int rows, int columns)
{
    if (rows != columns)
        return false;
    for (int row = 0; row < rows; row++)
        for (int column = row + 1; column < columns; column++)
            if (values[row * columns + column] != values[column * columns + row])
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
    for (int index = 0; index < size * size; index++)
        lower[index] = 0.0;
    for (int row = 0; row < size; row++)
    {
        for (int column = 0; column <= row; column++)
        {
            double sum = values[row * size + column];
            for (int inner = 0; inner < column; inner++)
                sum -= lower[row * size + inner] * lower[column * size + inner];
            if (row == column)
            {
                if (sum <= 0.0)
                    return false;
                lower[row * size + column] = mathblocks_square_root(sum);
            }
            else
            {
                lower[row * size + column] = sum / lower[column * size + column];
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
    mathblocks_matrix_copy(source, work, size * size);
    for (int iteration = 0; iteration < 64 * size * size; iteration++)
    {
        int pivot_row = 0;
        int pivot_column = 0;
        double largest = 0.0;
        for (int row = 0; row < size; row++)
        {
            for (int column = row + 1; column < size; column++)
            {
                double magnitude = fabs(work[row * size + column]);
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
            2.0 * work[pivot_row * size + pivot_column],
            work[pivot_column * size + pivot_column] -
                work[pivot_row * size + pivot_row]);
        double cosine = mathblocks_cosine(angle);
        double sine = mathblocks_sine(angle);
        double aa = work[pivot_row * size + pivot_row];
        double bb = work[pivot_column * size + pivot_column];
        double ab = work[pivot_row * size + pivot_column];
        work[pivot_row * size + pivot_row] =
            cosine * cosine * aa - 2.0 * sine * cosine * ab + sine * sine * bb;
        work[pivot_column * size + pivot_column] =
            sine * sine * aa + 2.0 * sine * cosine * ab + cosine * cosine * bb;
        work[pivot_row * size + pivot_column] = 0.0;
        work[pivot_column * size + pivot_row] = 0.0;
        for (int other = 0; other < size; other++)
        {
            if (other == pivot_row || other == pivot_column)
                continue;
            double first = work[other * size + pivot_row];
            double second = work[other * size + pivot_column];
            double first_value = cosine * first - sine * second;
            double second_value = sine * first + cosine * second;
            work[other * size + pivot_row] = first_value;
            work[pivot_row * size + other] = first_value;
            work[other * size + pivot_column] = second_value;
            work[pivot_column * size + other] = second_value;
        }
    }
    for (int index = 0; index < size; index++)
        eigenvalues[index] = work[index * size + index];
    for (int index = 1; index < size; index++)
    {
        double value = eigenvalues[index];
        int position = index;
        while (position > 0 && eigenvalues[position - 1] > value)
        {
            eigenvalues[position] = eigenvalues[position - 1];
            position--;
        }
        eigenvalues[position] = value;
    }
}

__device__ int mathblocks_matrix_rank(const double* source, int rows, int columns, double* work)
{
    mathblocks_matrix_copy(source, work, rows * columns);
    int rank = 0;
    int pivot_column = 0;
    while (rank < rows && pivot_column < columns)
    {
        int pivot_row = rank;
        for (int row = rank + 1; row < rows; row++)
            if (fabs(work[row * columns + pivot_column]) >
                fabs(work[pivot_row * columns + pivot_column]))
            {
                pivot_row = row;
            }
        if (work[pivot_row * columns + pivot_column] == 0.0)
        {
            pivot_column++;
            continue;
        }
        mathblocks_matrix_swap_rows(work, columns, rank, pivot_row);
        double pivot = work[rank * columns + pivot_column];
        for (int row = rank + 1; row < rows; row++)
        {
            double scale = work[row * columns + pivot_column] / pivot;
            for (int column = pivot_column; column < columns; column++)
                work[row * columns + column] -= scale * work[rank * columns + column];
        }
        rank++;
        pivot_column++;
    }
    return rank;
}

__device__ void mathblocks_matrix_multiply_square(
    const double* left,
    const double* right,
    int size,
    double* destination)
{
    for (int row = 0; row < size; row++)
    {
        for (int column = 0; column < size; column++)
        {
            double sum = 0.0;
            for (int inner = 0; inner < size; inner++)
                sum += left[row * size + inner] * right[inner * size + column];
            destination[row * size + column] = sum;
        }
    }
}

__device__ int mathblocks_pop_count(int value)
{
    int count = 0;
    while (value != 0)
    {
        count += value & 1;
        value >>= 1;
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
    for (int row = 0; row < 31; row++)
    {
        if ((row_mask & (1 << row)) == 0)
            continue;
        int output_column = 0;
        for (int column = 0; column < 31; column++)
        {
            if ((column_mask & (1 << column)) == 0)
                continue;
            destination[output_row * order + output_column] =
                source[row * source_columns + column];
            output_column++;
        }
        output_row++;
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
        for (int index = 0; index < input_count; index++)
            if (inputs[index] == nullptr || !inputs[index]->valid) output->valid = 0;
    }
    __syncthreads();
    if (!output->valid)
        return;

    const double* a = first == nullptr ? nullptr : (const double*)first->data_pointer;
    const double* b = second == nullptr ? nullptr : (const double*)second->data_pointer;
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
        for (int index = thread; output->valid && index < first->count; index += blockDim.x)
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
            mathblocks_matrix_shape(output, first->rows + 1, first->columns);
            if (second->count != first->columns) output->valid = 0;
        }
        __syncthreads();
        for (int index = thread; output->valid && index < output->count; index += blockDim.x)
            result[index] = index < first->count ? a[index] : b[index - first->count];
        return;
    }

    if (opcode == 2 || opcode == 18)
    {
        int count = opcode == 2 ? first->columns : first->rows;
        if (thread == 0) mathblocks_set_vector_shape(output, count);
        __syncthreads();
        for (int index = thread; output->valid && index < count; index += blockDim.x)
        {
            double sum = 0.0;
            if (opcode == 2)
            {
                for (int row = 0; row < first->rows; row++)
                    sum += a[row * first->columns + index];
            }
            else
            {
                for (int column = 0; column < first->columns; column++)
                    sum += a[index * first->columns + column];
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
        for (int index = thread; output->valid && index < count; index += blockDim.x)
            result[index] = opcode == 3
                ? a[index * first->columns + selected]
                : a[selected * first->columns + index];
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
        for (int flat = thread; output->valid && flat < output->count; flat += blockDim.x)
        {
            int row = flat / first->columns;
            int column = flat - row * first->columns;
            double left_product = 0.0;
            double right_product = 0.0;
            for (int inner = 0; inner < first->columns; inner++)
            {
                left_product += a[row * first->columns + inner] *
                    b[inner * second->columns + column];
                right_product += b[row * second->columns + inner] *
                    a[inner * first->columns + column];
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
        for (int flat = thread; output->valid && flat < size * size; flat += blockDim.x)
        {
            int row = flat / size;
            int column = flat - row * size;
            result[flat] = row == column ? a[row] : 0.0;
        }
        return;
    }

    if (opcode == 6)
    {
        int count = first->rows < first->columns ? first->rows : first->columns;
        if (thread == 0) mathblocks_set_vector_shape(output, count);
        __syncthreads();
        for (int index = thread; output->valid && index < count; index += blockDim.x)
            result[index] = a[index * first->columns + index];
        return;
    }

    if (opcode == 7)
    {
        if (thread == 0) mathblocks_set_vector_shape(output, first->count);
        __syncthreads();
        for (int index = thread; output->valid && index < first->count; index += blockDim.x)
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
        for (int flat = thread; output->valid && flat < size * size; flat += blockDim.x)
        {
            int row = flat / size;
            int column = flat - row * size;
            double sum = 0.0;
            for (int inner = 0; inner < first->rows; inner++)
                sum += a[inner * first->columns + row] * a[inner * first->columns + column];
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
                (opcode == 11 ? a[first->count - 1] != b[0] : a[0] != b[0]))
            {
                output->valid = 0;
            }
        }
        __syncthreads();
        for (int flat = thread; output->valid && flat < output->count; flat += blockDim.x)
        {
            int row = flat / second->count;
            int column = flat - row * second->count;
            if (opcode == 23)
            {
                result[flat] = column >= row ? b[column - row] : a[row - column];
            }
            else
            {
                int index = row + column;
                result[flat] = index < first->count ? a[index] : b[index - first->count + 1];
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
        for (int flat = thread; output->valid && flat < size * size; flat += blockDim.x)
        {
            int row = flat / size;
            int column = flat - row * size;
            result[flat] = row == column ? 1.0 : 0.0;
        }
        return;
    }

    if (opcode == 13)
    {
        int rows = first->rows * second->rows;
        int columns = first->columns * second->columns;
        if (thread == 0) mathblocks_matrix_shape(output, rows, columns);
        __syncthreads();
        for (int flat = thread; output->valid && flat < rows * columns; flat += blockDim.x)
        {
            int row = flat / columns;
            int column = flat - row * columns;
            int left_row = row / second->rows;
            int right_row = row - left_row * second->rows;
            int left_column = column / second->columns;
            int right_column = column - left_column * second->columns;
            result[flat] = a[left_row * first->columns + left_column] *
                b[right_row * second->columns + right_column];
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
        for (int row = thread; output->valid && row < first->rows; row += blockDim.x)
        {
            double sum = 0.0;
            for (int column = 0; column < first->columns; column++)
                sum += a[row * first->columns + column] * b[column];
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
        for (int flat = thread; output->valid && flat < output->count; flat += blockDim.x)
        {
            int row = flat / second->columns;
            int column = flat - row * second->columns;
            double sum = 0.0;
            for (int inner = 0; inner < first->columns; inner++)
                sum += a[row * first->columns + inner] * b[inner * second->columns + column];
            result[flat] = sum;
            if (!isfinite(sum)) atomicExch(&output->valid, 0);
        }
        return;
    }

    if (opcode == 16)
    {
        if (thread == 0) mathblocks_matrix_shape(output, first->count, second->count);
        __syncthreads();
        for (int flat = thread; output->valid && flat < output->count; flat += blockDim.x)
        {
            int row = flat / second->count;
            int column = flat - row * second->count;
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
                (long long)rows * columns != first->count)
            {
                output->valid = 0;
            }
        }
        __syncthreads();
        for (int index = thread; output->valid && index < first->count; index += blockDim.x)
            result[index] = a[index];
        return;
    }

    if (opcode == 20)
    {
        if (thread == 0) mathblocks_matrix_shape(output, first->rows, first->columns);
        __syncthreads();
        for (int index = thread; output->valid && index < first->count; index += blockDim.x)
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
        for (int index = thread; output->valid && index < first->count; index += blockDim.x)
        {
            result[index] = a[index];
            result[first->count + index] = b[index];
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
            for (int index = 0; index < first->rows; index++)
                trace += a[index * first->columns + index];
            output->scalar_value = trace;
            if (!isfinite(trace)) output->valid = 0;
        }
        return;
    }

    if (opcode == 25)
    {
        if (thread == 0) mathblocks_matrix_shape(output, first->columns, first->rows);
        __syncthreads();
        for (int flat = thread; output->valid && flat < output->count; flat += blockDim.x)
        {
            int row = flat / first->rows;
            int column = flat - row * first->rows;
            result[flat] = a[column * first->columns + row];
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
        for (int row = 0; row < size; row++)
        {
            double row_sum = 0.0;
            for (int column = 0; column < size; column++)
                row_sum += fabs(a[row * size + column]);
            if (row_sum > norm) norm = row_sum;
        }
        int scaling = norm > 1.0 ? (int)ceil(mathblocks_binary_logarithm(norm)) : 0;
        if (scaling < 0) scaling = 0;
        double scale = mathblocks_power(2.0, (double)-scaling);
        double* scaled = scratch;
        double* term = scratch + count;
        double* temporary = scratch + count * 2;
        for (int index = 0; index < count; index++)
        {
            scaled[index] = a[index] * scale;
            result[index] = 0.0;
            term[index] = 0.0;
        }
        for (int index = 0; index < size; index++)
        {
            result[index * size + index] = 1.0;
            term[index * size + index] = 1.0;
        }
        for (int order = 1; order <= 48; order++)
        {
            mathblocks_matrix_multiply_square(term, scaled, size, temporary);
            double order_scale = 1.0 / order;
            for (int index = 0; index < count; index++)
            {
                term[index] = temporary[index] * order_scale;
                result[index] += term[index];
            }
        }
        for (int iteration = 0; iteration < scaling; iteration++)
        {
            mathblocks_matrix_multiply_square(result, result, size, temporary);
            mathblocks_matrix_copy(temporary, result, count);
        }
        for (int index = 0; index < count; index++)
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
        double* temporary = scratch + count;
        mathblocks_matrix_copy(a, power, count);
        for (int index = 0; index < count; index++)
            result[index] = 0.0;
        for (int index = 0; index < size; index++)
            result[index * size + index] = 1.0;
        while (exponent > 0)
        {
            if ((exponent & 1) != 0)
            {
                mathblocks_matrix_multiply_square(result, power, size, temporary);
                mathblocks_matrix_copy(temporary, result, count);
            }
            exponent >>= 1;
            if (exponent > 0)
            {
                mathblocks_matrix_multiply_square(power, power, size, temporary);
                mathblocks_matrix_copy(temporary, power, count);
            }
        }
        for (int index = 0; index < count; index++)
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
        double* solution = scratch + size * (size + 1);
        for (int column = 0; column < size; column++)
        {
            if (!mathblocks_matrix_try_solve_basis(a, size, column, augmented, solution))
            {
                output->valid = 0;
                return;
            }
            for (int row = 0; row < size; row++)
                result[row * size + column] = solution[row];
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
        double* work = scratch + first->count;
        output->boolean_value = 1;
        for (int order = 1; output->boolean_value && order <=
            (first->rows < first->columns ? first->rows : first->columns); order++)
        {
            int row_limit = 1 << first->rows;
            int column_limit = 1 << first->columns;
            for (int row_mask = 1; output->boolean_value && row_mask < row_limit; row_mask++)
            {
                if (mathblocks_pop_count(row_mask) != order)
                    continue;
                for (int column_mask = 1; column_mask < column_limit; column_mask++)
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
        double* eigenvalues = opcode == 43 ? result : scratch + first->count;
        if (opcode == 43 && !mathblocks_matrix_is_symmetric(a, size, size))
        {
            output->valid = 0;
            return;
        }
        if (opcode == 43) mathblocks_set_vector_shape(output, size);
        mathblocks_matrix_symmetric_eigenvalues(a, size, scratch, eigenvalues);
        if (opcode == 33) output->scalar_value = eigenvalues[size - 1];
        else if (opcode == 40) output->scalar_value = eigenvalues[0];
        for (int index = 0; index < size; index++)
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
        double* work = scratch + first->count;
        int row_mask = (1 << first->rows) - 1;
        int limit = 1 << first->columns;
        int output_index = 0;
        for (int column_mask = 1; column_mask < limit; column_mask++)
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
            result[output_index++] = mathblocks_matrix_determinant(
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
        for (int index = 0; index < first->count; index++)
            if (a[index] < 0.0) output->valid = 0;
        if (!output->valid)
            return;
        double* vector = opcode == 36 ? result : scratch;
        double* next = opcode == 36 ? scratch : scratch + size;
        double* products = opcode == 36 ? scratch + size : scratch + size * 2;
        for (int index = 0; index < size; index++)
            vector[index] = 1.0 / size;
        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int row = 0; row < size; row++)
            {
                double sum = 0.0;
                for (int column = 0; column < size; column++)
                    sum += a[row * size + column] * vector[column];
                next[row] = sum + vector[row];
            }
            double norm = mathblocks_compensated_sum(next, size);
            for (int index = 0; index < size; index++)
                vector[index] = next[index] / norm;
        }
        if (opcode == 36)
        {
            mathblocks_set_vector_shape(output, size);
            return;
        }
        for (int row = 0; row < size; row++)
        {
            double sum = 0.0;
            for (int column = 0; column < size; column++)
                sum += a[row * size + column] * vector[column];
            next[row] = sum;
        }
        for (int index = 0; index < size; index++)
            products[index] = vector[index] * next[index];
        double numerator = mathblocks_compensated_sum(products, size);
        for (int index = 0; index < size; index++)
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
        int limit = 1 << size;
        mathblocks_set_vector_shape(output, limit - 1);
        double* submatrix = scratch;
        double* work = scratch + first->count;
        for (int mask = 1; mask < limit; mask++)
        {
            int order = mathblocks_pop_count(mask);
            mathblocks_matrix_submatrix_from_masks(
                a,
                size,
                mask,
                mask,
                order,
                submatrix);
            result[mask - 1] = mathblocks_matrix_determinant(submatrix, order, work);
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
        int eliminated = size - retained;
        double* leading = scratch;
        double* upper = leading + retained * retained;
        double* lower = upper + retained * eliminated;
        double* trailing = lower + eliminated * retained;
        double* inverse = trailing + eliminated * eliminated;
        double* augmented = inverse + eliminated * eliminated;
        double* solution = augmented + eliminated * (eliminated + 1);
        double* upper_inverse = solution + eliminated;
        double* product = upper_inverse + retained * eliminated;
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                double value = a[row * size + column];
                if (row < retained && column < retained)
                    leading[row * retained + column] = value;
                else if (row < retained)
                    upper[row * eliminated + column - retained] = value;
                else if (column < retained)
                    lower[(row - retained) * retained + column] = value;
                else
                    trailing[(row - retained) * eliminated + column - retained] = value;
            }
        }
        for (int column = 0; column < eliminated; column++)
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
            for (int row = 0; row < eliminated; row++)
                inverse[row * eliminated + column] = solution[row];
        }
        for (int row = 0; row < retained; row++)
        {
            for (int column = 0; column < eliminated; column++)
            {
                double sum = 0.0;
                for (int inner = 0; inner < eliminated; inner++)
                    sum += upper[row * eliminated + inner] *
                        inverse[inner * eliminated + column];
                upper_inverse[row * eliminated + column] = sum;
            }
        }
        for (int row = 0; row < retained; row++)
        {
            for (int column = 0; column < retained; column++)
            {
                double sum = 0.0;
                for (int inner = 0; inner < eliminated; inner++)
                    sum += upper_inverse[row * eliminated + inner] *
                        lower[inner * retained + column];
                product[row * retained + column] = sum;
            }
        }
        mathblocks_matrix_shape(output, retained, retained);
        for (int index = 0; index < retained * retained; index++)
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
        double* work = gram + size * size;
        double* eigenvalues = work + size * size;
        for (int row = 0; row < size; row++)
        {
            for (int column = 0; column < size; column++)
            {
                double sum = 0.0;
                for (int inner = 0; inner < first->rows; inner++)
                    sum += a[inner * first->columns + row] *
                        a[inner * first->columns + column];
                gram[row * size + column] = sum;
            }
        }
        mathblocks_matrix_symmetric_eigenvalues(gram, size, work, eigenvalues);
        double largest = eigenvalues[size - 1];
        if (largest < 0.0) largest = 0.0;
        output->scalar_value = mathblocks_square_root(largest);
        if (!isfinite(output->scalar_value)) output->valid = 0;
    }
}