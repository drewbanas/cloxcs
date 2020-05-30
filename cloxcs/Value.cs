#define NAN_BOXING
#if NAN_BOXING
using Value_t = System.UInt64;
#endif

namespace cloxcs
{

#if NAN_BOXING

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    public struct DoubleUnion
    {
        [System.Runtime.InteropServices.FieldOffset(0)]
        public ulong bits;
        [System.Runtime.InteropServices.FieldOffset(0)]
        public double num;
    }


#else
    public enum ValueType
    {
        VAL_BOOL,
        VAL_NIL,
        VAL_NUMBER,
        VAL_OBJ
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
    public struct _NumberUnion
    {
        [System.Runtime.InteropServices.FieldOffset(0)]
        public long Long;
        [System.Runtime.InteropServices.FieldOffset(0)]
        public double Double;
    }

    public struct Value_t
    {
        public ValueType type;
        public _NumberUnion data;
    }
#endif

    public struct ValueArray
    {
        public int capacity;
        public int count;
        public Value_t[] values;
    }

    static class Value
    {
#if !NAN_BOXING
        public const long FALSE_VALUE = 1;
        public const long TRUE_VALUE = 0;
#endif


#if NAN_BOXING
        const ulong QNAN = (ulong)0x7ffc000000000000;
        const ulong SIGN_BIT = (ulong)0x8000000000000000;

        const ulong TAG_NIL = 1; // 01
        const ulong TAG_FALSE = 2; // 10
        const ulong TAG_TRUE = 3; // 11

        const ulong _NIL_VAL = (Value_t)(ulong)(QNAN | TAG_NIL);
        const ulong FALSE_VAL = (Value_t)(ulong)(QNAN | TAG_FALSE);
        const ulong TRUE_VAL = (Value_t)(ulong)(QNAN | TAG_TRUE);
        static DoubleUnion _data = new DoubleUnion(); // work around

        static double valueToNum(Value_t value)
        {
            _data.bits = value;
            return _data.num;
        }

        static Value_t numToValue(double num)
        {
            _data.num = num;
            return _data.bits;
        }

        public static Value_t BOOL_VAL(bool b)
        {
            return b ? TRUE_VAL : FALSE_VAL;
        }

        public static Value_t NIL_VAL()
        {
            return _NIL_VAL;// (Value_t)(ulong)(QNAN | TAG_NIL);
        }

        public static Value_t NUMBER_VAL(double num)
        {
            return numToValue(num);
        }

        public static bool IS_BOOL(Value_t v)
        {
            //return (v & FALSE_VAL) == FALSE_VAL;
            return ((v) == TRUE_VAL || (v) == FALSE_VAL); // works better than Macro safe version
        }

        public static bool IS_NIL(Value_t v)
        {
            return v == _NIL_VAL;// NIL_VAL();
        }

        public static bool IS_NUMBER(Value_t v)
        {
            return (v & QNAN) != QNAN;
        }

        public static bool IS_OBJ(Value_t v)
        {
            return (v & (QNAN | SIGN_BIT)) == (QNAN | SIGN_BIT);
        }

        public static bool AS_BOOL(Value_t v)
        {
            return (v == TRUE_VAL);
        }

        public static double AS_NUMBER(Value_t v)
        {
            return valueToNum(v);
        }

        public static Obj AS_OBJ(Value_t v)
        {
            return (Obj)cHeap.objects.get((int)(v & ~(SIGN_BIT | QNAN)));
        }

        public static Value_t OBJ_VAL(Obj object_)
        {
            Value_t value = (Value_t)(SIGN_BIT | QNAN | (ulong)(uint)object_._mem_id);
            return value;
        }

#else
        public static bool IS_BOOL(Value_t value)
        {
            return value.type == ValueType.VAL_BOOL;
        }

        public static bool IS_NIL(Value_t value)
        {
            return value.type == ValueType.VAL_NIL;
        }

        public static bool IS_NUMBER(Value_t value)
        {
            return value.type == ValueType.VAL_NUMBER;
        }

