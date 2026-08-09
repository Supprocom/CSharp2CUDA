struct MathBlockSlot
{
    double scalar_value;
    unsigned long long data_pointer;
    unsigned long long scratch_pointer;
    int boolean_value;
    int valid;
    int rows;
    int columns;
    int count;
    int capacity;
};

__device__ double mathblocks_positive_infinity()
{
    return __longlong_as_double((long long)0x7ff0000000000000ull);
}

__device__ double mathblocks_quiet_nan()
{
    return __longlong_as_double((long long)0x7ff8000000000000ull);
}

__device__ double mathblocks_square_root(double value)
{
    if (value == 0.0 || (isinf(value) && value > 0.0))
        return value;
    if (value < 0.0 || isnan(value))
        return mathblocks_quiet_nan();

    double scaled = value;
    double correction = 1.0;
    unsigned long long bits = (unsigned long long)__double_as_longlong(scaled);
    if ((bits & 0x7ff0000000000000ull) == 0ull)
    {
        scaled *= __longlong_as_double((long long)0x4350000000000000ull);
        correction = __longlong_as_double((long long)0x3e40000000000000ull);
        bits = (unsigned long long)__double_as_longlong(scaled);
    }

    double estimate = __longlong_as_double((long long)((bits >> 1) + 0x1ff8000000000000ull));
    for (int iteration = 0; iteration < 7; iteration++)
        estimate = 0.5 * (estimate + scaled / estimate);
    return estimate * correction;
}

__device__ double mathblocks_exponential(double value)
{
    if (value > 709.782712893383973096)
        return mathblocks_positive_infinity();
    if (value < -745.13321910194110842)
        return 0.0;

    double exponent_value = floor(1.44269504088896340736 * value + 0.5);
    int exponent = (int)exponent_value;
    double reduced = value - exponent_value * 0.693359375;
    reduced -= exponent_value * -0.000212194440054690582768;
    double square = reduced * reduced;
    double numerator = reduced * ((
        0.000126177193074810590878 * square + 0.03029944077074419613) * square + 1.0);
    double denominator = (((
        0.00000300198505138664455042 * square + 0.00252448340349684104192) * square +
        0.227265548208155028766) * square + 2.0);
    double result = 1.0 + 2.0 * numerator / (denominator - numerator);
    return ldexp(result, exponent);
}

__device__ double mathblocks_natural_logarithm(double value)
{
    if (value == 0.0)
        return -mathblocks_positive_infinity();
    if (value < 0.0 || isnan(value))
        return mathblocks_quiet_nan();
    if (isinf(value))
        return mathblocks_positive_infinity();

    int exponent = ilogb(value) + 1;
    double reduced = ldexp(value, -exponent);
    if (reduced < 0.70710678118654752440)
    {
        exponent--;
        reduced = 2.0 * reduced - 1.0;
    }
    else
    {
        reduced -= 1.0;
    }

    double square = reduced * reduced;
    double numerator = (((((
        0.000101875663804580931796 * reduced + 0.497494994976747001425) * reduced +
        4.70579119878881725854) * reduced + 14.4989225341610930846) * reduced +
        17.9368678507819816313) * reduced + 7.70838733755885391666);
    double denominator = (((((
        reduced + 11.2873587189167450590) * reduced + 45.2279145837532221105) * reduced +
        82.9875266912776603211) * reduced + 71.1544750618563894466) * reduced +
        23.1251620126765340583);
    double correction = reduced * (square * numerator / denominator);
    correction -= exponent * 0.000212194440054690582768;
    correction -= 0.5 * square;
    return reduced + correction + exponent * 0.693359375;
}

__device__ double mathblocks_log_one_plus(double value)
{
    double sum = 1.0 + value;
    return sum == 1.0
        ? value
        : mathblocks_natural_logarithm(sum) - ((sum - 1.0) - value) / sum;
}

__device__ double mathblocks_binary_logarithm(double value)
{
    unsigned long long bits = __double_as_longlong(value);
    int exponent = (int)((bits >> 52) & 0x7ffull);
    unsigned long long fraction = bits & 0x000fffffffffffffull;
    if (exponent > 0 && exponent < 0x7ff && fraction == 0ull)
        return (double)(exponent - 1023);
    return mathblocks_natural_logarithm(value) / 0.69314718055994530942;
}

