#if DEBUG
#define DEBUG_STRESS_GC
#define DEBUG_LOG_GC
#endif

#define NAN_BOXING
#if NAN_BOXING
using Value_t = System.UInt64;
#endif

namespace cloxcs
{

    class Memory
    {
        const uint GC_HEAP_GROW_FACTOR = 2;

        public static T[] ALLOCATE<T>(int count)
        {
            T[] _mem = new T[0];
            System.Array.Resize(ref _mem, count);
            return _mem;
        }

        public static void FREE<T>(ref T[] _array)
        {
            System.Array.Resize<T>(ref _array, 0); // a "FREE" in csharp!
            _array = null;
        }

        public static int GROW_CAPACITY(int capacity)
        {
            return capacity < 8 ? 8 : capacity * 2;
        }

        public static object GROW_ARRAY<T>(ref T[] previous, System.Type type, int oldCapacity, int capacity)
        {
            return reallocate<T>(ref previous, oldCapacity, capacity);
        }

        public static void FREE_ARRAY<T>(System.Type type, ref T[] pointer, int oldCount)
        {
            reallocate<T>(ref pointer, oldCount, 0);
        }


        public static object reallocate<T>(ref T[] previous, int oldSize, int newSize)
        {
            VM.vm.bytesAllocated += (uint)(newSize - oldSize);

            if (newSize > oldSize)
            {
#if DEBUG_STRESS_GC
                collectGarbage();
#endif

                if (VM.vm.bytesAllocated > VM.vm.nextGC)
                {
                    collectGarbage();
                }
            }

            if (newSize == 0)
            {
                FREE<T>(ref previous);
                return null;
            }

            System.Array.Resize(ref previous, newSize); // realloc in clox
            return previous;
        }

        public static void markObject(Obj object_)
        {
            if (object_ == null)
                return;
            if (object_.isMarked)
                return;
#if DEBUG_LOG_GC
            System.Console.Write("{0} mark ", object_._mem_id.ToString());
            Value.printValue(Value.OBJ_VAL(object_));
            System.Console.WriteLine();
#endif
            object_.isMarked = true;

            if (VM.vm.grayCapacity < VM.vm.grayCount + 1)
            {
                VM.vm.grayCapacity = GROW_CAPACITY(VM.vm.grayCapacity);
                System.Array.Resize<Obj>(ref VM.vm.grayStack, VM.vm.grayCapacity);
            }

            VM.vm.grayStack[VM.vm.grayCount++] = object_;
        }

        public static void markValue(ref Value_t value)
        {
            if (!Value.IS_OBJ(value))
                return;
            markObject(Value.AS_OBJ(value));
        }

        private static void markArray(ref ValueArray array)
        {
            for (int i = 0; i < array.count; i++)
            {
                markValue(ref array.values[i]);
            }
        }

        private static void blackenObject(Obj object_)
        {
#if DEBUG_LOG_GC
            System.Console.Write("{0} blacken ", object_._mem_id.ToString());
            Value.printValue(Value.OBJ_VAL(object_));
            System.Console.WriteLine();
#endif

            switch (object_.type)
            {
                case ObjType.OBJ_BOUND_METHOD:
                    {
                        ObjBoundMethod bound = (ObjBoundMethod)object_;
                        markValue(ref bound.receiver);
                        markObject((Obj)bound.method);
                        break;
                    }

                case ObjType.OBJ_CLASS:
                    {
                        ObjClass klass = (ObjClass)object_;
                        markObject((Obj)klass.name);
                        Table.markTable(ref klass.methods);
                        break;
                    }

                case ObjType.OBJ_CLOSURE:
                    {
                        ObjClosure closure = (ObjClosure)object_;
                        markObject((Obj)closure.function);
                        for (int i = 0; i < closure.upvalueCount; i++)
                        {
                            markObject((Obj)closure.upvalues[i]);
                        }
                        break;
                    }

                case ObjType.OBJ_FUNCTION:
                    {
                        ObjFunction function = (ObjFunction)object_;
                        markObject((Obj)function.name);
                        markArray(ref function.chunk.constants);
                        break;
                    }

                case ObjType.OBJ_INSTANCE:
                    {
                        ObjInstance instance = (ObjInstance)object_;
                        markObject((Obj)(instance.klass));
                        Table.markTable(ref instance.fields);
                        break;
                    }

                case ObjType.OBJ_UPVALUE:
                    markValue(ref ((ObjUpvalue)object_).closed);
                    break;

                case ObjType.OBJ_NATIVE:
                case ObjType.OBJ_STRING:
                    break;
            }
        }

