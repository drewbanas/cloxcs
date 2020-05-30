# cloxcs

A “*sharp yet pointless*” port of the Lox bytecode virtual machine interpreter from Bob Nystrom’s [*Crafting Interpreters*](https://craftinginterpreters.com/) book. An attempt is made to stay close to the original C code so one can refer to the Crafting Interpreters book for explanations. Similarity to the original code is emphasized over efficiency with the new implementing language. Nonetheless, to get clox working in Csharp some notable differences are unavoidable.

## Main differences from clox
-   Pointers are approximated by pairs of arrays and indices. It was decided not to  use “unsafe” features in Csharp as that would end up being used a lot. I may as  well just re-wrote it in C, but that would be less engaging (For something more straightforward, see the jlox port from Java to Csharp).
-   Variables referred to by other variables have to be updated explicitly.
-   Class names are priotized as they appear in the code more often. Hence some structs got a "_t" suffix (e.g. "Value_t" struct, since there is a "Value" class).
-   Some structs have to be converted into classes to allow certain things in Csharp.
-   Macros are converted to functions (inefficient, but simple).
-   “ref” arguments are excessively used, perhaps even where it is not necessary.
-   Variable name clashes with the implementing language (now Csharp) are suffixed with an underscore. C name clashes in clox that do not clash with Csharp, have their underscore removed.
-   Except in cases where the workaround is a full-blown class, variable names that are not in clox are prefixed with an underscore.
-   An ad hoc memory manager (I have no idea how one is supposed to work, and I have not fully tested this) is implemented (cMem) to have numbers that can represent objects associated to them (fake pointers).
-   Some C string/byte manipulation functions (memcpy, memcmp, strtod) were implemented ad hoc.
-   Error/disassembly printing functions take in Csharp strings since char arrays are inconvenient for literal arguments.

## Known issues
-   The instruction pointer had to be incremented “manually” for classes and native functions.
-   Either the Garbage Collector is not freeing up any memory or/(and) (most probably) I couldn’t write proper Lox tests that would make the GC reclaim memory. At least it is not violently crashing (so far).

## Advice for would-be clox porters (things I did too late)
-   Test often. Get the disassembly output right early on.
-   Have a working C clox with debug ouputs enabled for reference.
-   Modifying the reference C clox code backwards through the chapters can help (In other words, "undoing" the changes in the chapters). This allows direct comparisons at earlier stages through the book.
-   Have a copy of the [reference implementation](https://github.com/munificent/craftinginterpreters).
