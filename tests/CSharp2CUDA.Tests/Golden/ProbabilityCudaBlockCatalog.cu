__device__ bool mathblocks_probability_integer(double value, int* result)
{
    if (value < -2147483648.0 || value > 2147483647.0 || value != trunc(value))
        return false;
    *result = (int)value;
    return true;
}

__device__ double mathblocks_probability_binomial(int n, int k)
{
    if (k < 0 || k > n)
        return 0.0;
    if (k > n - k)
        k = n - k;
    double result = 1.0;
    for (int index = 1; index <= k; index++)
        result = result * (n - k + index) / index;
    return result;
}

__device__ bool mathblocks_probability_distribution(const double* values, int count)
{
    if (count <= 0)
        return false;
    for (int index = 0; index < count; index++)
        if (values[index] < 0.0) return false;
    return fabs(mathblocks_compensated_sum(values, count) - 1.0) <= 1e-10;
}

__device__ double mathblocks_probability_entropy(const double* values, int count)
{
    double entropy = 0.0;
    for (int index = 0; index < count; index++)
        if (values[index] > 0.0)
            entropy -= values[index] * mathblocks_natural_logarithm(values[index]);
    return entropy;
}

__device__ double mathblocks_probability_kl(
    const double* probabilities,
    const double* reference,
    int count)
{
    double result = 0.0;
    for (int index = 0; index < count; index++)
        if (probabilities[index] > 0.0)
            result += probabilities[index] *
                mathblocks_natural_logarithm(probabilities[index] / reference[index]);
    return result;
}

__device__ double mathblocks_probability_log_gamma_core(double value)
{
    const double coefficients[8] =
    {
        676.5203681218851,
        -1259.1392167224028,
        771.32342877765313,
        -176.61502916214059,
        12.507343278686905,
        -0.13857109526572012,
        9.9843695780195716e-6,
        1.5056327351493116e-7
    };
    value -= 1.0;
    double sum = 0.99999999999980993;
    for (int index = 0; index < 8; index++)
        sum += coefficients[index] / (value + index + 1.0);
    double t = value + 7.5;
    return 0.5 * mathblocks_natural_logarithm(
               2.0 * 3.141592653589793238462643383279502884) +
           (value + 0.5) * mathblocks_natural_logarithm(t) - t +
           mathblocks_natural_logarithm(sum);
}

__device__ double mathblocks_probability_log_gamma(double value)
{
    if (value < 0.5)
    {
        return mathblocks_natural_logarithm(3.141592653589793238462643383279502884) -
            mathblocks_natural_logarithm(
                mathblocks_sine(3.141592653589793238462643383279502884 * value)) -
            mathblocks_probability_log_gamma_core(1.0 - value);
    }
    return mathblocks_probability_log_gamma_core(value);
}

__device__ double mathblocks_probability_beta_fraction(double x, double left, double right)
{
    const int maximum_iterations = 256;
    const double tolerance = 3e-14;
    const double minimum = 1e-300;
    double qab = left + right;
    double qap = left + 1.0;
    double qam = left - 1.0;
    double c = 1.0;
    double d = 1.0 - qab * x / qap;
    if (fabs(d) < minimum) d = minimum;
    d = 1.0 / d;
    double result = d;
    for (int iteration = 1; iteration <= maximum_iterations; iteration++)
    {
        double doubled = 2.0 * iteration;
        double coefficient = iteration * (right - iteration) * x /
            ((qam + doubled) * (left + doubled));
        d = 1.0 + coefficient * d;
        if (fabs(d) < minimum) d = minimum;
        c = 1.0 + coefficient / c;
        if (fabs(c) < minimum) c = minimum;
        d = 1.0 / d;
        result *= d * c;
        coefficient = -(left + iteration) * (qab + iteration) * x /
            ((left + doubled) * (qap + doubled));
        d = 1.0 + coefficient * d;
        if (fabs(d) < minimum) d = minimum;
        c = 1.0 + coefficient / c;
        if (fabs(c) < minimum) c = minimum;
        d = 1.0 / d;
        double delta = d * c;
        result *= delta;
        if (fabs(delta - 1.0) <= tolerance)
            break;
    }
    return result;
}

