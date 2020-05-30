namespace cloxcs
{
    class main
    {
        static void repl()
        {
            string line;
            for (;;)
            {
                System.Console.Write("> ");
                line = System.Console.ReadLine() +'\0';
                if (line == null)
                {
                    System.Console.WriteLine();
                    break;
                }

                VM.interpret(line.ToCharArray());
            }

        }

        static string readFile(string path)
        {
            System.Text.StringBuilder buffer = null;
            if (!System.IO.File.Exists(path))
            {
                System.Console.WriteLine("Could not open file {0}.", path );
                System.Environment.Exit(74);
            }
            buffer = new System.Text.StringBuilder( System.IO.File.ReadAllText(path));
            buffer.Append('\0');
            if (buffer == null)
            {
                System.Console.WriteLine("Not enough memory to read {0}.", path);
                System.Environment.Exit(74);
            }
            return buffer.ToString();
        }

        private static void runFile(string path)
        {
            char[] source = readFile(path).ToCharArray();
            InterpretResult result = VM.interpret(source);
            Memory.FREE<char>(ref source);

            if (result == InterpretResult.INTERPRET_COMPILE_ERROR)
                System.Environment.Exit(65);
            if (result == InterpretResult.INTERPRET_RUNTIME_ERROR)
                System.Environment.Exit(70);
        }

        static void Main(string[] args)
        {
            VM.initVM();

            if (args.Length == 0)
            {
                repl();
            }
            else if (args.Length == 1)
            {
                runFile(args[0]);
            }
            else
            {
                System.Console.WriteLine("Usage: clox [path]");
                System.Environment.Exit(64);
            }

            VM.freeVM();
#if DEBUG
            System.Console.WriteLine("\n\nPress a key to exit.");
            System.Console.ReadKey();
#endif
        }
    }
}
