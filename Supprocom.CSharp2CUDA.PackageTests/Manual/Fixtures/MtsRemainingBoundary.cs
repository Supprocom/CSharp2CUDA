using System;
using Supprocom.CSharp2CUDA;

internal static unsafe class MtsRemainingBoundaryCudaModule
{
    public struct ResearchGpuType
    {
        public int kind;
        public int rows;
        public int columns;
    }

    public struct ResearchHash128
    {
        public ulong first;
        public ulong second;
    }

    public struct ResearchEvolutionTerminal
    {
        public ResearchGpuType type;
        public int maximum_lookback;
        public int flags;
        public int reserved;
        public ResearchHash128 structural;
    }

    public struct ResearchEvolutionOperation
    {
        public int family;
        public int opcode;
        public int arity;
        public int temporal;
        public int type_rule;
        public int requires_row_count;
        public int contract_index;
        public int reserved;
        public int output_kind;

        [CudaInlineArray(3)]
        public int* input_kinds;

        public ulong cost;
        public ResearchHash128 structural;
    }

    public struct ResearchEvolutionNode
    {
        public int operation;
        public int arity;

        [CudaInlineArray(3)]
        public int* operands;

        [CudaInlineArray(3)]
        public int* operand_kinds;

        [CudaInlineArray(3)]
        public ulong* constant_bands;

        public int temporal;
        public int maximum_lookback;
        public ResearchGpuType type;
        public int alias_of;
        public int payload_slot;
        public int reserved;
        public ulong cost;
        public int wave_owner;
        public int wave_flags;
    }

    public struct ResearchEvolutionEntry
    {
        public int status;
        public int reserved;
        public int reserved_age;
        public int operation_count;
        public int maximum_lookback;
        public int quality_cell;
        public double quality;
        public double aggregate_quality;
        public double lower_era_quality;
        public double median_era_quality;
        public double confidence_interval_lower;
        public double confidence_interval_upper;
        public ulong deterministic_cost;
        public ulong proposal_cursor;
        public ResearchHash128 structural;
        public ResearchHash128 semantic;
        public int eligible_count;
        public int active_count;
        public int inactive_count;
        public int positive_count;
        public int active_positive_count;
        public int finite_era_count;
        public int orientation;
        public int feasible;
        public int dispatched_operation_count;
        public int avoided_operation_count;

        [CudaInlineArray(32)]
        public ResearchEvolutionNode* nodes;
    }

    public struct ResearchEvolutionState
    {
        public ulong schedule_cell_cursor;
        public ulong evaluated_trial_count;
        public ulong cycle_count;
        public ulong operation_dispatch_count;
        public ulong avoided_operation_count;
        public ulong objective_evaluation_count;
        public ulong static_rejection_count;
        public ulong semantic_duplicate_count;
        public ulong objective_rejection_count;
        public ulong runtime_invalid_count;
        public int pareto_count;
        public int quality_count;
        public ulong skipped_schedule_cell_count;
        public ulong schedule_formula_rank;

        [CudaInlineArray(7)]
        public ulong* runtime_invalid_by_operation;

        public ulong runtime_invalid_without_operation;
    }

    public struct ResearchEvolutionControl
    {
        public int active_bank;
        public int failure_code;
        public int barrier_count;
        public int barrier_phase;
        public int stop_at_boundary;
        public ulong schedule_clocks;
    }

    public struct ResearchMappedCheckpointHeader
    {
        public int ready;
        public int byte_count;
        public ulong sequence;
        public ulong valid_formula_count;
        public ulong checkpoint_interval_nanoseconds;
    }

    public struct MathBlockSlot
    {
        public double scalar_value;
        public ulong data_pointer;
        public ulong scratch_pointer;
        public int boolean_value;
        public int valid;
        public int rows;
        public int columns;
        public int count;
        public int capacity;
    }

    [CudaExternalDevice(Name = "mathblocks_operation_dispatch")]
    private static void Dispatch(
        int family,
        int opcode,
        [CudaReadOnly] MathBlockSlot** inputs,
        int input_count,
        MathBlockSlot* output) => throw new NotSupportedException();

    [CudaDevice(Name = "research_evolution_timestamp_nanoseconds")]
    private static ulong TimestampNanoseconds()
    {
        return Cuda.GlobalTimer();
    }

    [CudaGlobal(Name = "mts_research_owned_evolution")]
    private static void Run(
        byte* arena,
        byte* layered_arena,
        byte* mapped,
        int wave_count,
        int lane_count,
        int injected_failure_wave,
        int injected_failure_stage,
        int persistent_mode,
        int checkpoint_slot_count,
        int checkpoint_payload_bytes,
        int checkpoint_slot_stride)
    {
        ResearchEvolutionOperation* operations =
            (ResearchEvolutionOperation*)(arena + 128);
        ResearchEvolutionEntry* entries = (ResearchEvolutionEntry*)(arena + 512);
        ResearchEvolutionState* state = (ResearchEvolutionState*)(arena + 4096);
        ResearchEvolutionControl* control = (ResearchEvolutionControl*)(arena + 8192);
        int sharedStatus = Cuda.Shared<int>();
        int* sharedValues = Cuda.SharedArray<int>(3);
        byte* dynamicStorage = Cuda.DynamicSharedBytes(8);
        MathBlockSlot* slots = stackalloc MathBlockSlot[3];
        MathBlockSlot** dispatchInputs = stackalloc MathBlockSlot*[2];
        sharedValues[0] = operations[0].input_kinds[0];
        entries[0].nodes[0].operands[0] = sharedValues[0];
        state->runtime_invalid_by_operation[0] = 0UL;
        sharedStatus = dynamicStorage == null || layered_arena == null ? 1 : 0;
        slots[0].scalar_value = (double)wave_count;
        slots[1].scalar_value = (double)lane_count;
        dispatchInputs[0] = &slots[0];
        dispatchInputs[1] = &slots[1];
        Dispatch(1, 2, dispatchInputs, 2, &slots[2]);

        if (persistent_mode != 0)
        {
            int writerFailure = Cuda.VolatileLoadInt32(mapped, 12UL);
            int stopRequested = Cuda.VolatileLoad((int*)mapped);
            control->stop_at_boundary = stopRequested != 0 ? 1 : 0;
            if (writerFailure != 0)
                control->failure_code = 401;
            if (control->stop_at_boundary != 0)
            {
                ulong persistedSequence = Cuda.VolatileLoadUInt64(mapped, 16UL);
                writerFailure = Cuda.VolatileLoadInt32(mapped, 12UL);
                while (persistedSequence < state->cycle_count && writerFailure == 0)
                {
                    Cuda.NanoSleep(128u);
                    persistedSequence = Cuda.VolatileLoadUInt64(mapped, 16UL);
                    writerFailure = Cuda.VolatileLoadInt32(mapped, 12UL);
                }
            }
        }

        control->schedule_clocks = TimestampNanoseconds();
        if (Cuda.BlockIdx.X == 0 && Cuda.ThreadIdx.X == 0)
        {
            Cuda.VolatileStoreInt32(mapped, 8UL, control->failure_code + sharedStatus);
            Cuda.ThreadFenceSystem();
            Cuda.VolatileStore((int*)(mapped + 4), 1);
            Cuda.ThreadFenceSystem();
        }
    }
}
