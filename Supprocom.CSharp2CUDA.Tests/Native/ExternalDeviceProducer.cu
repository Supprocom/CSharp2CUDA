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

__device__ void mathblocks_operation_dispatch(
    int family,
    int opcode,
    const MathBlockSlot* const* inputs,
    int input_count,
    MathBlockSlot* output)
{
    output->scalar_value = inputs[0]->scalar_value + inputs[1]->scalar_value +
        (double)(family + opcode + input_count);
    output->valid = 1;
}
