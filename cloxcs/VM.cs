#if DEBUG
#define DEBUG_TRACE_EXECUTION
#endif

#define NAN_BOXING
#if NAN_BOXING
using Value_t = System.UInt64;
#endif

namespace cloxcs
{
	public struct CallFrame
	{
		public ObjClosure closure;
		public byte[] ip;
		public Value_t[] slots;

		public int _ip_index;
		public int _slot_offset;
		public int _ip_count;
	}

	public struct VM_t
	{
		public CallFrame[] frames;//[FRAMES_MAX]
		public int frameCount;
		public Value_t[] stack;
		public int stackTop;
		public Table_t globals;
		public Table_t strings;
		public ObjString initString;
		public ObjUpvalue openUpvalues;

		public uint bytesAllocated;
		public uint nextGC;

		public Obj objects;  // clox Obj* objects linked list
		public int grayCount;
		public int grayCapacity;
		public Obj[] grayStack;
	}

	public enum InterpretResult
	{
		INTERPRET_OK,
		INTERPRET_COMPILE_ERROR,
		INTERPRET_RUNTIME_ERROR,
	}

	static class VM
	{
		const int FRAMES_MAX = 64;
		const int STACK_MAX = FRAMES_MAX * Compiler.UINT8_COUNT;
		public static VM_t vm;

		private static int _caller_ip_index;


		private static Value_t clockNative(int argCount, int _args_on_stack)
		{
			return Value.NUMBER_VAL((double)System.DateTime.Now.Ticks / System.TimeSpan.TicksPerSecond);
		}

		static void resetStack()
		{
			vm.stackTop = 0; // clox: vm.stack
			vm.frameCount = 0;
			vm.openUpvalues = null;
		}

		static void runtimeError(string format, params string[] args)
		{
			System.Console.Write(format, args);
			System.Console.WriteLine();

			for (int i = vm.frameCount - 1; i >= 0; i--)
			{
				CallFrame frame = vm.frames[i];
				ObjFunction function = frame.closure.function;
				int instruction = frame._ip_index - 1;
				System.Console.Write("[line {0}] in ", function.chunk.lines[instruction].ToString());

				if (function.name == null)
				{
					System.Console.WriteLine("script");
				}
				else
				{
					System.Console.WriteLine("{0} ()", new string(function.name.chars, 0, function.name.chars.Length - 1));
				}

			}

#if DEBUG
			System.Console.ReadKey(); // convenience pause
#endif
			resetStack();
		}


		private static void defineNative(char[] name, NativeFn function)
		{
			push(Value.OBJ_VAL(Object.copyString(name, 0, name.Length)));
			push(Value.OBJ_VAL(Object.newNative(function)));
			Table.tableSet(ref vm.globals, Object.AS_STRING(vm.stack[0]), vm.stack[1]);
			pop();
			pop();
		}

		public static void initVM()
		{
			vm.stack = new Value_t[STACK_MAX]; // fix: cannot set in struct declaration
			vm.frames = new CallFrame[FRAMES_MAX]; // fix

			resetStack();
			vm.objects = null;
			vm.bytesAllocated = 0;
			vm.nextGC = 1024 * 1024;

			vm.grayCount = 0;
			vm.grayCapacity = 0;
			vm.grayStack = null;


			Table.initTable(ref vm.globals);
			Table.initTable(ref vm.strings);

			vm.initString = null;
			vm.initString = Object.copyString("init".ToCharArray(), 0, 4);

			defineNative("clock".ToCharArray(), clockNative);
		}

		public static void freeVM()
		{
			Table.freeTable(ref vm.globals);
			Table.freeTable(ref vm.strings);
			vm.initString = null;
			Memory.freeObjects();

			cHeap.objects.free();
			cHeap.values.free();
		}

		public static void push(Value_t value)
		{
			vm.stack[vm.stackTop] = value;
			vm.stackTop++;
		}

		public static Value_t pop()
		{
			vm.stackTop--;
			return vm.stack[vm.stackTop];
		}

		private static Value_t peek(int distance)
		{
			return vm.stack[vm.stackTop - 1 - distance];
		}

