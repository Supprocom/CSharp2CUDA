__device__ void mathblocks_transport_sort_values(
    const double* values,
    int count,
    double* result)
{
    for (int index = 0; index < count; index++)
    {
        double value = values[index];
        int position = index;
        while (position > 0 && result[position - 1] > value)
        {
            result[position] = result[position - 1];
            position--;
        }
        result[position] = value;
    }
}

__device__ void mathblocks_transport_sort_indices(
    const double* locations,
    int count,
    int* result)
{
    for (int index = 0; index < count; index++)
    {
        int position = index;
        while (position > 0 && locations[result[position - 1]] > locations[index])
        {
            result[position] = result[position - 1];
            position--;
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
    for (int left_index = 0; left_index < left_count; left_index++)
        for (int right_index = 0; right_index < right_count; right_index++)
            sum += fabs(left[left_index] - right[right_index]);
    return sum / (left_count * right_count);
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
        for (int index = 0; index < input_count; index++)
            if (inputs[index] == nullptr || !inputs[index]->valid) output->valid = 0;
    }
    __syncthreads();
    if (!output->valid)
        return;

    const double* a = first == nullptr ? nullptr : (const double*)first->data_pointer;
    const double* b = second == nullptr ? nullptr : (const double*)second->data_pointer;
    const double* c = third == nullptr ? nullptr : (const double*)third->data_pointer;
    const double* d = fourth == nullptr ? nullptr : (const double*)fourth->data_pointer;
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
                for (int row = 0; row < first->rows; row++)
                {
                    int column = 0;
                    if (!mathblocks_nonnegative_integer(b[row], &column) || column >= first->columns)
                    {
                        output->valid = 0;
                        break;
                    }
                    total += a[row * first->columns + column];
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
                for (int row = 0; row < first->rows; row++)
                    for (int column = 0; column < first->columns; column++)
                        total += a[row * first->columns + column] *
                                 b[row * first->columns + column];
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
                int state_count = 1 << size;
                mathblocks_sequence_set_vector_shape(output, size);
                double* values = scratch;
                int* previous_mask = (int*)(values + state_count);
                int* chosen_column = previous_mask + state_count;
                for (int index = 0; index < state_count; index++)
                {
                    values[index] = mathblocks_positive_infinity();
                    previous_mask[index] = 0;
                    chosen_column[index] = 0;
                }
                values[0] = 0.0;
                for (int mask = 0; mask < state_count; mask++)
                {
                    int row = mathblocks_advanced_popcount(mask);
                    if (row >= size || !isfinite(values[mask]))
                        continue;
                    for (int column = 0; column < size; column++)
                    {
                        if ((mask & (1 << column)) != 0)
                            continue;
                        int next = mask | (1 << column);
                        double candidate = values[mask] + a[row * size + column];
                        if (candidate >= values[next])
                            continue;
                        values[next] = candidate;
                        previous_mask[next] = mask;
                        chosen_column[next] = column;
                    }
                }
                int current = state_count - 1;
                for (int row = size - 1; row >= 0; row--)
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
                for (int index = 0; index < output->count; index++)
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
                    result[left_index * second->count + right_index] += amount;
                    left_remaining -= amount;
                    right_remaining -= amount;
                    if (left_remaining == 0.0 && ++left_index < first->count)
                        left_remaining = a[left_index];
                    if (right_remaining == 0.0 && ++right_index < second->count)
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
                for (int index = 0; index < first->count - 1; index++)
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
                double* left_scale = kernel + first->count;
                double* right_scale = left_scale + first->rows;
                for (int row = 0; row < first->rows; row++)
                    for (int column = 0; column < first->columns; column++)
                        kernel[row * first->columns + column] =
                            mathblocks_exponential(-a[row * first->columns + column] /
                                                   fourth->scalar_value);
                for (int row = 0; row < first->rows; row++)
                    left_scale[row] = 1.0;
                for (int column = 0; column < first->columns; column++)
                    right_scale[column] = 1.0;
                for (int iteration = 0; iteration < iterations; iteration++)
                {
                    for (int row = 0; row < first->rows; row++)
                    {
                        double sum = 0.0;
                        for (int column = 0; column < first->columns; column++)
                            sum += kernel[row * first->columns + column] * right_scale[column];
                        left_scale[row] = b[row] / sum;
                    }
                    for (int column = 0; column < first->columns; column++)
                    {
                        double sum = 0.0;
                        for (int row = 0; row < first->rows; row++)
                            sum += kernel[row * first->columns + column] * left_scale[row];
                        right_scale[column] = c[column] / sum;
                    }
                }
                for (int row = 0; row < first->rows; row++)
                    for (int column = 0; column < first->columns; column++)
                        result[row * first->columns + column] =
                            left_scale[row] * kernel[row * first->columns + column] *
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
                double* right_sorted = scratch + first->count;
                mathblocks_transport_sort_values(a, first->count, left_sorted);
                mathblocks_transport_sort_values(b, second->count, right_sorted);
                double sum = 0.0;
                for (int index = 0; index < first->count; index++)
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
                int* right_order = left_order + first->count;
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
                    if (left_remaining == 0.0 && ++left_index < first->count)
                        left_remaining = b[left_order[left_index]];
                    if (right_remaining == 0.0 && ++right_index < third->count)
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
                for (int row = 0; row < first->rows; row++)
                    for (int column = 0; column < second->columns; column++)
                    {
                        double selected = opcode == 9
                            ? -mathblocks_positive_infinity()
                            : mathblocks_positive_infinity();
                        for (int inner = 0; inner < first->columns; inner++)
                        {
                            double candidate = a[row * first->columns + inner] +
                                b[inner * second->columns + column];
                            selected = opcode == 9
                                ? (selected > candidate ? selected : candidate)
                                : (selected < candidate ? selected : candidate);
                        }
                        result[row * second->columns + column] = selected;
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
            for (int index = 0; index < output->count; index++)
                if (!isfinite(result[index])) output->valid = 0;
    }
}