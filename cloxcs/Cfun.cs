#define NAN_BOXING
#if NAN_BOXING
using Value_t = System.UInt64;
#endif

namespace cloxcs
{
    /*
     * Ad hoc memory manager
     * fake pseudo pointers
     */
    class cMem<T>
    {
        public T[] contents;
        private System.Collections.BitArray isVacant;
        private int count;
        private int capacity = 16;// preallocate

        // vacancy management
        private const int VACANT_COUNT = 128;
        private int[] vacantSlots;
        private int vacantTop;
        private bool isFragmented;
        private bool isTooFragmented;


        public cMem()
        {
            contents = new T[capacity];
            isVacant = new System.Collections.BitArray(capacity);
            isVacant.SetAll(true);
            isVacant[0] = false;
            count = 1;

            // vacancy management        
            vacantSlots = new int[VACANT_COUNT];
            vacantTop = 0;
            isFragmented = false;
            isTooFragmented = false;
        }

        public int store(T data)
        {
            count++;
            if (count >= capacity)
            {
                int oldCapacity = capacity;
                capacity = Memory.GROW_CAPACITY(oldCapacity);
                Memory.reallocate<T>(ref contents, oldCapacity, capacity);
                isVacant.Length = capacity;

                for (int i = count; i < capacity; i++)
                {
                    isVacant[i] = true;
                }

                isFragmented = false;
                isTooFragmented = false;
            }

            int index = count - 1;
            if (isFragmented)
            {
                if (vacantTop > 0)
                {
                    index = vacantSlots[vacantTop - 1];
                    vacantTop--;
                    if (vacantTop == 0 && !isTooFragmented)
                        isFragmented = false;
                }
                else
                {
                    for (int i = capacity - 1; i >= 0; --i)
                    {
                        if (isVacant[i])
                        {
                            index = i;
                            break;
                        }
                    }
                }
            }

            contents[index] = data;
            isVacant[index] = false;
            return index; // The fake "pointer"
        }

        public T get(int index)
        {
            return contents[index];
        }

        public void remove(int index)
        {
            if (index == 0) // first is reserved (or null?)
                return;

            isVacant[index] = true;
            count--;

            if (count == 1)
            {
                isFragmented = false;
                isTooFragmented = false;
                return;
            }

            if (index != count)
                isFragmented = true;

            if (isFragmented && !isTooFragmented)
            {
                vacantSlots[vacantTop++] = index;
            }

            if (vacantTop == VACANT_COUNT)
                isTooFragmented = true;
        }

        public void free()
        {
            Memory.FREE_ARRAY<T>(typeof(T), ref contents, count);
            isVacant.Length = 0;
            contents = null;
            isVacant = null;
        }
    }

    static class cHeap
    {
        public static cMem<Obj> objects = new cMem<Obj>();
        public static cMem<Value_t> values = new cMem<Value_t>();
    }

    static class Cfun
    {
        public static double _strtod(char[] src, int start, int length)
        {
            string str = new string(src);
            string numStr = str.Substring(start, length);
            return double.Parse(numStr);

        }

        public static bool _memcmp(char[] source, int start, string rest, int length)
        {
            for (int i = 0; i < length; i++)
            {
                if (source[i + start] != rest[i])
                    return false;
            }
            return true;
        }

        public static bool _memcmp(char[] source1, int start1, char[] source2, int start2, int length)
        {
            for (int i = 0; i < length; i++)
            {
                if (source1[i + start1] != source2[i + start2])
                    return false;
            }
            return true;
        }

        // used by concatenate
        public static void _memcpy<T>(T[] dest, int destOffset, T[] src, int srcOffset, int length)
        {
            for (int i = 0; i < length; i++)
            {
                dest[destOffset + i] = src[srcOffset + i];
            }
        }

        // used by concatenate
        public static void _memcpy<T>(T[] dest, T[] src, int srcOffset, int length)
        {
            for (int i = 0; i < length; i++)
            {
                dest[i] = src[srcOffset + i];
            }
        }

    }
}
