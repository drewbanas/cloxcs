namespace cloxcs
{
    static class Debug
    {
        public static void disassembleChunk(ref Chunk_t chunk, char[] name)
        {
            System.Console.WriteLine("== {0} ==", new string(name, 0, name.Length - 1));

            for (int offset = 0; offset < chunk.count;)
            {
                offset = disassembleInstruction(ref chunk, offset);
            }
        }

        private static int constantInstruction(string name, ref Chunk_t chunk, int offset)
        {
            byte constant = chunk.code[offset + 1];
            System.Console.Write("{0,-16} {1,4} '", name , constant.ToString());
            Value.printValue(chunk.constants.values[constant]);
            System.Console.WriteLine("'");
            return offset + 2;
        }

        private static int invokeInstruction(string name, ref Chunk_t chunk, int offset)
        {
            byte constant = chunk.code[offset + 1];
            byte argCount = chunk.code[offset + 2];
            System.Console.Write("{0,-16} ({1} args) {2,4} '", name, argCount.ToString(), constant.ToString());
            Value.printValue(chunk.constants.values[constant]);
            System.Console.WriteLine();
            return offset + 3;
        }

        private static int simpleInstruction(string name, int offset)
        {
            System.Console.WriteLine(name);
            return offset + 1;
        }

        private static int byteInstruction(string name, ref Chunk_t chunk, int offset)
        {
            byte slot = chunk.code[offset + 1];
            System.Console.WriteLine("{0,-16}{1,4}",  name , slot.ToString());
            return offset + 2;
        }

        private static int jumpInstruction(string name, int sign, ref Chunk_t chunk, int offset)
        {
            ushort jump = (ushort)(chunk.code[offset + 1] << 8);
            jump |= (ushort)chunk.code[offset + 2];
            System.Console.WriteLine("{0,-16}{1,4} -> {2}", name, offset.ToString(), (offset + 3 + sign * jump).ToString());
            return offset + 3;
        }

        public static int disassembleInstruction(ref Chunk_t chunk, int offset)
        {
            System.Console.Write("{0} ", offset.ToString("D4"));
            if (offset > 0 && chunk.lines[offset] == chunk.lines[offset - 1])
            {
                System.Console.Write("   | ");
            }
            else
            {
                System.Console.Write("{0,4} ", chunk.lines[offset].ToString());
            }


            OpCode instruction = (OpCode)chunk.code[offset];

            switch (instruction)
            {
                case OpCode.OP_CONSTANT:
                    return constantInstruction("OP_CONSTANT", ref chunk, offset);
                case OpCode.OP_NIL:
                    return simpleInstruction("OP_NIL", offset);
                case OpCode.OP_TRUE:
                    return simpleInstruction("OP_TRUE", offset);
                case OpCode.OP_FALSE:
                    return simpleInstruction("OP_FALSE", offset);
                case OpCode.OP_POP:
                    return simpleInstruction("OP_POP", offset);
                case OpCode.OP_GET_LOCAL:
                    return byteInstruction("OP_GET_LOCAL", ref chunk, offset);
                case OpCode.OP_SET_LOCAL:
                    return byteInstruction("OP_SET_LOCAL", ref chunk, offset);
                case OpCode.OP_GET_GLOBAL:
                    return constantInstruction("OP_GET_GLOBAL", ref chunk, offset);
                case OpCode.OP_DEFINE_GLOBAL:
                    return constantInstruction("OP_DEFINE_GLOBAL", ref chunk, offset);
                case OpCode.OP_SET_GLOBAL:
                    return constantInstruction("OP_SET_GLOBAL", ref chunk, offset);
                case OpCode.OP_GET_UPVALUE:
                    return byteInstruction("OP_GET_UPVALUE", ref chunk, offset);
                case OpCode.OP_SET_UPVALUE:
                    return byteInstruction("OP_SET_UPVALUE", ref chunk, offset);
                case OpCode.OP_GET_PROPERTY:
                    return constantInstruction("OP_GET_PROPERTY", ref chunk, offset);
                case OpCode.OP_SET_PROPERTY:
                    return constantInstruction("OP_SET_PROPERTY", ref chunk, offset);
                case OpCode.OP_GET_SUPER:
                    return constantInstruction("OP_GET_SUPER", ref chunk, offset);
                case OpCode.OP_EQUAL:
                    return simpleInstruction("OP_EQUAL", offset);
                case OpCode.OP_GREATER:
                    return simpleInstruction("OP_GREATER", offset);
                case OpCode.OP_LESS:
                    return simpleInstruction("OP_LESS", offset);
                case OpCode.OP_ADD:
                    return simpleInstruction("OP_ADD", offset);
                case OpCode.OP_SUBTRACT:
                    return simpleInstruction("OP_SUBTRACT", offset);
                case OpCode.OP_MULTIPLY:
                    return simpleInstruction("OP_MULTIPLY", offset);
                case OpCode.OP_DIVIDE:
                    return simpleInstruction("OP_DIVIDE", offset);
                case OpCode.OP_NOT:
                    return simpleInstruction("OP_NOT", offset);
                case OpCode.OP_NEGATE:
                    return simpleInstruction("OP_NEGATE", offset);
                case OpCode.OP_PRINT:
                    return simpleInstruction("OP_PRINT", offset);
                case OpCode.OP_JUMP:
                    return jumpInstruction("OP_JUMP", 1, ref chunk, offset);
                case OpCode.OP_JUMP_IF_FALSE:
                    return jumpInstruction("OP_JUMP_IF_FALSE", 1, ref chunk, offset);
                case OpCode.OP_LOOP:
                    return jumpInstruction("OP_LOOP", -1, ref chunk, offset);
                case OpCode.OP_CALL:
                    return byteInstruction("OP_CALL", ref chunk, offset);
                case OpCode.OP_INVOKE:
                    return invokeInstruction("OP_INVOKE", ref chunk, offset);
                case OpCode.OP_SUPER_INVOKE:
                    return invokeInstruction("OP_SUPER_INVOKE", ref chunk, offset);
                case OpCode.OP_CLOSURE:
                    {
                        offset++;
                        byte constant = chunk.code[offset++];
                        System.Console.Write("{0,-16} {1,4} ", "OP_CLOSURE ", constant.ToString());
                        Value.printValue(chunk.constants.values[constant]);
                        System.Console.WriteLine();

                        ObjFunction function = Object.AS_FUNCTION(chunk.constants.values[constant]);
                        for (int j = 0; j < function.upvalueCount; j++)
                        {
                            int isLocal = chunk.code[offset++];
                            int index = chunk.code[offset++];
                            System.Console.WriteLine("{0}      |                     {1} {2}", (offset - 2).ToString("D4"), ((isLocal != 0) ? "local" : "upvalue"), index.ToString());
                        }

                        return offset;
                    }
                case OpCode.OP_CLOSE_UPVALUE:
                    return simpleInstruction("OP_CLOSE_UPVALUE", offset);
                case OpCode.OP_RETURN:
                    return simpleInstruction("OP_RETURN", offset);
                case OpCode.OP_CLASS:
                    return constantInstruction("OP_CLASS", ref chunk, offset);
                case OpCode.OP_INHERIT:
                    return simpleInstruction("OP_INHERIT", offset);
                case OpCode.OP_METHOD:
                    return constantInstruction("OP_METHOD", ref chunk, offset);
                default:
                    System.Console.WriteLine("Unkown opcode " + ((byte)instruction).ToString());
                    return offset + 1;
            }
        }
    }
}