__device__ double mathblocks_integer_power(double value, long long exponent)
{
    if (exponent == 0)
        return 1.0;
    bool negative = exponent < 0;
    unsigned long long remaining = negative
        ? (unsigned long long)(-(exponent + 1)) + 1ull
        : (unsigned long long)exponent;
    double power_base = value;
    double result = 1.0;
    while (remaining != 0ull)
    {
        if ((remaining & 1ull) != 0ull)
            result *= power_base;
        remaining >>= 1;
        if (remaining != 0ull)
            power_base *= power_base;
    }
    return negative ? 1.0 / result : result;
}

__device__ double mathblocks_power(double value, double exponent)
{
    if (exponent == trunc(exponent) && fabs(exponent) <= 9223372036854775807.0)
        return mathblocks_integer_power(value, (long long)exponent);
    if (value < 0.0)
        return mathblocks_quiet_nan();
    if (value == 0.0)
        return exponent > 0.0 ? 0.0 : mathblocks_positive_infinity();
    return mathblocks_exponential(exponent * mathblocks_natural_logarithm(value));
}

__device__ double mathblocks_cube_root(double value)
{
    if (value == 0.0)
        return value;
    double magnitude = fabs(value);
    double estimate = mathblocks_exponential(mathblocks_natural_logarithm(magnitude) / 3.0);
    for (int iteration = 0; iteration < 3; iteration++)
        estimate = (2.0 * estimate + magnitude / (estimate * estimate)) / 3.0;
    return copysign(estimate, value);
}

__device__ double mathblocks_sine(double value)
{
    double sign = 1.0;
    double x = value;
    if (x < 0.0)
    {
        sign = -1.0;
        x = -x;
    }
    double octant_value = floor(x / 0.78539816339744830962);
    int octant = (int)(octant_value - floor(octant_value * 0.125) * 8.0);
    if ((octant & 1) != 0)
    {
        octant++;
        octant_value++;
    }
    octant &= 7;
    if (octant > 3)
    {
        sign = -sign;
        octant -= 4;
    }
    double reduced = ((x - octant_value * 0.785398125648498535156) -
                      octant_value * 0.0000000377489470793079817668) -
                     octant_value * 0.00000000000000269515142907905952645;
    double square = reduced * reduced;
    if (octant == 1 || octant == 2)
    {
        double polynomial = (((((
            -0.0000000000113585365213876817300 * square +
            0.00000000208757008419747316778) * square -
            0.000000275573141792967388112) * square +
            0.0000248015872888517045348) * square -
            0.00138888888888730564116) * square +
            0.0416666666666665929218);
        return sign * (1.0 - 0.5 * square + square * square * polynomial);
    }
    double sine_polynomial = (((((
        0.000000000158962301576546568060 * square -
        0.0000000250507477628578072866) * square +
        0.00000275573136213857245213) * square -
        0.000198412698295895385996) * square +
        0.00833333333332211858878) * square -
        0.166666666666666307295);
    return sign * (reduced + reduced * square * sine_polynomial);
}

__device__ double mathblocks_cosine(double value)
{
    double x = fabs(value);
    double sign = 1.0;
    double octant_value = floor(x / 0.78539816339744830962);
    int octant = (int)(octant_value - floor(octant_value * 0.125) * 8.0);
    if ((octant & 1) != 0)
    {
        octant++;
        octant_value++;
    }
    octant &= 7;
    if (octant > 3)
    {
        octant -= 4;
        sign = -sign;
    }
    if (octant > 1)
        sign = -sign;
    double reduced = ((x - octant_value * 0.785398125648498535156) -
                      octant_value * 0.0000000377489470793079817668) -
                     octant_value * 0.00000000000000269515142907905952645;
    double square = reduced * reduced;
    if (octant == 1 || octant == 2)
    {
        double sine_polynomial = (((((
            0.000000000158962301576546568060 * square -
            0.0000000250507477628578072866) * square +
            0.00000275573136213857245213) * square -
            0.000198412698295895385996) * square +
            0.00833333333332211858878) * square -
            0.166666666666666307295);
        return sign * (reduced + reduced * square * sine_polynomial);
    }
    double polynomial = (((((
        -0.0000000000113585365213876817300 * square +
        0.00000000208757008419747316778) * square -
        0.000000275573141792967388112) * square +
        0.0000248015872888517045348) * square -
        0.00138888888888730564116) * square +
        0.0416666666666665929218);
    return sign * (1.0 - 0.5 * square + square * square * polynomial);
}

