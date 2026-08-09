__device__ double mathblocks_minimum(double first, double second)
{
    if (first < second)
        return first;
    if (second < first)
        return second;
    if (first == 0.0)
        return signbit(first) ? first : second;
    return first;
}

__device__ double mathblocks_maximum(double first, double second)
{
    if (first > second)
        return first;
    if (second > first)
        return second;
    if (first == 0.0)
        return signbit(first) ? second : first;
    return first;
}

__device__ bool mathblocks_nonnegative_integer(double value, int* result)
{
    if (value < 0.0 || value > 2147483647.0 || value != trunc(value))
        return false;
    *result = (int)value;
    return true;
}

__device__ double mathblocks_compensated_sum(const double* values, int count)
{
    double sum = 0.0;
    double correction = 0.0;
    for (int index = 0; index < count; index++)
    {
        double value = values[index];
        double next = sum + value;
        correction += fabs(sum) >= fabs(value)
            ? sum - next + value
            : value - next + sum;
        sum = next;
    }
    return sum + correction;
}

__device__ double mathblocks_compensated_product_sum(
    const double* first,
    const double* second,
    int count)
{
    double sum = 0.0;
    double correction = 0.0;
    for (int index = 0; index < count; index++)
    {
        double value = first[index] * second[index];
        double next = sum + value;
        correction += fabs(sum) >= fabs(value)
            ? sum - next + value
            : value - next + sum;
        sum = next;
    }
    return sum + correction;
}

__device__ double mathblocks_compensated_absolute_sum(const double* values, int count)
{
    double sum = 0.0;
    double correction = 0.0;
    for (int index = 0; index < count; index++)
    {
        double value = fabs(values[index]);
        double next = sum + value;
        correction += fabs(sum) >= fabs(value)
            ? sum - next + value
            : value - next + sum;
        sum = next;
    }
    return sum + correction;
}

__device__ void mathblocks_set_vector_shape(MathBlockSlot* output, int count)
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

__device__ void mathblocks_copy_and_sort(
    const MathBlockSlot* input,
    MathBlockSlot* output)
{
    double* scratch = (double*)(output->data_pointer != 0
        ? output->data_pointer
        : output->scratch_pointer);
    const double* source = (const double*)input->data_pointer;
    for (int index = 0; index < input->count; index++)
    {
        double value = source[index];
        int position = index;
        while (position > 0 && scratch[position - 1] > value)
        {
            scratch[position] = scratch[position - 1];
            position--;
        }
        scratch[position] = value;
    }
}

__device__ double mathblocks_quantile(
    const MathBlockSlot* input,
    MathBlockSlot* output,
    double probability)
{
    mathblocks_copy_and_sort(input, output);
    double* scratch = (double*)(output->data_pointer != 0
        ? output->data_pointer
        : output->scratch_pointer);
    if (input->count == 1)
        return scratch[0];
    double position = probability * (input->count - 1);
    int lower = (int)floor(position);
    int upper = (int)ceil(position);
    double weight = position - lower;
    return scratch[lower] * (1.0 - weight) + scratch[upper] * weight;
}

