#pragma warning disable CS0078, CS0649

using System;
using Supprocom.CSharp2CUDA;

internal static unsafe class AdvancedModule
{
    [CudaExternal]
    public struct MathBlockSlot
    {
        public double scalar_value;
        public ulong data_pointer;
        public ulong scratch_pointer;
        public CudaInt32 boolean_value;
        public CudaInt32 valid;
        public int rows;
        public int columns;
        public int count;
        public int capacity;
    }

    [CudaExternal(IsPure = true)]
    private static double mathblocks_compensated_sum([CudaReadOnly] double* values, int count) => throw new NotSupportedException();

    [CudaExternal(IsPure = true)]
    private static double mathblocks_natural_logarithm(double value) => throw new NotSupportedException();

    [CudaExternal(IsPure = true)]
    private static double mathblocks_positive_infinity() => throw new NotSupportedException();

    [CudaExternal(IsPure = true)]
    private static bool mathblocks_probability_distribution([CudaReadOnly] double* values, int count) => throw new NotSupportedException();

    [CudaExternal]
    private static bool mathblocks_sequence_positive_integer(double value, int* result) => throw new NotSupportedException();

    [CudaExternal]
    private static void mathblocks_sequence_set_vector_shape(MathBlockSlot* output, int count) => throw new NotSupportedException();
    [CudaDevice]
    private static bool mathblocks_advanced_power_of_two(int value)
    {
        return value > 0 && (value & (value - 1)) == 0;
    }

    [CudaDevice]
    private static int mathblocks_advanced_log_two(int value)
    {
        int result = 0;
        while (value > 1)
        {
            value >>= 1;
            result++;
        }
        return result;
    }

    [CudaDevice]
    private static int mathblocks_advanced_popcount(int value)
    {
        int result = 0;
        while (value != 0)
        {
            result += value & 1;
            value >>= 1;
        }
        return result;
    }

    [CudaDevice]
    private static double mathblocks_advanced_factorial(int value)
    {
        double result = 1.0;
        for (int index = 2; index <= value; index++)
            result *= index;
        return result;
    }

    [CudaDevice]
    private static bool mathblocks_advanced_distribution([CudaReadOnly] double* values, int count)
    {
        return mathblocks_probability_distribution(values, count);
    }

    [CudaDevice]
    private static bool mathblocks_advanced_transition(
        [CudaReadOnly] double* values,
        int rows,
        int columns)
    {
        if (rows != columns)
            return false;
        for (int row = 0; row < rows; row++)
        {
            double sum = 0.0;
            for (int column = 0; column < columns; column++)
            {
                double value = values[row * columns + column];
                if (value < 0.0)
                    return false;
                sum += value;
            }
            if (Math.Abs(sum - 1.0) > 1e-10)
                return false;
        }
        return true;
    }

    [CudaDevice]
    private static void mathblocks_advanced_sort_descending(
        [CudaReadOnly] double* values,
        int count,
        double* result)
    {
        for (int index = 0; index < count; index++)
        {
            double value = values[index];
            int position = index;
            while (position > 0 && result[position - 1] < value)
            {
                result[position] = result[position - 1];
                position--;
            }
            result[position] = value;
        }
    }

