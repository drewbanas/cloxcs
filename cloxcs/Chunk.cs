#define NAN_BOXING
#if NAN_BOXING
using Value_t = System.UInt64;
#endif

namespace cloxcs
{
    public enum OpCode
    {
        OP_CONSTANT,
        OP_NIL,
        OP_TRUE,
        OP_FALSE,
        OP_POP,
        OP_GET_LOCAL,
        OP_SET_LOCAL,
        OP_GET_GLOBAL,
        OP_DEFINE_GLOBAL,
        OP_SET_GLOBAL,
        OP_GET_UPVALUE,
        OP_SET_UPVALUE,
        OP_GET_PROPERTY,
        OP_SET_PROPERTY,
        OP_GET_SUPER,
        OP_EQUAL,
        OP_GREATER,
        OP_LESS,
        OP_ADD,
        OP_SUBTRACT,
        OP_MULTIPLY,
        OP_DIVIDE,
        OP_NOT,
        OP_NEGATE,
        OP_PRINT,
        OP_JUMP,
        OP_JUMP_IF_FALSE,
        OP_LOOP,
        OP_CALL,
        OP_INVOKE,
        OP_SUPER_INVOKE,
        OP_CLOSURE,
        OP_CLOSE_UPVALUE,
        OP_RETURN,
        OP_CLASS,
        OP_INHERIT,
        OP_METHOD,
        Count
    }

    public struct Chunk_t
    {
        public int count;
        public int capacity;
        public byte[] code; // the bytecode.
        public int[] lines;

        public ValueArray constants;
    }

    static class Chunk
    {
        public static void initChunk(ref Chunk_t chunk)
        {
            chunk.count = 0;
            chunk.capacity = 0;
            chunk.code = null;
            chunk.lines = null;
            Value.initiValueArray(ref chunk.constants);
        }

        public static void freeChunk(ref Chunk_t chunk)
        {
            Memory.FREE_ARRAY<byte>(typeof(byte), ref chunk.code, chunk.capacity);
            Memory.FREE_ARRAY<int>(typeof(int), ref chunk.lines, chunk.capacity);

            Value.freeValueArray(ref chunk.constants);

            initChunk(ref chunk);
        }

        public static void writeChunk(ref Chunk_t chunk, OpCode opcode, int line)
        {
            writeChunk(ref chunk, (byte)opcode, line);
        }

        public static void writeChunk(ref Chunk_t chunk, byte byte_, int line)
        {
            if (chunk.capacity < chunk.count + 1)
            {
                int oldCapacity = chunk.capacity;
                chunk.capacity = Memory.GROW_CAPACITY(oldCapacity);
                chunk.code = (byte[])Memory.GROW_ARRAY<byte>(ref chunk.code, typeof(byte), oldCapacity, chunk.capacity);
                chunk.lines = (int[])Memory.GROW_ARRAY<int>(ref chunk.lines, typeof(int), oldCapacity, chunk.capacity);
            }

            chunk.code[chunk.count] = byte_;
            chunk.lines[chunk.count] = line;
            chunk.count++;
        }


        public static int addConstant(ref Chunk_t chunk, Value_t value)
        {
            VM.push(value);
            Value.writeValueArray(ref chunk.constants, value);
            VM.pop();
            return chunk.constants.count - 1;
        }

    }
}
