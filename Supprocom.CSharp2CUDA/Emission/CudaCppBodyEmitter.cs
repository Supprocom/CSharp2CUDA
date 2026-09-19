using System.Globalization;
using System.Text;
using Supprocom.CSharp2CUDA.Semantics;

namespace Supprocom.CSharp2CUDA.Emission;

internal sealed class CudaCppBodyEmitter(string newLine)
{
    private readonly StringBuilder output = new();
    private readonly Stack<LoopEmissionContext> loops = [];
    private int indentation;

    public string Emit(CudaFunctionBodyIr body)
    {
        EmitStatement(body.Body);
        return output.ToString().TrimEnd('\r', '\n');
    }

    private void EmitStatement(CudaStatementIr statement)
    {
        switch (statement)
        {
            case CudaBlockStatementIr block:
                EmitBlock(block);
                break;
            case CudaStatementGroupIr group:
                foreach (var item in group.Statements)
                    EmitStatement(item);
                break;
            case CudaVariableDeclarationStatementIr declaration:
                WriteIndent();
                if (declaration.IsConst)
                    output.Append("const ");
                output.Append(declaration.TypeName)
                    .Append(' ')
                    .Append(declaration.Name);
                if (declaration.Initializer is not null)
                    output.Append(" = ").Append(declaration.Initializer.Code);
                WriteLine(";");
                break;
            case CudaFixedArrayDeclarationStatementIr array:
                WriteIndent();
                if (array.IsConst)
                    output.Append("const ");
                output.Append(array.ElementTypeName)
                    .Append(' ')
                    .Append(array.Name)
                    .Append('[')
                    .Append(array.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(']');
                if (!array.Initializers.IsEmpty)
                {
                    output.Append(" = { ");
                    for (var index = 0; index < array.Initializers.Length; index++)
                    {
                        if (index > 0)
                            output.Append(", ");
                        output.Append(array.Initializers[index].Code);
                    }
                    output.Append(" }");
                }
                WriteLine(";");
                break;
            case CudaStorageDeclarationStatementIr storage:
                EmitStorage(storage);
                break;
            case CudaExpressionStatementIr expression:
                EmitPrefix(expression.Expression);
                WriteIndent();
                output.Append(expression.Expression.Value.Code);
                WriteLine(";");
                break;
            case CudaIfStatementIr conditional:
                EmitIf(conditional);
                break;
            case CudaWhileStatementIr loop:
                EmitWhile(loop);
                break;
            case CudaDoWhileStatementIr loop:
                EmitDoWhile(loop);
                break;
            case CudaForStatementIr loop:
                EmitFor(loop);
                break;
            case CudaSwitchStatementIr selection:
                EmitSwitch(selection);
                break;
            case CudaReturnStatementIr returned:
                EmitReturn(returned);
                break;
            case CudaBreakStatementIr:
                WriteIndentedLine("break;");
                break;
            case CudaContinueStatementIr:
                EmitContinue();
                break;
            case CudaEmptyStatementIr:
                WriteIndentedLine(";");
                break;
            case CudaTrapStatementIr:
                WriteIndentedLine("asm volatile(\"trap;\");");
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown CUDA statement IR '{statement.GetType().Name}'.");
        }
    }

    private void EmitBlock(CudaBlockStatementIr block)
    {
        WriteIndentedLine("{");
        indentation++;
        foreach (var statement in block.Statements)
            EmitStatement(statement);
        indentation--;
        WriteIndentedLine("}");
    }

    private void EmitStorage(CudaStorageDeclarationStatementIr storage)
    {
        WriteIndent();
        switch (storage.Kind)
        {
            case CudaStorageKind.SharedScalar:
                output.Append("__shared__ ")
                    .Append(storage.ElementTypeName)
                    .Append(' ')
                    .Append(storage.Name);
                break;
            case CudaStorageKind.SharedArray:
                output.Append("__shared__ ")
                    .Append(storage.ElementTypeName)
                    .Append(' ')
                    .Append(storage.Name)
                    .Append('[')
                    .Append(storage.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(']');
                break;
            case CudaStorageKind.DynamicSharedBytes:
                output.Append("extern __shared__ __align__(")
                    .Append(storage.Alignment.ToString(CultureInfo.InvariantCulture))
                    .Append(") unsigned char ")
                    .Append(storage.Name)
                    .Append("[]");
                break;
            default:
                throw new InvalidOperationException(
                    $"Unknown CUDA storage kind '{storage.Kind}'.");
        }
        WriteLine(";");
    }

    private void EmitIf(CudaIfStatementIr conditional)
    {
        EmitPrefix(conditional.Condition);
        WriteIndent();
        output.Append("if (").Append(conditional.Condition.Value.Code).Append(')');
        WriteLine();
        EmitEmbeddedStatement(conditional.WhenTrue);
        if (conditional.WhenFalse is null)
            return;
        WriteIndentedLine("else");
        EmitEmbeddedStatement(conditional.WhenFalse);
    }

    private void EmitWhile(CudaWhileStatementIr loop)
    {
        WriteIndentedLine("while (true)");
        WriteIndentedLine("{");
        indentation++;
        EmitPrefix(loop.Condition);
        WriteIndent();
        output.Append("if (!(").Append(loop.Condition.Value.Code).Append("))");
        WriteLine();
        indentation++;
        WriteIndentedLine("break;");
        indentation--;
        loops.Push(new LoopEmissionContext(null));
        EmitStatement(loop.Body);
        loops.Pop();
        indentation--;
        WriteIndentedLine("}");
    }

    private void EmitDoWhile(CudaDoWhileStatementIr loop)
    {
        WriteIndentedLine("while (true)");
        WriteIndentedLine("{");
        indentation++;
        var context = new LoopEmissionContext(loop.ContinueLabel);
        loops.Push(context);
        EmitStatement(loop.Body);
        loops.Pop();
        if (context.Used)
        {
            WriteIndent();
            output.Append(loop.ContinueLabel).Append(':');
            WriteLine();
        }
        EmitPrefix(loop.Condition);
        WriteIndent();
        output.Append("if (!(").Append(loop.Condition.Value.Code).Append("))");
        WriteLine();
        indentation++;
        WriteIndentedLine("break;");
        indentation--;
        indentation--;
        WriteIndentedLine("}");
    }

    private void EmitFor(CudaForStatementIr loop)
    {
        WriteIndentedLine("{");
        indentation++;
        foreach (var initializer in loop.Initializers)
            EmitStatement(initializer);
        WriteIndentedLine("while (true)");
        WriteIndentedLine("{");
        indentation++;
        if (loop.Condition is not null)
        {
            EmitPrefix(loop.Condition);
            WriteIndent();
            output.Append("if (!(").Append(loop.Condition.Value.Code).Append("))");
            WriteLine();
            indentation++;
            WriteIndentedLine("break;");
            indentation--;
        }

        var context = new LoopEmissionContext(loop.ContinueLabel);
        loops.Push(context);
        EmitStatement(loop.Body);
        loops.Pop();
        if (context.Used)
        {
            WriteIndent();
            output.Append(loop.ContinueLabel).Append(':');
            WriteLine();
        }
        foreach (var incrementor in loop.Incrementors)
        {
            EmitPrefix(incrementor);
            WriteIndent();
            output.Append(incrementor.Value.Code);
            WriteLine(";");
        }
        if (context.Used && loop.Incrementors.IsEmpty)
            WriteIndentedLine(";");
        indentation--;
        WriteIndentedLine("}");
        indentation--;
        WriteIndentedLine("}");
    }

    private void EmitSwitch(CudaSwitchStatementIr selection)
    {
        EmitPrefix(selection.Value);
        WriteIndent();
        output.Append("switch (").Append(selection.Value.Value.Code).Append(')');
        WriteLine();
        WriteIndentedLine("{");
        indentation++;
        foreach (var section in selection.Sections)
        {
            foreach (var label in section.Labels)
                WriteIndentedLine(label is null ? "default:" : $"case {label}:");
            indentation++;
            foreach (var statement in section.Statements)
                EmitStatement(statement);
            indentation--;
        }
        indentation--;
        WriteIndentedLine("}");
    }

    private void EmitReturn(CudaReturnStatementIr returned)
    {
        if (returned.Expression is null)
        {
            WriteIndentedLine("return;");
            return;
        }
        EmitPrefix(returned.Expression);
        WriteIndent();
        output.Append("return ").Append(returned.Expression.Value.Code);
        WriteLine(";");
    }

    private void EmitContinue()
    {
        var context = loops.Peek();
        if (context.Label is null)
        {
            WriteIndentedLine("continue;");
            return;
        }
        context.Used = true;
        WriteIndent();
        output.Append("goto ").Append(context.Label);
        WriteLine(";");
    }

    private void EmitPrefix(CudaExpressionIr expression)
    {
        foreach (var statement in expression.Prefix)
            EmitStatement(statement);
    }

    private void EmitEmbeddedStatement(CudaStatementIr statement)
    {
        if (statement is CudaBlockStatementIr)
        {
            EmitStatement(statement);
            return;
        }
        indentation++;
        EmitStatement(statement);
        indentation--;
    }

    private void WriteIndentedLine(string text)
    {
        WriteIndent();
        WriteLine(text);
    }

    private void WriteIndent() => output.Append(' ', indentation * 4);

    private void WriteLine(string text = "") => output.Append(text).Append(newLine);

    private sealed class LoopEmissionContext(string? label)
    {
        public string? Label { get; } = label;

        public bool Used { get; set; }
    }
}
