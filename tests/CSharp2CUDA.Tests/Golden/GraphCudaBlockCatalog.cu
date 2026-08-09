struct MathBlockGraphKernelEdge
{
    int from;
    int to;
    double weight;
};

__device__ int mathblocks_graph_component_count(
    const MathBlockGraphKernelEdge* edges,
    int edge_count,
    int vertex_count,
    int* visited,
    int* queue)
{
    for (int index = 0; index < vertex_count; index++)
        visited[index] = 0;
    int components = 0;
    for (int start = 0; start < vertex_count; start++)
    {
        if (visited[start])
            continue;
        components++;
        int head = 0;
        int tail = 0;
        queue[tail++] = start;
        visited[start] = 1;
        while (head < tail)
        {
            int vertex = queue[head++];
            for (int edge_index = 0; edge_index < edge_count; edge_index++)
            {
                int neighbor = edges[edge_index].from == vertex
                    ? edges[edge_index].to
                    : edges[edge_index].to == vertex
                        ? edges[edge_index].from
                        : -1;
                if (neighbor < 0 || visited[neighbor])
                    continue;
                visited[neighbor] = 1;
                queue[tail++] = neighbor;
            }
        }
    }
    return components;
}

__device__ bool mathblocks_graph_edge_less(
    const MathBlockGraphKernelEdge& left,
    const MathBlockGraphKernelEdge& right)
{
    if (left.weight < right.weight)
        return true;
    if (right.weight < left.weight)
        return false;
    if (left.from < right.from)
        return true;
    if (right.from < left.from)
        return false;
    return left.to < right.to;
}

__device__ int mathblocks_graph_find(int* parent, int vertex)
{
    while (parent[vertex] != vertex)
    {
        parent[vertex] = parent[parent[vertex]];
        vertex = parent[vertex];
    }
    return vertex;
}