		private static bool call(ObjClosure closure, int argCount)
		{
			if (argCount != closure.function.arity)
			{
				runtimeError("Expected {0} but got {1}.", closure.function.arity.ToString(), argCount.ToString());
				return false;
			}

			if (vm.frameCount == FRAMES_MAX)
			{
				runtimeError("Stack overflow.");
				return false;
			}

			if (vm.frameCount > 0)
				vm.frames[vm.frameCount - 1]._ip_index = _caller_ip_index;

			CallFrame frame = vm.frames[vm.frameCount++];
			frame.closure = closure;
			frame.ip = closure.function.chunk.code;
			frame._ip_index = 0;

			frame._ip_count = frame.closure.function.chunk.count;

			frame.slots = vm.stack;
			frame._slot_offset = vm.stackTop - argCount - 1;

			vm.frames[vm.frameCount - 1] = frame; // Csharp ref WORK AROUND

			return true;
		}

		private static bool callValue(Value_t callee, int argCount)
		{
			if (Value.IS_OBJ(callee))
			{

				switch (Object.OBJ_TYPE(callee))
				{
					case ObjType.OBJ_BOUND_METHOD:
						{
							ObjBoundMethod bound = Object.AS_BOUND_METHOD(callee);
							vm.stack[vm.stackTop - argCount - 1] = bound.receiver;
							return call(bound.method, argCount);
						}

					case ObjType.OBJ_CLASS:
						{
							ObjClass klass = Object.AS_CLASS(callee);
							vm.stack[vm.stackTop - argCount - 1] = Value.OBJ_VAL(Object.newInstance(klass));
							Value_t initializer = new Value_t();
							if (Table.tableGet(ref klass.methods, vm.initString, ref initializer))
							{
								return call(Object.AS_CLOSURE(initializer), argCount);
							}
							else if (argCount != 0)
							{
								runtimeError("Expected 0 arguments but got {0}.", argCount.ToString());
								return false;
							}
							vm.frames[vm.frameCount - 1]._ip_index += 2; // HACK FIX
							return true;
						}

					case ObjType.OBJ_CLOSURE:
						return call(Object.AS_CLOSURE(callee), argCount);

					case ObjType.OBJ_NATIVE:
						{
							NativeFn native = Object.AS_NATIVE(callee);
							Value_t result = native(argCount, vm.stackTop - argCount);
							vm.stackTop -= argCount + 1;
							push(result);

							vm.frames[vm.frameCount - 1]._ip_index += 2; // HACK FIX
							return true;
						}
					default:
						// Non-callable object type.
						break;
				}
			}

			runtimeError("Can only call functions and classes.");
			return false;
		}

		private static bool invokeFromClass(ObjClass klass, ObjString name, int argCount)
		{
			Value_t method = new Value_t();
			if (!Table.tableGet(ref klass.methods, name, ref method))
			{
				runtimeError("Undefined property '{0}'.", new string(name.chars, 0, name.chars.Length - 1));
				return false;
			}

			return call(Object.AS_CLOSURE(method), argCount);
		}

		private static bool invoke(ObjString name, int argCount)
		{
			Value_t receiver = peek(argCount);

			if (!Object.IS_INSTANCE(receiver))
			{
				runtimeError("Only instances have methods.");
				return false;
			}

			ObjInstance instance = Object.AS_INSTANCE(receiver);

			Value_t value = new Value_t();
			if (Table.tableGet(ref instance.fields, name, ref value))
			{
				vm.stack[vm.stackTop - argCount - 1] = value;
				return callValue(value, argCount);
			}

			return invokeFromClass(instance.klass, name, argCount);
		}

		private static bool bindMethod(ObjClass klass, ObjString name)
		{
			Value_t method = new Value_t();
			if (!Table.tableGet(ref klass.methods, name, ref method))
			{
				runtimeError("Undefined property '{0}'.", new string(name.chars, 0, name.chars.Length - 1));
				return false;
			}

			ObjBoundMethod bound = Object.newBoundMethod(peek(0), Object.AS_CLOSURE(method));
			pop();
			push(Value.OBJ_VAL(bound));
			return true;
		}

		private static ObjUpvalue captureUpvalue(int local)
		{
			ObjUpvalue prevUpvalue = null;
			ObjUpvalue upvalue = vm.openUpvalues;

			while (upvalue != null && upvalue.location > local)
			{
				prevUpvalue = upvalue;
				upvalue = upvalue.next;
			}

			if (upvalue != null && upvalue.location == local)
				return upvalue;

			ObjUpvalue createdUpvalue = Object.newUpvalue(local);
			createdUpvalue.next = upvalue;

			if (prevUpvalue == null)
			{
				vm.openUpvalues = createdUpvalue;
			}
			else
			{
				prevUpvalue.next = createdUpvalue;
			}

			return createdUpvalue;
		}