__device__ double mathblocks_arc_tangent(double value)
{
    double sign = value < 0.0 ? -1.0 : 1.0;
    double x = fabs(value);
    double offset = 0.0;
    if (x > 2.4142135623730950488)
    {
        offset = 3.14159265358979323846 / 2.0;
        x = -1.0 / x;
    }
    else if (x > 0.4142135623730950488)
    {
        offset = 3.14159265358979323846 / 4.0;
        x = (x - 1.0) / (x + 1.0);
    }

    double z = x * x;
    double numerator = ((((
        -0.8750608600031904122785 * z - 16.15753718733365076637) * z -
        75.00855792314704667340) * z - 122.8866684490136173410) * z -
        64.85021904942025371773);
    double denominator = (((((
        z + 24.85846490142306297962) * z + 165.0270098316988542046) * z +
        432.8810604912902668951) * z + 485.3903996359136964868) * z +
        194.5506571482613964425);
    return sign * (offset + x + x * z * numerator / denominator);
}

__device__ double mathblocks_arc_tangent_2(double y, double x)
{
    const double pi = 3.14159265358979323846;
    if (x > 0.0)
        return mathblocks_arc_tangent(y / x);
    if (x < 0.0)
        return y >= 0.0
            ? mathblocks_arc_tangent(y / x) + pi
            : mathblocks_arc_tangent(y / x) - pi;
    if (y > 0.0)
        return pi / 2.0;
    if (y < 0.0)
        return -pi / 2.0;
    return 0.0;
}

__device__ double mathblocks_arc_cosine(double value)
{
    if (value < -1.0 || value > 1.0)
        return mathblocks_quiet_nan();
    return mathblocks_arc_tangent_2(
        mathblocks_square_root((1.0 - value) * (1.0 + value)),
        value);
}

__device__ double mathblocks_inverse_hyperbolic_sine(double value)
{
    if (value == 0.0)
        return value;
    double magnitude = fabs(value);
    return copysign(
        mathblocks_natural_logarithm(magnitude + mathblocks_square_root(magnitude * magnitude + 1.0)),
        value);
}

__device__ double mathblocks_error_function(double value)
{
    if (value == 0.0)
        return 0.0;
    double magnitude = fabs(value);
    double t = 1.0 / (1.0 + 0.5 * magnitude);
    double tau = t * mathblocks_exponential(
        -magnitude * magnitude - 1.26551223 +
        t * (1.00002368 +
        t * (0.37409196 +
        t * (0.09678418 +
        t * (-0.18628806 +
        t * (0.27886807 +
        t * (-1.13520398 +
        t * (1.48851587 +
        t * (-0.82215223 + t * 0.17087277)))))))));
    return copysign(1.0 - tau, value);
}

