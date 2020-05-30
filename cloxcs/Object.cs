#if DEBUG
#define DEBUG_LOG_GC
#endif

#define NAN_BOXING 
#if NAN_BOXING
using Value_t = System.UInt64;
#endif



namespace cloxcs
{
    public enum ObjType
    {
        OBJ_BOUND_METHOD,
        OBJ_CLASS,
        OBJ_CLOSURE,
        OBJ_FUNCTION,
        OBJ_INSTANCE,
        OBJ_NATIVE,
        OBJ_STRING,
        OBJ_UPVALUE
    }

    public class Obj
    {
        public ObjType type;
        public bool isMarked;
        public Obj next;

        public int _mem_id; // work around for pointers
        public Obj()
        {
            // Fake pointer
            this._mem_id = cHeap.objects.store(this);
        }

        public void _free()
        {
            cHeap.objects.remove(this._mem_id);
            this._mem_id = -1; // trigger an error if used
        }
    }

    public class ObjFunction : Obj
    {
        //public Obj obj;
        public int arity;
        public int upvalueCount;
        public Chunk_t chunk;
        public ObjString name = new ObjString();
    }

    public delegate Value_t NativeFn(int argCount, int _args_on_stack);

    public class ObjNative : Obj // cannot inherit structs in csharp
    {
        //public Obj obj;
        public NativeFn function;
    }

    public class ObjString : Obj
    {
        //public Obj obj;
        public int length;
        public char[] chars;
        public uint hash;

        public int _start;
    }

    public class ObjUpvalue : Obj
    {
        //public Obj obj;
        public int location;
        public Value_t closed;
        public new ObjUpvalue next;

        public Value_t[] _value_src;
    }

    public class ObjClosure : Obj
    {
        //public Obj obj;
        public ObjFunction function = new ObjFunction();
        public ObjUpvalue[] upvalues;
        public int upvalueCount;
    }

    public class ObjClass : Obj
    {
        public ObjString name = new ObjString();
        public Table_t methods;
    }

    public class ObjInstance : Obj
    {
        public ObjClass klass = new ObjClass();
        public Table_t fields;
    }

    public class ObjBoundMethod : Obj
    {
        public Value_t receiver;
        public ObjClosure method;
    }

    class Object
    {
        static bool isObjType(Value_t value, ObjType type)
        {
            return Value.IS_OBJ(value) && Value.AS_OBJ(value).type == type;
        }

        public static ObjType OBJ_TYPE(Value_t value)
        {
            return Value.AS_OBJ(value).type;
        }

        public static bool IS_BOUND_METHOD(Value_t value)
        {
            return isObjType(value, ObjType.OBJ_BOUND_METHOD);
        }

        public static bool IS_CLASS(Value_t value)
        {
            return isObjType(value, ObjType.OBJ_CLASS);
        }

        public static bool IS_CLOSURE(Value_t value)
        {
            return isObjType(value, ObjType.OBJ_CLOSURE);
        }

        public static bool IS_FUNCTION(Value_t value)
        {
            return isObjType(value, ObjType.OBJ_FUNCTION);
        }

        public static bool IS_INSTANCE(Value_t value)
        {
            return isObjType(value, ObjType.OBJ_INSTANCE);
        }

        public static bool IS_NATIVE(Value_t value)
        {
            return isObjType(value, ObjType.OBJ_NATIVE);
        }

        public static bool IS_STRING(Value_t value)
        {
            return isObjType(value, ObjType.OBJ_STRING);
        }

        public static ObjBoundMethod AS_BOUND_METHOD(Value_t value)
        {
            return (ObjBoundMethod)Value.AS_OBJ(value);
        }

        public static ObjClass AS_CLASS(Value_t value)
        {
            return (ObjClass)Value.AS_OBJ(value);
        }

        public static ObjClosure AS_CLOSURE(Value_t value)
        {
            return (ObjClosure)Value.AS_OBJ(value);
        }