extern "C" __global__ void mathblocks_vector(
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

    const double* a = first == nullptr ? nullptr : (const double*)first->data_pointer;
    const double* b = second == nullptr ? nullptr : (const double*)second->data_pointer;
    const double* c = third == nullptr ? nullptr : (const double*)third->data_pointer;
    const int* boolean_a = first == nullptr ? nullptr : (const int*)first->data_pointer;
    const int* boolean_b = second == nullptr ? nullptr : (const int*)second->data_pointer;
    double* result = (double*)(output->data_pointer != 0
        ? output->data_pointer
        : output->scratch_pointer);
    int* boolean_result = (int*)output->data_pointer;

    switch (opcode)
    {
        case 0:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
                result[index] = fabs(a[index]);
            break;
        case 1:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
            {
                result[index] = a[index] + second->scalar_value;
                if (!isfinite(result[index])) atomicExch(&output->valid, 0);
            }
            break;
        case 2:
        case 9:
        case 26:
        case 47:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, first->count);
                if (first->count != second->count) output->valid = 0;
            }
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
            {
                double value = opcode == 2 ? a[index] + b[index]
                    : opcode == 9 ? a[index] / b[index]
                    : opcode == 26 ? a[index] * b[index]
                    : a[index] - b[index];
                result[index] = value;
                if (!isfinite(value)) atomicExch(&output->valid, 0);
            }
            break;
        case 3:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count + 1);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
                result[index] = a[index];
            if (thread == 0 && output->valid) result[first->count] = second->scalar_value;
            break;
        case 4:
        case 5:
            if (thread == 0)
            {
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
                int selected = 0;
                for (int index = 1; index < first->count; index++)
                    if (opcode == 4 ? a[index] > a[selected] : a[index] < a[selected]) selected = index;
                output->scalar_value = (double)selected;
            }
            break;
        case 6:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count + second->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
                result[index] = a[index];
            for (int index = thread; output->valid && index < second->count; index += blockDim.x)
                result[first->count + index] = b[index];
            break;
        case 7:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, first->count);
                double product = 1.0;
                for (int index = 0; output->valid && index < first->count; index++)
                {
                    product *= a[index];
                    result[index] = product;
                    if (!isfinite(product)) output->valid = 0;
                }
            }
            break;
        case 8:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, first->count);
                double sum = 0.0;
                for (int index = 0; output->valid && index < first->count; index++)
                {
                    sum += a[index];
                    result[index] = sum;
                    if (!isfinite(sum)) output->valid = 0;
                }
            }
            break;
        case 10:
            if (thread == 0)
            {
                if (first->count != second->count) output->valid = 0;
                else
                {
                    output->scalar_value = mathblocks_compensated_product_sum(a, b, first->count);
                    if (!isfinite(output->scalar_value)) output->valid = 0;
                }
            }
            break;
        case 11:
        case 15:
        case 20:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, first->count);
                if (first->count != second->count) output->valid = 0;
            }
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
                boolean_result[index] = opcode == 11 ? a[index] == b[index]
                    : opcode == 15 ? a[index] > b[index]
                    : a[index] < b[index];
            break;
        case 12:
        case 27:
        case 44:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
            {
                double value = opcode == 12 ? mathblocks_exponential(a[index])
                    : opcode == 27 ? mathblocks_natural_logarithm(a[index])
                    : mathblocks_square_root(a[index]);
                result[index] = value;
                if (!isfinite(value)) atomicExch(&output->valid, 0);
            }
            break;
        case 13:
            if (thread == 0) mathblocks_set_vector_shape(output, second->count);
            __syncthreads();
            for (int index = thread; output->valid && index < second->count; index += blockDim.x)
            {
                int source_index = 0;
                if (!mathblocks_nonnegative_integer(b[index], &source_index) || source_index >= first->count)
                    atomicExch(&output->valid, 0);
                else
                    result[index] = a[source_index];
            }
            break;
        case 14:
            if (thread == 0)
            {
                if (first->count <= 0 || output->scratch_pointer == 0)
                {
                    output->valid = 0;
                    break;
                }
                for (int index = 0; index < first->count; index++)
                {
                    result[index] = mathblocks_natural_logarithm(a[index]);
                    if (!isfinite(result[index])) output->valid = 0;
                }
                if (output->valid)
                {
                    output->scalar_value = mathblocks_exponential(
                        mathblocks_compensated_sum(result, first->count) / first->count);
                    if (!isfinite(output->scalar_value)) output->valid = 0;
                }
            }
            break;
        case 16:
            if (thread == 0)
            {
                int index = 0;
                if (!mathblocks_nonnegative_integer(second->scalar_value, &index) || index >= first->count)
                    output->valid = 0;
                else
                    output->scalar_value = a[index];
            }
            break;
        case 17:
        case 18:
            if (thread == 0)
            {
                double norm = opcode == 17
                    ? mathblocks_compensated_absolute_sum(a, first->count)
                    : mathblocks_square_root(mathblocks_compensated_product_sum(a, a, first->count));
                output->scalar_value = norm;
                if (!isfinite(norm)) output->valid = 0;
            }
            break;
        case 19:
            if (thread == 0) output->scalar_value = (double)first->count;
            break;
        case 21:
            if (thread == 0)
            {
                int count = 0;
                if (!mathblocks_nonnegative_integer(third->scalar_value, &count) || count <= 0 || count > 1000000)
                    output->valid = 0;
                else
                    mathblocks_set_vector_shape(output, count);
            }
            __syncthreads();
            if (output->valid)
            {
                double start = first->scalar_value;
                double end = second->scalar_value;
                double step = output->count == 1 ? 0.0 : (end - start) / (output->count - 1);
                for (int index = thread; index < output->count; index += blockDim.x)
                    result[index] = index == output->count - 1 ? end : start + step * index;
            }
            break;
        case 22:
        case 25:
            if (thread == 0)
            {
                if (first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
                double selected = a[0];
                for (int index = 1; index < first->count; index++)
                    selected = opcode == 22
                        ? mathblocks_maximum(selected, a[index])
                        : mathblocks_minimum(selected, a[index]);
                output->scalar_value = selected;
            }
            break;
        case 23:
        case 48:
            if (thread == 0)
            {
                double sum = mathblocks_compensated_sum(a, first->count);
                output->scalar_value = opcode == 23 ? sum / first->count : sum;
                if (!isfinite(output->scalar_value)) output->valid = 0;
            }
            break;
        case 24:
        case 35:
            if (thread == 0)
            {
                double probability = opcode == 24 ? 0.5 : second->scalar_value;
                if (first->count <= 0 || output->scratch_pointer == 0 ||
                    probability < 0.0 || probability > 1.0)
                    output->valid = 0;
                else
                {
                    output->scalar_value = mathblocks_quantile(first, output, probability);
                    if (!isfinite(output->scalar_value)) output->valid = 0;
                }
            }
            break;
        case 28:
        case 29:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, first->count);
                output->scalar_value = opcode == 28
                    ? mathblocks_compensated_absolute_sum(a, first->count)
                    : mathblocks_square_root(mathblocks_compensated_product_sum(a, a, first->count));
                if (!isfinite(output->scalar_value) || output->scalar_value == 0.0) output->valid = 0;
            }
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
            {
                result[index] = a[index] * (1.0 / output->scalar_value);
                if (!isfinite(result[index])) atomicExch(&output->valid, 0);
            }
            break;
        case 30:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, 2);
                if (output->valid)
                {
                    result[0] = first->scalar_value;
                    result[1] = second->scalar_value;
                }
            }
            break;
        case 31:
        case 41:
        case 45:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
            {
                double value = opcode == 31 ? mathblocks_maximum(a[index], 0.0)
                    : opcode == 41 ? (double)((a[index] > 0.0) - (a[index] < 0.0))
                    : a[index] * a[index];
                result[index] = value;
                if (!isfinite(value)) atomicExch(&output->valid, 0);
            }
            break;
        case 32:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
            {
                result[index] = mathblocks_power(a[index], second->scalar_value);
                if (!isfinite(result[index])) atomicExch(&output->valid, 0);
            }
            break;
        case 33:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count + 1);
            __syncthreads();
            if (thread == 0 && output->valid) result[0] = second->scalar_value;
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
                result[index + 1] = a[index];
            break;
        case 34:
            if (thread == 0)
            {
                double product = 1.0;
                for (int index = 0; index < first->count; index++) product *= a[index];
                output->scalar_value = product;
                if (!isfinite(product)) output->valid = 0;
            }
            break;
        case 36:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
            {
                int less = 0;
                int equal = 0;
                for (int other = 0; other < first->count; other++)
                {
                    if (a[other] < a[index]) less++;
                    else if (a[other] == a[index]) equal++;
                }
                result[index] = less + (equal + 1.0) / 2.0;
            }
            break;
        case 37:
            if (thread == 0)
            {
                int count = 0;
                if (!mathblocks_nonnegative_integer(second->scalar_value, &count) || count > 1000000)
                    output->valid = 0;
                else
                    mathblocks_set_vector_shape(output, count);
            }
            __syncthreads();
            for (int index = thread; output->valid && index < output->count; index += blockDim.x)
                result[index] = first->scalar_value;
            break;
        case 38:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
                result[index] = a[first->count - index - 1];
            break;
        case 39:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
            {
                result[index] = a[index] * second->scalar_value;
                if (!isfinite(result[index])) atomicExch(&output->valid, 0);
            }
            break;
        case 40:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, first->count);
                if (first->count != second->count || first->count != third->count) output->valid = 0;
            }
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
                result[index] = boolean_a[index] ? b[index] : c[index];
            break;
        case 42:
            if (thread == 0)
            {
                int start = 0;
                int length = 0;
                if (!mathblocks_nonnegative_integer(second->scalar_value, &start) ||
                    !mathblocks_nonnegative_integer(third->scalar_value, &length) ||
                    start > first->count || length > first->count - start)
                    output->valid = 0;
                else
                    mathblocks_set_vector_shape(output, length);
            }
            __syncthreads();
            if (output->valid)
            {
                int start = (int)second->scalar_value;
                for (int index = thread; index < output->count; index += blockDim.x)
                    result[index] = a[start + index];
            }
            break;
        case 43:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, first->count);
                if (output->valid) mathblocks_copy_and_sort(first, output);
            }
            break;
        case 46:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, first->count);
                if (!output->valid || first->count <= 0)
                {
                    output->valid = 0;
                    break;
                }
                double mean = mathblocks_compensated_sum(a, first->count) / first->count;
                double variance = 0.0;
                for (int index = 0; index < first->count; index++)
                {
                    double difference = a[index] - mean;
                    variance += difference * difference;
                }
                variance /= first->count;
                double deviation = mathblocks_square_root(variance);
                for (int index = 0; index < first->count; index++)
                {
                    result[index] = (a[index] - mean) / deviation;
                    if (!isfinite(result[index])) output->valid = 0;
                }
            }
            break;
        case 49:
            if (thread == 0)
            {
                int count = 0;
                for (int index = 0; index < first->count; index++)
                {
                    bool found = false;
                    for (int prior = 0; prior < count; prior++)
                        if (result[prior] == a[index]) { found = true; break; }
                    if (!found) result[count++] = a[index];
                }
                mathblocks_set_vector_shape(output, count);
            }
            break;
        case 50:
        case 52:
        case 55:
        {
            if (thread == 0)
            {
                output->boolean_value = opcode == 50 ? 1 : 0;
                output->scalar_value = 0.0;
            }
            __syncthreads();
            int local_count = 0;
            bool local_all = true;
            bool local_any = false;
            for (int index = thread; index < first->count; index += blockDim.x)
            {
                bool value = boolean_a[index] != 0;
                if (value) local_count++;
                local_all = local_all && value;
                local_any = local_any || value;
            }
            if (opcode == 55 && local_count != 0)
                atomicAdd(&output->boolean_value, local_count);
            else if (opcode == 50 && !local_all)
                atomicExch(&output->boolean_value, 0);
            else if (opcode == 52 && local_any)
                atomicExch(&output->boolean_value, 1);
            __syncthreads();
            if (thread == 0 && opcode == 55)
            {
                output->scalar_value = (double)output->boolean_value;
                output->boolean_value = 0;
            }
            break;
        }
        case 51:
        case 54:
        case 57:
            if (thread == 0)
            {
                mathblocks_set_vector_shape(output, first->count);
                if (first->count != second->count) output->valid = 0;
            }
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
                boolean_result[index] = opcode == 51 ? boolean_a[index] && boolean_b[index]
                    : opcode == 54 ? boolean_a[index] || boolean_b[index]
                    : boolean_a[index] != boolean_b[index];
            break;
        case 53:
            if (thread == 0) mathblocks_set_vector_shape(output, first->count);
            __syncthreads();
            for (int index = thread; output->valid && index < first->count; index += blockDim.x)
                boolean_result[index] = !boolean_a[index];
            break;
        case 56:
            if (thread == 0)
            {
                int count = 0;
                for (int index = 0; index < first->count; index++)
                    if (boolean_a[index]) result[count++] = (double)index;
                mathblocks_set_vector_shape(output, count);
            }
            break;
        default:
            if (thread == 0) output->valid = 0;
            break;
    }
}