extern "C" __global__ void mathblocks_scalar(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    if (blockIdx.x != 0 || threadIdx.x != 0)
        return;

    const MathBlockSlot* first = input_count > 0 ? inputs[0] : nullptr;
    const MathBlockSlot* second = input_count > 1 ? inputs[1] : nullptr;
    const MathBlockSlot* third = input_count > 2 ? inputs[2] : nullptr;

    output->scalar_value = 0.0;
    output->boolean_value = 0;
    output->valid = first == nullptr || first->valid;
    if (second != nullptr)
        output->valid = output->valid && second->valid;
    if (third != nullptr)
        output->valid = output->valid && third->valid;
    if (!output->valid)
        return;

    double a = first == nullptr ? 0.0 : first->scalar_value;
    double b = second == nullptr ? 0.0 : second->scalar_value;
    double c = third == nullptr ? 0.0 : third->scalar_value;
    bool scalar_output = true;

    switch (opcode)
    {
        case 0: output->scalar_value = a + b; break;
        case 1: output->scalar_value = a - b; break;
        case 2: output->scalar_value = a * b; break;
        case 3: output->scalar_value = a / b; break;
        case 4: output->scalar_value = -a; break;
        case 5: output->scalar_value = fabs(a); break;
        case 6: output->scalar_value = (a > 0.0) - (a < 0.0); break;
        case 7: output->scalar_value = a > 0.0 ? a : 0.0; break;
        case 8: output->scalar_value = fmin(a, b); break;
        case 9: output->scalar_value = fmax(a, b); break;
        case 10: output->scalar_value = fmin(fmax(a, b), c); break;
        case 11: output->scalar_value = 1.0 / a; break;
        case 12: output->scalar_value = a * a; break;
        case 13: output->scalar_value = a * a * a; break;
        case 14: output->scalar_value = mathblocks_square_root(a); break;
        case 15: output->scalar_value = mathblocks_cube_root(a); break;
        case 16: output->scalar_value = mathblocks_power(a, b); break;
        case 17: output->scalar_value = mathblocks_exponential(a); break;
        case 18: output->scalar_value = mathblocks_natural_logarithm(a); break;
        case 19: output->scalar_value = mathblocks_binary_logarithm(a); break;
        case 20:
            output->scalar_value = mathblocks_natural_logarithm(a) / 2.30258509299404568402;
            break;
        case 21: output->scalar_value = mathblocks_sine(a); break;
        case 22: output->scalar_value = mathblocks_cosine(a); break;
        case 23: output->scalar_value = mathblocks_sine(a) / mathblocks_cosine(a); break;
        case 24: output->scalar_value = asin(a); break;
        case 25: output->scalar_value = mathblocks_arc_cosine(a); break;
        case 26: output->scalar_value = mathblocks_arc_tangent(a); break;
        case 27: output->scalar_value = mathblocks_arc_tangent_2(a, b); break;
        case 28:
        {
            double positive = mathblocks_exponential(a);
            double negative = mathblocks_exponential(-a);
            output->scalar_value = (positive - negative) / 2.0;
            break;
        }
        case 29:
        {
            double positive = mathblocks_exponential(a);
            double negative = mathblocks_exponential(-a);
            output->scalar_value = (positive + negative) / 2.0;
            break;
        }
        case 30:
        {
            double positive = mathblocks_exponential(a);
            double negative = mathblocks_exponential(-a);
            output->scalar_value = (positive - negative) / (positive + negative);
            break;
        }
        case 31: output->scalar_value = mathblocks_inverse_hyperbolic_sine(a); break;
        case 32:
            output->scalar_value = mathblocks_natural_logarithm(
                a + mathblocks_square_root(a * a - 1.0));
            break;
        case 33: output->scalar_value = 0.5 * mathblocks_log_one_plus(2.0 * a / (1.0 - a)); break;
        case 34: output->scalar_value = floor(a); break;
        case 35: output->scalar_value = ceil(a); break;
        case 36: output->scalar_value = nearbyint(a); break;
        case 37: output->scalar_value = trunc(a); break;
        case 38: output->scalar_value = fmod(a, b); break;
        case 39:
            output->scalar_value = a >= 0.0
                ? 1.0 / (1.0 + mathblocks_exponential(-a))
                : mathblocks_exponential(a) / (1.0 + mathblocks_exponential(a));
            break;
        case 40:
            output->scalar_value = mathblocks_natural_logarithm(a / (1.0 - a));
            break;
        case 41:
            output->scalar_value = fmax(a, 0.0) +
                mathblocks_log_one_plus(mathblocks_exponential(-fabs(a)));
            break;
        case 42: output->scalar_value = mathblocks_log_one_plus(a); break;
        case 43: output->scalar_value = mathblocks_error_function(a); break;
        case 44: output->boolean_value = a == b; scalar_output = false; break;
        case 45: output->boolean_value = a != b; scalar_output = false; break;
        case 46: output->boolean_value = a < b; scalar_output = false; break;
        case 47: output->boolean_value = a <= b; scalar_output = false; break;
        case 48: output->boolean_value = a > b; scalar_output = false; break;
        case 49: output->boolean_value = a >= b; scalar_output = false; break;
        case 50:
            output->boolean_value = first->boolean_value && second->boolean_value;
            scalar_output = false;
            break;
        case 51:
            output->boolean_value = first->boolean_value || second->boolean_value;
            scalar_output = false;
            break;
        case 52:
            output->boolean_value = first->boolean_value != second->boolean_value;
            scalar_output = false;
            break;
        case 53:
            output->boolean_value = !first->boolean_value;
            scalar_output = false;
            break;
        case 54:
            output->scalar_value = first->boolean_value ? second->scalar_value : third->scalar_value;
            break;
        default: output->valid = 0; return;
    }

    if (scalar_output && !isfinite(output->scalar_value))
        output->valid = 0;
}