        private static void freeObject(ref Obj object_)
        {
#if DEBUG_LOG_GC
            System.Console.WriteLine("{0} free type {1}", object_._mem_id.ToString(), object_.type.ToString());
#endif
            switch (object_.type)
            {
                case ObjType.OBJ_BOUND_METHOD:
                    object_._free();
                    object_ = null;
                    break;

                case ObjType.OBJ_CLASS:
                    {
                        ObjClass klass = (ObjClass)object_;
                        Table.freeTable(ref klass.methods);
                        //FREE<ObjClass>(ref object_);
                        object_._free();
                        object_ = null;
                        break;
                    }

                case ObjType.OBJ_CLOSURE:
                    {
                        ObjClosure closure = (ObjClosure)object_;
                        FREE_ARRAY<ObjUpvalue>(typeof(ObjUpvalue), ref closure.upvalues, closure.upvalueCount);
                        object_._free();
                        object_ = null;
                        break;
                    }

                case ObjType.OBJ_FUNCTION:
                    {
                        ObjFunction function = (ObjFunction)object_;
                        Chunk.freeChunk(ref function.chunk); // the function's byte code
                        object_._free();
                        object_ = null;
                        break;
                    }

                case ObjType.OBJ_INSTANCE:
                    {
                        ObjInstance instance = (ObjInstance)object_;
                        Table.freeTable(ref instance.fields);
                        object_._free();
                        object_ = null;
                        break;
                    }

                case ObjType.OBJ_NATIVE:
                    {
                        object_._free();
                        object_ = null;
                        break;
                    }

                case ObjType.OBJ_STRING:
                    {
                        ObjString string_ = (ObjString)object_;
                        FREE_ARRAY<char>(typeof(char), ref string_.chars, string_.length + 1);
                        object_._free();
                        object_ = null;
                        break;
                    }

                case ObjType.OBJ_UPVALUE:
                    cHeap.values.remove(((ObjUpvalue)object_).location);
                    object_._free();
                    object_ = null;
                    break;
            }
        }

        private static void markRoots()
        {
            for (int slot = 0; slot < VM.vm.stackTop; slot++)
            {
                markValue(ref VM.vm.stack[slot]);
            }

            for (int i = 0; i < VM.vm.frameCount; i++)
            {
                markObject((Obj)VM.vm.frames[i].closure);
            }

            for (ObjUpvalue upvalue = VM.vm.openUpvalues; upvalue != null; upvalue = upvalue.next)
            {
                markObject((Obj)upvalue);
            }


            Table.markTable(ref VM.vm.globals);
            Compiler.markCompilerRoots();
            markObject((Obj)VM.vm.initString);
        }

        private static void traceReferences()
        {
            while (VM.vm.grayCount > 0)
            {
                Obj object_ = VM.vm.grayStack[--VM.vm.grayCount];
                blackenObject(object_);
            }
        }

        private static void sweep()
        {
            Obj previous = null;
            Obj object_ = VM.vm.objects;
            while (object_ != null)
            {
                if (object_.isMarked)
                {
                    object_.isMarked = false;
                    previous = object_;
                    object_ = object_.next;
                }
                else
                {
                    Obj unreached = object_;
                    object_ = object_.next;
                    if (previous != null)
                    {
                        previous.next = object_;
                    }
                    else
                    {
                        VM.vm.objects = object_;
                    }

                    freeObject(ref unreached);
                }
            }

        }

        public static void collectGarbage()
        {
#if DEBUG_LOG_GC
            System.Console.WriteLine("-- gc begin");
            uint before = VM.vm.bytesAllocated;
#endif
            markRoots();
            traceReferences();
            Table.tableRemoveWhite(ref VM.vm.strings);
            sweep();

            VM.vm.nextGC = VM.vm.bytesAllocated * GC_HEAP_GROW_FACTOR;

#if DEBUG_LOG_GC
            System.Console.WriteLine("-- gc end");
            System.Console.WriteLine("   collected {0} bytes (from {1} to {2}) next at {3}", (before - VM.vm.bytesAllocated).ToString(), before.ToString(), VM.vm.bytesAllocated.ToString(), VM.vm.nextGC.ToString());
#endif
        }

        public static void freeObjects()
        {
            Obj object_ = VM.vm.objects;
            while (object_ != null)
            {
                Obj next = object_.next;
                freeObject(ref object_);
                object_ = next;
            }

            FREE<Obj>(ref VM.vm.grayStack);
        }
    }
}