        public static bool IS_OBJ(Value_t value)
        {
            return value.type == ValueType.VAL_OBJ;
        }

        public static Obj AS_OBJ(Value_t value)
        {
            return (Obj)cHeap.objects.get((int)value.data.Long);
        }

        public static bool AS_BOOL(Value_t value)
        {
            return (value.data.Long == TRUE_VALUE);
        }

        public static double AS_NUMBER(Value_t value)
        {
            return value.data.Double;
        }

        public static Value_t BOOL_VAL(bool b)
        {
            Value_t value = new Value_t();
            value.type = ValueType.VAL_BOOL;
            if (b)
                value.data.Long = TRUE_VALUE;
            else
                value.data.Long = FALSE_VALUE;
            return value;
        }

        public static Value_t NIL_VAL()
        {
            Value_t value = new Value_t();
            value.type = ValueType.VAL_NIL;
            value.data.Long = 0;
            return value;
        }

        public static Value_t NUMBER_VAL(double val)
        {
            Value_t value = new Value_t();
            value.type = ValueType.VAL_NUMBER;
            value.data.Double = val;
            return value;
        }

        public static Value_t OBJ_VAL(Obj object_)
        {
            Value_t value = new Value_t();
            value.type = ValueType.VAL_OBJ;            
            value.data.Long = object_._pointer;
            return value;
        }
#endif


        public static void initiValueArray(ref ValueArray array)
        {
            array.values = null;
            array.capacity = 0;
            array.count = 0;
        }

        public static void writeValueArray(ref ValueArray array, Value_t value)
        {
            if (array.capacity < array.count + 1)
            {
                int oldCapacity = array.capacity;
                array.capacity = Memory.GROW_CAPACITY(oldCapacity);
                array.values = (Value_t[])Memory.GROW_ARRAY<Value_t>(ref array.values, typeof(Value_t), oldCapacity, array.capacity);
            }

            array.values[array.count] = value;
            array.count++;
        }

        public static void freeValueArray(ref ValueArray array)
        {
            Memory.FREE_ARRAY<Value_t>(typeof(Value_t), ref array.values, array.capacity);
            initiValueArray(ref array);
        }

        public static void printValue(Value_t value)
        {
#if NAN_BOXING
            if (IS_BOOL(value))
            {
                System.Console.Write(AS_BOOL(value) ? "true" : "false");
            }
            else if (IS_NIL(value))
            {
                System.Console.Write("nil");
            }
            else if (IS_NUMBER(value))
            {
                System.Console.Write(AS_NUMBER(value).ToString("G"));
            }
            else if (IS_OBJ(value))
            {
                Object.printObject(value);
            }
#else
            switch (value.type)
            {
                case ValueType.VAL_BOOL:
                    System.Console.Write(AS_BOOL(value) ? "true" : "false");
                    break;
                case ValueType.VAL_NIL:
                    System.Console.Write("nil");
                    break;
                case ValueType.VAL_NUMBER:
                    System.Console.Write(AS_NUMBER(value).ToString("G"));
                    break;
                case ValueType.VAL_OBJ:
                    Object.printObject(value);
                    break;
            }
#endif
        }

        public static bool valuesEqual(Value_t a, Value_t b)
        {
#if NAN_BOXING
            if (IS_NUMBER(a) && IS_NUMBER(b))
                return AS_NUMBER(a) == AS_NUMBER(b);

            return a == b;
#else
            if (a.type != b.type)
                return false;

            switch (a.type)
            {
                case ValueType.VAL_BOOL:
                    return AS_BOOL(a) == AS_BOOL(b);
                case ValueType.VAL_NIL:
                    return true;
                case ValueType.VAL_NUMBER:
                    return AS_NUMBER(a) == AS_NUMBER(b);
                case ValueType.VAL_OBJ:
                    return AS_OBJ(a) == AS_OBJ(b);
            }

            return false; // _unreachable
#endif
        }
    }
}