        public static ObjFunction AS_FUNCTION(Value_t value)
        {
            return (ObjFunction)Value.AS_OBJ(value);
        }

        public static ObjInstance AS_INSTANCE(Value_t value)
        {
            return (ObjInstance)Value.AS_OBJ(value);
        }

        public static NativeFn AS_NATIVE(Value_t value)
        {
            return ((ObjNative)Value.AS_OBJ(value)).function;
        }

        public static ObjString AS_STRING(Value_t value)
        {
            return (ObjString)Value.AS_OBJ(value);
        }

        public static char[] AS_CSTRING(Value_t value)
        {
            return ((ObjString)Value.AS_OBJ(value)).chars;
        }


        static Obj ALLOCATE_OBJ(ObjType type)
        {
            return allocateObject(1, type);
        }

        // Generics, <T>, doesn't seem to work with classes?
        static Obj allocateObject(int size, ObjType type)
        {

            Obj object_ = null;

            switch (type)
            {
                case ObjType.OBJ_STRING:
                    object_ = new ObjString();
                    break;
                case ObjType.OBJ_FUNCTION:
                    object_ = new ObjFunction();
                    break;
                case ObjType.OBJ_INSTANCE:
                    object_ = new ObjInstance();
                    break;
                case ObjType.OBJ_NATIVE:
                    object_ = new ObjNative();
                    break;
                case ObjType.OBJ_CLOSURE:
                    object_ = new ObjClosure();
                    break;
                case ObjType.OBJ_UPVALUE:
                    object_ = new ObjUpvalue();
                    break;
                case ObjType.OBJ_CLASS:
                    object_ = new ObjClass();
                    break;
                case ObjType.OBJ_BOUND_METHOD:
                    object_ = new ObjBoundMethod();
                    break;
                default:
                    object_ = null;// clox: (Obj*)reallocate(NULL, 0, size);
                    break;
            }

            object_.type = type;
            object_.isMarked = false;

            object_.next = VM.vm.objects;
            VM.vm.objects = object_;

#if DEBUG_LOG_GC
            System.Console.WriteLine("{0} allocate {1} for {2}", object_._mem_id.ToString(), size.ToString(), type.ToString());
#endif
            return object_;
        }

        public static ObjBoundMethod newBoundMethod(Value_t receiver, ObjClosure method)
        {
            ObjBoundMethod bound = (ObjBoundMethod)ALLOCATE_OBJ(ObjType.OBJ_BOUND_METHOD);
            bound.receiver = receiver;
            bound.method = method;
            return bound;
        }


        public static ObjClass newClass(ObjString name)
        {
            ObjClass klass = (ObjClass)ALLOCATE_OBJ(ObjType.OBJ_CLASS);
            klass.name = name;
            Table.initTable(ref klass.methods);
            return klass;
        }

        public static ObjClosure newClosure(ObjFunction function)
        {
            ObjUpvalue[] upvalues = Memory.ALLOCATE<ObjUpvalue>(function.upvalueCount);
            for (int i = 0; i < function.upvalueCount; i++)
            {
                upvalues[i] = null;
            }

            ObjClosure closure = (ObjClosure)ALLOCATE_OBJ(ObjType.OBJ_CLOSURE);
            closure.function = function;
            closure.upvalues = upvalues;
            closure.upvalueCount = function.upvalueCount;
            return closure;
        }

        public static ObjFunction newFunction()
        {
            ObjFunction function = (ObjFunction)ALLOCATE_OBJ(ObjType.OBJ_FUNCTION);

            function.arity = 0;
            function.upvalueCount = 0;
            function.name = null;
            Chunk.initChunk(ref function.chunk);
            return function;
        }

        public static ObjInstance newInstance(ObjClass klass)
        {
            ObjInstance instance = (ObjInstance)ALLOCATE_OBJ(ObjType.OBJ_INSTANCE);
            instance.klass = klass;
            Table.initTable(ref instance.fields);
            return instance;
        }