		private static void closeUpvalues(int last)
		{
			while (vm.openUpvalues != null && vm.openUpvalues.location >= last)
			{
				ObjUpvalue upvalue = vm.openUpvalues;
				upvalue.closed = vm.stack[upvalue.location];
				upvalue.location = cHeap.values.store(upvalue.closed);
				upvalue._value_src = cHeap.values.contents;

				vm.openUpvalues = upvalue.next;
			}
		}

		private static void defineMethod(ObjString name)
		{
			Value_t method = peek(0);
			ObjClass klass = Object.AS_CLASS(peek(1));
			Table.tableSet(ref klass.methods, name, method);
			pop();
		}

		private static bool isFalsey(Value_t value)
		{
			return Value.IS_NIL(value) || (Value.IS_BOOL(value) && !Value.AS_BOOL(value));
		}

		private static void concatenate()
		{
			ObjString b = Object.AS_STRING(peek(0));
			ObjString a = Object.AS_STRING(peek(1));

			int length = a.length + b.length;
			char[] chars = Memory.ALLOCATE<char>(length + 1);
			Cfun._memcpy<char>(chars, a.chars, a._start, a.length);
			Cfun._memcpy<char>(chars, a.length, b.chars, b._start, b.length);
			chars[length] = '\0';

			ObjString result = Object.takeString(chars, 0, length);
			pop();
			pop();
			push(Value.OBJ_VAL(result));
		}

		private static byte READ_BYTE(ref CallFrame frame)
		{
			return frame.ip[frame._ip_index++];
		}

		private static ushort READ_SHORT(ref CallFrame frame)
		{
			frame._ip_index += 2;
			return (ushort)((frame.ip[frame._ip_index - 2] << 8) | frame.ip[frame._ip_index - 1]);
		}

		private static Value_t READ_CONSTANT(ref CallFrame frame)
		{
			return frame.closure.function.chunk.constants.values[READ_BYTE(ref frame)];
		}

		private static ObjString READ_STRING(ref CallFrame frame)
		{
			return Object.AS_STRING(READ_CONSTANT(ref frame));
		}