extern "C" __global__ void mathblocks_graph(
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

    const MathBlockGraphKernelEdge* edges =
        first == nullptr ? nullptr : (const MathBlockGraphKernelEdge*)first->data_pointer;
    const double* matrix = first == nullptr ? nullptr : (const double*)first->data_pointer;
    const int* boolean_values = second == nullptr ? nullptr : (const int*)second->data_pointer;
    const double* vector = second == nullptr ? nullptr : (const double*)second->data_pointer;
    double* result = (double*)output->data_pointer;
    double* scratch = (double*)output->scratch_pointer;

    if (thread == 0)
    {
        int vertex_count = first->rows;
        switch (opcode)
        {
            case 0:
                if (vertex_count <= 1)
                {
                    output->scalar_value = 0.0;
                    break;
                }
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                double* laplacian = scratch;
                double* work = laplacian + vertex_count * vertex_count;
                double* eigenvalues = work + vertex_count * vertex_count;
                for (int index = 0; index < vertex_count * vertex_count; index++)
                    laplacian[index] = 0.0;
                for (int index = 0; index < first->count; index++)
                {
                    int from = edges[index].from;
                    int to = edges[index].to;
                    double weight = edges[index].weight;
                    laplacian[from * vertex_count + from] += weight;
                    laplacian[to * vertex_count + to] += weight;
                    laplacian[from * vertex_count + to] -= weight;
                    laplacian[to * vertex_count + from] -= weight;
                }
                mathblocks_matrix_symmetric_eigenvalues(
                    laplacian,
                    vertex_count,
                    work,
                    eigenvalues);
                output->scalar_value = eigenvalues[1];
                break;
            }
            case 1:
                if (second->count != vertex_count)
                {
                    output->valid = 0;
                    break;
                }
            {
                bool all = true;
                bool none = true;
                for (int index = 0; index < second->count; index++)
                {
                    all = all && boolean_values[index] != 0;
                    none = none && boolean_values[index] == 0;
                }
                double cut = 0.0;
                double left_volume = 0.0;
                double right_volume = 0.0;
                for (int index = 0; index < first->count; index++)
                {
                    if (edges[index].weight < 0.0)
                    {
                        output->valid = 0;
                        break;
                    }
                    if (boolean_values[edges[index].from])
                        left_volume += edges[index].weight;
                    else
                        right_volume += edges[index].weight;
                    if (boolean_values[edges[index].to])
                        left_volume += edges[index].weight;
                    else
                        right_volume += edges[index].weight;
                    if (boolean_values[edges[index].from] != boolean_values[edges[index].to])
                        cut += edges[index].weight;
                }
                if (all || none)
                    output->valid = 0;
                else
                    output->scalar_value = cut /
                        (left_volume < right_volume ? left_volume : right_volume);
                break;
            }
            case 2:
            case 7:
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int* visited = (int*)scratch;
                int* queue = visited + vertex_count;
                int components = mathblocks_graph_component_count(
                    edges,
                    first->count,
                    vertex_count,
                    visited,
                    queue);
                if (opcode == 2)
                    output->scalar_value = (double)components;
                else
                    output->boolean_value = components == 1 ? 1 : 0;
                break;
            }
            case 3:
            case 15:
                mathblocks_sequence_set_vector_shape(output, vertex_count);
                for (int index = 0; index < vertex_count; index++)
                    result[index] = 0.0;
                for (int index = 0; index < first->count; index++)
                {
                    double amount = opcode == 3 ? 1.0 : edges[index].weight;
                    result[edges[index].from] += amount;
                    result[edges[index].to] += amount;
                }
                break;
            case 4:
                if (first->rows != first->columns)
                {
                    output->valid = 0;
                    break;
                }
            {
                MathBlockGraphKernelEdge* graph =
                    (MathBlockGraphKernelEdge*)output->data_pointer;
                int edge_count = 0;
                for (int row = 0; row < first->rows; row++)
                    for (int column = 0; column < first->columns; column++)
                        if (row != column && matrix[row * first->columns + column] != 0.0)
                        {
                            if (edge_count >= output->capacity)
                            {
                                output->count = output->capacity == 2147483647
                                    ? -1
                                    : output->capacity + 1;
                                output->valid = 0;
                                break;
                            }
                            graph[edge_count].from = row;
                            graph[edge_count].to = column;
                            graph[edge_count].weight = matrix[row * first->columns + column];
                            edge_count++;
                        }
                output->rows = first->rows;
                if (output->valid)
                    output->count = edge_count;
                break;
            }
            case 5:
                mathblocks_sequence_set_vector_shape(output, vertex_count);
                if (vertex_count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
                result[0] = 0.0;
                if (vertex_count == 1)
                    break;
            {
                int size = vertex_count - 1;
                double* reduced_matrix = scratch;
                double* right = reduced_matrix + size * size;
                double* augmented = right + size;
                for (int index = 0; index < size * size; index++)
                    reduced_matrix[index] = 0.0;
                for (int index = 0; index < size; index++)
                    right[index] = 0.0;
                for (int index = 0; index < first->count; index++)
                {
                    int from = edges[index].from;
                    int to = edges[index].to;
                    if (from != 0)
                    {
                        reduced_matrix[(from - 1) * size + from - 1] += 1.0;
                        right[from - 1] -= edges[index].weight;
                    }
                    if (to != 0)
                    {
                        reduced_matrix[(to - 1) * size + to - 1] += 1.0;
                        right[to - 1] += edges[index].weight;
                    }
                    if (from != 0 && to != 0)
                    {
                        reduced_matrix[(from - 1) * size + to - 1] -= 1.0;
                        reduced_matrix[(to - 1) * size + from - 1] -= 1.0;
                    }
                }
                if (!mathblocks_matrix_try_solve(
                    reduced_matrix,
                    right,
                    size,
                    augmented,
                    result + 1))
                {
                    output->valid = 0;
                }
                break;
            }
            case 6:
                if (second->count != vertex_count)
                {
                    output->valid = 0;
                    break;
                }
            {
                double sum_squares = 0.0;
                for (int index = 0; index < first->count; index++)
                {
                    double residual = vector[edges[index].to] -
                                      vector[edges[index].from] -
                                      edges[index].weight;
                    sum_squares += residual * residual;
                }
                output->scalar_value = mathblocks_square_root(sum_squares);
                break;
            }
            case 8:
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                MathBlockGraphKernelEdge* work = (MathBlockGraphKernelEdge*)scratch;
                for (int index = 0; index < first->count; index++)
                    work[index] = edges[index];
                for (int index = 1; index < first->count; index++)
                {
                    MathBlockGraphKernelEdge value = work[index];
                    int position = index;
                    while (position > 0 && mathblocks_graph_edge_less(value, work[position - 1]))
                    {
                        work[position] = work[position - 1];
                        position--;
                    }
                    work[position] = value;
                }
                int* parent = (int*)(work + first->count);
                unsigned char* rank = (unsigned char*)(parent + vertex_count);
                for (int index = 0; index < vertex_count; index++)
                {
                    parent[index] = index;
                    rank[index] = 0;
                }
                MathBlockGraphKernelEdge* selected =
                    (MathBlockGraphKernelEdge*)output->data_pointer;
                int selected_count = 0;
                for (int index = 0; index < first->count; index++)
                {
                    int left = mathblocks_graph_find(parent, work[index].from);
                    int right = mathblocks_graph_find(parent, work[index].to);
                    if (left == right)
                        continue;
                    if (rank[left] < rank[right])
                        parent[left] = right;
                    else if (rank[left] > rank[right])
                        parent[right] = left;
                    else
                    {
                        parent[right] = left;
                        rank[left]++;
                    }
                    selected[selected_count++] = work[index];
                }
                output->rows = vertex_count;
                output->count = selected_count;
                break;
            }
            case 9:
                mathblocks_sequence_set_vector_shape(output, vertex_count);
                if (vertex_count <= 0 || scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                double damping = second->scalar_value;
                int iterations = 0;
                if (damping < 0.0 || damping > 1.0 ||
                    !mathblocks_sequence_positive_integer(third->scalar_value, &iterations) ||
                    iterations > 10000)
                {
                    output->valid = 0;
                    break;
                }
                double* outgoing = scratch;
                double* next = scratch + vertex_count;
                for (int vertex = 0; vertex < vertex_count; vertex++)
                {
                    result[vertex] = 1.0 / vertex_count;
                    outgoing[vertex] = 0.0;
                }
                for (int index = 0; index < first->count; index++)
                {
                    if (edges[index].weight < 0.0)
                    {
                        output->valid = 0;
                        break;
                    }
                    outgoing[edges[index].from] += edges[index].weight;
                }
                for (int iteration = 0; output->valid && iteration < iterations; iteration++)
                {
                    for (int vertex = 0; vertex < vertex_count; vertex++)
                        next[vertex] = (1.0 - damping) / vertex_count;
                    double dangling = 0.0;
                    for (int vertex = 0; vertex < vertex_count; vertex++)
                        if (outgoing[vertex] == 0.0)
                            dangling += result[vertex];
                    double dangling_share = damping * dangling / vertex_count;
                    for (int vertex = 0; vertex < vertex_count; vertex++)
                        next[vertex] += dangling_share;
                    for (int index = 0; index < first->count; index++)
                        next[edges[index].to] += damping * result[edges[index].from] *
                            edges[index].weight / outgoing[edges[index].from];
                    for (int vertex = 0; vertex < vertex_count; vertex++)
                        result[vertex] = next[vertex];
                }
                break;
            }
            case 10:
            case 12:
            case 13:
                mathblocks_sequence_set_matrix_shape(output, vertex_count, vertex_count);
                for (int index = 0; index < vertex_count * vertex_count; index++)
                    result[index] = 0.0;
                for (int index = 0; index < first->count; index++)
                {
                    int from = edges[index].from;
                    int to = edges[index].to;
                    double weight = edges[index].weight;
                    if (opcode == 10)
                    {
                        result[from * vertex_count + to] += weight;
                    }
                    else if (opcode == 12)
                    {
                        result[from * vertex_count + to] += weight;
                        result[to * vertex_count + from] += weight;
                    }
                    else
                    {
                        result[from * vertex_count + from] += weight;
                        result[to * vertex_count + to] += weight;
                        result[from * vertex_count + to] -= weight;
                        result[to * vertex_count + from] -= weight;
                    }
                }
                break;
            case 11:
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int* adjacency = (int*)scratch;
                for (int index = 0; index < vertex_count * vertex_count; index++)
                    adjacency[index] = 0;
                for (int index = 0; index < first->count; index++)
                {
                    adjacency[edges[index].from * vertex_count + edges[index].to] = 1;
                    adjacency[edges[index].to * vertex_count + edges[index].from] = 1;
                }
                int count = 0;
                for (int one = 0; one < vertex_count; one++)
                    for (int two = one + 1; two < vertex_count; two++)
                        for (int three = two + 1; three < vertex_count; three++)
                            if (adjacency[one * vertex_count + two] &&
                                adjacency[one * vertex_count + three] &&
                                adjacency[two * vertex_count + three])
                            {
                                count++;
                            }
                output->scalar_value = (double)count;
                break;
            }
            case 14:
                mathblocks_sequence_set_vector_shape(output, vertex_count);
                if (scratch == nullptr)
                {
                    output->valid = 0;
                    break;
                }
            {
                int source = 0;
                if (!mathblocks_nonnegative_integer(second->scalar_value, &source) || source >= vertex_count)
                {
                    output->valid = 0;
                    break;
                }
                int* visited = (int*)scratch;
                for (int vertex = 0; vertex < vertex_count; vertex++)
                {
                    result[vertex] = mathblocks_positive_infinity();
                    visited[vertex] = 0;
                }
                result[source] = 0.0;
                for (int iteration = 0; iteration < vertex_count; iteration++)
                {
                    int vertex = -1;
                    double best = mathblocks_positive_infinity();
                    for (int candidate = 0; candidate < vertex_count; candidate++)
                        if (!visited[candidate] && result[candidate] < best)
                        {
                            best = result[candidate];
                            vertex = candidate;
                        }
                    if (vertex < 0)
                        break;
                    visited[vertex] = 1;
                    for (int index = 0; index < first->count; index++)
                    {
                        if (edges[index].weight < 0.0)
                        {
                            output->valid = 0;
                            break;
                        }
                        int neighbor = edges[index].from == vertex
                            ? edges[index].to
                            : edges[index].to == vertex
                                ? edges[index].from
                                : -1;
                        if (neighbor < 0)
                            continue;
                        double candidate = result[vertex] + edges[index].weight;
                        result[neighbor] = result[neighbor] < candidate
                            ? result[neighbor]
                            : candidate;
                    }
                }
                break;
            }
        }

        if (output->valid &&
            opcode != 3 && opcode != 4 && opcode != 5 && opcode != 7 && opcode != 8 &&
            opcode != 9 && opcode != 10 && opcode != 12 && opcode != 13 && opcode != 14 &&
            opcode != 15 && !isfinite(output->scalar_value))
        {
            output->valid = 0;
        }
        if (output->valid &&
            (opcode == 3 || opcode == 5 || opcode == 9 || opcode == 10 || opcode == 12 ||
             opcode == 13 || opcode == 14 || opcode == 15))
        {
            for (int index = 0; index < output->count; index++)
                if (!isfinite(result[index])) output->valid = 0;
        }
    }
}