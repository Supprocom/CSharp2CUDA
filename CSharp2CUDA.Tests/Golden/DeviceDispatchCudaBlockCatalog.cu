__device__ void mathblocks_operation_dispatch(
    int family,
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    switch (family)
    {
        case 0: mathblocks_advanced_dispatch(opcode, inputs, input_count, output); break;
        case 1: mathblocks_complex_dispatch(opcode, inputs, input_count, output); break;
        case 2: mathblocks_geometry_dispatch(opcode, inputs, input_count, output); break;
        case 3: mathblocks_graph_dispatch(opcode, inputs, input_count, output); break;
        case 4: mathblocks_matrix_dispatch(opcode, inputs, input_count, output); break;
        case 5: mathblocks_probability_dispatch(opcode, inputs, input_count, output); break;
        case 6: mathblocks_scalar_dispatch(opcode, inputs, input_count, output); break;
        case 7: mathblocks_sequence_path_dispatch(opcode, inputs, input_count, output); break;
        case 8: mathblocks_statistics_dispatch(opcode, inputs, input_count, output); break;
        case 9: mathblocks_transport_dispatch(opcode, inputs, input_count, output); break;
        case 10: mathblocks_vector_dispatch(opcode, inputs, input_count, output); break;
        default:
            if (threadIdx.x == 0)
            {
                output->valid = 0;
                output->count = 0;
            }
            break;
    }
    __syncthreads();
}

extern "C" __global__ void mathblocks_scalar(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_scalar_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_vector(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_vector_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_complex(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_complex_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_matrix(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_matrix_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_probability(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_probability_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_sequence_path(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_sequence_path_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_statistics(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_statistics_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_geometry(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_geometry_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_graph(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_graph_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_advanced(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_advanced_dispatch(opcode, inputs, input_count, output);
}

extern "C" __global__ void mathblocks_transport(
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    mathblocks_transport_dispatch(opcode, inputs, input_count, output);
}