		public static InterpretResult run()
		{
			CallFrame frame = vm.frames[vm.frameCount - 1];
			frame._slot_offset = 0; 

			for (;;)
			{
#if DEBUG_TRACE_EXECUTION
#if BYPASS_THIS_BLOCK

				if(prev_frame_count != vm.frameCount)
				{
					prev_frame_count = vm.frameCount;
					for (int i = 0; i < frame._ip_count; i++)
					{
						if (frame.ip[i] < (byte)OpCode.Count)
						{
							System.Console.WriteLine(i.ToString("D2") + "- opcode\t" + ((OpCode)frame.ip[i]).ToString());
						}
						else
						{
							System.Console.WriteLine(i.ToString("D2") + "- constn\t" + (frame.ip[i]).ToString());
						}
					}
				}
#endif
				System.Console.Write("          ");
				for (int slot = 0; slot < vm.stackTop; slot++)
				{
					System.Console.Write("[ ");
					Value.printValue(vm.stack[slot]);
					System.Console.Write(" ]");
				}
				System.Console.WriteLine();

				Debug.disassembleInstruction(ref frame.closure.function.chunk, frame._ip_index);
#endif

				OpCode instruction;
				switch (instruction = (OpCode)READ_BYTE(ref frame))
				{
					case OpCode.OP_CONSTANT:
						{
							Value_t constant = READ_CONSTANT(ref frame);
							push(constant);
							break;
						}

					case OpCode.OP_NIL:
						push(Value.NIL_VAL());
						break;
					case OpCode.OP_TRUE:
						push(Value.BOOL_VAL(true));
						break;
					case OpCode.OP_FALSE:
						push(Value.BOOL_VAL(false));
						break;
					case OpCode.OP_POP:
						pop();
						break;
					case OpCode.OP_GET_LOCAL:
						{
							byte slot = READ_BYTE(ref frame);
							push(frame.slots[frame._slot_offset + slot]);
							break;
						}

					case OpCode.OP_SET_LOCAL:
						{
							byte slot = READ_BYTE(ref frame);
							frame.slots[frame._slot_offset + slot] = peek(0);
							break;
						}

					case OpCode.OP_GET_GLOBAL:
						{
							ObjString name = READ_STRING(ref frame);
							Value_t value = new Value_t();
							if (!Table.tableGet(ref vm.globals, name, ref value))
							{
								runtimeError("Undefined variable '{0}'.", new string(name.chars, 0, name.chars.Length - 1));
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}
							push(value);
							break;
						}

					case OpCode.OP_DEFINE_GLOBAL:
						{
							ObjString name = READ_STRING(ref frame);
							Table.tableSet(ref vm.globals, name, peek(0));
							pop();
							break;
						}

					case OpCode.OP_SET_GLOBAL:
						{
							ObjString name = READ_STRING(ref frame);
							if (Table.tableSet(ref vm.globals, name, peek(0)))
							{
								Table.tableDelete(ref vm.globals, name);
								runtimeError("Undefined variable '{0}'.", new string(name.chars, 0, name.chars.Length - 1));
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}
							break;
						}

					case OpCode.OP_GET_UPVALUE:
						{
							byte slot = READ_BYTE(ref frame);
							push(frame.closure.upvalues[slot]._value_src[frame.closure.upvalues[slot].location]);
							break;
						}

					case OpCode.OP_SET_UPVALUE:
						{
							byte slot = READ_BYTE(ref frame);
							frame.closure.upvalues[slot]._value_src[frame.closure.upvalues[slot].location] = peek(0);
							break;
						}

					case OpCode.OP_GET_PROPERTY:
						{
							if (!Object.IS_INSTANCE(peek(0)))
							{
								runtimeError("Only instances have properties.");
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}

							ObjInstance instance = Object.AS_INSTANCE(peek(0));
							ObjString name = READ_STRING(ref frame);

							Value_t value = new Value_t();
							if (Table.tableGet(ref instance.fields, name, ref value))
							{
								pop(); // Instance.
								push(value);
								break;
							}

							if (!bindMethod(instance.klass, name))
							{
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}
							break;
						}

					case OpCode.OP_SET_PROPERTY:
						{
							if (!Object.IS_INSTANCE(peek(1)))
							{
								runtimeError("Only instances have fields.");
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}

							ObjInstance instance = Object.AS_INSTANCE(peek(1));
							Table.tableSet(ref instance.fields, READ_STRING(ref frame), peek(0));

							Value_t value = pop();
							pop();
							push(value);
							break;
						}

					case OpCode.OP_GET_SUPER:
						{
							ObjString name = READ_STRING(ref frame);
							ObjClass superclass = Object.AS_CLASS(pop());
							if (!bindMethod(superclass, name))
							{
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}
							break;
						}

					case OpCode.OP_EQUAL:
						{
							Value_t b = pop();
							Value_t a = pop();
							push(Value.BOOL_VAL(Value.valuesEqual(a, b)));
							break;
						}

					case OpCode.OP_ADD:
						{
							if (Object.IS_STRING(peek(0)) && Object.IS_STRING(peek(1)))
							{
								concatenate();
							}
							else if (Value.IS_NUMBER(peek(0)) && Value.IS_NUMBER(peek(1)))
							{
								BINARY_OP(instruction);
							}
							else
							{
								runtimeError("Operands must be two numbers or two strings.");
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}
						}
						break;
					case OpCode.OP_GREATER:
					case OpCode.OP_LESS:
					case OpCode.OP_SUBTRACT:
					case OpCode.OP_MULTIPLY:
					case OpCode.OP_DIVIDE:
						if (!BINARY_OP(instruction))
						{
							return InterpretResult.INTERPRET_RUNTIME_ERROR;
						}
						break;

					case OpCode.OP_NOT:
						push(Value.BOOL_VAL(isFalsey(pop())));
						break;
					case OpCode.OP_NEGATE:
						if (!Value.IS_NUMBER(peek(0)))
						{
							runtimeError("Operand must be a number.");
							return InterpretResult.INTERPRET_RUNTIME_ERROR;
						}

						push(Value.NUMBER_VAL(-Value.AS_NUMBER(pop())));
						break;
					case OpCode.OP_PRINT:
						{
							Value.printValue(pop());
							System.Console.WriteLine();
							break;
						}
					case OpCode.OP_JUMP:
						{
							ushort offset = READ_SHORT(ref frame);
							frame._ip_index += offset;
							break;
						}
					case OpCode.OP_JUMP_IF_FALSE:
						{
							ushort offset = READ_SHORT(ref frame);
							if (isFalsey(peek(0)))
								frame._ip_index += offset;
							break;
						}

					case OpCode.OP_LOOP:
						{
							ushort offset = READ_SHORT(ref frame);
							frame._ip_index -= offset;
							break;
						}
					case OpCode.OP_CALL:
						{
							int argCount = READ_BYTE(ref frame);
							_caller_ip_index = frame._ip_index;
							if (!callValue(peek(argCount), argCount))
							{
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}
							frame = vm.frames[vm.frameCount - 1];
							break;
						}

					case OpCode.OP_INVOKE:
						{
							ObjString method = READ_STRING(ref frame);
							int argCount = READ_BYTE(ref frame);

							_caller_ip_index = frame._ip_index;
							if (!invoke(method, argCount))
							{
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}
							frame = vm.frames[vm.frameCount - 1];
							break;
						}

					case OpCode.OP_SUPER_INVOKE:
						{
							ObjString method = READ_STRING(ref frame);
							int argCount = READ_BYTE(ref frame);
							ObjClass superclass = Object.AS_CLASS(pop());

							_caller_ip_index = frame._ip_index;
							if (!invokeFromClass(superclass, method, argCount))
							{
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}
							frame = vm.frames[vm.frameCount - 1];
							break;
						}

					case OpCode.OP_CLOSURE:
						{
							ObjFunction function = Object.AS_FUNCTION(READ_CONSTANT(ref frame));
							ObjClosure closure = Object.newClosure(function);

							for (int i = 0; i < closure.upvalueCount; i++)
							{
								byte isLocal = READ_BYTE(ref frame);
								byte index = READ_BYTE(ref frame);
								if (isLocal != 0)
								{
									closure.upvalues[i] = captureUpvalue(frame._slot_offset + index);
								}
								else
								{
									closure.upvalues[i] = frame.closure.upvalues[index];
								}

							}

							push(Value.OBJ_VAL(closure)); // cs push after modify?

							break;
						}

					case OpCode.OP_CLOSE_UPVALUE:
						closeUpvalues(vm.stackTop - 1);
						pop();
						break;

					case OpCode.OP_RETURN:
						{
							Value_t result = pop();
							closeUpvalues(frame._slot_offset);

							vm.frameCount--;
							if (vm.frameCount == 0)
							{
								pop();
								return InterpretResult.INTERPRET_OK;
							}

							vm.stackTop = frame._slot_offset;
							push(result);

							frame = vm.frames[vm.frameCount - 1];
							break;
						}

					case OpCode.OP_CLASS:
						push(Value.OBJ_VAL(Object.newClass(READ_STRING(ref frame))));
						break;

					case OpCode.OP_INHERIT:
						{
							Value_t superclass = peek(1);
							if (!Object.IS_CLASS(superclass))
							{
								runtimeError("Superclass must be a class.");
								return InterpretResult.INTERPRET_RUNTIME_ERROR;
							}

							ObjClass subclass = Object.AS_CLASS(peek(0));
							Table.tableAddAll(ref Object.AS_CLASS(superclass).methods, ref subclass.methods);
							pop(); // Subclass.
							break;
						}

					case OpCode.OP_METHOD:
						defineMethod(READ_STRING(ref frame));
						break;
				}

				vm.frames[vm.frameCount - 1]._ip_index = frame._ip_index; //Csharp reference updating workaround
			}

		}