        public static ObjNative newNative(NativeFn function)
        {
            ObjNative native = (ObjNative)ALLOCATE_OBJ(ObjType.OBJ_NATIVE);
            native.function = function;
            return native;
        }

        private static ObjString allocateString(char[] chars, int length, uint hash)
        {
            ObjString string_ = (ObjString)ALLOCATE_OBJ(ObjType.OBJ_STRING);
            string_.length = length;
            string_.chars = chars;
            string_.hash = hash;
            string_._start = 0;
            string_.chars[length] = '\0';

            VM.push(Value.OBJ_VAL(string_));
            Table.tableSet(ref VM.vm.strings, string_, Value.NIL_VAL());
            VM.pop();

            return string_;
        }

        private static uint hashString(char[] key, int _start, int length)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < length; i++)
            {
                hash ^= key[i + _start];
                hash *= 16777619;
            }
            return hash;
        }

        public static ObjString takeString(char[] chars, int _start, int length)
        {
            uint hash = hashString(chars, _start, length);
            ObjString interned = Table.tableFindString(ref VM.vm.strings, chars, _start, length, hash);

            if (interned != null)
            {
                Memory.FREE_ARRAY<char>(typeof(char), ref chars, length + 1);
                return interned;
            }

            return allocateString(chars, length, hash);
        }

        public static ObjString copyString(char[] chars, int _start, int length)
        {
            uint hash = hashString(chars, _start, length);
            ObjString interned = Table.tableFindString(ref VM.vm.strings, chars, _start, length, hash);

            if (interned != null)
                return interned;

            char[] heapChars = Memory.ALLOCATE<char>(length + 1);
            Cfun._memcpy<char>(heapChars, chars, _start, length);
            heapChars[length] = '\0';

            return allocateString(heapChars, length, hash);
        }

        public static ObjUpvalue newUpvalue(int slot)
        {
            ObjUpvalue upvalue = (ObjUpvalue)ALLOCATE_OBJ(ObjType.OBJ_UPVALUE);
            upvalue.closed = Value.NIL_VAL();
            upvalue.location = slot;
            upvalue.next = null;

            // not needed in clox where "slot" is a pointer
            upvalue._value_src = VM.vm.stack; // default for open upvalues
            return upvalue;
        }

        private static void printFunction(ObjFunction function)
        {
            if (function.name == null)
            {
                System.Console.Write("<script>");
                return;
            }
            System.Console.Write("<fn {0}>", new string(function.name.chars, function.name._start, function.name.length));
        }

        public static void printObject(Value_t value)
        {
            char[] _chars; // for string conversion
            switch (OBJ_TYPE(value))
            {
                case ObjType.OBJ_CLASS:
                    _chars = AS_CLASS(value).name.chars;
                    System.Console.Write(new string(_chars, 0, _chars.Length - 1));
                    break;
                case ObjType.OBJ_BOUND_METHOD:
                    printFunction(AS_BOUND_METHOD(value).method.function);
                    break;
                case ObjType.OBJ_CLOSURE:
                    printFunction(AS_CLOSURE(value).function);
                    break;
                case ObjType.OBJ_FUNCTION:
                    printFunction(AS_FUNCTION(value));
                    break;
                case ObjType.OBJ_INSTANCE:
                    _chars = AS_INSTANCE(value).klass.name.chars;
                    System.Console.Write("{0} instance", new string(_chars, 0, _chars.Length - 1));
                    break;
                case ObjType.OBJ_NATIVE:
                    System.Console.Write("<native fn>");
                    break;
                case ObjType.OBJ_STRING:
                    string _str = new string(AS_CSTRING(value));
                    System.Console.Write(_str.Substring(0, _str.Length - 1));
                    break;
                case ObjType.OBJ_UPVALUE:
                    System.Console.Write("upvalue");
                    break;
            }
        }
    }
}