    [CudaGlobal]
    private static void mathblocks_advanced(
        int opcode,
        [CudaReadOnly] MathBlockSlot** inputs,
        int input_count,
        MathBlockSlot* output)
    {
        int thread = (int)Cuda.ThreadIdx.X;
        if (Cuda.BlockIdx.X != 0)
            return;

        MathBlockSlot* first = Cuda.ReadOnly(input_count > 0 ? inputs[0] : null);
        MathBlockSlot* second = Cuda.ReadOnly(input_count > 1 ? inputs[1] : null);
        MathBlockSlot* third = Cuda.ReadOnly(input_count > 2 ? inputs[2] : null);
        MathBlockSlot* fourth = Cuda.ReadOnly(input_count > 3 ? inputs[3] : null);
        if (thread == 0)
        {
            output->scalar_value = 0.0;
            output->boolean_value = 0;
            output->rows = 0;
            output->columns = 0;
            output->count = 0;
            output->valid = 1;
            for (int index = 0; index < input_count; index++)
                if (inputs[index] == null || !inputs[index]->valid) output->valid = 0;
        }
        Cuda.SyncThreads();
        if (!output->valid)
            return;

        double* a = Cuda.ReadOnly(first == null ? null : (double*)first->data_pointer);
        double* b = Cuda.ReadOnly(second == null ? null : (double*)second->data_pointer);
        double* c = Cuda.ReadOnly(third == null ? null : (double*)third->data_pointer);
        double* result = (double*)output->data_pointer;
        double* scratch = (double*)output->scratch_pointer;

        if (thread == 0)
        {
            switch (opcode)
            {
                case 0:
                    if (first->count <= 0 || first->count >= 31 ||
                        second->count != (1 << first->count) || scratch == null)
                    {
                        output->valid = 0;
                        break;
                    }
                    for (int index = 0; index < first->count; index++)
                    {
                        if (a[index] < 0.0)
                        {
                            output->valid = 0;
                            break;
                        }
                        int position = index;
                        while (position > 0 && a[(int)scratch[position - 1]] > a[index])
                        {
                            scratch[position] = scratch[position - 1];
                            position--;
                        }
                        scratch[position] = (double)index;
                    }
                    if (output->valid)
                    {
                        double total = 0.0;
                        double previous = 0.0;
                        for (int position = 0; position < first->count; position++)
                        {
                            int coalition = 0;
                            for (int index = position; index < first->count; index++)
                                coalition |= 1 << (int)scratch[index];
                            int ordered = (int)scratch[position];
                            total += (a[ordered] - previous) * b[coalition];
                            previous = a[ordered];
                        }
                        output->scalar_value = total;
                    }
                    break;
                case 1:
                    if (!mathblocks_advanced_power_of_two(first->count) || first->count > (1 << 12))
                    {
                        output->valid = 0;
                        break;
                    }
                    output->boolean_value = 1;
                    for (int left = 0; left < first->count && output->boolean_value; left++)
                        for (int right = 0; right < first->count; right++)
                            if (a[left] + a[right] < a[left | right] + a[left & right])
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
                    for (int index = 0; index < first->count; index++)
                        result[index] = a[index];
                    for (int bit = 0; bit < mathblocks_advanced_log_two(first->count); bit++)
                        for (int mask = 0; mask < first->count; mask++)
                            if ((mask & (1 << bit)) != 0)
                                result[mask] -= result[mask ^ (1 << bit)];
                    break;
                case 3:
                    if (!mathblocks_advanced_power_of_two(first->count) || first->count > (1 << 20))
                    {
                        output->valid = 0;
                        break;
                    }
                {
                    int player_count = mathblocks_advanced_log_two(first->count);
                    mathblocks_sequence_set_vector_shape(output, player_count);
                    double denominator = mathblocks_advanced_factorial(player_count);
                    for (int player = 0; player < player_count; player++)
                    {
                        result[player] = 0.0;
                        for (int coalition = 0; coalition < first->count; coalition++)
                        {
                            if ((coalition & (1 << player)) != 0)
                                continue;
                            int size = mathblocks_advanced_popcount(coalition);
                            double weight = mathblocks_advanced_factorial(size) *
                                mathblocks_advanced_factorial(player_count - size - 1) /
                                denominator;
                            result[player] += weight *
                                (a[coalition | (1 << player)] - a[coalition]);
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
                    for (int query = 0; query < third->count; query++)
                    {
                        double selected = opcode == 4
                            ? mathblocks_positive_infinity()
                            : -mathblocks_positive_infinity();
                        for (int index = 0; index < first->count; index++)
                        {
                            double candidate = opcode == 4
                                ? b[index] + fourth->scalar_value * Math.Abs(c[query] - a[index])
                                : b[index] - fourth->scalar_value * Math.Abs(c[query] - a[index]);
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
                    for (int left = 0; left < first->count; left++)
                        for (int right = 0; right < first->count; right++)
                            sum += Math.Abs(a[left] - a[right]);
                    output->scalar_value = sum /
                        (2.0 * first->count * mathblocks_compensated_sum(a, first->count));
                    break;
                }
                case 7:
                    mathblocks_sequence_set_vector_shape(output, first->count + 1);
                    if (first->count <= 0)
                    {
                        output->valid = 0;
                        break;
                    }
                    result[0] = 0.0;
                    for (int index = 0; index < first->count; index++)
                    {
                        double value = a[index];
                        int position = index;
                        while (position > 0 && result[position] > value)
                        {
                            result[position + 1] = result[position];
                            position--;
                        }
                        result[position + 1] = value;
                    }
                {
                    double total = mathblocks_compensated_sum(result + 1, first->count);
                    for (int index = 0; index < first->count; index++)
                        result[index + 1] = result[index] + result[index + 1] / total;
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
                    for (int row = 0; row < first->rows; row++)
                        for (int column = 0; column < first->columns; column++)
                        {
                            double forward = b[row] * a[row * first->columns + column];
                            double reverse = b[column] * a[column * first->columns + row];
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
                    if (!mathblocks_advanced_transition(a, first->rows, first->columns) || scratch == null)
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
                    for (int index = 0; index < first->rows; index++)
                        result[index] = 1.0 / first->rows;
                    for (int iteration = 0; iteration < iterations; iteration++)
                    {
                        for (int index = 0; index < first->rows; index++)
                            scratch[index] = 0.0;
                        for (int row = 0; row < first->rows; row++)
                            for (int column = 0; column < first->columns; column++)
                                scratch[column] += result[row] * a[row * first->columns + column];
                        for (int index = 0; index < first->rows; index++)
                            result[index] = scratch[index];
                    }
                    break;
                }
                case 10:
                    mathblocks_sequence_set_vector_shape(output, first->count);
                    if (first->count <= 0 || scratch == null)
                    {
                        output->valid = 0;
                        break;
                    }
                {
                    double* means = scratch;
                    int* weights = (int*)(means + first->count);
                    int* starts = weights + first->count;
                    int block_count = 0;
                    for (int index = 0; index < first->count; index++)
                    {
                        means[block_count] = a[index];
                        weights[block_count] = 1;
                        starts[block_count] = index;
                        block_count++;
                        while (block_count >= 2 && means[block_count - 2] > means[block_count - 1])
                        {
                            int combined_weight = weights[block_count - 2] + weights[block_count - 1];
                            means[block_count - 2] =
                                (means[block_count - 2] * weights[block_count - 2] +
                                 means[block_count - 1] * weights[block_count - 1]) /
                                combined_weight;
                            weights[block_count - 2] = combined_weight;
                            block_count--;
                        }
                    }
                    for (int block = 0; block < block_count; block++)
                    {
                        int end = block + 1 < block_count ? starts[block + 1] : first->count;
                        for (int index = starts[block]; index < end; index++)
                            result[index] = means[block];
                    }
                    break;
                }
                case 11:
                    if (first->count != second->count || scratch == null)
                    {
                        output->valid = 0;
                        break;
                    }
                {
                    double* left_sorted = scratch;
                    double* right_sorted = scratch + first->count;
                    mathblocks_advanced_sort_descending(a, first->count, left_sorted);
                    mathblocks_advanced_sort_descending(b, first->count, right_sorted);
                    double left_sum = 0.0;
                    double right_sum = 0.0;
                    bool majorizes = true;
                    for (int index = 0; index < first->count; index++)
                    {
                        left_sum += left_sorted[index];
                        right_sum += right_sorted[index];
                        if (index < first->count - 1 && left_sum < right_sum)
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
                    for (int index = 0; index < first->count; index++)
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
                    if (first->count <= 0 || scratch == null)
                    {
                        output->valid = 0;
                        break;
                    }
                {
                    int* hull = (int*)scratch;
                    int hull_count = 0;
                    bool concave = opcode == 16;
                    for (int index = 0; index < first->count; index++)
                    {
                        hull[hull_count++] = index;
                        while (hull_count >= 3)
                        {
                            int one = hull[hull_count - 3];
                            int middle = hull[hull_count - 2];
                            int last = hull[hull_count - 1];
                            double first_slope = (a[middle] - a[one]) / (middle - one);
                            double second_slope = (a[last] - a[middle]) / (last - middle);
                            if (concave ? first_slope >= second_slope : first_slope <= second_slope)
                                break;
                            hull[hull_count - 2] = hull[hull_count - 1];
                            hull_count--;
                        }
                    }
                    for (int segment = 1; segment < hull_count; segment++)
                    {
                        int start = hull[segment - 1];
                        int end = hull[segment];
                        for (int index = start; index <= end; index++)
                        {
                            double weight = (double)(index - start) / (end - start);
                            result[index] = a[start] * (1.0 - weight) + a[end] * weight;
                        }
                    }
                    break;
                }
                case 14:
                    if (scratch == null)
                    {
                        output->valid = 0;
                        break;
                    }
                    for (int index = 0; index < first->count; index++)
                        scratch[index] = a[index];
                    output->boolean_value = 1;
                    for (int order = 0, length = first->count;
                         order < first->count && output->boolean_value;
                         order++, length--)
                    {
                        double sign = (order & 1) == 0 ? 1.0 : -1.0;
                        for (int index = 0; index < length; index++)
                            if (sign * scratch[index] < 0.0)
                            {
                                output->boolean_value = 0;
                                break;
                            }
                        for (int index = 1; output->boolean_value && index < length; index++)
                            scratch[index - 1] = scratch[index] - scratch[index - 1];
                    }
                    break;
                case 15:
                    output->boolean_value = 1;
                    for (int index = 0; index < first->count; index++)
                        if (a[index] < 0.0) output->boolean_value = 0;
                    for (int index = 1; output->boolean_value && index < first->count - 1; index++)
                        if (a[index] * a[index] < a[index - 1] * a[index + 1])
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
                    for (int index = 0; index < first->count; index++)
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
                    for (int index = 0; index < first->count; index++)
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
                !double.IsFinite(output->scalar_value))
            {
                output->valid = 0;
            }
            if (output->valid &&
                (opcode == 2 || opcode == 3 || opcode == 4 || opcode == 5 || opcode == 7 ||
                 opcode == 9 || opcode == 10 || opcode == 13 || opcode == 16 || opcode == 17 ||
                 opcode == 18))
            {
                for (int index = 0; index < output->count; index++)
                    if (!double.IsFinite(result[index])) output->valid = 0;
            }
        }
    }
}
