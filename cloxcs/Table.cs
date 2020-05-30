#define NAN_BOXING
#if NAN_BOXING
using Value_t = System.UInt64;
#endif

namespace cloxcs
{
    public class Entry // Csharp struct cannot be null (for findEntry, tombstone)
    {
        public ObjString key;
        public Value_t value;
    }

    public struct Table_t
    {
        public int count;
        public int capacity;
        public Entry[] entries;
    }

    static class Table
    {
        private const float TABLE_MAX_LOAD = 0.75f;

        public static void initTable(ref Table_t table)
        {
            table.count = 0;
            table.capacity = -1;
            table.entries = null;
        }

        public static void freeTable(ref Table_t table)
        {
            Memory.FREE_ARRAY<Entry>(typeof(Entry), ref table.entries, table.capacity + 1);
            initTable(ref table);
        }

        private static Entry findEntry(ref Entry[] entries, int capacity, ObjString key, ref int _foundIndex)
        {
            _foundIndex = -1;
            uint index = key.hash & (uint)capacity;
            Entry tombstone = null;

            for (;;)
            {
                Entry entry = entries[index]; // clox &entries[index]

                if (entry.key == null)
                {
                    if (Value.IS_NIL(entry.value))
                    {
                        // Empty entry.
                        _foundIndex = (int)index;
                        return tombstone != null ? tombstone : entry;
                    }
                    else
                    {
                        // We found a tombstone.
                        if (tombstone == null)
                            tombstone = entry;
                    }
                }
                else if (entry.key == key)
                {
                    // We found the key.
                    _foundIndex = (int)index;
                    return entry;
                }

                index = (index + 1) & (uint)capacity;

            }

        }

        public static bool tableGet(ref Table_t table, ObjString key, ref Value_t value)
        {
            if (table.count == 0)
                return false;
            int _index = -1; // work around
            Entry entry = findEntry(ref table.entries, table.capacity, key, ref _index);
            if (entry.key == null)
                return false;

            value = entry.value;
            return true;
        }

        private static void adjustCapacity(ref Table_t table, int capacity)
        {
            Entry[] entries = Memory.ALLOCATE<Entry>(capacity + 1);

            //fix
            for (int i = 0; i < entries.Length; i++)
                entries[i] = new Entry();

            for (int i = 0; i <= capacity; i++)
            {
                entries[i].key = null;
                entries[i].value = Value.NIL_VAL();
            }

            table.count = 0;

            for (int i = 0; i <= table.capacity; i++)
            {
                Entry entry = table.entries[i];
                if (entry.key == null)
                    continue;

                int _index = -1; // work around
                Entry dest = findEntry(ref entries, capacity, entry.key, ref _index);
                dest.key = entry.key;
                dest.value = entry.value;

                entries[_index] = dest; // CS workaround

                table.count++;
            }


            Memory.FREE_ARRAY<Entry>(typeof(Entry), ref table.entries, table.capacity + 1);
            table.entries = entries;
            table.capacity = capacity;
        }

        public static bool tableSet(ref Table_t table, ObjString key, Value_t value)
        {
            if (table.count + 1 > (table.capacity + 1) * TABLE_MAX_LOAD)
            {
                int capacity = Memory.GROW_CAPACITY(table.capacity + 1) - 1;
                adjustCapacity(ref table, capacity);
            }

            int _index = -1; // work around
            Entry entry = findEntry(ref table.entries, table.capacity, key, ref _index);

            bool isNewKey = entry.key == null;
            if (isNewKey && Value.IS_NIL(entry.value))
                table.count++;

            entry.key = key;
            entry.value = value;

            table.entries[_index] = entry; // CS workaround

            return isNewKey;
        }

        public static bool tableDelete(ref Table_t table, ObjString key)
        {
            if (table.count == 0)
                return false;

            // Find the entry.
            int _index = -1;
            Entry entry = findEntry(ref table.entries, table.capacity, key, ref _index);
            if (entry.key == null)
                return false;

            // Place a tombstone in the entry.
            entry.key = null;
            entry.value = Value.BOOL_VAL(true);
            table.entries[_index] = entry; // Csharp ref workaround

            return true;
        }

        public static void tableAddAll(ref Table_t from, ref Table_t to)
        {
            for (int i = 0; i <= from.capacity; i++)
            {
                Entry entry = from.entries[i];
                if (entry.key != null)
                {
                    tableSet(ref to, entry.key, entry.value);
                }
            }
        }

        public static ObjString tableFindString(ref Table_t table, char[] chars, int _start, int length, uint hash)
        {
            if (table.count == 0)
                return null;

            uint index = (uint)(hash & table.capacity);

            for (;;)
            {
                Entry entry = table.entries[index];

                if (entry.key == null)
                {
                    // Stop if we find an empty non-tombstone entry.
                    if (Value.IS_NIL(entry.value))
                        return null;
                }
                else if (entry.key.length == length &&
                    entry.key.hash == hash &&
                    Cfun._memcmp(entry.key.chars, entry.key._start, chars, _start, length))
                {
                    // We found it.
                    return entry.key;
                }


                index = (uint)((index + 1) & table.capacity);
            }
        }

        public static void tableRemoveWhite(ref Table_t table)
        {
            for (int i = 0; i <= table.capacity; i++)
            {
                Entry entry = table.entries[i];
                if (entry.key != null && !entry.key.isMarked) // clox: entry->key->obj.isMarked
                {
                    tableDelete(ref table, entry.key);
                }
            }
        }

        public static void markTable(ref Table_t table)
        {
            for (int i = 0; i <= table.capacity; i++)
            {
                Entry entry = table.entries[i];
                Memory.markObject((Obj)entry.key);
                Memory.markValue(ref entry.value);
            }
        }
    }
}