__device__ double mathblocks_probability_incomplete_beta(
    double x,
    double left,
    double right)
{
    if (x == 0.0) return 0.0;
    if (x == 1.0) return 1.0;
    double front = mathblocks_exponential(
        mathblocks_probability_log_gamma(left + right) -
        mathblocks_probability_log_gamma(left) -
        mathblocks_probability_log_gamma(right) +
        left * mathblocks_natural_logarithm(x) +
        right * mathblocks_log_one_plus(-x));
    return x < (left + 1.0) / (left + right + 2.0)
        ? front * mathblocks_probability_beta_fraction(x, left, right) / left
        : 1.0 - front * mathblocks_probability_beta_fraction(1.0 - x, right, left) / right;
}

__device__ MathBlockComplexValue mathblocks_probability_complex_cube_root(
    MathBlockComplexValue value)
{
    if (value.real == 0.0 && value.imaginary == 0.0)
        return mathblocks_complex_make(0.0, 0.0);
    return mathblocks_complex_from_polar(
        mathblocks_cube_root(mathblocks_complex_magnitude(value)),
        mathblocks_complex_phase(value) / 3.0);
}

extern "C" __global__ void mathblocks_probability(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    int thread = (int)threadIdx.x;
    const MathBlockSlot* first = input_count > 0 ? inputs[0] : nullptr;
    const MathBlockSlot* second = input_count > 1 ? inputs[1] : nullptr;
    const MathBlockSlot* third = input_count > 2 ? inputs[2] : nullptr;
    const MathBlockSlot* fourth = input_count > 3 ? inputs[3] : nullptr;
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

    if (opcode == 3)
    {
        if (thread == 0)
        {
            if (first->count > 20)
            {
                output->valid = 0;
                return;
            }
            mathblocks_set_vector_shape(output, (1 << first->count) - 1);
        }
        __syncthreads();
        for (int mask = thread + 1; output->valid && mask <= output->count; mask += blockDim.x)
        {
            double sum = 0.0;
            for (int index = 0; index < first->count; index++)
                if ((mask & (1 << index)) != 0) sum += a[index];
            result[mask - 1] = sum;
        }
        return;
    }

    if (opcode == 19)
    {
        int count = first->count <= 1 ? 1 : first->count - 1;
        if (thread == 0) mathblocks_set_vector_shape(output, count);
        __syncthreads();
        if (first->count <= 1)
        {
            if (thread == 0) result[0] = 0.0;
        }
        else
        {
            for (int index = thread + 1; index < first->count; index += blockDim.x)
                result[index - 1] = index * a[index];
        }
        return;
    }

    if (opcode == 24 || opcode == 27)
    {
        if (thread == 0)
        {
            if (first->count <= 0)
            {
                output->valid = 0;
                return;
            }
            for (int index = 0; index < first->count; index++)
                if (a[index] < 0.0) output->valid = 0;
            mathblocks_set_vector_shape(output, first->count);
            if (opcode == 24)
            {
                double total = mathblocks_compensated_sum(a, first->count);
                for (int index = 0; output->valid && index < first->count; index++)
                    result[index] = a[index] * (1.0 / total);
            }
            else
            {
                double maximum = a[0];
                for (int index = 1; index < first->count; index++)
                    maximum = mathblocks_maximum(maximum, a[index]);
                for (int index = 0; index < first->count; index++)
                    result[index] = mathblocks_exponential(a[index] - maximum);
                double total = mathblocks_compensated_sum(result, first->count);
                double scale = 1.0 / total;
                for (int index = 0; index < first->count; index++)
                    result[index] *= scale;
            }
            for (int index = 0; index < first->count; index++)
                if (!isfinite(result[index])) output->valid = 0;
        }
        return;
    }

    if (thread != 0)
        return;

    if (opcode == 0 || opcode == 1 || opcode == 2)
    {
        int first_integer = 0;
        int second_integer = 0;
        if (!mathblocks_probability_integer(first->scalar_value, &first_integer) || first_integer < 0 ||
            (opcode != 2 && !mathblocks_probability_integer(second->scalar_value, &second_integer)) ||
            (opcode == 2 && first_integer > 170) ||
            (opcode == 0 && (first_integer <= second_integer || second_integer < 0)))
        {
            output->valid = 0;
            return;
        }
        if (opcode == 0)
        {
            output->scalar_value = (double)(first_integer - second_integer) /
                (first_integer + second_integer) *
                mathblocks_probability_binomial(first_integer + second_integer, second_integer);
        }
        else if (opcode == 1)
        {
            output->scalar_value = mathblocks_probability_binomial(first_integer, second_integer);
        }
        else
        {
            double factorial = 1.0;
            for (int index = 2; index <= first_integer; index++) factorial *= index;
            output->scalar_value = factorial;
        }
        if (!isfinite(output->scalar_value)) output->valid = 0;
        return;
    }

    if (opcode >= 4 && opcode <= 16)
    {
        bool first_distribution = opcode == 12
            ? mathblocks_probability_distribution(a, first->count)
            : mathblocks_probability_distribution(a, first->count);
        bool pair = opcode == 4 || opcode == 7 || opcode == 9 || opcode == 10 ||
            opcode == 11 || opcode == 15;
        if (!first_distribution || (pair &&
            (first->count != second->count || !mathblocks_probability_distribution(b, second->count))))
        {
            output->valid = 0;
            return;
        }
        double value = 0.0;
        if (opcode == 4)
        {
            for (int index = 0; index < first->count; index++)
                value += mathblocks_square_root(a[index] * b[index]);
        }
        else if (opcode == 5)
        {
            value = mathblocks_probability_entropy(a, first->count) /
                mathblocks_natural_logarithm(2.0);
        }
        else if (opcode == 6)
        {
            int first_count = 0;
            int second_count = 0;
            int condition_count = 0;
            if (!mathblocks_probability_integer(second->scalar_value, &first_count) || first_count <= 0 ||
                !mathblocks_probability_integer(third->scalar_value, &second_count) || second_count <= 0 ||
                !mathblocks_probability_integer(fourth->scalar_value, &condition_count) || condition_count <= 0 ||
                (long long)first_count * second_count * condition_count != first->count || scratch == nullptr)
            {
                output->valid = 0;
                return;
            }
            int first_condition_count = first_count * condition_count;
            int second_condition_count = second_count * condition_count;
            double* first_condition = scratch;
            double* second_condition = first_condition + first_condition_count;
            double* condition = second_condition + second_condition_count;
            for (int index = 0; index < first_condition_count + second_condition_count + condition_count; index++)
                scratch[index] = 0.0;
            for (int first_index = 0; first_index < first_count; first_index++)
                for (int second_index = 0; second_index < second_count; second_index++)
                    for (int state = 0; state < condition_count; state++)
                    {
                        double probability = a[(first_index * second_count + second_index) *
                            condition_count + state];
                        first_condition[first_index * condition_count + state] += probability;
                        second_condition[second_index * condition_count + state] += probability;
                        condition[state] += probability;
                    }
            for (int first_index = 0; first_index < first_count; first_index++)
                for (int second_index = 0; second_index < second_count; second_index++)
                    for (int state = 0; state < condition_count; state++)
                    {
                        double probability = a[(first_index * second_count + second_index) *
                            condition_count + state];
                        if (probability == 0.0) continue;
                        value += probability * mathblocks_natural_logarithm(
                            probability * condition[state] /
                            (first_condition[first_index * condition_count + state] *
                             second_condition[second_index * condition_count + state]));
                    }
        }
        else if (opcode == 7)
        {
            for (int index = 0; index < first->count; index++)
            {
                if (a[index] > 0.0 && b[index] == 0.0)
                {
                    output->valid = 0;
                    return;
                }
                if (a[index] > 0.0)
                    value -= a[index] * mathblocks_natural_logarithm(b[index]);
            }
        }
        else if (opcode == 8)
        {
            value = 1.0 - mathblocks_compensated_product_sum(a, a, first->count);
        }
        else if (opcode == 9)
        {
            for (int index = 0; index < first->count; index++)
            {
                double difference = mathblocks_square_root(a[index]) - mathblocks_square_root(b[index]);
                value += difference * difference;
            }
            value = mathblocks_square_root(value / 2.0);
        }
        else if (opcode == 10)
        {
            if (scratch == nullptr)
            {
                output->valid = 0;
                return;
            }
            for (int index = 0; index < first->count; index++)
                scratch[index] = (a[index] + b[index]) / 2.0;
            value = 0.5 * (mathblocks_probability_kl(a, scratch, first->count) +
                           mathblocks_probability_kl(b, scratch, first->count));
        }
        else if (opcode == 11)
        {
            for (int index = 0; index < first->count; index++)
                if (a[index] > 0.0 && b[index] == 0.0)
                {
                    output->valid = 0;
                    return;
                }
            value = mathblocks_probability_kl(a, b, first->count);
        }
        else if (opcode == 12)
        {
            int rows = first->rows;
            int columns = first->columns;
            if (scratch == nullptr)
            {
                output->valid = 0;
                return;
            }
            double* row_totals = scratch;
            double* column_totals = scratch + rows;
            for (int index = 0; index < rows + columns; index++) scratch[index] = 0.0;
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    double probability = a[row * columns + column];
                    row_totals[row] += probability;
                    column_totals[column] += probability;
                }
            for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    double probability = a[row * columns + column];
                    if (probability > 0.0)
                        value += probability * mathblocks_natural_logarithm(
                            probability / (row_totals[row] * column_totals[column]));
                }
        }
        else if (opcode == 13 || opcode == 16)
        {
            double order = second->scalar_value;
            if (order <= 0.0)
            {
                output->valid = 0;
                return;
            }
            if (order == 1.0)
            {
                value = mathblocks_probability_entropy(a, first->count);
            }
            else
            {
                double sum = 0.0;
                for (int index = 0; index < first->count; index++)
                    sum += mathblocks_power(a[index], order);
                value = opcode == 13
                    ? mathblocks_natural_logarithm(sum) / (1.0 - order)
                    : (1.0 - sum) / (order - 1.0);
            }
        }
        else if (opcode == 14)
        {
            value = mathblocks_probability_entropy(a, first->count);
        }
        else if (opcode == 15)
        {
            for (int index = 0; index < first->count; index++)
                value += fabs(a[index] - b[index]);
            value /= 2.0;
        }
        output->scalar_value = value;
        if (!isfinite(value)) output->valid = 0;
        return;
    }

    if (opcode == 17)
    {
        double parameter = second->scalar_value;
        if (first->count <= 0 || parameter < 0.0 || parameter > 1.0)
        {
            output->valid = 0;
            return;
        }
        int degree = first->count - 1;
        double value = 0.0;
        for (int index = 0; index <= degree; index++)
            value += a[index] * mathblocks_probability_binomial(degree, index) *
                mathblocks_power(parameter, (double)index) *
                mathblocks_power(1.0 - parameter, (double)(degree - index));
        output->scalar_value = value;
        if (!isfinite(value)) output->valid = 0;
        return;
    }

    if (opcode == 18)
    {
        if (first->count != 4)
        {
            output->valid = 0;
            return;
        }
        MathBlockComplexValue* roots = (MathBlockComplexValue*)output->data_pointer;
        mathblocks_complex_shape(output, 3);
        double constant = a[0];
        double linear = a[1];
        double quadratic = a[2];
        double leading = a[3];
        if (leading == 0.0)
        {
            output->valid = 0;
            return;
        }
        double normalized_a = quadratic / leading;
        double normalized_b = linear / leading;
        double normalized_c = constant / leading;
        double p = normalized_b - normalized_a * normalized_a / 3.0;
        double q = 2.0 * normalized_a * normalized_a * normalized_a / 27.0 -
            normalized_a * normalized_b / 3.0 + normalized_c;
        MathBlockComplexValue square_root = mathblocks_complex_square_root(
            mathblocks_complex_make(q * q / 4.0 + p * p * p / 27.0, 0.0));
        MathBlockComplexValue u = mathblocks_probability_complex_cube_root(
            mathblocks_complex_add(mathblocks_complex_make(-q / 2.0, 0.0), square_root));
        MathBlockComplexValue v = u.real == 0.0 && u.imaginary == 0.0
            ? mathblocks_probability_complex_cube_root(
                mathblocks_complex_subtract(mathblocks_complex_make(-q / 2.0, 0.0), square_root))
            : mathblocks_complex_divide(
                mathblocks_complex_make(-p, 0.0),
                mathblocks_complex_multiply(mathblocks_complex_make(3.0, 0.0), u));
        MathBlockComplexValue omega = mathblocks_complex_make(
            -0.5,
            mathblocks_square_root(3.0) / 2.0);
        roots[0] = mathblocks_complex_subtract(
            mathblocks_complex_add(u, v),
            mathblocks_complex_make(normalized_a / 3.0, 0.0));
        roots[1] = mathblocks_complex_subtract(
            mathblocks_complex_add(
                mathblocks_complex_multiply(omega, u),
                mathblocks_complex_multiply(mathblocks_complex_conjugate(omega), v)),
            mathblocks_complex_make(normalized_a / 3.0, 0.0));
        roots[2] = mathblocks_complex_subtract(
            mathblocks_complex_add(
                mathblocks_complex_multiply(mathblocks_complex_conjugate(omega), u),
                mathblocks_complex_multiply(omega, v)),
            mathblocks_complex_make(normalized_a / 3.0, 0.0));
        for (int index = 0; index < 3; index++)
            if (!mathblocks_complex_finite(roots[index])) output->valid = 0;
        return;
    }

    if (opcode == 20)
    {
        int order = 0;
        if (!mathblocks_probability_integer(second->scalar_value, &order) ||
            order < 0 || order > first->count || scratch == nullptr)
        {
            output->valid = 0;
            return;
        }
        for (int index = 0; index <= order; index++) scratch[index] = 0.0;
        scratch[0] = 1.0;
        for (int value_index = 0; value_index < first->count; value_index++)
        {
            int maximum = order < value_index + 1 ? order : value_index + 1;
            for (int degree = maximum; degree >= 1; degree--)
                scratch[degree] += a[value_index] * scratch[degree - 1];
        }
        output->scalar_value = scratch[order];
        return;
    }

    if (opcode == 21)
    {
        double value = 0.0;
        for (int index = first->count - 1; index >= 0; index--)
            value = value * second->scalar_value + a[index];
        output->scalar_value = value;
        if (!isfinite(value)) output->valid = 0;
        return;
    }

    if (opcode == 22)
    {
        if (first->count <= 0)
        {
            output->valid = 0;
            return;
        }
        double maximum = a[0];
        for (int index = 1; index < first->count; index++)
            maximum = mathblocks_maximum(maximum, a[index]);
        double sum = 0.0;
        for (int index = 0; index < first->count; index++)
            sum += mathblocks_exponential(a[index] - maximum);
        output->scalar_value = maximum + mathblocks_natural_logarithm(sum);
        if (!isfinite(output->scalar_value)) output->valid = 0;
        return;
    }

    if (opcode == 23)
    {
        output->scalar_value = 0.5 * (1.0 + mathblocks_error_function(
            first->scalar_value / mathblocks_square_root(2.0)));
        if (!isfinite(output->scalar_value)) output->valid = 0;
        return;
    }

    if (opcode == 25 || opcode == 26)
    {
        int count = 0;
        double rate = first->scalar_value;
        if (!mathblocks_probability_integer(second->scalar_value, &count) || count < 0 || rate < 0.0)
        {
            output->valid = 0;
            return;
        }
        double value = 0.0;
        int start = opcode == 25 ? 0 : count;
        int end = count;
        for (int index = start; index <= end; index++)
        {
            double probability = rate == 0.0
                ? (index == 0 ? 1.0 : 0.0)
                : mathblocks_exponential(
                    -rate + index * mathblocks_natural_logarithm(rate) -
                    mathblocks_probability_log_gamma(index + 1.0));
            value += probability;
        }
        output->scalar_value = value;
        if (!isfinite(value)) output->valid = 0;
        return;
    }

    if (opcode == 28)
    {
        output->scalar_value = mathblocks_exponential(
            mathblocks_probability_log_gamma(first->scalar_value) +
            mathblocks_probability_log_gamma(second->scalar_value) -
            mathblocks_probability_log_gamma(first->scalar_value + second->scalar_value));
    }
    else if (opcode == 29)
    {
        output->scalar_value = mathblocks_probability_log_gamma(first->scalar_value);
    }
    else if (opcode == 30)
    {
        double x = first->scalar_value;
        double left = second->scalar_value;
        double right = third->scalar_value;
        if (x < 0.0 || x > 1.0 || left <= 0.0 || right <= 0.0)
        {
            output->valid = 0;
            return;
        }
        output->scalar_value = mathblocks_probability_incomplete_beta(x, left, right);
    }
    if (!isfinite(output->scalar_value)) output->valid = 0;
}