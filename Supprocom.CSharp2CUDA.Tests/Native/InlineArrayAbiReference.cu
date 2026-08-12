struct InlineElement
{
    int code;
    unsigned long long value;
};

struct InlinePayload
{
    unsigned char prefix;
    int input_kinds[3];
    int operands[3];
    int operand_kinds[3];
    InlineElement nodes[5];
    unsigned long long runtime_invalid_by_operation[4];
    int suffix;
};

struct InlineAlignmentProbe
{
    unsigned char prefix;
    InlinePayload payload;
};

extern "C" __global__ void native_inline_array_abi(
    unsigned char* storage,
    unsigned long long* output)
{
    InlineAlignmentProbe* probe = (InlineAlignmentProbe*)storage;
    InlinePayload* value = &probe->payload;
    unsigned long long address = (unsigned long long)value;
    output[0] = (unsigned long long)(value + 1) - address;
    output[1] = (unsigned long long)&probe->payload - (unsigned long long)probe;
    output[2] = (unsigned long long)&value->input_kinds[0] - address;
    output[3] = (unsigned long long)&value->operands[0] - address;
    output[4] = (unsigned long long)&value->operand_kinds[0] - address;
    output[5] = (unsigned long long)&value->nodes[0] - address;
    output[6] = (unsigned long long)&value->runtime_invalid_by_operation[0] - address;
    output[7] = (unsigned long long)&value->suffix - address;
    output[8] = (unsigned long long)(&value->nodes[1] + 1) -
        (unsigned long long)&value->nodes[1];
    output[9] = (unsigned long long)&value->nodes[1].value -
        (unsigned long long)&value->nodes[1];

    value->input_kinds[2] = 17;
    value->operand_kinds[1] = 23;
    value->runtime_invalid_by_operation[3] = 29ull;
    int* primitive = value->operands;
    primitive[2] = 31;
    InlineElement* structures = value->nodes;
    structures[4].code = 37;
    structures[4].value = 41ull;
    output[10] = (unsigned long long)value->input_kinds[2];
    output[11] = (unsigned long long)value->operand_kinds[1];
    output[12] = value->runtime_invalid_by_operation[3];
    output[13] = (unsigned long long)primitive[2];
    output[14] = (unsigned long long)structures[4].code;
    output[15] = structures[4].value;
}