		private static bool BINARY_OP(OpCode op)
		{
			if (!Value.IS_NUMBER(peek(0)) || !Value.IS_NUMBER(peek(1)))
			{
				runtimeError("Operands must be numbers.");
				return false;
			}

			double b = Value.AS_NUMBER(pop());
			double a = Value.AS_NUMBER(pop());

			switch (op)
			{
				case OpCode.OP_ADD:
					push(Value.NUMBER_VAL(a + b));
					break;
				case OpCode.OP_SUBTRACT:
					push(Value.NUMBER_VAL(a - b));
					break;
				case OpCode.OP_MULTIPLY:
					push(Value.NUMBER_VAL(a * b));
					break;
				case OpCode.OP_DIVIDE:
					push(Value.NUMBER_VAL(a / b));
					break;
				case OpCode.OP_GREATER:
					push(Value.BOOL_VAL(a > b));
					break;
				case OpCode.OP_LESS:
					push(Value.BOOL_VAL(a < b));
					break;
			}

			return true;
		}

		public static InterpretResult interpret(char[] source)
		{

			ObjFunction function = Compiler.compile(source);
			if (function == null)
				return InterpretResult.INTERPRET_COMPILE_ERROR;

			push(Value.OBJ_VAL(function));

			ObjClosure closure = Object.newClosure(function);
			pop();
			push(Value.OBJ_VAL(closure));
			callValue(Value.OBJ_VAL(closure), 0);

			return run();
		}
	}
}
